using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ARDU_OTK.Services.Fc;
using Microsoft.Data.Sqlite;

namespace ARDU_OTK.Services.Store;

/// <summary>
/// Ошибка хранилища прогонов: несовместимая схема, недописанный прогон,
/// нарушенная целостность. Отдельный тип нужен, чтобы композиционный корень мог
/// увести пользователя на экран восстановления, а не показать общий сбой.
/// </summary>
public sealed class CalibrationStoreException : Exception
{
    public CalibrationStoreException(string message) : base(message)
    {
    }

    public CalibrationStoreException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// Реестр прогонов приёмки на SQLite (<c>Microsoft.Data.Sqlite</c>).
/// </summary>
/// <remarks>
/// <para>
/// Пишет по ходу дела: строка прогона появляется в момент старта, записи
/// параметров, проверки и сообщения борта фиксируются по мере поступления,
/// каждая своей короткой транзакцией. Убитый процесс обязан оставить
/// восстановимый частичный прогон, а не пустую базу — а убить процесс может и
/// само приложение, применив обновление Velopack.
/// </para>
/// <para>
/// Обращения сериализованы одним семафором: стенд однопользовательский, писатель
/// всегда один, и такая дисциплина полностью снимает «database is locked» без
/// повторов и таймаутов.
/// </para>
/// </remarks>
public sealed class SqliteCalibrationStore : ICalibrationStore, IDisposable
{
    /// <summary>Версия схемы, которую понимает эта сборка.</summary>
    /// <remarks>
    /// v2 добавила профили изделий (<see cref="CalibrationProfile"/>), настройки
    /// рабочего места и привязку прогона к профилю.
    /// </remarks>
    public const int SchemaVersion = 2;

    // Формат меток времени: UTC, фиксированная ширина. Такой текст сравнивается
    // и сортируется лексикографически ровно как хронологически — на этом держатся
    // индексы по времени и вычисление конца прерванного прогона.
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private const string VerdictPass = "pass";
    private const string VerdictFail = "fail";
    private const string VerdictAborted = "aborted";

    /// <summary>Литерал версии схемы. Обязан совпадать с <see cref="SchemaVersion"/>: PRAGMA не принимает параметр.</summary>
    private const string SetSchemaVersionSql = "PRAGMA user_version = 2;";

    /// <summary>
    /// Профили изделий и настройки рабочего места.
    /// </summary>
    /// <remarks>
    /// 🔴 Один и тот же текст используется и при создании базы с нуля, и в
    /// миграции v1→v2. Два пути создания одной таблицы неизбежно расходятся, и
    /// расхождение обнаруживается только на машине, которая обновлялась, — то
    /// есть в цеху, а не у разработчика.
    /// </remarks>
    private const string ProfileSchemaSql = """
        -- Профиль изделия: постоянная часть технологической карты. Эталон хранится
        -- содержимым, а не путём: файл на сетевом ресурсе исчезает ровно тогда,
        -- когда по нему разбирают рекламацию.
        --
        -- Ожидаемый состав компасов здесь НЕ хранится: он полностью выводится из
        -- ReferenceText, и вторая копия той же истины со временем разойдётся с первой.
        CREATE TABLE IF NOT EXISTS Profile (
            Id                    INTEGER PRIMARY KEY,
            Name                  TEXT    NOT NULL,
            NameNormalized        TEXT    NOT NULL UNIQUE,
            Description           TEXT    NOT NULL DEFAULT '',
            ReferenceFileName     TEXT    NOT NULL,
            ReferenceFormat       TEXT    NOT NULL,
            ReferenceText         TEXT    NOT NULL,
            ReferenceHash         TEXT    NOT NULL,
            ParamCount            INTEGER NOT NULL,
            HeadingVsJigDeg       REAL    NOT NULL,
            InterCompassSpreadDeg REAL    NOT NULL,
            TransferMotorComp     INTEGER NOT NULL DEFAULT 1,
            CreatedBy             TEXT    NOT NULL,
            CreatedUtc            TEXT    NOT NULL,
            RetiredUtc            TEXT
        );

        -- Настройки рабочего места: азимут стапеля, оператор, последний профиль.
        -- Ключ-значение, потому что набор растёт, а схему ради каждой галки
        -- мигрировать нельзя — миграция требует резервной копии всей базы.
        CREATE TABLE IF NOT EXISTS Setting (
            Name         TEXT PRIMARY KEY,
            SettingValue TEXT NOT NULL,
            UpdatedUtc   TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_Profile_Active ON Profile(NameNormalized) WHERE RetiredUtc IS NULL;
        """;

    /// <summary>
    /// Полная схема текущей версии. Профили создаются первыми: на них ссылается
    /// столбец <c>Run.ProfileId</c>.
    /// </summary>
    private const string CreateSchemaSql = ProfileSchemaSql + "\n" + CoreSchemaSql;

