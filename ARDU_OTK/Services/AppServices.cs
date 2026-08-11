using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ARDU_OTK.Services.Fc;
using ARDU_OTK.Services.Fc.Mavlink;
using ARDU_OTK.Services.Store;

namespace ARDU_OTK.Services;

/// <summary>
/// Корень композиции приложения: пути к данным, реестр прогонов, обновления
/// и запуск процедур работы с полётным контроллером.
/// </summary>
/// <remarks>
/// Здесь же замыкается блокировка обновления. <see cref="UpdateService.IsBusy"/>
/// по умолчанию всегда возвращает «свободен», поэтому без этой связки Velopack
/// имеет право подменить файлы и перезапустить процесс посреди записи в плату.
/// Перезапуск между <c>PARAM_SET</c> и его проверочным чтением оставляет борт
/// частично записанным, и оператор не может узнать, что успело примениться.
/// </remarks>
public sealed class AppServices
{
    private static AppServices? _instance;

    private volatile bool _procedureRunning;

    private AppServices()
    {
        Updates = new UpdateService();

        // Путь к данным выбирается по факту установки, а не по символу сборки:
        // Release, запущенный из bin, обязан работать с отладочным хранилищем.
        var isInstalled = Updates.State != UpdateState.NotInstalled;

        Paths = AppPaths.ForInstallState(isInstalled);
        Paths.EnsureCreated();

        Store = new SqliteCalibrationStore(Paths, Updates.CurrentVersion ?? "0.0.0-dev");

        Updates.IsBusy = () =>
        {
            try
            {
                return _procedureRunning || Store.HasOpenRun;
            }
            catch
            {
                // Не смогли определить состояние — считаем стенд занятым.
                // Ошибиться в эту сторону безопасно, в обратную — нет.
                return true;
            }
        };
    }

    public static AppServices Instance => _instance ??= new AppServices();

    public UpdateService Updates { get; }

    public AppPaths Paths { get; }

    public SqliteCalibrationStore Store { get; }

    /// <summary>
    /// Готовит хранилище и закрывает прогоны, брошенные предыдущим запуском
    /// процесса, — в том числе прерванные обновлением.
    /// </summary>
    /// <remarks>
    /// 🔴 Работа с SQLite вынесена в пул потоков намеренно. Первое обращение к
    /// нативной части SQLite из UI-потока WinUI (STA) убивает процесс с
    /// 0xC000027B: управляемого исключения не возникает, обработчики
    /// <see cref="Application.UnhandledException"/> и try/catch не срабатывают,
    /// в журнале Windows остаётся только stowed exception. Проверено бисекцией:
    /// тот же код через <see cref="Task.Run(Func{Task})"/> отрабатывает штатно.
    /// Не «упрощать», убирая обёртку.
    /// </remarks>
    public Task InitializeAsync(CancellationToken ct = default) => Task.Run(
        async () =>
        {
            await Store.InitializeAsync(ct).ConfigureAwait(false);
            await Store.SweepAbandonedRunsAsync(ct).ConfigureAwait(false);
        },
        ct);

