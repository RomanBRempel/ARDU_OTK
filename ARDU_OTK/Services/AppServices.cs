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

    /// <inheritdoc cref="InitializeAsync"/>
    public Task<IReadOnlyList<RunSummary>> LoadHistoryAsync() => Task.Run(() => Store.ListRunsAsync());

    /// <inheritdoc cref="InitializeAsync"/>
    public Task<IReadOnlyList<CalibrationProfile>> LoadProfilesAsync() =>
        Task.Run(() => Store.ListProfilesAsync());

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
