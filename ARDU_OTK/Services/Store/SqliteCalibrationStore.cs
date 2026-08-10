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
    public const int SchemaVersion = 1;

    // Формат меток времени: UTC, фиксированная ширина. Такой текст сравнивается
    // и сортируется лексикографически ровно как хронологически — на этом держатся
    // индексы по времени и вычисление конца прерванного прогона.
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private const string VerdictPass = "pass";
    private const string VerdictFail = "fail";
    private const string VerdictAborted = "aborted";

    /// <summary>Литерал версии схемы. Обязан совпадать с <see cref="SchemaVersion"/>: PRAGMA не принимает параметр.</summary>
    private const string SetSchemaVersionSql = "PRAGMA user_version = 1;";

    private const string CreateSchemaSql = """
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
            FailedCheckCount INTEGER NOT NULL DEFAULT 0
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
    public static string NormalizeUnitId(string? raw)
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

        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (value is null || value is DBNull)
        {
            throw new CalibrationStoreException($"Запрос не вернул значения: {sql}");
        }

        return value;
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

    private static Task ApplyMigrationAsync(SqliteConnection connection, int fromVersion, CancellationToken ct)
    {
        // Миграции упорядочены, только вперёд, каждая в своей транзакции и
        // завершается собственным литералом PRAGMA user_version. Они вправе
        // добавлять таблицы, столбцы и индексы, но не переписывать строки
        // RunWrite, RunCheck и RunMessage — это доказательная база.
        _ = connection;
        _ = ct;

        throw new CalibrationStoreException(
            $"Миграция схемы с версии {fromVersion} не реализована в этой сборке. " +
            "Хранилище оставлено на последней исправной версии.");
    }

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
