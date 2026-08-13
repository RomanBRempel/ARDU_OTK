using System.Buffers.Binary;
using System.IO.Ports;
using System.Text;
using System.Threading.Channels;
using Microsoft.Win32;

namespace ARDU_OTK.Services.Fc.Mavlink;

/// <summary>
/// Реализация <see cref="IVehicleLink"/> поверх последовательного порта.
/// </summary>
/// <remarks>
/// Чтение идёт в фоновой задаче и никогда не трогает поток UI. Всё состояние
/// борта (последний <c>SYS_STATUS</c>, последний фикс GPS, собранные
/// <c>STATUSTEXT</c>) обновляется в этой же задаче, а наружу отдаётся под
/// блокировкой.
/// <para>
/// Класс рассчитан на один борт на порт. Кадры от чужой системы отбрасываются
/// сразу после того, как номер целевой системы защёлкнут первым
/// <c>HEARTBEAT</c>: на шине может оказаться второй автопилот или наземная
/// станция, и их телеметрия не должна попасть в замер.
/// </para>
/// </remarks>
public sealed class SerialVehicleLink : IVehicleLink, IVehicleFileTransfer
{
    /// <summary>
    /// Скорость порта. Через USB CDC это значение — фикция: композитное
    /// устройство игнорирует запрошенную скорость, данные идут на скорости шины.
    /// Настройка важна только для настоящего UART (телеметрийный радиомодем),
    /// где расхождение скоростей сторон превращает поток в мусор.
    /// </summary>
    private const int BaudRate = 115200;

    /// <summary>
    /// <c>MAV_CMD_SET_MESSAGE_INTERVAL</c>. В <see cref="MavCommand"/> её нет:
    /// это команда транспорта, а не процедуры калибровки.
    /// </summary>
    private const ushort SetMessageInterval = 511;

    /// <summary><c>MAV_CMD_REQUEST_MESSAGE</c>: попросить борт прислать сообщение по номеру.</summary>
    private const ushort RequestMessage = 512;

    private const int ParamReadRetries = 2;
    private static readonly TimeSpan ParamReadTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StreamRequestAckTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RebootAckTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PrearmAckTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Пауза после закрытия порта, чтобы плата успела уйти с шины USB.</summary>
    private static readonly TimeSpan BusDropoutDelay = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan PortPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReconnectAttemptTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LoopShutdownTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Незаконченная сборка <c>STATUSTEXT</c> живёт не дольше этого срока.</summary>
    private static readonly TimeSpan StatusTextAssemblyLifetime = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Потоки, которые приёмке нужны, и период их выдачи в микросекундах.
    /// </summary>
    /// <remarks>
    /// ArduCopter по умолчанию не передаёт ничего: все группы <c>SRn_*</c>
    /// выставлены в 0 Гц, и без явного запроса телеметрия просто не пойдёт.
    /// Заданные здесь интервалы живут только до перезагрузки борта, в
    /// энергонезависимую память не попадают и после ребута запрашиваются заново
    /// (см. <see cref="RebootAndReconnectAsync"/>).
    /// </remarks>
    private static readonly (uint MessageId, int IntervalMicroseconds, string Name)[] RequiredStreams =
    {
        (MavMessageId.Attitude, 100_000, "ATTITUDE"),
        (MavMessageId.RawImu, 200_000, "RAW_IMU"),
        (MavMessageId.ScaledImu2, 200_000, "SCALED_IMU2"),
        (MavMessageId.ScaledImu3, 200_000, "SCALED_IMU3"),
        (MavMessageId.SysStatus, 500_000, "SYS_STATUS"),
        (MavMessageId.GpsRawInt, 1_000_000, "GPS_RAW_INT"),
        (MavMessageId.Gps2Raw, 1_000_000, "GPS2_RAW"),
        (MavMessageId.VfrHud, 500_000, "VFR_HUD"),
    };

    private readonly MavlinkEncoder _encoder = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly object _subscriptionLock = new();
    private readonly object _sessionLock = new();
    private readonly object _statusTextLock = new();

    private readonly List<FrameSubscription> _subscriptions = new();
    private readonly List<TelemetrySession> _sessions = new();
    private readonly Dictionary<ushort, StatusTextAssembly> _statusTextAssemblies = new();

    private SerialPort? _port;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private int _disposed;

    private volatile bool _heartbeatSeen;
    private volatile byte _targetSystem;
    private volatile byte _targetComponent;
    private volatile string? _portName;
    private volatile string? _firmwareBanner;

    private GpsFix? _lastGpsFix;
    private GpsFix? _lastGps2Fix;
    private AirData? _lastAir;
    private SensorHealth? _lastHealth;

    // Последнее состояние для индикатора. Хранится отдельно от сессий
    // измерения: индикатору нужно «что сейчас», а не среднее по окну.
    private AttitudeSample? _lastAttitude;
    private HeartbeatMessage? _lastHeartbeat;
    private SysStatusMessage? _lastSysStatus;
    private readonly Dictionary<int, MagSample> _lastMags = [];
    private DateTimeOffset _lastStateUpdateUtc;

    /// <summary>
    /// Диагностический канал уровня транспорта: отказ запроса потока, потеря
    /// порта, недособранный <c>STATUSTEXT</c>.
    /// </summary>
    /// <remarks>
    /// Это не сообщения борта — те приходят через <see cref="StatusTextReceived"/>.
    /// Обработчик вызывается из фоновой задачи, поэтому обязан быть
    /// потокобезопасным и не должен обращаться к UI напрямую.
    /// </remarks>
    public Action<MavSeverity, string>? DiagnosticLog { get; set; }

    /// <inheritdoc />
    public bool IsConnected
    {
        get
        {
            lock (_stateLock)
            {
                return _port is { IsOpen: true } && _heartbeatSeen;
            }
        }
    }

    /// <inheritdoc />
    public byte TargetSystem => _targetSystem;

    /// <inheritdoc />
    public byte TargetComponent => _targetComponent;

    /// <inheritdoc />
    public string? FirmwareBanner => _firmwareBanner;

    /// <inheritdoc />
    public VehicleLiveState? LiveState
    {
        get
        {
            lock (_stateLock)
            {
                if (_lastStateUpdateUtc == default)
                {
                    return null;
                }

                var heartbeat = _lastHeartbeat;
                var status = _lastSysStatus;

                // Сентинелы MAVLink: 65535 у напряжения и -1 у тока означают
                // «борт не сообщает», а не ноль. Показывать их как нули нельзя.
                double? volts = status is { VoltageBattery: not ushort.MaxValue } s1
                    ? s1.VoltageBattery / 1000.0
                    : null;

                double? amps = status is { CurrentBattery: > -1 } s2
                    ? s2.CurrentBattery / 100.0
                    : null;

                int? percent = status is { BatteryRemaining: >= 0 } s3
                    ? s3.BatteryRemaining
                    : null;

                return new VehicleLiveState(
                    _lastAttitude,
                    volts,
                    amps,
                    percent,
                    heartbeat is { } hb ? ArduPilotModes.Describe(hb.Type, hb.CustomMode) : "—",
                    heartbeat is { } armed && (armed.BaseMode & 0x80) != 0,
                    heartbeat?.Type ?? 0,
                    [.. _lastMags.Values],
                    _lastStateUpdateUtc)
                {
                    Gps = _lastGpsFix,
                    Gps2 = _lastGps2Fix,
                    Air = _lastAir,
                };
            }
        }
    }

    /// <summary>Имя открытого порта, если соединение установлено.</summary>
    public string? PortName => _portName;

    /// <inheritdoc />
    public event EventHandler<StatusTextEvent>? StatusTextReceived;