    private const string CoreSchemaSql = """
        -- Изделие: борт так, как его называет предприятие. Ключ — нормализованный
        -- идентификатор, поэтому история по борту не рассыпается на регистр и пробелы.
        CREATE TABLE IF NOT EXISTS Unit (
            Id            INTEGER PRIMARY KEY,
            UnitId        TEXT    NOT NULL UNIQUE,
            UnitIdDisplay TEXT    NOT NULL,
            FirstSeenUtc  TEXT    NOT NULL,
            LastSeenUtc   TEXT    NOT NULL,
            RunCount      INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Run (
            Id               INTEGER PRIMARY KEY,
            UnitRowId        INTEGER NOT NULL REFERENCES Unit(Id) ON DELETE RESTRICT,
            UnitIdAtRun      TEXT    NOT NULL,
            UnitIdRawAtRun   TEXT    NOT NULL,
            Operator         TEXT    NOT NULL,
            PortName         TEXT    NOT NULL,
            JigAzimuthDeg    REAL    NOT NULL,
            ReferencePath    TEXT    NOT NULL,
            ReferenceHash    TEXT    NOT NULL,
            ReferenceFormat  TEXT    NOT NULL,
            AppVersion       TEXT    NOT NULL,
            StartedUtc       TEXT    NOT NULL,
            EndedUtc         TEXT,
            Verdict          TEXT    CHECK (Verdict IS NULL OR Verdict IN ('pass','fail','aborted')),
            VerdictReason    TEXT,
            FailedStage      TEXT,
            FirmwareBanner   TEXT,
            WriteCount       INTEGER NOT NULL DEFAULT 0,
            CheckCount       INTEGER NOT NULL DEFAULT 0,
            FailedCheckCount INTEGER NOT NULL DEFAULT 0,

            -- Профиль, по которому сдавали. NULL законен и означает ровно одно:
            -- прогон сделан до введения профилей (схема v1). Имя дублируется
            -- снимком по той же причине, что и UnitIdAtRun: профиль могли
            -- переименовать, а протокол обязан читаться так, как его подписывали.
            ProfileId        INTEGER REFERENCES Profile(Id) ON DELETE RESTRICT,
            ProfileNameAtRun TEXT
        );

        -- Аудит записи параметров. ON DELETE RESTRICT намеренно: это единственное
        -- свидетельство того, что борт вообще меняли, и удаление прогона не должно
        -- уносить его за собой.
        CREATE TABLE IF NOT EXISTS RunWrite (
            Id             INTEGER PRIMARY KEY,
            RunId          INTEGER NOT NULL REFERENCES Run(Id) ON DELETE RESTRICT,
            Name           TEXT    NOT NULL,
            BeforeValue    REAL,
            RequestedValue REAL    NOT NULL,
            ReadBackValue  REAL,
            Outcome        TEXT    NOT NULL,
            AtUtc          TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS RunCheck (
            Id         INTEGER PRIMARY KEY,
            RunId      INTEGER NOT NULL REFERENCES Run(Id) ON DELETE CASCADE,
            CheckId    TEXT    NOT NULL,
            Title      TEXT    NOT NULL,
            Outcome    TEXT    NOT NULL,
            Detail     TEXT    NOT NULL,
            Measured   REAL,
            LimitValue REAL,
            AtUtc      TEXT    NOT NULL
        );

        -- STATUSTEXT дословно. Severity хранится и числом (сортировка по тяжести),
        -- и именем (читаемость ad-hoc запроса и выгрузки).
        CREATE TABLE IF NOT EXISTS RunMessage (
            Id           INTEGER PRIMARY KEY,
            RunId        INTEGER NOT NULL REFERENCES Run(Id) ON DELETE CASCADE,
            Severity     INTEGER NOT NULL,
            SeverityName TEXT    NOT NULL,
            Text         TEXT    NOT NULL,
            ReceivedUtc  TEXT    NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_Run_Unit_Started ON Run(UnitRowId, StartedUtc DESC);
        CREATE INDEX IF NOT EXISTS IX_Run_Started      ON Run(StartedUtc DESC, Id DESC);
        CREATE INDEX IF NOT EXISTS IX_Run_UnitId_Start ON Run(UnitIdAtRun, StartedUtc DESC);
        CREATE INDEX IF NOT EXISTS IX_Run_Open         ON Run(Id) WHERE EndedUtc IS NULL;
        CREATE INDEX IF NOT EXISTS IX_RunWrite_Run     ON RunWrite(RunId, AtUtc);
        CREATE INDEX IF NOT EXISTS IX_RunCheck_Run     ON RunCheck(RunId, Id);
        CREATE INDEX IF NOT EXISTS IX_RunMessage_Run   ON RunMessage(RunId, ReceivedUtc);
        CREATE INDEX IF NOT EXISTS IX_Unit_LastSeen    ON Unit(LastSeenUtc DESC);
        CREATE INDEX IF NOT EXISTS IX_Run_Profile      ON Run(ProfileId, StartedUtc DESC);
        """;

    private readonly AppPaths _paths;
    private readonly string _appVersion;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Счётчик открытых прогонов этого процесса. Именно он, а не запрос к базе,
    // отвечает на HasOpenRun: свойство дёргают из пути обновления в момент
    // решения о перезапуске.
    private int _openRunCount;

    // Состояние неизвестно, пока не отработала InitializeAsync, и снова
    // становится неизвестным, если фиксация транзакции завершилась неоднозначно.
    private volatile bool _stateUncertain = true;

    private bool _disposed;