    /// <summary>Выполняет серийную калибровку компаса на подключённом борту.</summary>
    public async Task<CalibrationRunResult> RunCompassCalibrationAsync(
        CalibrationRequest request,
        ICalibrationProgress progress,
        CancellationToken ct)
    {
        _procedureRunning = true;
        try
        {
            // Порт открывается монопольно: наблюдательное соединение нужно
            // закрыть, иначе процедура не откроет тот же COM-порт.
            await DisconnectAsync().ConfigureAwait(false);

            // Как и InitializeAsync — только не в UI-потоке: процедура пишет в
            // SQLite и работает с портом, а первое обращение к нативной части
            // SQLite из STA-потока WinUI роняет процесс без исключения.
            return await Task.Run(
                async () =>
                {
                    var link = new SerialVehicleLink();
                    await using (link.ConfigureAwait(false))
                    {
                        var job = new SerialCompassCalibrationJob(
                            link, Store, progress, new CalibrationTolerances());
                        return await job.RunAsync(request, ct).ConfigureAwait(false);
                    }
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _procedureRunning = false;
        }
    }

    /// <summary>
    /// Повторно записывает на борт эталонные значения параметров, не прошедших
    /// проверку.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Вердикт прогона это действие <b>не меняет</b>. Прогон, на котором борт
    /// не принял значение, остаётся проваленным: изделие сдаётся повторным
    /// прогоном целиком, а не подкруткой отдельных параметров задним числом.
    /// Иначе в реестре появилась бы плата, «прошедшая» проверку, которую она не
    /// проходила.
    /// </para>
    /// <para>
    /// Попытка ложится в тот же прогон отдельными записями аудита. Молчаливая
    /// перезапись оставила бы борт с иными значениями, чем те, по которым
    /// закрыт прогон, — и расхождение всплыло бы только при разборе рекламации.
    /// </para>
    /// <para>
    /// Порт открывается заново: к моменту нажатия кнопки соединение прогона уже
    /// закрыто, а наблюдательное могло быть поднято на том же порту — его
    /// приходится погасить, потому что COM открывается монопольно.
    /// </para>
    /// </remarks>
    /// <param name="runId">Прогон, в который лягут записи аудита. <c>0</c> — прогон не открывался, аудит пропускается.</param>
    /// <param name="portName">Порт борта.</param>
    /// <param name="mismatches">Что перезаписать.</param>
    /// <param name="progress">Куда сообщать ход; может быть <c>null</c>.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Исход по каждому имени в порядке входного списка.</returns>
    public async Task<IReadOnlyList<ParamWriteRecord>> RewriteFromReferenceAsync(
        long runId,
        string portName,
        IReadOnlyList<ParamMismatch> mismatches,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentNullException.ThrowIfNull(mismatches);

        if (mismatches.Count == 0)
        {
            return Array.Empty<ParamWriteRecord>();
        }

        _procedureRunning = true;
        try
        {
            await DisconnectAsync().ConfigureAwait(false);

            return await Task.Run(
                async () =>
                {
                    var records = new List<ParamWriteRecord>(mismatches.Count);

                    var link = new SerialVehicleLink();
                    await using (link.ConfigureAwait(false))
                    {
                        await link.ConnectAsync(portName, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                        if (!link.IsConnected)
                        {
                            throw new VehicleLinkException(
                                $"Порт {portName} открыт, но HEARTBEAT не получен: перезаписывать нечего и некуда.");
                        }

                        foreach (var mismatch in mismatches)
                        {
                            ct.ThrowIfCancellationRequested();
                            progress?.Report($"Запись {mismatch.Name}…");

                            var record = await RewriteOneAsync(link, mismatch, ct).ConfigureAwait(false);
                            records.Add(record);

                            if (runId > 0)
                            {
                                try
                                {
                                    await Store.RecordWriteAsync(runId, record, ct).ConfigureAwait(false);
                                }
                                catch (CalibrationStoreException ex)
                                {
                                    // Отказ реестра не отменяет уже выполненную
                                    // запись в борт и не должен её скрыть.
                                    progress?.Report($"Аудит {mismatch.Name} не записан: {ex.Message}");
                                }
                            }
                        }
                    }

                    return (IReadOnlyList<ParamWriteRecord>)records;
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _procedureRunning = false;
        }
    }

    /// <summary>
    /// Одна повторная запись: пишем и тут же проверяем независимым чтением.
    /// </summary>
    /// <remarks>
    /// Эхо <c>PARAM_SET</c> подтверждением не считается — оно широковещательное
    /// и приходит с недостоверным индексом. Провалившаяся повторная запись
    /// возвращается исходом <see cref="WriteOutcome.Mismatch"/>, а не
    /// исключением: оператору нужен результат по всем именам, а не останов на
    /// первом упрямом.
    /// </remarks>
    private static async Task<ParamWriteRecord> RewriteOneAsync(
        SerialVehicleLink link,
        ParamMismatch mismatch,
        CancellationToken ct)
    {
        double? before = null;
        try
        {
            var current = await link.ReadParamAsync(mismatch.Name, ct).ConfigureAwait(false);
            before = current.Value;

            await link.WriteParamAsync(mismatch.Name, (float)mismatch.Expected, current.Type, ct)
                .ConfigureAwait(false);

            var readBack = await link.ReadParamAsync(mismatch.Name, ct).ConfigureAwait(false);

            var accepted = ReferenceParamFile.ValuesEqual(mismatch.Expected, readBack);

            return new ParamWriteRecord(
                mismatch.Name,
                before,
                mismatch.Expected,
                readBack.Value,
                accepted ? WriteOutcome.Verified : WriteOutcome.Mismatch,
                DateTimeOffset.UtcNow);
        }
        catch (VehicleLinkException)
        {
            return new ParamWriteRecord(
                mismatch.Name,
                before,
                mismatch.Expected,
                null,
                WriteOutcome.Failed,
                DateTimeOffset.UtcNow);
        }
    }

    /// <inheritdoc cref="InitializeAsync"/>
    public Task<IReadOnlyList<RunSummary>> LoadHistoryAsync() => Task.Run(() => Store.ListRunsAsync());

    /// <inheritdoc cref="InitializeAsync"/>
    public Task<IReadOnlyList<CalibrationReference>> LoadReferencesAsync() =>
        Task.Run(() => Store.ListReferencesAsync());

    /// <inheritdoc cref="InitializeAsync"/>
    public Task<WorkstationSettings> LoadSettingsAsync() =>
        Task.Run(() => Store.GetWorkstationSettingsAsync());

    /// <inheritdoc cref="InitializeAsync"/>
    public Task SaveSettingsAsync(WorkstationSettings settings) =>
        Task.Run(() => Store.SaveWorkstationSettingsAsync(settings));

    // --- Связь с полётным контроллером ------------------------------------

    private SerialVehicleLink? _link;

    /// <summary>Порт, на котором сейчас установлена связь, или <c>null</c>.</summary>
    public string? ConnectedPort { get; private set; }

    /// <summary>Строка версии прошивки подключённого борта, если борт её прислал.</summary>
    public string? ConnectedFirmware { get; private set; }

    public bool IsLinkConnected => _link is { IsConnected: true };

    /// <summary>
    /// Текущее состояние борта для индикатора. Чтение дешёвое и безопасное из
    /// любого потока: канал складывает последние сообщения под замком.
    /// </summary>
    public VehicleLiveState? LiveState => _link?.LiveState;

    /// <summary>Состояние связи изменилось. Может прийти из любого потока.</summary>
    public event EventHandler? LinkChanged;

    /// <summary>
    /// Подключается к борту и удерживает соединение до явного отключения.
    /// </summary>
    /// <remarks>
    /// Порт открывается монопольно, поэтому перед запуском процедуры это
    /// соединение закрывается: иначе процедура не сможет открыть тот же порт.
    /// </remarks>
    public Task ConnectAsync(string portName, CancellationToken ct = default) => Task.Run(
        async () =>
        {
            await DisconnectAsync().ConfigureAwait(false);

            var link = new SerialVehicleLink();
            try
            {
                await link.ConnectAsync(portName, TimeSpan.FromSeconds(12), ct).ConfigureAwait(false);
            }
            catch
            {
                await link.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            _link = link;
            ConnectedPort = portName;
            ConnectedFirmware = link.FirmwareBanner;
            LinkChanged?.Invoke(this, EventArgs.Empty);
        },
        ct);

    /// <summary>
    /// Снимает эталон компасов с подключённого образцового изделия.
    /// </summary>
    /// <remarks>
    /// Читает по уже открытому каналу — ради этого живое соединение и держится.
    /// Отдельного подключения не делает: второй открыватель того же COM-порта
    /// получил бы отказ, а закрывать наблюдательный канал ради снимка значило бы
    /// гасить индикатор в момент, когда оператор смотрит на борт.
    /// </remarks>
    /// <exception cref="VehicleLinkException">Связи нет либо борт не отдал состав компасов.</exception>
    public Task<CompassSnapshot> ReadCompassSnapshotAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default) => Task.Run(
        () =>
        {
            var link = _link;
            if (link is not { IsConnected: true })
            {
                throw new VehicleLinkException(
                    "Нет связи с бортом: снимать эталон не с чего. Подключите образцовое изделие.");
            }

            return CompassParameterSnapshot.ReadAsync(link, progress, ct);
        },
        ct);

    /// <summary>
    /// Снимает с образца всю таблицу параметров — то, из чего собирается эталон.
    /// </summary>
    /// <remarks>
    /// Читает по уже открытому наблюдательному каналу: второй открыватель того
    /// же COM-порта получил бы отказ, а гасить канал ради снимка значило бы
    /// выключить индикатор в момент, когда оператор смотрит на борт.
    /// </remarks>
    /// <exception cref="VehicleLinkException">Связи нет либо борт не отдал ни одного параметра.</exception>
    public Task<FullParameterSet> ReadAllParametersAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default) => Task.Run(
        () =>
        {
            var link = _link;
            if (link is not { IsConnected: true })
            {
                throw new VehicleLinkException(
                    "Нет связи с бортом: снимать эталон не с чего. Подключите образцовое изделие.");
            }

            return link.ReadAllParamsAsync(progress, ct);
        },
        ct);

    /// <summary>Читает один параметр по уже открытому наблюдательному каналу.</summary>
    /// <exception cref="VehicleLinkException">Связи нет либо борт имени не знает.</exception>
    public Task<double> ReadParameterAsync(string name, CancellationToken ct = default) => Task.Run(
        async () =>
        {
            var link = _link;
            if (link is not { IsConnected: true })
            {
                throw new VehicleLinkException("Нет связи с бортом.");
            }

            var value = await link.ReadParamAsync(name, ct).ConfigureAwait(false);
            return (double)value.Value;
        },
        ct);

    /// <summary>
    /// Снимает скрипты с подключённого образцового изделия.
    /// </summary>
    /// <remarks>
    /// Читает по уже открытому каналу — как и снимок компасов, и по той же
    /// причине: второй открыватель того же COM-порта получил бы отказ.
    /// </remarks>
    /// <param name="unreadable">Сюда попадают файлы, которые прочитать не удалось.</param>
    /// <exception cref="VehicleLinkException">Связи нет.</exception>
    public Task<IReadOnlyList<ReferenceScript>> ReadScriptsAsync(
        List<string> unreadable,
        IProgress<string>? progress = null,
        CancellationToken ct = default) => Task.Run(
        () =>
        {
            var link = _link;
            if (link is not { IsConnected: true })
            {
                throw new VehicleLinkException(
                    "Нет связи с бортом: снимать скрипты не с чего. Подключите образцовое изделие.");
            }

            return ScriptTransfer.ReadAllAsync(link, progress, unreadable, ct);
        },
        ct);

    /// <summary>
    /// Сверяет всю конфигурацию целевого борта с эталоном.
    /// </summary>
    /// <remarks>
    /// 🔴 Сверяется всё, что эталон объявил контролируемым, а не компасный
    /// блок. Изделие отличается от изделия каналами приёмника, выходами,
    /// полётными режимами и регуляторами; сверка одного компаса пропускала бы
    /// чужую сборку как годную.
    /// </remarks>
    /// <returns>План: что сошлось, что расходится и что из этого можно записать.</returns>
    public async Task<ParameterTransferPlan> CompareParametersAsync(
        string portName,
        CalibrationReference reference,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentNullException.ThrowIfNull(reference);

        var expected = reference.ParseParameters().Values;
        var roles = reference.ToRoleMap();

        _procedureRunning = true;
        try
        {
            await DisconnectAsync().ConfigureAwait(false);

            return await Task.Run(
                async () =>
                {
                    var link = new SerialVehicleLink();
                    await using (link.ConfigureAwait(false))
                    {
                        await link.ConnectAsync(portName, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                        if (!link.IsConnected)
                        {
                            throw new VehicleLinkException($"Порт {portName} открыт, но HEARTBEAT не получен.");
                        }

                        var board = await link.ReadAllParamsAsync(progress, ct).ConfigureAwait(false);
                        return ParameterTransfer.Plan(expected, board.Values, roles);
                    }
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _procedureRunning = false;
        }
    }

    /// <summary>
    /// Приводит расходящиеся параметры борта к эталону.
    /// </summary>
    /// <remarks>
    /// 🔴 Каждая запись подтверждается независимым обратным чтением, а исход по
    /// каждому имени ложится в реестр строкой аудита. Вердикт прогона это
    /// действие не меняет: изделие сдаётся повторным прогоном целиком.
    /// </remarks>
    /// <param name="runId">Прогон для аудита. <c>0</c> — писать аудит некуда.</param>
    public async Task<IReadOnlyList<ParamWriteRecord>> ApplyParametersAsync(
        long runId,
        string portName,
        IReadOnlyList<ParameterDifference> differences,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentNullException.ThrowIfNull(differences);

        if (differences.Count == 0)
        {
            return Array.Empty<ParamWriteRecord>();
        }

        _procedureRunning = true;
        try
        {
            await DisconnectAsync().ConfigureAwait(false);

            return await Task.Run(
                async () =>
                {
                    var link = new SerialVehicleLink();
                    await using (link.ConfigureAwait(false))
                    {
                        await link.ConnectAsync(portName, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                        if (!link.IsConnected)
                        {
                            throw new VehicleLinkException($"Порт {portName} открыт, но HEARTBEAT не получен.");
                        }

                        var records = await ParameterTransfer
                            .ApplyAsync(link, differences, progress, ct)
                            .ConfigureAwait(false);

                        if (runId > 0)
                        {
                            foreach (var record in records)
                            {
                                try
                                {
                                    await Store.RecordWriteAsync(runId, record, ct).ConfigureAwait(false);
                                }
                                catch (CalibrationStoreException ex)
                                {
                                    progress?.Report($"Аудит {record.Name} не записан: {ex.Message}");
                                }
                            }
                        }

                        return records;
                    }
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _procedureRunning = false;
        }
    }

    /// <summary>
    /// Сверяет скрипты целевого борта с эталоном.
    /// </summary>
    /// <remarks>
    /// Открывает собственное соединение: сверка выполняется после прогона,
    /// когда канал процедуры уже закрыт. Наблюдательное соединение при этом
    /// гасится — COM-порт открывается монопольно.
    /// </remarks>
    /// <returns>Расхождения; пустой список — скрипты борта совпали с эталоном.</returns>
    public async Task<IReadOnlyList<ScriptDifference>> VerifyScriptsAsync(
        string portName,
        IReadOnlyList<ReferenceScript> expected,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentNullException.ThrowIfNull(expected);

        _procedureRunning = true;
        try
        {
            await DisconnectAsync().ConfigureAwait(false);

            return await Task.Run(
                async () =>
                {
                    var link = new SerialVehicleLink();
                    await using (link.ConfigureAwait(false))
                    {
                        await link.ConnectAsync(portName, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                        if (!link.IsConnected)
                        {
                            throw new VehicleLinkException($"Порт {portName} открыт, но HEARTBEAT не получен.");
                        }

                        var unreadable = new List<string>();
                        var actual = await ScriptTransfer
                            .ReadAllAsync(link, progress, unreadable, ct)
                            .ConfigureAwait(false);

                        return ScriptTransfer.Compare(expected, actual, unreadable);
                    }
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _procedureRunning = false;
        }
    }

    /// <summary>
    /// Приводит скрипты целевого борта к эталону: дописывает недостающие,
    /// переписывает разошедшиеся, удаляет лишние.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Вердикт прогона это действие не меняет — ровно как повторная запись
    /// параметров. Изделие сдаётся повторным прогоном целиком.
    /// </para>
    /// <para>
    /// 🔴 Записанный скрипт начнёт исполняться только после перезагрузки борта:
    /// Lua подхватывается при старте. Сказать об этом обязан вызывающий —
    /// молчание здесь означало бы «применено», чего не произошло.
    /// </para>
    /// </remarks>
    /// <returns>Пути, по которым действие выполнено, и текст отказа по остальным.</returns>
    public async Task<IReadOnlyList<(string Path, string? Failure)>> ApplyScriptsAsync(
        string portName,
        IReadOnlyList<ScriptDifference> differences,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentNullException.ThrowIfNull(differences);

        if (differences.Count == 0)
        {
            return Array.Empty<(string, string?)>();
        }

        _procedureRunning = true;
        try
        {
            await DisconnectAsync().ConfigureAwait(false);

            return await Task.Run(
                async () =>
                {
                    var outcomes = new List<(string Path, string? Failure)>(differences.Count);

                    var link = new SerialVehicleLink();
                    await using (link.ConfigureAwait(false))
                    {
                        await link.ConnectAsync(portName, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                        if (!link.IsConnected)
                        {
                            throw new VehicleLinkException(
                                $"Порт {portName} открыт, но HEARTBEAT не получен.");
                        }

                        foreach (var difference in differences)
                        {
                            ct.ThrowIfCancellationRequested();
                            outcomes.Add(await ApplyOneScriptAsync(link, difference, progress, ct)
                                .ConfigureAwait(false));
                        }
                    }

                    return (IReadOnlyList<(string, string?)>)outcomes;
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _procedureRunning = false;
        }
    }

    /// <summary>
    /// Одно действие над скриптом с проверкой обратным чтением.
    /// </summary>
    /// <remarks>
    /// Обратное чтение обязательно и здесь: подтверждения записи MAVFTP
    /// достаточно, чтобы знать, что борт принял байты, но не чтобы знать, что
    /// на карте лежит именно тот файл. Сверка по SHA-256 отвечает на второй
    /// вопрос.
    /// </remarks>
    private static async Task<(string Path, string? Failure)> ApplyOneScriptAsync(
        SerialVehicleLink link,
        ScriptDifference difference,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        try
        {
            if (difference.Outcome == ScriptComparison.ExtraOnBoard)
            {
                progress?.Report($"Удаление {difference.Path}…");
                await link.RemoveFileAsync(difference.Path, ct).ConfigureAwait(false);
                return (difference.Path, null);
            }

            if (difference.Expected is not { } script)
            {
                return (difference.Path, "нечего записывать: в эталоне этого скрипта нет");
            }

            progress?.Report($"Запись {difference.Path}…");
            await link.WriteFileAsync(script.Path, script.ToBytes(), null, ct).ConfigureAwait(false);

            progress?.Report($"Сверка {difference.Path}…");
            var readBack = await link.ReadFileAsync(script.Path, null, ct).ConfigureAwait(false);
            var actual = ReferenceScript.ComputeHash(readBack);

            return string.Equals(actual, script.Hash, StringComparison.Ordinal)
                ? (difference.Path, null)
                : (difference.Path,
                    $"обратное чтение дало другой файл: эталон {ReferenceParameters.ShortHash(script.Hash)}, "
                  + $"борт {ReferenceParameters.ShortHash(actual)}");
        }
        catch (Exception ex) when (ex is VehicleLinkException or InvalidDataException)
        {
            return (difference.Path, ex.Message);
        }
    }

    public Task DisconnectAsync() => Task.Run(async () =>
    {
        var link = _link;
        _link = null;
        ConnectedPort = null;
        ConnectedFirmware = null;

        if (link is not null)
        {
            await link.DisposeAsync().ConfigureAwait(false);
            LinkChanged?.Invoke(this, EventArgs.Empty);
        }
    });
}