    /// <inheritdoc />
    public async Task ConnectAsync(string portName, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        // Повторный вызов — это переоткрытие порта, а не ошибка: так работает
        // возврат борта после перезагрузки.
        await ShutdownAsync().ConfigureAwait(false);

        OpenPort(portName);
        StartReceiveLoop();

        try
        {
            await WaitForFirstHeartbeatAsync(timeout, ct).ConfigureAwait(false);
        }
        catch
        {
            // Полуоткрытое соединение хуже закрытого: порт остался бы занят, а
            // IsConnected всё равно врал бы. Закрываем и отдаём исходную ошибку.
            await ShutdownAsync().ConfigureAwait(false);
            throw;
        }

        await RequestRequiredStreamsAsync(ct).ConfigureAwait(false);
        await RequestAutopilotVersionAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ParamValue> ReadParamAsync(string name, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureConnected();

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                // Подписка ставится до отправки запроса: ответ борта может прийти
                // раньше, чем управление вернётся из записи в порт.
                using FrameSubscription subscription = Subscribe(frame =>
                    frame.MessageId == MavMessageId.ParamValue
                    && string.Equals(
                        ParamValueMessage.Decode(frame.Payload.Span).ParamId,
                        name,
                        StringComparison.Ordinal));

                byte[] request = _encoder.EncodeParamRequestRead(
                    TargetSystem,
                    TargetComponent,
                    name,
                    paramIndex: -1);
                await SendAsync(request, ct).ConfigureAwait(false);

                MavlinkFrame reply = await subscription
                    .ReadAsync(ParamReadTimeout, $"PARAM_VALUE {name}", ct)
                    .ConfigureAwait(false);

                ParamValueMessage value = ParamValueMessage.Decode(reply.Payload.Span);
                return new ParamValue(value.ParamId, value.ParamValue, (MavParamType)value.ParamType);
            }
            catch (VehicleLinkException) when (attempt < ParamReadRetries)
            {
                // Потеря запроса или ответа на USB-линии — штатное явление.
                // Исключение обработано повтором; после исчерпания попыток
                // сработает ветка ниже и ошибка уйдёт наверх.
                Log(MavSeverity.Debug, $"Параметр {name}: попытка {attempt + 1} не удалась, повтор.");
            }

            if (attempt >= ParamReadRetries)
            {
                throw new VehicleLinkException(
                    $"Параметр {name} не прочитан: борт не ответил на {ParamReadRetries + 1} запроса " +
                    $"PARAM_REQUEST_READ с таймаутом {ParamReadTimeout.TotalSeconds:0.#} с.");
            }
        }
    }

    /// <summary>Тишина в потоке <c>PARAM_VALUE</c>, после которой поток считается закончившимся.</summary>
    private static readonly TimeSpan ParamStreamIdle = TimeSpan.FromSeconds(4);

    /// <inheritdoc />
    public async Task<FullParameterSet> ReadAllParamsAsync(
        IProgress<string>? progress,
        IProgress<ParameterProgress>? detail,
        CancellationToken ct)
    {
        EnsureConnected();

        var values = new Dictionary<string, ParamValue>(StringComparer.Ordinal);
        var seen = new HashSet<int>();
        var declared = 0;

        using (FrameSubscription stream = Subscribe(static f => f.MessageId == MavMessageId.ParamValue))
        {
            await SendAsync(
                _encoder.EncodeParamRequestList(TargetSystem, TargetComponent), ct).ConfigureAwait(false);

            // Поток идёт, пока борт присылает новое. Признак конца — не счётчик
            // (часть имён теряется на USB-линии и поток встанет недосчитавшись),
            // а тишина: борт всё, что хотел, уже отправил.
            while (true)
            {
                MavlinkFrame frame;
                try
                {
                    frame = await stream.ReadAsync(ParamStreamIdle, "поток PARAM_VALUE", ct)
                        .ConfigureAwait(false);
                }
                catch (VehicleLinkException)
                {
                    break;
                }

                var message = ParamValueMessage.Decode(frame.Payload.Span);
                declared = Math.Max(declared, message.ParamCount);

                values[message.ParamId] = new ParamValue(
                    message.ParamId, message.ParamValue, (MavParamType)message.ParamType);

                if (message.ParamIndex != ParamValueMessage.BroadcastIndex)
                {
                    seen.Add(message.ParamIndex);
                }

                detail?.Report(new ParameterProgress(message.ParamId, values.Count, declared));

                if (values.Count % 50 == 0)
                {
                    progress?.Report($"Принято параметров: {values.Count} из {declared}.");
                }

                if (declared > 0 && seen.Count >= declared)
                {
                    break;
                }
            }
        }

        if (values.Count == 0)
        {
            throw new VehicleLinkException(
                "Борт не прислал ни одного параметра в ответ на PARAM_REQUEST_LIST. Снимать эталон не с чего.");
        }

        var missing = await FillGapsAsync(values, seen, declared, progress, ct).ConfigureAwait(false);

        var set = new FullParameterSet(values, declared, missing);

        // 🔴 Признак полноты один и тот же и в сообщении, и в наборе. Пока их
        // было два, оператор читал «Прочитано всё» на наборе, который
        // FullParameterSet.IsComplete считал неполным, и верил сообщению.
        progress?.Report(set.IsComplete
            ? $"Снято параметров: {values.Count}. Прочитано всё."
            : $"Снято параметров: {values.Count}, борт объявил {declared}"
              + (missing.Count > 0 ? $", не отдал {missing.Count}" : string.Empty) + ".");

        return set;
    }

    /// <summary>
    /// Добирает поштучно то, что борт не прислал потоком.
    /// </summary>
    /// <remarks>
    /// 🔴 Именно по индексу, а не повтором <c>PARAM_REQUEST_LIST</c>: повторный
    /// запрос списка заставляет прошивку начать поток с начала, и на наборе в
    /// тысячу имён недостача одного превращается в вечный круг.
    /// </remarks>
    private async Task<IReadOnlyList<int>> FillGapsAsync(
        Dictionary<string, ParamValue> values,
        HashSet<int> seen,
        int declared,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (declared <= 0)
        {
            return Array.Empty<int>();
        }

        var missing = new List<int>();
        for (var index = 0; index < declared; index++)
        {
            if (!seen.Contains(index))
            {
                missing.Add(index);
            }
        }

        if (missing.Count == 0)
        {
            return missing;
        }

        progress?.Report($"Борт не прислал {missing.Count} параметров, добираю поштучно…");

        var stillMissing = new List<int>();
        foreach (var index in missing)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using FrameSubscription one = Subscribe(f =>
                    f.MessageId == MavMessageId.ParamValue
                    && ParamValueMessage.Decode(f.Payload.Span).ParamIndex == index);

                await SendAsync(
                    _encoder.EncodeParamRequestRead(TargetSystem, TargetComponent, string.Empty, (short)index),
                    ct).ConfigureAwait(false);

                MavlinkFrame reply = await one
                    .ReadAsync(ParamReadTimeout, $"PARAM_VALUE с индексом {index}", ct)
                    .ConfigureAwait(false);

                var message = ParamValueMessage.Decode(reply.Payload.Span);
                values[message.ParamId] = new ParamValue(
                    message.ParamId, message.ParamValue, (MavParamType)message.ParamType);
            }
            catch (VehicleLinkException)
            {
                // Индекс, которого борт не отдал и поштучно, — это факт о
                // наборе, а не сбой снятия: он уходит наверх списком.
                stillMissing.Add(index);
            }
        }

        return stillMissing;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Метод завершается сразу после отправки кадра. Широковещательный
    /// <c>PARAM_VALUE</c>, который прошивка рассылает после изменения параметра,
    /// подтверждением не является: он асинхронный, приходит с
    /// <c>param_index = 0xFFFF</c> и может относиться к записи, сделанной другой
    /// станцией. Проверку выполняет вызывающий слой обратным чтением.
    /// </remarks>
    public async Task WriteParamAsync(string name, float value, MavParamType type, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureConnected();

        byte[] frame = _encoder.EncodeParamSet(TargetSystem, TargetComponent, name, value, (byte)type);
        await SendAsync(frame, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<MavResult> SendCommandAsync(
        ushort command,
        float p1, float p2, float p3, float p4, float p5, float p6, float p7,
        TimeSpan ackTimeout,
        CancellationToken ct)
    {
        EnsureConnected();

        using FrameSubscription subscription = Subscribe(frame =>
            frame.MessageId == MavMessageId.CommandAck
            && CommandAckMessage.Decode(frame.Payload.Span).Command == command);

        byte[] request = _encoder.EncodeCommandLong(
            TargetSystem, TargetComponent, command, confirmation: 0, p1, p2, p3, p4, p5, p6, p7);
        await SendAsync(request, ct).ConfigureAwait(false);

        DateTimeOffset deadline = DateTimeOffset.UtcNow + ackTimeout;
        while (true)
        {
            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new VehicleLinkException(
                    $"Команда {command} не подтверждена за {ackTimeout.TotalSeconds:0.#} с.");
            }

            MavlinkFrame ack = await subscription
                .ReadAsync(remaining, $"COMMAND_ACK для команды {command}", ct)
                .ConfigureAwait(false);
            var message = CommandAckMessage.Decode(ack.Payload.Span);
            var result = (MavResult)message.Result;

            if (result == MavResult.InProgress)
            {
                // Долгие команды сначала отвечают IN_PROGRESS и только потом
                // присылают окончательный результат. Это не ответ, а отчёт о ходе.
                Log(MavSeverity.Debug, $"Команда {command}: выполняется ({message.Progress}%).");
                continue;
            }

            return result;
        }
    }

    /// <inheritdoc />
    public async Task<GpsFix> WaitForGpsFixAsync(TimeSpan timeout, CancellationToken ct)
    {
        EnsureConnected();

        GpsFix? known = ReadLastGpsFix();
        if (known is { Is3D: true })
        {
            return known.Value;
        }

        using FrameSubscription subscription = Subscribe(frame => frame.MessageId == MavMessageId.GpsRawInt);
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

        while (true)
        {
            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw BuildGpsTimeoutException(timeout, known);
            }

            MavlinkFrame frame;
            try
            {
                frame = await subscription.ReadAsync(remaining, "GPS_RAW_INT", ct).ConfigureAwait(false);
            }
            catch (VehicleLinkException ex)
            {
                // Сообщение о таймаусе ожидания кадра бесполезно оператору:
                // ему нужно знать, что именно показывал приёмник.
                throw BuildGpsTimeoutException(timeout, known, ex);
            }

            GpsRawIntMessage gps = GpsRawIntMessage.Decode(frame.Payload.Span);
            var fix = new GpsFix(gps.FixType, gps.Lat, gps.Lon, gps.SatellitesVisible);
            known = fix;

            if (fix.Is3D)
            {
                return fix;
            }
        }
    }

    /// <inheritdoc />
    public async Task<TelemetrySnapshot> CaptureTelemetryAsync(TimeSpan window, CancellationToken ct)
    {
        EnsureConnected();

        var session = new TelemetrySession();
        lock (_sessionLock)
        {
            _sessions.Add(session);
        }

        try
        {
            await Task.Delay(window, ct).ConfigureAwait(false);
        }
        finally
        {
            lock (_sessionLock)
            {
                _sessions.Remove(session);
            }
        }

        return session.Build();
    }

    /// <inheritdoc />
    public async Task RebootAndReconnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        EnsureConnected();

        string originalPort = _portName
            ?? throw new VehicleLinkException("Имя порта неизвестно: перезагрузка невозможна.");

        await SendRebootAsync(ct).ConfigureAwait(false);

        // Плата уходит с шины USB без корректного detach, поэтому порт закрывается
        // немедленно — дожидаться отказов ввода-вывода бессмысленно.
        await ShutdownAsync().ConfigureAwait(false);
        await Task.Delay(BusDropoutDelay, ct).ConfigureAwait(false);

        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            foreach (string candidate in EnumerateCandidatePorts(originalPort))
            {
                TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                TimeSpan attemptTimeout = remaining < ReconnectAttemptTimeout ? remaining : ReconnectAttemptTimeout;
                try
                {
                    // ConnectAsync заново запрашивает потоки: заданные ранее
                    // интервалы перезагрузку не переживают.
                    await ConnectAsync(candidate, attemptTimeout, ct).ConfigureAwait(false);
                    Log(MavSeverity.Info, $"Борт вернулся на порту {candidate}.");
                    return;
                }
                catch (VehicleLinkException ex)
                {
                    // Порт ещё не поднялся, занят или принадлежит другому
                    // устройству — пробуем следующего кандидата.
                    Log(MavSeverity.Debug, $"Порт {candidate} не подошёл: {ex.Message}");
                    await ShutdownAsync().ConfigureAwait(false);
                }
            }

            await Task.Delay(PortPollInterval, ct).ConfigureAwait(false);
        }

        throw new VehicleLinkException(
            $"Плата не вернулась после перезагрузки за {timeout.TotalSeconds:0} с (board did not come back). " +
            $"Исходный порт {originalPort}, после ребута номер COM мог смениться.");
    }

    /// <inheritdoc />
    public async Task<PrearmReport> RunPrearmChecksAsync(TimeSpan window, CancellationToken ct)
    {
        EnsureConnected();

        var collected = new List<StatusTextEvent>();
        var session = new TelemetrySession();

        void OnStatusText(object? sender, StatusTextEvent message)
        {
            lock (collected)
            {
                collected.Add(message);
            }
        }

        StatusTextReceived += OnStatusText;
        lock (_sessionLock)
        {
            _sessions.Add(session);
        }

        MavResult? commandResult;
        try
        {
            DateTimeOffset windowEnd = DateTimeOffset.UtcNow + window;

            try
            {
                commandResult = await SendCommandAsync(
                    MavCommand.RunPrearmChecks, 0, 0, 0, 0, 0, 0, 0, PrearmAckTimeout, ct).ConfigureAwait(false);

                if (commandResult == MavResult.Unsupported)
                {
                    // Прошивки до 4.1 команду 401 не знают. Это не отказ: борт
                    // всё равно периодически перевыпускает свои PreArm-сообщения,
                    // и окно ниже их соберёт.
                    Log(MavSeverity.Notice, "Команда RUN_PREARM_CHECKS не поддержана прошивкой, работаем по пассивной рассылке.");
                }
            }
            catch (VehicleLinkException ex)
            {
                // Отсутствие подтверждения тоже не отменяет пассивный сбор.
                Log(MavSeverity.Warning, $"RUN_PREARM_CHECKS без подтверждения: {ex.Message}");
                commandResult = null;
            }

            TimeSpan remaining = windowEnd - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            StatusTextReceived -= OnStatusText;
            lock (_sessionLock)
            {
                _sessions.Remove(session);
            }
        }

        List<StatusTextEvent> prearmMessages;
        lock (collected)
        {
            prearmMessages = collected
                .Where(static message => message.Text.StartsWith(PrearmPrefix, StringComparison.Ordinal))
                .Select(static message => message with { Text = StripPrearmPrefix(message.Text) })
                .ToList();
        }

        return new PrearmReport(commandResult, prearmMessages, session.Build().Health);
    }

    /// <inheritdoc />
    /// <remarks>Повторный вызов безвреден и ничего не делает.</remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await ShutdownAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }

    // ---------------------------------------------------------------- порт

    /// <summary>
    /// Открывает порт 115200 8-N-1.
    /// </summary>
    /// <remarks>
    /// <see cref="SerialPort.Open"/> на Windows открывает устройство без
    /// разделения доступа, то есть монопольно — отдельного флага для этого нет.
    /// Поэтому занятый порт распознаётся по <see cref="UnauthorizedAccessException"/>.
    /// </remarks>
    private void OpenPort(string portName)
    {
        var port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,

            // ChibiOS отдаёт данные в USB CDC только при поднятом DTR: без него
            // порт откроется, но поток будет пустым.
            DtrEnable = true,
            RtsEnable = true,
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = 2000,
            ReadBufferSize = 64 * 1024,
        };

        try
        {
            port.Open();
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
        }
        catch (UnauthorizedAccessException ex)
        {
            port.Dispose();
            throw new VehicleLinkException(
                $"Порт {portName} занят другой программой (например, Mission Planner или QGroundControl). " +
                "Закройте её и повторите подключение.",
                ex);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            port.Dispose();
            throw new VehicleLinkException($"Не удалось открыть порт {portName}: {ex.Message}", ex);
        }

        lock (_stateLock)
        {
            _port = port;
            _lastGpsFix = null;
            _lastGps2Fix = null;
            _lastAir = null;
            _lastHealth = null;
        }

        _portName = portName;
        _heartbeatSeen = false;

        lock (_statusTextLock)
        {
            _statusTextAssemblies.Clear();
        }
    }

    private void StartReceiveLoop()
    {
        SerialPort port;
        CancellationTokenSource cts;

        lock (_stateLock)
        {
            port = _port ?? throw new VehicleLinkException("Порт не открыт.");
            cts = new CancellationTokenSource();
            _loopCts = cts;
        }

        Task loop = Task.Run(() => ReceiveLoopAsync(port, cts.Token), CancellationToken.None);

        lock (_stateLock)
        {
            _loopTask = loop;
        }
    }

    /// <summary>
    /// Фоновое чтение порта и разбор кадров.
    /// </summary>
    /// <remarks>
    /// Задача завершается сама, когда порт закрыт: <c>SerialStream.ReadAsync</c>
    /// на Windows не реагирует на токен отмены, пока чтение уже начато, и
    /// единственный надёжный способ его разбудить — закрыть порт. Токен нужен,
    /// чтобы не начинать следующую итерацию.
    /// </remarks>
    private async Task ReceiveLoopAsync(SerialPort port, CancellationToken ct)
    {
        var parser = new MavlinkParser();
        byte[] buffer = new byte[4096];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await port.BaseStream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException
                    or InvalidOperationException
                    or UnauthorizedAccessException
                    or ObjectDisposedException
                    or TimeoutException)
                {
                    // Порт исчез: борт ушёл в перезагрузку или выдернули кабель.
                    // Для процедуры это ожидаемое событие, а не сбой приложения.
                    Log(MavSeverity.Warning, $"Чтение порта {_portName} прервано: {ex.Message}");
                    break;
                }

                if (read <= 0)
                {
                    await Task.Delay(1, ct).ConfigureAwait(false);
                    continue;
                }

                parser.Append(buffer.AsSpan(0, read));

                while (parser.TryReadFrame(out MavlinkFrame? frame))
                {
                    Dispatch(frame);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатное завершение при закрытии соединения.
        }
        catch (Exception ex)
        {
            // Задача не имеет владельца, который дождётся её исключения, поэтому
            // необработанный сбой разбора обязан быть виден в диагностике, а не
            // потерян в финализаторе Task.
            Log(MavSeverity.Error, $"Цикл приёма остановлен из-за ошибки: {ex}");
        }
        finally
        {
            _heartbeatSeen = false;

            // Ожидающие вызовы должны упасть немедленно, а не досиживать свои
            // таймауты на уже мёртвой линии.
            CompleteAllSubscriptions();
        }
    }

    private void Dispatch(MavlinkFrame frame)
    {
        try
        {
            if (_heartbeatSeen && frame.SystemId != _targetSystem)
            {
                return;
            }

            switch (frame.MessageId)
            {
                case MavMessageId.Heartbeat:
                    HandleHeartbeat(frame);
                    break;

                case MavMessageId.SysStatus:
                    HandleSysStatus(frame);
                    break;

                case MavMessageId.Attitude:
                    HandleAttitude(frame);
                    break;

                case MavMessageId.RawImu:
                    HandleMag(0, ImuMessage.DecodeRawImu(frame.Payload.Span));
                    break;

                case MavMessageId.ScaledImu2:
                    HandleMag(1, ImuMessage.DecodeScaledImu(frame.Payload.Span));
                    break;

                case MavMessageId.ScaledImu3:
                    HandleMag(2, ImuMessage.DecodeScaledImu(frame.Payload.Span));
                    break;

                case MavMessageId.GpsRawInt:
                    HandleGps(frame);
                    break;

                case MavMessageId.Gps2Raw:
                    HandleGps2(frame);
                    break;

                case MavMessageId.VfrHud:
                    HandleVfrHud(frame);
                    break;

                case MavMessageId.AutopilotVersion:
                    HandleAutopilotVersion(frame);
                    break;

                case MavMessageId.StatusText:
                    HandleStatusText(StatusTextMessage.Decode(frame.Payload.Span));
                    break;
            }
        }
        catch (Exception ex)
        {
            // Одно кривое сообщение не имеет права уронить приём остальных.
            Log(MavSeverity.Error, $"Сообщение {frame.MessageId} не обработано: {ex.Message}");
        }

        OfferToSubscriptions(frame);
    }

    private void HandleHeartbeat(MavlinkFrame frame)
    {
        HeartbeatMessage heartbeat = HeartbeatMessage.Decode(frame.Payload.Span);
        if (!heartbeat.IsAutopilot)
        {
            return;
        }

        // Режим и признак взведения обновляются каждым HEARTBEAT, а не только
        // первым: индикатор обязан показывать текущее состояние.
        lock (_stateLock)
        {
            _lastHeartbeat = heartbeat;
            _lastStateUpdateUtc = DateTimeOffset.UtcNow;
        }

        if (_heartbeatSeen)
        {
            return;
        }

        _targetSystem = frame.SystemId;
        _targetComponent = frame.ComponentId;
        _heartbeatSeen = true;
        Log(MavSeverity.Info, $"Борт обнаружен: система {frame.SystemId}, компонент {frame.ComponentId}.");
    }

    private void HandleSysStatus(MavlinkFrame frame)
    {
        SysStatusMessage status = SysStatusMessage.Decode(frame.Payload.Span);
        var health = new SensorHealth(status.SensorsPresent, status.SensorsEnabled, status.SensorsHealth);

        lock (_stateLock)
        {
            _lastHealth = health;
            _lastSysStatus = status;
            _lastStateUpdateUtc = DateTimeOffset.UtcNow;
        }

        lock (_sessionLock)
        {
            foreach (TelemetrySession session in _sessions)
            {
                session.SetHealth(health);
            }
        }
    }

    private void HandleAttitude(MavlinkFrame frame)
    {
        AttitudeMessage attitude = AttitudeMessage.Decode(frame.Payload.Span);
        var sample = new AttitudeSample(attitude.Roll, attitude.Pitch, attitude.Yaw);

        lock (_stateLock)
        {
            _lastAttitude = sample;
            _lastStateUpdateUtc = DateTimeOffset.UtcNow;
        }

        lock (_sessionLock)
        {
            foreach (TelemetrySession session in _sessions)
            {
                session.AddAttitude(sample);
            }
        }
    }

    private void HandleMag(int instance, ImuMessage imu)
    {
        var sample = new MagSample(instance, imu.Xmag, imu.Ymag, imu.Zmag);

        lock (_stateLock)
        {
            _lastMags[instance] = sample;
            _lastStateUpdateUtc = DateTimeOffset.UtcNow;
        }

        lock (_sessionLock)
        {
            foreach (TelemetrySession session in _sessions)
            {
                session.AddMag(sample);
            }
        }
    }

    private void HandleGps(MavlinkFrame frame)
    {
        GpsRawIntMessage gps = GpsRawIntMessage.Decode(frame.Payload.Span);
        lock (_stateLock)
        {
            _lastGpsFix = new GpsFix(
                gps.FixType,
                gps.Lat,
                gps.Lon,
                gps.SatellitesVisible,
                gps.Eph,
                gps.Epv,
                gps.Alt);

            _lastStateUpdateUtc = DateTimeOffset.UtcNow;
        }
    }

    private void HandleGps2(MavlinkFrame frame)
    {
        Gps2RawMessage gps = Gps2RawMessage.Decode(frame.Payload.Span);
        lock (_stateLock)
        {
            _lastGps2Fix = new GpsFix(
                gps.FixType,
                gps.Lat,
                gps.Lon,
                gps.SatellitesVisible,
                gps.Eph,
                gps.Epv,
                gps.Alt);

            _lastStateUpdateUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Складывает версию прошивки в человекочитаемую строку.
    /// </summary>
    /// <remarks>
    /// 🔴 Баннер, напечатанный при загрузке, достаётся только той станции,
    /// которая подключилась сразу после включения борта. Станция, пришедшая к
    /// уже работающей плате, не получала его никогда и честно сообщала «версия
    /// не получена» — о борте, чью версию можно было спросить одной командой.
    /// Ответ на запрос ценнее баннера ещё и тем, что приходит в любой момент,
    /// в том числе после перезагрузки, поэтому он баннер перекрывает.
    /// </remarks>
    private void HandleAutopilotVersion(MavlinkFrame frame)
    {
        AutopilotVersionMessage version = AutopilotVersionMessage.Decode(frame.Payload.Span);
        if (!version.HasVersion)
        {
            return;
        }

        byte type;
        lock (_stateLock)
        {
            type = _lastHeartbeat?.Type ?? 0;
        }

        var product = type switch
        {
            1 or 19 or 20 or 21 or 22 or 23 or 24 or 25 or 28 => "ArduPlane",
            2 or 3 or 4 or 13 or 14 or 15 or 29 => "ArduCopter",
            10 or 11 => "ArduRover",
            12 => "ArduSub",
            _ => "ArduPilot",
        };

        var banner = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{product} V{version.Major}.{version.Minor}.{version.Patch}");

        if (version.FlightCustomVersion.Length > 0)
        {
            banner += $" ({version.FlightCustomVersion})";
        }

        _firmwareBanner = banner;
    }

    private void HandleVfrHud(MavlinkFrame frame)
    {
        VfrHudMessage hud = VfrHudMessage.Decode(frame.Payload.Span);
        lock (_stateLock)
        {
            _lastAir = new AirData(
                hud.Airspeed,
                hud.Groundspeed,
                hud.Alt,
                hud.Climb,
                hud.Heading,
                hud.Throttle);

            _lastStateUpdateUtc = DateTimeOffset.UtcNow;
        }
    }

    // ------------------------------------------------------- STATUSTEXT

    private const string PrearmPrefix = "PreArm:";

    /// <summary>
    /// Собирает <c>STATUSTEXT</c> из чанков и поднимает событие с готовым текстом.
    /// </summary>
    /// <remarks>
    /// Поднимать чанки по отдельности нельзя: граница чанка приходится на
    /// произвольный символ и запросто рассекает строку вида
    /// <c>PreArm: Compass not calibrated</c>. Приёмка ищет префикс
    /// <c>PreArm:</c> и подстроку про компас — по половинкам ни то, ни другое не
    /// находится, и проверка молча проходит.
    /// </remarks>
    private void HandleStatusText(StatusTextMessage message)
    {
        if (message.Id == 0)
        {
            // id = 0 — сообщение целое и в чанки не разбивалось.
            RaiseStatusText(message.Severity, message.Text);
            return;
        }

        byte severity;
        string text;

        lock (_statusTextLock)
        {
            PruneStaleAssemblies();

            if (!_statusTextAssemblies.TryGetValue(message.Id, out StatusTextAssembly? assembly))
            {
                if (message.ChunkSeq != 0)
                {
                    // Начало сообщения потеряно — склеивать не из чего.
                    Log(MavSeverity.Debug, $"STATUSTEXT {message.Id}: пропущено начало, чанк {message.ChunkSeq} отброшен.");
                    return;
                }

                assembly = new StatusTextAssembly(message.Severity);
                _statusTextAssemblies[message.Id] = assembly;
            }

            if (message.ChunkSeq != assembly.NextChunk)
            {
                // Дырка в нумерации: склеенный текст был бы правдоподобным, но
                // неверным, а это хуже отсутствующего.
                _statusTextAssemblies.Remove(message.Id);
                Log(MavSeverity.Debug, $"STATUSTEXT {message.Id}: ожидался чанк {assembly.NextChunk}, пришёл {message.ChunkSeq}.");
                return;
            }

            assembly.Append(message.Text);

            if (message.IsTextFieldFull)
            {
                // Поле text занято целиком — продолжение придёт следующим чанком.
                return;
            }

            _statusTextAssemblies.Remove(message.Id);
            severity = assembly.Severity;
            text = assembly.Text;
        }

        RaiseStatusText(severity, text);
    }

    private void PruneStaleAssemblies()
    {
        if (_statusTextAssemblies.Count == 0)
        {
            return;
        }

        DateTimeOffset threshold = DateTimeOffset.UtcNow - StatusTextAssemblyLifetime;
        List<ushort>? stale = null;

        foreach (KeyValuePair<ushort, StatusTextAssembly> entry in _statusTextAssemblies)
        {
            if (entry.Value.StartedUtc < threshold)
            {
                (stale ??= new List<ushort>()).Add(entry.Key);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (ushort id in stale)
        {
            _statusTextAssemblies.Remove(id);
            Log(MavSeverity.Debug, $"STATUSTEXT {id}: последний чанк не пришёл, сборка отброшена.");
        }
    }

    /// <summary>
    /// Поднимает <see cref="StatusTextReceived"/> с текстом ровно в том виде, в
    /// каком его прислал борт: снимать префикс <c>PreArm: </c> здесь нельзя,
    /// это решает потребитель.
    /// </summary>
    private void RaiseStatusText(byte severity, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        CaptureFirmwareBanner(text);

        // MAV_SEVERITY определён только до 7; всё, что выше, приводим к Debug,
        // чтобы приведение к enum не давало значения вне диапазона.
        var level = (MavSeverity)Math.Min(severity, (byte)MavSeverity.Debug);
        var message = new StatusTextEvent(level, text, DateTimeOffset.UtcNow);

        try
        {
            StatusTextReceived?.Invoke(this, message);
        }
        catch (Exception ex)
        {
            // Обработчик живёт в чужом слое (обычно UI). Его ошибка не должна
            // останавливать приём телеметрии, но и потеряться она не может.
            Log(MavSeverity.Error, $"Подписчик StatusTextReceived бросил {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Запоминает баннер прошивки. ArduPilot рассылает его как обычный
    /// <c>STATUSTEXT</c> сразу после старта, отдельного сообщения для него нет.
    /// </summary>
    private void CaptureFirmwareBanner(string text)
    {
        if (_firmwareBanner is not null)
        {
            return;
        }

        if (text.StartsWith("Ardu", StringComparison.Ordinal)
            || text.StartsWith("APM:", StringComparison.Ordinal)
            || text.StartsWith("AntennaTracker", StringComparison.Ordinal))
        {
            _firmwareBanner = text;
        }
    }

    private static string StripPrearmPrefix(string text)
    {
        string rest = text[PrearmPrefix.Length..];
        return rest.TrimStart();
    }

    // ------------------------------------------------------- подписки

    // ------------------------------------------------------------ MAVFTP

    /// <summary>Таймаут одного обмена MAVFTP.</summary>
    private static readonly TimeSpan FtpTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Сколько раз повторяется потерянный обмен.</summary>
    private const int FtpRetries = 3;

    /// <summary>
    /// Верхняя граница размера читаемого файла.
    /// </summary>
    /// <remarks>
    /// Скрипт Lua на борту — единицы килобайт. Граница стоит не ради экономии
    /// памяти, а против зацикливания: сервер, отвечающий пустыми кусками без
    /// признака конца файла, иначе тянул бы чтение бесконечно.
    /// </remarks>
    private const int FtpMaxFileBytes = 1 << 20;

    private readonly SemaphoreSlim _ftpGate = new(1, 1);

    private ushort _ftpSequence;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MavFtpEntry>> ListDirectoryAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureConnected();

        await _ftpGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = new List<MavFtpEntry>();
            uint offset = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var reply = await FtpRequestAsync(
                    session: 0,
                    MavFtpOpcode.ListDirectory,
                    offset,
                    Encoding.UTF8.GetBytes(NormalizeFtpPath(path)),
                    allowNak: true,
                    ct).ConfigureAwait(false);

                if (reply.Opcode == MavFtpOpcode.Nak)
                {
                    if (reply.Error == MavFtpError.EndOfFile)
                    {
                        break;
                    }

                    throw new VehicleLinkException(
                        $"Каталог «{path}» не прочитан: {reply.DescribeError()}.");
                }

                var consumed = MavFtpDirectory.Parse(reply.Data.Span, entries);
                if (consumed == 0)
                {
                    // Ответ без записей и без признака конца — дальше двигаться
                    // некуда, а повтор дал бы тот же ответ. Считаем каталог
                    // прочитанным, а не зацикливаемся.
                    break;
                }

                offset += (uint)consumed;
            }

            return entries;
        }
        finally
        {
            _ftpGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> ReadFileAsync(string path, IProgress<long>? progress, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureConnected();

        await _ftpGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var session = await OpenFtpFileAsync(
                MavFtpOpcode.OpenFileRO, path, "не открыт", ct).ConfigureAwait(false);

            var content = new List<byte>(4096);

            try
            {
                uint offset = 0;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    var chunk = await FtpRequestAsync(
                        session,
                        MavFtpOpcode.ReadFile,
                        offset,
                        ReadOnlyMemory<byte>.Empty,
                        allowNak: true,
                        ct,
                        requestedSize: MavFtpPayload.MaxDataLength).ConfigureAwait(false);

                    if (chunk.Opcode == MavFtpOpcode.Nak)
                    {
                        if (chunk.Error == MavFtpError.EndOfFile)
                        {
                            break;
                        }

                        throw new VehicleLinkException($"Файл «{path}» не дочитан: {chunk.DescribeError()}.");
                    }

                    if (chunk.Data.Length == 0)
                    {
                        // Пустой ACK без EOF: файл кончился, но сервер сообщил об
                        // этом иначе. Продолжать нечем.
                        break;
                    }

                    content.AddRange(chunk.Data.ToArray());
                    offset += (uint)chunk.Data.Length;
                    progress?.Report(content.Count);

                    if (content.Count > FtpMaxFileBytes)
                    {
                        throw new VehicleLinkException(
                            $"Файл «{path}» превысил {FtpMaxFileBytes / 1024} КиБ. "
                          + "Для скрипта это ненормальный размер; чтение прервано.");
                    }
                }
            }
            finally
            {
                await TerminateFtpSessionAsync(session).ConfigureAwait(false);
            }

            return content.ToArray();
        }
        finally
        {
            _ftpGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task WriteFileAsync(
        string path,
        ReadOnlyMemory<byte> content,
        IProgress<long>? progress,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureConnected();

        await _ftpGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // CreateFile создаёт файл заново, обрезая существующий. Именно это и
            // нужно: дозапись поверх старого содержимого дала бы склейку двух
            // скриптов, синтаксически верную и делающую не то.
            var session = await OpenFtpFileAsync(
                MavFtpOpcode.CreateFile, path, "не создан", ct).ConfigureAwait(false);

            try
            {
                var written = 0;
                while (written < content.Length)
                {
                    ct.ThrowIfCancellationRequested();

                    var size = Math.Min(MavFtpPayload.MaxDataLength, content.Length - written);
                    var chunk = content.Slice(written, size);

                    var ack = await FtpRequestAsync(
                        session,
                        MavFtpOpcode.WriteFile,
                        (uint)written,
                        chunk,
                        allowNak: true,
                        ct).ConfigureAwait(false);

                    if (ack.Opcode == MavFtpOpcode.Nak)
                    {
                        throw new VehicleLinkException(
                            $"Файл «{path}» не дописан со смещения {written}: {ack.DescribeError()}.");
                    }

                    written += size;
                    progress?.Report(written);
                }
            }
            finally
            {
                // Сессия закрывается в любом случае: у борта их единицы, и
                // брошенная сессия делает следующую запись невозможной до
                // перезагрузки.
                await TerminateFtpSessionAsync(session).ConfigureAwait(false);
            }
        }
        finally
        {
            _ftpGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RemoveFileAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureConnected();

        await _ftpGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var reply = await FtpRequestAsync(
                session: 0,
                MavFtpOpcode.RemoveFile,
                offset: 0,
                Encoding.UTF8.GetBytes(NormalizeFtpPath(path)),
                allowNak: true,
                ct).ConfigureAwait(false);

            // Отсутствие файла — это уже требуемое состояние, а не отказ:
            // повторное удаление не должно валить операцию.
            if (reply.Opcode == MavFtpOpcode.Nak && reply.Error != MavFtpError.FileNotFound)
            {
                throw new VehicleLinkException($"Файл «{path}» не удалён: {reply.DescribeError()}.");
            }
        }
        finally
        {
            _ftpGate.Release();
        }
    }

    /// <summary>
    /// Открывает файловую сессию борта под чтение или запись и возвращает её номер.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Открытие сессии — единственное место, где состояние борта видно клиенту,
    /// и потому единственное место, где его можно и нужно чинить. Сессия у борта
    /// одна: пока в ней открыт файл, любое следующее открытие отвергается общим
    /// кодом <c>Fail</c> — и так до перезагрузки борта. Закрыть чужую сессию некому,
    /// клиент её не открывал: она осталась от оборванного кабеля, снятого питания
    /// или потерянного ответа на предыдущее открытие, после которого повтор ушёл
    /// уже в занятый сервер.
    /// </para>
    /// <para>
    /// 🔴 Лечение стоит здесь, а не на месте вызова, потому что прежняя редакция
    /// лечила его на одном месте вызова из двух: запись сбрасывала сессии перед
    /// созданием файла, чтение — нет. Один и тот же отказ борта поэтому чинился
    /// при выгрузке скрипта и оставался вечным при его чтении: снятие с борта
    /// сообщало «файл не открыт: отказ файловой операции» по всем файлам подряд,
    /// и эталон снимался пустым при полной карте скриптов. Общая предпосылка
    /// операции обязана жить в одном месте, иначе она чинится по одному найденному
    /// симптому.
    /// </para>
    /// <para>
    /// Сброс делается по отказу, а не перед каждым открытием: лишний обмен на
    /// каждый файл снимка — это десятки лишних обменов на исправном борту ради
    /// случая, который виден по ответу.
    /// </para>
    /// </remarks>
    /// <param name="opcode"><see cref="MavFtpOpcode.OpenFileRO"/> либо <see cref="MavFtpOpcode.CreateFile"/>.</param>
    /// <param name="path">Путь на карте борта — в сообщении об отказе он приводится как задан.</param>
    /// <param name="failureVerb">Чем кончилось дело для оператора: «не открыт», «не создан».</param>
    /// <exception cref="VehicleLinkException">Борт отказал и после сброса сессий.</exception>
    private async Task<byte> OpenFtpFileAsync(
        MavFtpOpcode opcode,
        string path,
        string failureVerb,
        CancellationToken ct)
    {
        byte[] name = Encoding.UTF8.GetBytes(NormalizeFtpPath(path));

        var reply = await FtpRequestAsync(session: 0, opcode, offset: 0, name, allowNak: true, ct)
            .ConfigureAwait(false);

        if (reply.Opcode != MavFtpOpcode.Nak)
        {
            return reply.Session;
        }

        if (!reply.IndicatesStaleSession)
        {
            throw new VehicleLinkException($"Файл «{path}» {failureVerb}: {reply.DescribeError()}.");
        }

        Log(
            MavSeverity.Debug,
            $"MAVFTP {opcode} «{path}»: {reply.DescribeError()}. Сбрасываю файловые сессии и повторяю.");

        await ResetFtpSessionsAsync(ct).ConfigureAwait(false);

        reply = await FtpRequestAsync(session: 0, opcode, offset: 0, name, allowNak: true, ct)
            .ConfigureAwait(false);

        if (reply.Opcode == MavFtpOpcode.Nak)
        {
            throw new VehicleLinkException(
                $"Файл «{path}» {failureVerb} и после сброса файловых сессий: {reply.DescribeError()}.");
        }

        return reply.Session;
    }

    /// <summary>
    /// Сбрасывает файловые сессии борта.
    /// </summary>
    /// <remarks>
    /// Ответ на сброс не обязателен: часть прошивок на него не отвечает вовсе.
    /// Ждать подтверждения — значит превратить полезную гигиену в отказ там, где
    /// всё в порядке, поэтому неподтверждённый сброс только пишется в журнал.
    /// </remarks>
    private async Task ResetFtpSessionsAsync(CancellationToken ct)
    {
        try
        {
            await FtpRequestAsync(
                session: 0,
                MavFtpOpcode.ResetSessions,
                offset: 0,
                ReadOnlyMemory<byte>.Empty,
                allowNak: true,
                ct).ConfigureAwait(false);
        }
        catch (VehicleLinkException ex)
        {
            Log(MavSeverity.Debug, $"Сброс файловых сессий не подтверждён: {ex.Message}");
        }
    }

    /// <summary>
    /// Один обмен MAVFTP с повторами.
    /// </summary>
    /// <remarks>
    /// 🔴 Ответ опознаётся по номеру запроса плюс единица — так предписывает
    /// протокол и так поступают наземные станции. Опознание по одному лишь коду
    /// операции недопустимо: после повтора в канале оказываются два ответа на
    /// два разных запроса с одинаковым кодом, и второй кусок файла встал бы на
    /// место первого.
    /// </remarks>
    /// <param name="allowNak">
    /// <c>true</c> — отказ возвращается вызывающему как значение. Так сделано
    /// потому, что <see cref="MavFtpError.EndOfFile"/> — это штатное завершение
    /// чтения, а не сбой, и превращать его в исключение значит управлять
    /// нормальным ходом дела через исключения.
    /// </param>
    /// <param name="requestedSize">
    /// Сколько байт запрашивается у сервера. Осмысленно для чтения: поле
    /// <c>size</c> в запросе означает не длину данных, а желаемый объём ответа.
    /// </param>
    private async Task<MavFtpPayload> FtpRequestAsync(
        byte session,
        MavFtpOpcode opcode,
        uint offset,
        ReadOnlyMemory<byte> data,
        bool allowNak,
        CancellationToken ct,
        int requestedSize = -1)
    {
        // ReadOnlyMemory, а не ReadOnlySpan: ссылочная структура не может быть
        // параметром асинхронного метода — она не переживает точку ожидания.
        byte[] payload = MavFtpPayload.EncodeRequest(_ftpSequence, session, opcode, offset, data.Span);
        if (requestedSize >= 0)
        {
            payload[4] = (byte)Math.Min(requestedSize, MavFtpPayload.MaxDataLength);
        }

        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var expected = unchecked((ushort)(_ftpSequence + 1));

            try
            {
                using FrameSubscription subscription = Subscribe(frame =>
                    frame.MessageId == MavMessageId.FileTransferProtocol
                    && MavFtpPayload.TryDecode(frame.Payload.Span, out var reply)
                    && reply.Sequence == expected);

                byte[] frame = _encoder.EncodeFileTransferProtocol(TargetSystem, TargetComponent, payload);
                await SendAsync(frame, ct).ConfigureAwait(false);

                MavlinkFrame answer = await subscription
                    .ReadAsync(FtpTimeout, $"ответ MAVFTP на {opcode}", ct)
                    .ConfigureAwait(false);

                if (!MavFtpPayload.TryDecode(answer.Payload.Span, out var result))
                {
                    throw new VehicleLinkException($"Ответ MAVFTP на {opcode} не разбирается.");
                }

                // Номер двигается только после принятого ответа: иначе повтор
                // ушёл бы с новым номером, и запоздавший ответ на прошлую
                // попытку был бы принят за ответ на текущую.
                unchecked
                {
                    _ftpSequence += 2;
                }

                if (result.Opcode == MavFtpOpcode.Nak && !allowNak)
                {
                    throw new VehicleLinkException(
                        $"Борт отклонил операцию MAVFTP {opcode}: {result.DescribeError()}.");
                }

                return result;
            }
            catch (VehicleLinkException) when (attempt < FtpRetries)
            {
                Log(MavSeverity.Debug, $"MAVFTP {opcode}: попытка {attempt + 1} не удалась, повтор.");

                // Номер обязан смениться и у повтора, иначе запоздавший ответ на
                // предыдущую попытку не отличить от ответа на новую.
                unchecked
                {
                    _ftpSequence += 2;
                }

                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0), _ftpSequence);
            }
        }
    }

    /// <summary>
    /// Закрывает файловую сессию, не позволяя отказу закрытия скрыть исход
    /// самой операции.
    /// </summary>
    /// <remarks>
    /// Вызывается из <c>finally</c>, поэтому исключений не выпускает: сессий у
    /// борта единицы, но потерянная сессия — меньшая беда, чем подменённая
    /// причина отказа чтения.
    /// </remarks>
    private async Task TerminateFtpSessionAsync(byte session)
    {
        try
        {
            await FtpRequestAsync(
                session,
                MavFtpOpcode.TerminateSession,
                offset: 0,
                ReadOnlyMemory<byte>.Empty,
                allowNak: true,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log(MavSeverity.Debug, $"Сессия MAVFTP {session} не закрыта: {ex.Message}");
        }
    }

    /// <summary>
    /// Приводит путь к виду, который ждёт сервер: без ведущего слэша и с прямыми
    /// разделителями.
    /// </summary>
    /// <remarks>
    /// 🔴 Обратный слэш Windows на борту разделителем не является: он попадает
    /// в имя файла, и вместо <c>APM/scripts/x.lua</c> сервер ищет файл с именем
    /// <c>APM\scripts\x.lua</c> в корне — получая «файл не найден» там, где файл
    /// есть.
    /// </remarks>
    private static string NormalizeFtpPath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private FrameSubscription Subscribe(Func<MavlinkFrame, bool> predicate)
    {
        var subscription = new FrameSubscription(this, predicate);
        lock (_subscriptionLock)
        {
            _subscriptions.Add(subscription);
        }

        return subscription;
    }

    private void Unsubscribe(FrameSubscription subscription)
    {
        lock (_subscriptionLock)
        {
            _subscriptions.Remove(subscription);
        }
    }

    private void OfferToSubscriptions(MavlinkFrame frame)
    {
        FrameSubscription[] snapshot;
        lock (_subscriptionLock)
        {
            if (_subscriptions.Count == 0)
            {
                return;
            }

            snapshot = _subscriptions.ToArray();
        }

        foreach (FrameSubscription subscription in snapshot)
        {
            subscription.Offer(frame);
        }
    }

    private void CompleteAllSubscriptions()
    {
        FrameSubscription[] snapshot;
        lock (_subscriptionLock)
        {
            snapshot = _subscriptions.ToArray();
        }

        foreach (FrameSubscription subscription in snapshot)
        {
            subscription.Complete();
        }
    }

    // ------------------------------------------------------- отправка

    private async Task SendAsync(byte[] frame, CancellationToken ct)
    {
        SerialPort? port;
        lock (_stateLock)
        {
            port = _port;
        }

        if (port is not { IsOpen: true })
        {
            throw new VehicleLinkException("Порт закрыт: отправить сообщение борту невозможно.");
        }

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await port.BaseStream.WriteAsync(frame, ct).ConfigureAwait(false);
            await port.BaseStream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException
            or InvalidOperationException
            or ObjectDisposedException
            or UnauthorizedAccessException
            or TimeoutException)
        {
            throw new VehicleLinkException($"Не удалось отправить сообщение в порт {_portName}: {ex.Message}", ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ------------------------------------------------------- соединение

    private async Task WaitForFirstHeartbeatAsync(TimeSpan timeout, CancellationToken ct)
    {
        using FrameSubscription subscription = Subscribe(frame =>
            frame.MessageId == MavMessageId.Heartbeat
            && HeartbeatMessage.Decode(frame.Payload.Span).IsAutopilot);

        await subscription.ReadAsync(timeout, $"HEARTBEAT от борта на порту {_portName}", ct).ConfigureAwait(false);

        if (!_heartbeatSeen)
        {
            throw new VehicleLinkException($"Борт на порту {_portName} не опознан: HEARTBEAT получен, но не от автопилота.");
        }
    }

    /// <summary>
    /// Запрашивает потоки телеметрии командой <c>MAV_CMD_SET_MESSAGE_INTERVAL</c>.
    /// </summary>
    /// <remarks>
    /// Отказ по одному сообщению не отменяет соединение: старая прошивка может
    /// не уметь задавать интервал именно этому сообщению, а остальные потоки при
    /// этом пойдут. Оборвать здесь подключение значило бы потерять работоспособный
    /// борт из-за необязательного потока.
    /// </remarks>
    /// <summary>
    /// Просит борт прислать <c>AUTOPILOT_VERSION</c>.
    /// </summary>
    /// <remarks>
    /// Отказ не является отказом подключения: версия — сведение о борте, а не
    /// условие работы. Но и молчать о неудаче нельзя, иначе пустая версия
    /// выглядит как свойство прошивки.
    /// </remarks>
    private async Task RequestAutopilotVersionAsync(CancellationToken ct)
    {
        try
        {
            MavResult result = await SendCommandAsync(
                RequestMessage,
                MavMessageId.AutopilotVersion,
                0, 0, 0, 0, 0,
                p7: 0,
                StreamRequestAckTimeout,
                ct).ConfigureAwait(false);

            if (result != MavResult.Accepted)
            {
                Log(MavSeverity.Warning, $"Версия прошивки не запрошена: борт ответил {result}.");
            }
        }
        catch (VehicleLinkException ex)
        {
            Log(MavSeverity.Warning, $"Версия прошивки не запрошена: {ex.Message}");
        }
    }

    private async Task RequestRequiredStreamsAsync(CancellationToken ct)
    {
        foreach ((uint messageId, int intervalMicroseconds, string name) in RequiredStreams)
        {
            try
            {
                MavResult result = await SendCommandAsync(
                    SetMessageInterval,
                    messageId,
                    intervalMicroseconds,
                    0, 0, 0, 0,
                    p7: 0,
                    StreamRequestAckTimeout,
                    ct).ConfigureAwait(false);

                if (result != MavResult.Accepted)
                {
                    Log(MavSeverity.Warning, $"Поток {name} не запрошен: борт ответил {result}.");
                }
            }
            catch (VehicleLinkException ex)
            {
                Log(MavSeverity.Warning, $"Поток {name} не запрошен: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Отправляет <c>MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN</c>.
    /// </summary>
    /// <remarks>
    /// Прошивка подтверждает команду до того, как уйти в перезагрузку, поэтому
    /// подтверждение вполне может не успеть уйти в порт. Отсутствие
    /// <c>COMMAND_ACK</c> здесь не считается отказом, и повторять команду нельзя:
    /// вторая перезагрузка посреди загрузки хуже, чем неполученное подтверждение.
    /// </remarks>
    private async Task SendRebootAsync(CancellationToken ct)
    {
        try
        {
            MavResult result = await SendCommandAsync(
                MavCommand.PreflightRebootShutdown,
                p1: 1,
                0, 0, 0, 0, 0, 0,
                RebootAckTimeout,
                ct).ConfigureAwait(false);

            if (result != MavResult.Accepted)
            {
                Log(MavSeverity.Warning, $"Перезагрузка подтверждена результатом {result}; ждём возвращения борта.");
            }
        }
        catch (VehicleLinkException ex)
        {
            Log(MavSeverity.Notice, $"Подтверждение перезагрузки не получено ({ex.Message}); это ожидаемо, ждём возвращения борта.");
        }
    }

    private void EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (!IsConnected)
        {
            throw new VehicleLinkException("Соединение с бортом не установлено.");
        }
    }

    private GpsFix? ReadLastGpsFix()
    {
        lock (_stateLock)
        {
            return _lastGpsFix;
        }
    }

    private VehicleLinkException BuildGpsTimeoutException(TimeSpan timeout, GpsFix? last, Exception? inner = null)
    {
        GpsFix? known = last ?? ReadLastGpsFix();
        string detail = known is { } fix
            ? $"последний fix_type = {fix.FixType}, спутников видно {fix.SatellitesVisible}"
            : "сообщений GPS_RAW_INT не приходило вовсе";

        string message =
            $"GPS не вышел на трёхмерный фикс за {timeout.TotalSeconds:0} с: {detail}. " +
            "Если спутников мало — подождите дольше; если их ноль, перенесите стапель ближе к окну или проверьте антенну.";

        return inner is null ? new VehicleLinkException(message) : new VehicleLinkException(message, inner);
    }

    /// <summary>
    /// Останавливает цикл приёма и закрывает порт. Безопасно вызывать повторно и
    /// на незапущенном соединении.
    /// </summary>
    private async Task ShutdownAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;
        SerialPort? port;

        lock (_stateLock)
        {
            cts = _loopCts;
            loop = _loopTask;
            port = _port;
            _loopCts = null;
            _loopTask = null;
            _port = null;
        }

        _heartbeatSeen = false;

        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Источник уже освобождён параллельным завершением — цель достигнута.
            }
        }

        if (port is not null)
        {
            // SerialPort.Dispose умеет зависать, если внутри драйвера висит
            // незавершённое чтение. Закрытие уводится в пул потоков с таймаутом:
            // в худшем случае мы потеряем дескриптор, но не заблокируем вызывающего.
            Task close = Task.Run(() =>
            {
                try
                {
                    port.Dispose();
                }
                catch (Exception ex) when (ex is IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException)
                {
                    Log(MavSeverity.Warning, $"Порт закрылся с ошибкой: {ex.Message}");
                }
            });

            try
            {
                await close.WaitAsync(LoopShutdownTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Log(MavSeverity.Warning, $"Закрытие порта {_portName} не завершилось за {LoopShutdownTimeout.TotalSeconds:0} с.");
            }
        }

        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(LoopShutdownTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Log(MavSeverity.Warning, "Цикл приёма не остановился вовремя.");
            }
            catch (OperationCanceledException)
            {
                // Отмена — это и есть запрошенное завершение цикла.
            }
        }

        cts?.Dispose();
        CompleteAllSubscriptions();
    }

    private void Log(MavSeverity severity, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[SerialVehicleLink] {severity}: {message}");

        try
        {
            DiagnosticLog?.Invoke(severity, message);
        }
        catch (Exception ex)
        {
            // Сбой в диагностике не имеет права влиять на работу канала, и
            // логировать его тем же способом бессмысленно.
            System.Diagnostics.Debug.WriteLine($"[SerialVehicleLink] DiagnosticLog бросил {ex.GetType().Name}.");
        }
    }

    // ------------------------------------------------- поиск порта после ребута

    /// <summary>
    /// Идентификаторы USB, под которыми появляются платы ArduPilot.
    /// </summary>
    /// <remarks>
    /// У вендора 0x1209 (pid.codes, общая песочница для открытых проектов)
    /// проверяется и PID: сам по себе этот VID ничего не говорит о плате.
    /// </remarks>
    private static readonly (ushort Vendor, ushort Product)[] KnownVendorProductIds =
    {
        (0x1209, 0x5740),
        (0x1209, 0x5741),
    };

    /// <summary>Вендоры, у которых подходит любой продукт: CubePilot, Holybro, 3DR/PX4.</summary>
    private static readonly ushort[] KnownVendorIds = { 0x2DAE, 0x3162, 0x26AC };

    /// <summary>
    /// Собирает список портов-кандидатов после перезагрузки.
    /// </summary>
    /// <remarks>
    /// Сначала пробуется прежнее имя, потом порты с подходящим VID/PID. Номер
    /// COM после перезагрузки меняется штатно: плата уходит с шины без detach, и
    /// Windows может выдать ей новый номер.
    /// <para>
    /// Соответствие «USB-устройство — имя порта» берётся из реестра
    /// (<c>HKLM\SYSTEM\CurrentControlSet\Enum\USB</c>), а не из WMI: сборка
    /// <c>System.Management</c> в net10.0-windows без отдельной ссылки на пакет
    /// недоступна, а добавлять зависимость ради перечисления портов не нужно.
    /// Если реестр по какой-то причине закрыт, метод откатывается на
    /// <see cref="SerialPort.GetPortNames"/> и перебирает все порты подряд.
    /// </para>
    /// </remarks>
    private List<string> EnumerateCandidatePorts(string originalPort)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] present;
        try
        {
            present = SerialPort.GetPortNames();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(MavSeverity.Warning, $"Список портов недоступен: {ex.Message}");
            present = Array.Empty<string>();
        }

        var available = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);

        if (available.Contains(originalPort) && seen.Add(originalPort))
        {
            candidates.Add(originalPort);
        }

        IReadOnlyList<string>? matched = TryEnumerateArduPilotPorts();
        if (matched is null)
        {
            // Реестр недоступен — берём всё, что есть, и полагаемся на HEARTBEAT
            // как на единственный признак «это наш борт».
            foreach (string port in present)
            {
                if (seen.Add(port))
                {
                    candidates.Add(port);
                }
            }

            return candidates;
        }

        foreach (string port in matched)
        {
            if (available.Contains(port) && seen.Add(port))
            {
                candidates.Add(port);
            }
        }

        return candidates;
    }

    /// <summary>
    /// Перечисляет COM-порты плат ArduPilot по реестру.
    /// </summary>
    /// <returns><c>null</c>, если реестр недоступен и результат нельзя считать полным.</returns>
    private IReadOnlyList<string>? TryEnumerateArduPilotPorts()
    {
        var ports = new List<string>();

        try
        {
            using RegistryKey? usbRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usbRoot is null)
            {
                return null;
            }

            foreach (string deviceKeyName in usbRoot.GetSubKeyNames())
            {
                if (!MatchesKnownVendor(deviceKeyName))
                {
                    continue;
                }

                using RegistryKey? deviceKey = usbRoot.OpenSubKey(deviceKeyName);
                if (deviceKey is null)
                {
                    continue;
                }

                foreach (string instanceKeyName in deviceKey.GetSubKeyNames())
                {
                    using RegistryKey? instanceKey = deviceKey.OpenSubKey(instanceKeyName);
                    if (instanceKey is null)
                    {
                        continue;
                    }

                    string? description = instanceKey.GetValue("FriendlyName") as string
                        ?? instanceKey.GetValue("DeviceDesc") as string;

                    if (description is not null && IsBootloader(description))
                    {
                        // Загрузчик отдаёт свой COM-порт, но MAVLink на нём нет:
                        // попытка подключиться к нему съела бы весь таймаут.
                        continue;
                    }

                    using RegistryKey? parameters = instanceKey.OpenSubKey("Device Parameters");
                    if (parameters?.GetValue("PortName") is string portName
                        && portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                    {
                        ports.Add(portName);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException
            or UnauthorizedAccessException
            or IOException)
        {
            Log(MavSeverity.Debug, $"Реестр USB-устройств недоступен ({ex.GetType().Name}), перебираем все порты.");
            return null;
        }

        return ports;
    }

    /// <summary>
    /// Разбирает имя ключа вида <c>VID_1209&amp;PID_5741&amp;MI_00</c>.
    /// </summary>
    private static bool MatchesKnownVendor(string deviceKeyName)
    {
        if (!TryParseHexToken(deviceKeyName, "VID_", out ushort vendor))
        {
            return false;
        }

        if (Array.IndexOf(KnownVendorIds, vendor) >= 0)
        {
            return true;
        }

        if (!TryParseHexToken(deviceKeyName, "PID_", out ushort product))
        {
            return false;
        }

        foreach ((ushort knownVendor, ushort knownProduct) in KnownVendorProductIds)
        {
            if (vendor == knownVendor && product == knownProduct)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseHexToken(string source, string prefix, out ushort value)
    {
        value = 0;

        int start = source.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return false;
        }

        start += prefix.Length;
        if (start + 4 > source.Length)
        {
            return false;
        }

        return ushort.TryParse(
            source.AsSpan(start, 4),
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }

    /// <summary>
    /// Опознаёт загрузчик по имени вида <c>CubeOrange-BL</c>.
    /// </summary>
    /// <remarks>
    /// Windows дописывает к дружественному имени номер порта в скобках, поэтому
    /// перед проверкой суффикса хвост <c>" (COM7)"</c> отбрасывается.
    /// </remarks>
    private static bool IsBootloader(string description)
    {
        string name = description;

        int parenthesis = name.LastIndexOf('(');
        if (parenthesis > 0 && name.EndsWith(')'))
        {
            name = name[..parenthesis];
        }

        return name.TrimEnd().EndsWith("-BL", StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------- вспомогательные типы

    /// <summary>
    /// Очередь кадров, отобранных предикатом, для одной ожидающей операции.
    /// </summary>
    /// <remarks>
    /// Подписка ставится до отправки запроса, поэтому ответ не может проскочить
    /// между отправкой и началом ожидания. Канал не ограничен по размеру, но
    /// живёт ровно столько, сколько длится операция.
    /// </remarks>
    private sealed class FrameSubscription : IDisposable
    {
        private readonly SerialVehicleLink _owner;
        private readonly Func<MavlinkFrame, bool> _predicate;
        private readonly Channel<MavlinkFrame> _channel =
            Channel.CreateUnbounded<MavlinkFrame>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });

        private int _disposed;

        internal FrameSubscription(SerialVehicleLink owner, Func<MavlinkFrame, bool> predicate)
        {
            _owner = owner;
            _predicate = predicate;
        }

        /// <summary>Вызывается из цикла приёма для каждого разобранного кадра.</summary>
        internal void Offer(MavlinkFrame frame)
        {
            try
            {
                if (_predicate(frame))
                {
                    _channel.Writer.TryWrite(frame);
                }
            }
            catch (Exception ex)
            {
                // Предикат разбирает полезную нагрузку и в теории может упасть.
                // Цикл приёма из-за одной подписки останавливаться не должен.
                _owner.Log(MavSeverity.Error, $"Предикат подписки бросил {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>Закрывает очередь: линия оборвана, ждать больше нечего.</summary>
        internal void Complete() => _channel.Writer.TryComplete();

        internal async Task<MavlinkFrame> ReadAsync(TimeSpan timeout, string what, CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            try
            {
                return await _channel.Reader.ReadAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new VehicleLinkException(
                    $"Истёк таймаут {timeout.TotalSeconds:0.#} с при ожидании: {what}.");
            }
            catch (ChannelClosedException ex)
            {
                throw new VehicleLinkException($"Связь с бортом потеряна во время ожидания: {what}.", ex);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _owner.Unsubscribe(this);
            Complete();
        }
    }

    /// <summary>Накопитель телеметрии за одно окно наблюдения.</summary>
    private sealed class TelemetrySession
    {
        private readonly object _lock = new();
        private readonly Dictionary<int, MagAccumulator> _mags = new();

        private double _sinRoll;
        private double _cosRoll;
        private double _sinPitch;
        private double _cosPitch;
        private double _sinYaw;
        private double _cosYaw;
        private int _attitudeCount;
        private SensorHealth? _health;

        /// <summary>
        /// Углы копятся не суммой, а проекциями на единичную окружность.
        /// </summary>
        /// <remarks>
        /// Среднее арифметическое углов неверно на переходе через ±π: пара
        /// отсчётов 179° и −179° даёт 0° вместо 180°. Курс борта на стапеле может
        /// стоять как раз около этой границы, поэтому усредняются синус и косинус,
        /// а угол восстанавливается через <c>atan2</c>.
        /// </remarks>
        internal void AddAttitude(AttitudeSample sample)
        {
            lock (_lock)
            {
                _sinRoll += Math.Sin(sample.RollRad);
                _cosRoll += Math.Cos(sample.RollRad);
                _sinPitch += Math.Sin(sample.PitchRad);
                _cosPitch += Math.Cos(sample.PitchRad);
                _sinYaw += Math.Sin(sample.YawRad);
                _cosYaw += Math.Cos(sample.YawRad);
                _attitudeCount++;
            }
        }

        internal void AddMag(MagSample sample)
        {
            lock (_lock)
            {
                if (!_mags.TryGetValue(sample.Instance, out MagAccumulator? accumulator))
                {
                    accumulator = new MagAccumulator();
                    _mags[sample.Instance] = accumulator;
                }

                accumulator.Add(sample);
            }
        }

        internal void SetHealth(SensorHealth health)
        {
            lock (_lock)
            {
                _health = health;
            }
        }

        internal TelemetrySnapshot Build()
        {
            lock (_lock)
            {
                AttitudeSample attitude = _attitudeCount == 0
                    ? default
                    : new AttitudeSample(
                        (float)Math.Atan2(_sinRoll, _cosRoll),
                        (float)Math.Atan2(_sinPitch, _cosPitch),
                        (float)Math.Atan2(_sinYaw, _cosYaw));

                // В списке остаются только те экземпляры, от которых пришёл хотя бы
                // один отсчёт. Компас, отдающий нули, попадает сюда с IsEmpty = true:
                // «датчик есть, но данных нет» и «датчика нет» — разные диагнозы.
                List<MagSample> mags = _mags
                    .OrderBy(static entry => entry.Key)
                    .Select(static entry => entry.Value.Average(entry.Key))
                    .ToList();

                return new TelemetrySnapshot(attitude, mags, _health, _attitudeCount);
            }
        }

        private sealed class MagAccumulator
        {
            private double _x;
            private double _y;
            private double _z;
            private int _count;

            internal void Add(MagSample sample)
            {
                _x += sample.X;
                _y += sample.Y;
                _z += sample.Z;
                _count++;
            }

            internal MagSample Average(int instance)
            {
                if (_count == 0)
                {
                    return new MagSample(instance, 0, 0, 0);
                }

                return new MagSample(
                    instance,
                    ToShort(_x / _count),
                    ToShort(_y / _count),
                    ToShort(_z / _count));
            }

            private static short ToShort(double value) =>
                (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue);
        }
    }

    /// <summary>Незавершённая склейка многочанкового <c>STATUSTEXT</c>.</summary>
    private sealed class StatusTextAssembly
    {
        private readonly StringBuilder _builder = new();

        internal StatusTextAssembly(byte severity)
        {
            Severity = severity;
            StartedUtc = DateTimeOffset.UtcNow;
        }

        internal byte Severity { get; }

        internal DateTimeOffset StartedUtc { get; }

        /// <summary>Номер чанка, который ожидается следующим.</summary>
        internal int NextChunk { get; private set; }

        internal string Text => _builder.ToString();

        internal void Append(string chunk)
        {
            _builder.Append(chunk);
            NextChunk++;
        }
    }
}