    /// <param name="paths">Единственный источник путей — см. <see cref="AppPaths"/>.</param>
    /// <param name="appVersion">
    /// Версия работающей сборки. Нужна не для отчёта: разбор брошенных прогонов
    /// сравнивает её с версией, записанной в прогон, и по расхождению честно
    /// называет причину обрыва обновлением приложения.
    /// </param>
    public SqliteCalibrationStore(AppPaths paths, string appVersion)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _appVersion = string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion.Trim();

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
            ForeignKeys = true,
        }.ToString();
    }

    /// <summary>Путь к файлу базы — для окна диагностики и передачи файла разработчику.</summary>
    public string DatabaseFilePath => _paths.DatabaseFilePath;

    /// <inheritdoc />
    /// <remarks>
    /// Дешёвое, неблокирующее и не бросающее свойство: только чтение поля в
    /// памяти. При неизвестном состоянии возвращает <c>true</c> — отложить
    /// обновление не стоит ничего, а прерванный замер стоит детали и смены.
    /// </remarks>
    public bool HasOpenRun => _stateUncertain || Volatile.Read(ref _openRunCount) > 0;

    /// <summary>
    /// Нормализация идентификатора борта: обрезка, схлопывание внутренних
    /// пробелов, верхний регистр по инвариантной культуре и приведение к NFC.
    /// </summary>
    /// <remarks>
    /// 🔴 Именно <c>ToUpperInvariant</c>: турецкая раскладка отображает <c>i</c>
    /// в <c>İ</c>, и <c>UAV-i7</c> тихо распадается на два разных борта, а
    /// история по борту начинает возвращать подмножество прогонов.
    /// </remarks>
    public static string NormalizeUnitId(string? raw) => NormalizeName(raw);

    /// <summary>
    /// Общая нормализация человеко-введённых имён: борта и профиля.
    /// </summary>
    /// <remarks>
    /// Имя профиля нормализуется по тем же правилам и по той же причине, что и
    /// идентификатор борта: иначе «Гриф-2» и «Гриф-2 » заводятся как два разных
    /// профиля, и половина плат уезжает сданной по профилю-двойнику.
    /// </remarks>
    public static string NormalizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(raw.Length);
        var pendingSpace = false;
        foreach (var ch in raw.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Создаёт каталоги и схему, проверяет версию схемы и при необходимости
    /// мигрирует — после обязательной резервной копии.
    /// </remarks>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _paths.EnsureCreated();

            int version;
            await using (var probe = await OpenConnectionAsync(ct).ConfigureAwait(false))
            {
                // journal_mode=WAL сохраняется в самом файле, поэтому достаточно
                // выставить один раз.
                //
                // Что даёт WAL: зафиксированная транзакция переживает убийство
                // процесса и перезапуск — то есть ровно то, что делает с
                // приложением применение обновления Velopack. Чего WAL не даёт:
                // защиты от сбоя ОС и пропадания питания при synchronous=NORMAL
                // (последние коммиты могут не дойти до диска), защиты от порчи
                // файловой системы и от копирования живой базы без её спутников
                // -wal/-shm — такая копия окажется без самых свежих коммитов.
                // И WAL не работает по SMB/UNC: там нужна разделяемая память.
                await ExecuteAsync(probe, null, "PRAGMA journal_mode = WAL;", ct).ConfigureAwait(false);
                version = await ReadSchemaVersionAsync(probe, ct).ConfigureAwait(false);
            }

            if (version > SchemaVersion)
            {
                // Откатывать схему назад нельзя: «по возможности» здесь означает
                // потерю столбцов и смысла. Отказываемся, пока файл не тронут.
                throw new CalibrationStoreException(
                    $"Хранилище «{_paths.DatabaseFilePath}» создано более новой версией приложения " +
                    $"(схема {version}, эта сборка понимает {SchemaVersion}). Установите новую версию " +
                    "приложения или восстановите резервную копию, снятую до неё. " +
                    "Открывать и мигрировать такую базу назад недопустимо — данные будут испорчены.");
            }

            if (version > 0 && version < SchemaVersion)
            {
                // Копия снимается до первой миграции и остаётся, даже если
                // миграция удалась: это единственный путь отката вниз по версии.
                await BackupBeforeMigrationAsync(version, ct).ConfigureAwait(false);
            }

            await using (var connection = await OpenConnectionAsync(ct).ConfigureAwait(false))
            {
                if (version == 0)
                {
                    // Отсутствующая база — это первый запуск, а не ошибка: она
                    // создаётся сразу на текущей версии схемы, без проигрывания
                    // исторических миграций.
                    await ExecuteAsync(connection, null, CreateSchemaSql, ct).ConfigureAwait(false);
                    await ExecuteAsync(connection, null, SetSchemaVersionSql, ct).ConfigureAwait(false);
                }
                else
                {
                    for (var from = version; from < SchemaVersion; from++)
                    {
                        await ApplyMigrationAsync(connection, from, ct).ConfigureAwait(false);
                    }
                }

                // Целостность проверяется на старте, а не когда-нибудь потом:
                // нарушенные внешние ключи означают, что базу правили снаружи.
                await VerifyIntegrityAsync(connection, ct).ConfigureAwait(false);
            }

            _stateUncertain = false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Строка прогона вставляется немедленно и одной транзакцией вместе с
    /// созданием либо обновлением борта. Прогон считается открытым, пока
    /// <c>EndedUtc</c> равен <c>NULL</c>.
    /// </remarks>
    public async Task<long> BeginRunAsync(
        CalibrationRequest request,
        string referenceHash,
        string appVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        var normalizedUnit = NormalizeUnitId(request.UnitId);
        if (normalizedUnit.Length == 0)
        {
            throw new ArgumentException(
                "Идентификатор борта пуст: прогон невозможно привязать к изделию.", nameof(request));
        }

        var rawUnit = request.UnitId?.Trim() ?? string.Empty;
        var nowUtc = FormatUtc(DateTimeOffset.UtcNow);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                // Счётчик прогонов растёт уже здесь: прерванный прогон тоже
                // остаётся в истории борта и обязан быть в ней виден.
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    INSERT INTO Unit (UnitId, UnitIdDisplay, FirstSeenUtc, LastSeenUtc, RunCount)
                    VALUES ($unit, $display, $now, $now, 1)
                    ON CONFLICT(UnitId) DO UPDATE SET
                        LastSeenUtc = excluded.LastSeenUtc,
                        RunCount    = Unit.RunCount + 1;
                    """,
                    ct,
                    ("$unit", normalizedUnit),
                    ("$display", rawUnit.Length == 0 ? normalizedUnit : rawUnit),
                    ("$now", nowUtc)).ConfigureAwait(false);

                var unitRowId = Convert.ToInt64(
                    await ScalarAsync(
                        connection,
                        transaction,
                        "SELECT Id FROM Unit WHERE UnitId = $unit;",
                        ct,
                        ("$unit", normalizedUnit)).ConfigureAwait(false),
                    CultureInfo.InvariantCulture);

                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    INSERT INTO Run (
                        UnitRowId, UnitIdAtRun, UnitIdRawAtRun, Operator, PortName, JigAzimuthDeg,
                        ReferencePath, ReferenceHash, ReferenceFormat, AppVersion, StartedUtc)
                    VALUES (
                        $unitRowId, $unit, $rawUnit, $operator, $port, $azimuth,
                        $refPath, $refHash, $refFormat, $appVersion, $started);
                    """,
                    ct,
                    ("$unitRowId", unitRowId),
                    ("$unit", normalizedUnit),
                    ("$rawUnit", rawUnit),
                    ("$operator", request.Operator ?? string.Empty),
                    ("$port", request.PortName ?? string.Empty),
                    ("$azimuth", request.JigAzimuthDeg),
                    ("$refPath", request.ReferenceFilePath ?? string.Empty),
                    ("$refHash", referenceHash ?? string.Empty),
                    ("$refFormat", DetectReferenceFormat(request.ReferenceFilePath)),
                    ("$appVersion", string.IsNullOrWhiteSpace(appVersion) ? _appVersion : appVersion.Trim()),
                    ("$started", nowUtc)).ConfigureAwait(false);

                var runId = Convert.ToInt64(
                    await ScalarAsync(connection, transaction, "SELECT last_insert_rowid();", ct).ConfigureAwait(false),
                    CultureInfo.InvariantCulture);

                try
                {
                    await CommitAsync(transaction, ct).ConfigureAwait(false);
                }
                catch
                {
                    // Фиксация не подтвердилась: открыт прогон или нет — неизвестно.
                    // Считаем стенд занятым, пока следующий старт не разберёт
                    // брошенные прогоны. Отложенное обновление не стоит ничего,
                    // прерванный замер стоит детали и смены.
                    _stateUncertain = true;
                    throw;
                }

                Interlocked.Increment(ref _openRunCount);
                return runId;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Одиночный INSERT — сам себе транзакция: строка на диске до возврата
    /// управления. Записи аудита обязаны опережать возможный перезапуск, потому
    /// что после него они — единственное свидетельство того, что борт меняли.
    /// </remarks>
    public Task RecordWriteAsync(long runId, ParamWriteRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ThrowIfDisposed();

        return RunGuardedAsync(
            """
            INSERT INTO RunWrite (RunId, Name, BeforeValue, RequestedValue, ReadBackValue, Outcome, AtUtc)
            VALUES ($runId, $name, $before, $requested, $readBack, $outcome, $at);
            """,
            ct,
            ("$runId", runId),
            ("$name", record.Name ?? string.Empty),
            ("$before", record.Before),
            ("$requested", record.Requested),
            ("$readBack", record.ReadBack),
            ("$outcome", record.Outcome.ToString()),
            ("$at", FormatUtc(record.AtUtc)));
    }

    /// <inheritdoc />
    public Task RecordCheckAsync(long runId, CheckResult result, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ThrowIfDisposed();

        return RunGuardedAsync(
            """
            INSERT INTO RunCheck (RunId, CheckId, Title, Outcome, Detail, Measured, LimitValue, AtUtc)
            VALUES ($runId, $checkId, $title, $outcome, $detail, $measured, $limit, $at);
            """,
            ct,
            ("$runId", runId),
            ("$checkId", result.Id ?? string.Empty),
            ("$title", result.Title ?? string.Empty),
            ("$outcome", result.Outcome.ToString()),
            ("$detail", result.Detail ?? string.Empty),
            ("$measured", result.Measured),
            ("$limit", result.Limit),
            ("$at", FormatUtc(DateTimeOffset.UtcNow)));
    }

    /// <inheritdoc />
    public Task RecordMessageAsync(long runId, StatusTextEvent message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ThrowIfDisposed();

        return RunGuardedAsync(
            """
            INSERT INTO RunMessage (RunId, Severity, SeverityName, Text, ReceivedUtc)
            VALUES ($runId, $severity, $severityName, $text, $at);
            """,
            ct,
            ("$runId", runId),
            ("$severity", (long)message.Severity),
            ("$severityName", message.Severity.ToString()),
            ("$text", message.Text ?? string.Empty),
            ("$at", FormatUtc(message.ReceivedUtc)));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Единственное место, где проставляется вердикт. Счётчики берутся из
    /// итогового отчёта прогона — это его полный учёт; у прерванного прогона
    /// отчёта нет, и там счётчики считаются по фактически записанным строкам.
    /// </remarks>
    public async Task CompleteRunAsync(long runId, CalibrationRunResult result, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ThrowIfDisposed();

        var failedChecks = result.Checks?.Count(c => c.Outcome == CheckOutcome.Fail) ?? 0;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            int affected;
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                affected = await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    UPDATE Run SET
                        EndedUtc         = $ended,
                        Verdict          = $verdict,
                        VerdictReason    = $reason,
                        FailedStage      = $stage,
                        FirmwareBanner   = $banner,
                        WriteCount       = $writes,
                        CheckCount       = $checks,
                        FailedCheckCount = $failedChecks
                    WHERE Id = $runId AND EndedUtc IS NULL;
                    """,
                    ct,
                    ("$ended", FormatUtc(result.EndedUtc)),
                    ("$verdict", result.Passed ? VerdictPass : VerdictFail),
                    ("$reason", result.FailureReason),
                    ("$stage", result.FailedStage?.ToString()),
                    ("$banner", result.FirmwareBanner),
                    ("$writes", (long)(result.Writes?.Count ?? 0)),
                    ("$checks", (long)(result.Checks?.Count ?? 0)),
                    ("$failedChecks", (long)failedChecks),
                    ("$runId", runId)).ConfigureAwait(false);

                if (affected == 0)
                {
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    throw new CalibrationStoreException(
                        $"Прогон {runId} не найден или уже закрыт: вердикт не записан. " +
                        "Повторное закрытие прогона запрещено — запись прогона неизменяема.");
                }

                await CommitAsync(transaction, ct).ConfigureAwait(false);
            }

            if (Volatile.Read(ref _openRunCount) > 0)
            {
                Interlocked.Decrement(ref _openRunCount);
            }

            // Контрольная точка на естественной границе: скопированный после неё
            // файл .db близок к полному, а не отстаёт на содержимое WAL.
            await ExecuteAsync(connection, null, "PRAGMA wal_checkpoint(TRUNCATE);", ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Вызывается на старте, до того как UI привяжется к данным. Любой прогон с
    /// пустым <c>EndedUtc</c> принадлежит сессии, которой больше нет, и
    /// закрывается как <b>прерванный</b>.
    /// </para>
    /// <para>
    /// 🔴 Прерванный прогон отличим в данных и от пройденного, и от
    /// проваленного: <c>Verdict = 'aborted'</c>. Он никогда не удаляется, не
    /// прячется из истории и не показывается как завершённый. Записи параметров
    /// и проверки, сделанные до обрыва, сохраняются: они — единственное
    /// свидетельство того, что уже сделано с бортом.
    /// </para>
    /// <para>
    /// Причина обрыва называется честно: если версия приложения в прогоне не
    /// совпадает с текущей, процесс был заменён обновлением — <b>применение
    /// обновления Velopack подменяет каталог приложения и перезапускает
    /// процесс</b>, и это самая частая причина. Совпадает — приложение просто
    /// закрыли или оно упало.
    /// </para>
    /// <para>
    /// Прогон никогда не возобновляется: после перезапуска нет ни связи, ни
    /// живого состояния борта, и приписывать новые наблюдения старой привязке
    /// нельзя.
    /// </para>
    /// </remarks>
    public async Task<int> SweepAbandonedRunsAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);

            var abandoned = new List<(long RunId, string AppVersion)>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, AppVersion FROM Run WHERE EndedUtc IS NULL ORDER BY Id;";
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    abandoned.Add((reader.GetInt64(0), reader.GetString(1)));
                }
            }

            if (abandoned.Count == 0)
            {
                return 0;
            }

            var closed = 0;
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                foreach (var (runId, runAppVersion) in abandoned)
                {
                    var reason = string.Equals(runAppVersion, _appVersion, StringComparison.Ordinal)
                        ? "Прогон прерван: процесс приложения завершился до вердикта. " +
                          "Данные, записанные до обрыва, сохранены; прогон не возобновляется."
                        : $"Прогон прерван обновлением приложения: версия {runAppVersion} сменилась на {_appVersion}. " +
                          "Применение обновления подменяет каталог приложения и перезапускает процесс. " +
                          "Данные, записанные до обрыва, сохранены; прогон не возобновляется.";

                    // Конец прогона — время последнего реального свидетельства, а
                    // не момент, когда разбор его заметил. Сравнение MAX по тексту
                    // корректно: метки фиксированной ширины в UTC.
                    closed += await ExecuteAsync(
                        connection,
                        transaction,
                        """
                        UPDATE Run SET
                            EndedUtc = COALESCE((
                                SELECT MAX(t) FROM (
                                    SELECT MAX(AtUtc)       AS t FROM RunWrite   WHERE RunId = $runId
                                    UNION ALL
                                    SELECT MAX(AtUtc)       AS t FROM RunCheck   WHERE RunId = $runId
                                    UNION ALL
                                    SELECT MAX(ReceivedUtc) AS t FROM RunMessage WHERE RunId = $runId)), StartedUtc),
                            Verdict          = $verdict,
                            VerdictReason    = $reason,
                            WriteCount       = (SELECT COUNT(*) FROM RunWrite WHERE RunId = $runId),
                            CheckCount       = (SELECT COUNT(*) FROM RunCheck WHERE RunId = $runId),
                            FailedCheckCount = (SELECT COUNT(*) FROM RunCheck WHERE RunId = $runId AND Outcome = 'Fail')
                        WHERE Id = $runId AND EndedUtc IS NULL;
                        """,
                        ct,
                        ("$runId", runId),
                        ("$verdict", VerdictAborted),
                        ("$reason", reason)).ConfigureAwait(false);
                }

                await CommitAsync(transaction, ct).ConfigureAwait(false);
            }

            return closed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Свежие сверху. Прерванный прогон нельзя спутать с пройденным или
    /// проваленным: <see cref="RunSummary.Passed"/> у него <c>null</c>, а в
    /// <see cref="RunSummary.FailedStage"/> вместо стадии стоит маркер
    /// <c>aborted: …</c> с причиной; у ещё идущего прогона — <c>in-progress</c>.
    /// Стадии процедуры пишутся с заглавной буквы, поэтому маркеры с ними не
    /// пересекаются.
    /// </remarks>
    public async Task<IReadOnlyList<RunSummary>> ListRunsAsync(
        string? unitIdFilter = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var normalizedFilter = NormalizeUnitId(unitIdFilter);
        var effectiveLimit = Math.Clamp(limit, 1, 10_000);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, UnitIdAtRun, StartedUtc, Operator, ReferencePath, ReferenceHash,
                       Verdict, VerdictReason, FailedStage, FailedCheckCount
                FROM Run
                WHERE ($unit IS NULL OR UnitIdAtRun = $unit)
                ORDER BY StartedUtc DESC, Id DESC
                LIMIT $limit;
                """;
            AddParameter(command, "$unit", normalizedFilter.Length == 0 ? null : normalizedFilter);
            AddParameter(command, "$limit", (long)effectiveLimit);

            var rows = new List<RunSummary>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var verdict = reader.IsDBNull(6) ? null : reader.GetString(6);
                var reason = reader.IsDBNull(7) ? null : reader.GetString(7);
                var failedStage = reader.IsDBNull(8) ? null : reader.GetString(8);

                rows.Add(new RunSummary(
                    RunId: reader.GetInt64(0),
                    UnitId: reader.GetString(1),
                    StartedUtc: ParseUtc(reader.GetString(2)),
                    Operator: reader.GetString(3),
                    ReferenceFile: reader.GetString(4),
                    ReferenceHash: reader.GetString(5),
                    Passed: verdict switch
                    {
                        VerdictPass => true,
                        VerdictFail => false,
                        _ => null,
                    },
                    FailedStage: verdict switch
                    {
                        VerdictAborted => string.IsNullOrWhiteSpace(reason) ? "aborted" : "aborted: " + reason,
                        null => "in-progress",
                        _ => failedStage,
                    },
                    FailedCheckCount: reader.GetInt32(9)));
            }

            return rows;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ==================================================================
    // Профили изделий
    // ==================================================================

    /// <summary>Верхняя граница допусков процедуры, градусы.</summary>
    /// <remarks>
    /// Выше этого сравнивать нечего: прошивка сама объявляет компасы
    /// несогласованными при расхождении 60° по горизонтали и 90° по всем осям,
    /// поэтому допуск шире 90° не отбраковал бы ничего и означал бы отключённую
    /// проверку. Отключать проверку следует явно, а не подсовывая ей допуск,
    /// который нельзя нарушить.
    /// </remarks>
    private const double MaxToleranceDeg = 90.0;

    private const string ProfileColumnsSql = """
        SELECT p.Id, p.Name, p.Description, p.ReferenceFileName, p.ReferenceFormat, p.ReferenceText,
               p.ReferenceHash, p.ParamCount, p.HeadingVsJigDeg, p.InterCompassSpreadDeg,
               p.TransferMotorComp, p.CreatedBy, p.CreatedUtc, p.RetiredUtc,
               (SELECT COUNT(*) FROM Run r WHERE r.ProfileId = p.Id) AS RunCount
        FROM Profile p
        """;

    /// <summary>
    /// Профили в порядке имени; выведенные из обращения — в конце.
    /// </summary>
    /// <param name="includeRetired">
    /// Включать выведенные из обращения. Для выбора профиля перед прогоном —
    /// <c>false</c>; для раздела настроек и для чтения истории — <c>true</c>.
    /// </param>
    public async Task<IReadOnlyList<CalibrationProfile>> ListProfilesAsync(
        bool includeRetired = false,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = ProfileColumnsSql + """

                WHERE $includeRetired = 1 OR p.RetiredUtc IS NULL
                ORDER BY p.RetiredUtc IS NOT NULL, p.NameNormalized;
                """;
            AddParameter(command, "$includeRetired", includeRetired ? 1L : 0L);

            var rows = new List<CalibrationProfile>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(ReadProfile(reader));
            }

            return rows;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Профиль по ключу. <c>null</c>, если такого нет.</summary>
    public async Task<CalibrationProfile?> GetProfileAsync(long profileId, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = ProfileColumnsSql + """

                WHERE p.Id = $id;
                """;
            AddParameter(command, "$id", profileId);

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadProfile(reader) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Заводит профиль и возвращает его ключ.
    /// </summary>
    /// <exception cref="ArgumentException">Заготовка не проходит проверку.</exception>
    /// <exception cref="CalibrationStoreException">Профиль с таким именем уже есть.</exception>
    public async Task<long> CreateProfileAsync(NewCalibrationProfile draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ThrowIfDisposed();

        var name = draft.Name?.Trim() ?? string.Empty;
        var normalized = NormalizeName(name);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Имя профиля пусто.", nameof(draft));
        }

        if (draft.Reference is null || string.IsNullOrWhiteSpace(draft.Reference.Text))
        {
            throw new ArgumentException(
                "Профиль без содержимого эталона не имеет смысла: переносить будет нечего.", nameof(draft));
        }

        ValidateTolerance(draft.HeadingVsJigDeg, "допуск курса против азимута стапеля", nameof(draft));
        ValidateTolerance(draft.InterCompassSpreadDeg, "допуск расхождения курсов между компасами", nameof(draft));

        var nowUtc = FormatUtc(DateTimeOffset.UtcNow);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await ThrowIfNameTakenAsync(connection, transaction, normalized, excludeId: null, name, ct)
                    .ConfigureAwait(false);

                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    INSERT INTO Profile (
                        Name, NameNormalized, Description, ReferenceFileName, ReferenceFormat,
                        ReferenceText, ReferenceHash, ParamCount, HeadingVsJigDeg,
                        InterCompassSpreadDeg, TransferMotorComp, CreatedBy, CreatedUtc)
                    VALUES (
                        $name, $normalized, $description, $fileName, $format,
                        $text, $hash, $paramCount, $heading,
                        $spread, $motorComp, $createdBy, $created);
                    """,
                    ct,
                    ("$name", name),
                    ("$normalized", normalized),
                    ("$description", draft.Description?.Trim() ?? string.Empty),
                    ("$fileName", draft.Reference.FileName ?? string.Empty),
                    ("$format", draft.Reference.Format ?? string.Empty),
                    ("$text", draft.Reference.Text),
                    ("$hash", draft.Reference.Hash ?? string.Empty),
                    ("$paramCount", (long)draft.Reference.ParamCount),
                    ("$heading", draft.HeadingVsJigDeg),
                    ("$spread", draft.InterCompassSpreadDeg),
                    ("$motorComp", draft.TransferMotorComp ? 1L : 0L),
                    ("$createdBy", draft.CreatedBy?.Trim() ?? string.Empty),
                    ("$created", nowUtc)).ConfigureAwait(false);

                var profileId = Convert.ToInt64(
                    await ScalarAsync(connection, transaction, "SELECT last_insert_rowid();", ct).ConfigureAwait(false),
                    CultureInfo.InvariantCulture);

                await CommitAsync(transaction, ct).ConfigureAwait(false);
                return profileId;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Правит профиль.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Эталон неизменяем — ни при каких условиях. Подменённый эталон делает
    /// ложными все прогоны, уже сданные по этому профилю: реестр утверждал бы,
    /// что плату сдавали по набору, которого в тот момент не существовало.
    /// Другой эталон — это другой профиль.
    /// </para>
    /// <para>
    /// Допуски неизменяемы с первого прогона по той же причине. Имя и описание
    /// править можно всегда: это подпись, а не суть, и в прогоне уже лежит
    /// снимок имени на момент сдачи.
    /// </para>
    /// </remarks>
    /// <exception cref="CalibrationStoreException">
    /// Профиля нет, имя занято либо предпринята попытка изменить допуски
    /// профиля, по которому уже сдавали платы.
    /// </exception>
    public async Task UpdateProfileAsync(
        long profileId,
        string name,
        string description,
        double headingVsJigDeg,
        double interCompassSpreadDeg,
        bool transferMotorComp,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var trimmedName = name?.Trim() ?? string.Empty;
        var normalized = NormalizeName(trimmedName);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Имя профиля пусто.", nameof(name));
        }

        ValidateTolerance(headingVsJigDeg, "допуск курса против азимута стапеля", nameof(headingVsJigDeg));
        ValidateTolerance(interCompassSpreadDeg, "допуск расхождения курсов между компасами", nameof(interCompassSpreadDeg));

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var current = await ReadProfileCoreAsync(connection, transaction, profileId, ct).ConfigureAwait(false)
                    ?? throw new CalibrationStoreException(
                        $"Профиль {profileId} не найден: правка отменена.");

                var tolerancesChanged =
                    !NearlyEqual(current.HeadingVsJigDeg, headingVsJigDeg)
                    || !NearlyEqual(current.InterCompassSpreadDeg, interCompassSpreadDeg)
                    || current.TransferMotorComp != transferMotorComp;

                if (tolerancesChanged && current.RunCount > 0)
                {
                    throw new CalibrationStoreException(
                        $"По профилю «{current.Name}» уже сдано прогонов: {current.RunCount}. " +
                        "Допуски и правило переноса COMPASS_MOT* менять нельзя — прогоны в реестре " +
                        "выполнялись по прежним значениям, и правка сделала бы реестр ложным. " +
                        "Заведите новый профиль.");
                }

                await ThrowIfNameTakenAsync(connection, transaction, normalized, profileId, trimmedName, ct)
                    .ConfigureAwait(false);

                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    UPDATE Profile SET
                        Name                  = $name,
                        NameNormalized        = $normalized,
                        Description           = $description,
                        HeadingVsJigDeg       = $heading,
                        InterCompassSpreadDeg = $spread,
                        TransferMotorComp     = $motorComp
                    WHERE Id = $id;
                    """,
                    ct,
                    ("$name", trimmedName),
                    ("$normalized", normalized),
                    ("$description", description?.Trim() ?? string.Empty),
                    ("$heading", headingVsJigDeg),
                    ("$spread", interCompassSpreadDeg),
                    ("$motorComp", transferMotorComp ? 1L : 0L),
                    ("$id", profileId)).ConfigureAwait(false);

                await CommitAsync(transaction, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Выводит профиль из обращения либо возвращает его в обращение.
    /// </summary>
    /// <remarks>
    /// Удаления профиля нет и не будет: <c>Run.ProfileId</c> ссылается на него с
    /// <c>ON DELETE RESTRICT</c>, потому что профиль — часть доказательства того,
    /// по чему сдавали плату. Выведенный из обращения профиль не предлагается
    /// для новых прогонов, но остаётся читаемым в истории.
    /// </remarks>
    public async Task SetProfileRetiredAsync(long profileId, bool retired, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var affected = await RunGuardedNonQueryAsync(
            """
            UPDATE Profile SET RetiredUtc = $retiredUtc WHERE Id = $id;
            """,
            ct,
            ("$retiredUtc", retired ? FormatUtc(DateTimeOffset.UtcNow) : null),
            ("$id", profileId)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new CalibrationStoreException($"Профиль {profileId} не найден.");
        }
    }

    // ==================================================================
    // Настройки рабочего места
    // ==================================================================

    private const string SettingJigAzimuth = "workstation.jigAzimuthDeg";
    private const string SettingOperator = "workstation.operator";
    private const string SettingLastProfile = "workstation.lastProfileId";

    /// <summary>
    /// Настройки стенда. Отсутствующий ключ означает «не настроено» и никогда не
    /// подменяется значением по умолчанию: для азимута ноль — законный север.
    /// </summary>
    public async Task<WorkstationSettings> GetWorkstationSettingsAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Name, SettingValue FROM Setting;";

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    values[reader.GetString(0)] = reader.GetString(1);
                }
            }

            return new WorkstationSettings
            {
                // Неразбираемое значение приравнивается к «не настроено»: молча
                // подставить ноль означало бы направить стапель на север.
                JigAzimuthDeg = values.TryGetValue(SettingJigAzimuth, out var azimuthText)
                    && double.TryParse(azimuthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var azimuth)
                    && azimuth is >= 0 and <= 360
                        ? azimuth
                        : null,
                DefaultOperator = values.TryGetValue(SettingOperator, out var op) ? op : string.Empty,
                LastProfileId = values.TryGetValue(SettingLastProfile, out var profileText)
                    && long.TryParse(profileText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lastProfile)
                        ? lastProfile
                        : null,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Сохраняет настройки стенда целиком, одной транзакцией.</summary>
    public async Task SaveWorkstationSettingsAsync(WorkstationSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfDisposed();

        if (settings.JigAzimuthDeg is { } azimuth && (double.IsNaN(azimuth) || azimuth is < 0 or > 360))
        {
            throw new ArgumentException(
                $"Азимут стапеля {azimuth.ToString("G9", CultureInfo.InvariantCulture)} вне диапазона 0…360.",
                nameof(settings));
        }

        var nowUtc = FormatUtc(DateTimeOffset.UtcNow);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await UpsertSettingAsync(
                    connection, transaction, SettingJigAzimuth,
                    settings.JigAzimuthDeg?.ToString("R", CultureInfo.InvariantCulture), nowUtc, ct)
                    .ConfigureAwait(false);

                await UpsertSettingAsync(
                    connection, transaction, SettingOperator,
                    settings.DefaultOperator?.Trim(), nowUtc, ct).ConfigureAwait(false);

                await UpsertSettingAsync(
                    connection, transaction, SettingLastProfile,
                    settings.LastProfileId?.ToString(CultureInfo.InvariantCulture), nowUtc, ct)
                    .ConfigureAwait(false);

                await CommitAsync(transaction, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Пустое значение стирает ключ: «не настроено» хранится отсутствием строки, а не пустой строкой.</summary>
    private static Task UpsertSettingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string? value,
        string nowUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ExecuteAsync(connection, transaction, "DELETE FROM Setting WHERE Name = $name;", ct, ("$name", key));
        }

        return ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO Setting (Name, SettingValue, UpdatedUtc)
            VALUES ($name, $value, $now)
            ON CONFLICT(Name) DO UPDATE SET
                SettingValue = excluded.SettingValue,
                UpdatedUtc   = excluded.UpdatedUtc;
            """,
            ct,
            ("$name", key),
            ("$value", value),
            ("$now", nowUtc));
    }

    // ------------------------------------------------------------------
    // Помощники профилей
    // ------------------------------------------------------------------

    private static CalibrationProfile ReadProfile(SqliteDataReader reader) => new(
        Id: reader.GetInt64(0),
        Name: reader.GetString(1),
        Description: reader.GetString(2),
        ReferenceFileName: reader.GetString(3),
        ReferenceFormat: reader.GetString(4),
        ReferenceText: reader.GetString(5),
        ReferenceHash: reader.GetString(6),
        ParamCount: reader.GetInt32(7),
        HeadingVsJigDeg: reader.GetDouble(8),
        InterCompassSpreadDeg: reader.GetDouble(9),
        TransferMotorComp: reader.GetInt64(10) != 0,
        CreatedBy: reader.GetString(11),
        CreatedUtc: ParseUtc(reader.GetString(12)),
        RetiredUtc: reader.IsDBNull(13) ? null : ParseUtc(reader.GetString(13)),
        RunCount: reader.GetInt32(14));

    /// <summary>Только те поля профиля, которые нужны проверкам правки.</summary>
    private static async Task<(string Name, double HeadingVsJigDeg, double InterCompassSpreadDeg,
        bool TransferMotorComp, int RunCount)?> ReadProfileCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long profileId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT p.Name, p.HeadingVsJigDeg, p.InterCompassSpreadDeg, p.TransferMotorComp,
                   (SELECT COUNT(*) FROM Run r WHERE r.ProfileId = p.Id)
            FROM Profile p
            WHERE p.Id = $id;
            """;
        AddParameter(command, "$id", profileId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return (reader.GetString(0), reader.GetDouble(1), reader.GetDouble(2),
            reader.GetInt64(3) != 0, reader.GetInt32(4));
    }

    /// <summary>
    /// Проверяет имя до вставки. Ограничение <c>UNIQUE</c> в схеме остаётся
    /// последней линией, но сообщение SQLite оператору ничего не объясняет.
    /// </summary>
    private static async Task ThrowIfNameTakenAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string normalized,
        long? excludeId,
        string displayName,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id FROM Profile
            WHERE NameNormalized = $normalized AND ($excludeId IS NULL OR Id <> $excludeId)
            LIMIT 1;
            """;
        AddParameter(command, "$normalized", normalized);
        AddParameter(command, "$excludeId", excludeId);

        var existing = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (existing is not null && existing is not DBNull)
        {
            throw new CalibrationStoreException(
                $"Профиль с именем «{displayName}» уже есть (запись {existing}). " +
                "Имена профилей сравниваются без учёта регистра и лишних пробелов: " +
                "два профиля-двойника развели бы историю сдачи по разным записям.");
        }
    }

    private static void ValidateTolerance(double value, string what, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > MaxToleranceDeg)
        {
            throw new ArgumentException(
                $"Значение «{what}» должно быть в диапазоне (0; {MaxToleranceDeg.ToString("F0", CultureInfo.InvariantCulture)}] градусов, " +
                $"получено {value.ToString("G9", CultureInfo.InvariantCulture)}.",
                parameterName);
        }
    }

    /// <summary>Сравнение допусков в решётке binary32 — в ней они и хранятся у борта.</summary>
    private static bool NearlyEqual(double a, double b) => (float)a == (float)b;

    private async Task<int> RunGuardedNonQueryAsync(
        string sql,
        CancellationToken ct,
        params (string Name, object? Value)[] args)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            return await ExecuteAsync(connection, null, sql, ct, args).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private async Task RunGuardedAsync(
        string sql,
        CancellationToken ct,
        params (string Name, object? Value)[] args)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await ExecuteAsync(connection, null, sql, ct, args).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);

            // foreign_keys в SQLite выключен по умолчанию и настраивается на
            // каждое соединение — без него ON DELETE RESTRICT не действует.
            await ExecuteAsync(connection, null, "PRAGMA foreign_keys = ON;", ct).ConfigureAwait(false);
            await ExecuteAsync(connection, null, "PRAGMA busy_timeout = 5000;", ct).ConfigureAwait(false);
            await ExecuteAsync(connection, null, "PRAGMA synchronous = NORMAL;", ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task CommitAsync(SqliteTransaction transaction, CancellationToken ct)
    {
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task<int> ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken ct,
        params (string Name, object? Value)[] args)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in args)
        {
            AddParameter(command, name, value);
        }

        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<object?> ScalarAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken ct,
        params (string Name, object? Value)[] args)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in args)
        {
            AddParameter(command, name, value);
        }

        var scalar = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (scalar is null || scalar is DBNull)
        {
            throw new CalibrationStoreException($"Запрос не вернул значения: {sql}");
        }

        return scalar;
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static async Task<int> ReadSchemaVersionAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private async Task BackupBeforeMigrationAsync(int fromVersion, CancellationToken ct)
    {
        var backupPath = _paths.NewBackupFilePath(_appVersion, fromVersion, DateTimeOffset.UtcNow);
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);

        // VACUUM INTO даёт согласованный файл целиком, в отличие от простого
        // копирования: копия живой базы с непроведённым WAL теряет самые свежие
        // коммиты. VACUUM нельзя выполнять внутри транзакции.
        await ExecuteAsync(connection, null, "VACUUM INTO $path;", ct, ("$path", backupPath))
            .ConfigureAwait(false);

        if (!File.Exists(backupPath))
        {
            throw new CalibrationStoreException(
                $"Резервная копия «{backupPath}» не создана — миграция схемы отменена. " +
                "Непроверенная копия копией не считается.");
        }
    }

    private static async Task ApplyMigrationAsync(SqliteConnection connection, int fromVersion, CancellationToken ct)
    {
        // Миграции упорядочены, только вперёд, каждая в своей транзакции и
        // завершается собственным литералом PRAGMA user_version. Они вправе
        // добавлять таблицы, столбцы и индексы, но не переписывать строки
        // RunWrite, RunCheck и RunMessage — это доказательная база.
        var sql = fromVersion switch
        {
            1 => MigrateV1ToV2Sql,
            _ => throw new CalibrationStoreException(
                $"Миграция схемы с версии {fromVersion} не реализована в этой сборке. " +
                "Хранилище оставлено на последней исправной версии."),
        };

        var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await ExecuteAsync(connection, transaction, sql, ct).ConfigureAwait(false);
            await CommitAsync(transaction, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// v1 → v2: профили изделий, настройки рабочего места, привязка прогона к профилю.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Существующие прогоны не переписываются: у них <c>ProfileId IS NULL</c>, и
    /// это честное «прогон сделан до введения профилей». Задним числом приписать
    /// им профиль нельзя — никто не знает, каким эталоном их сдавали, кроме уже
    /// записанного <c>ReferencePath</c>/<c>ReferenceHash</c>.
    /// </para>
    /// <para>
    /// Оба <c>ALTER TABLE</c> добавляют столбцы без значения по умолчанию.
    /// Это требование SQLite: при включённом <c>foreign_keys</c> добавляемый
    /// столбец с <c>REFERENCES</c> обязан иметь умолчание <c>NULL</c>.
    /// </para>
    /// </remarks>
    private const string MigrateV1ToV2Sql = ProfileSchemaSql + """

        ALTER TABLE Run ADD COLUMN ProfileId        INTEGER REFERENCES Profile(Id) ON DELETE RESTRICT;
        ALTER TABLE Run ADD COLUMN ProfileNameAtRun TEXT;

        CREATE INDEX IF NOT EXISTS IX_Run_Profile ON Run(ProfileId, StartedUtc DESC);

        PRAGMA user_version = 2;
        """;

    private static async Task VerifyIntegrityAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using (var quickCheck = connection.CreateCommand())
        {
            quickCheck.CommandText = "PRAGMA quick_check;";
            var result = await quickCheck.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new CalibrationStoreException(
                    $"Проверка целостности хранилища не пройдена: {result ?? "нет ответа"}. " +
                    "Работать с повреждённой базой нельзя — восстановите резервную копию.");
            }
        }

        await using var fkCheck = connection.CreateCommand();
        fkCheck.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await fkCheck.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new CalibrationStoreException(
                "В хранилище нарушены внешние ключи: база правилась вне приложения. " +
                "Восстановите резервную копию.");
        }
    }

    /// <summary>
    /// Формат эталона по расширению файла. Контракт <c>BeginRunAsync</c> не
    /// передаёт разобранный <see cref="ReferenceParamSet"/>, а формат нужен
    /// протоколу, чтобы объяснить, откуда взялись типы параметров.
    /// </summary>
    private static string DetectReferenceFormat(string? referencePath)
    {
        if (string.IsNullOrWhiteSpace(referencePath))
        {
            return "Unknown";
        }

        return Path.GetExtension(referencePath).ToLowerInvariant() switch
        {
            ".param" or ".parm" => "MissionPlannerParam",
            ".params" => "QgcParams",
            ".pck" => "MavFtpParamPck",
            _ => "Unknown",
        };
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string text) =>
        DateTimeOffset.ParseExact(
            text,
            TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
