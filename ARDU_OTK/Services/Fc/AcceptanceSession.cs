using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ARDU_OTK.Services.Fc.Mavlink;

namespace ARDU_OTK.Services.Fc;

/// <summary>Итог одного шага приёмки.</summary>
/// <param name="Ok">Шаг выполнен.</param>
/// <param name="Message">Что именно произошло — для оператора и протокола.</param>
public sealed record StepResult(bool Ok, string Message)
{
    public static StepResult Pass(string message) => new(true, message);

    public static StepResult Fail(string message) => new(false, message);
}

/// <summary>Готовность борта к взведению.</summary>
/// <param name="Ready">Предполётные проверки не назвали ни одной причины отказа.</param>
/// <param name="Blockers">Что мешает взвестись, строками прошивки без префикса.</param>
/// <param name="Detail">Сводка для протокола.</param>
public sealed record ArmReadiness(bool Ready, IReadOnlyList<string> Blockers, string Detail);

/// <summary>
/// Приёмка изделия: последовательность шагов, между которыми решает оператор.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 Это не автоматический прогон. Порядок разорван намеренно: после переноса
/// конфигурации и перезагрузки оператор смотрит на оставшиеся расхождения и
/// решает — править или принять как есть. Автомат, проходящий эту развилку сам,
/// либо останавливает годное изделие, либо пропускает негодное; решать здесь
/// должен человек, а программа обязана показать ему, что именно расходится.
/// </para>
/// <para>
/// Канал связи держится открытым на всю приёмку и переживает перезагрузки:
/// открывать порт заново на каждый шаг значит терять по два десятка секунд на
/// каждом и ловить чужой захват порта в промежутках.
/// </para>
/// <para>
/// Азимут в сессию не передаётся и в настройках не хранится. Он вводится
/// оператором в момент калибровки компаса — это свойство текущего положения
/// борта, а не рабочего места: борт можно повернуть между двумя прогонами, а
/// настройка стенда об этом не узнает.
/// </para>
/// </remarks>
public sealed class AcceptanceSession : IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RebootTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PrearmWindow = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Пауза после команды калибровки. Прошивка досылает сообщения о результате
    /// уже после <c>COMMAND_ACK</c>, и без этой паузы они попадут в протокол
    /// следующего шага либо потеряются вовсе.
    /// </summary>
    private static readonly TimeSpan PostCommandSettle = TimeSpan.FromSeconds(2);

    private readonly SerialVehicleLink _link = new();
    private readonly List<StatusTextEvent> _inbox = new();
    private readonly object _inboxLock = new();

    private bool _connected;

    public AcceptanceSession()
    {
        _link.StatusTextReceived += OnStatusText;
    }

    /// <summary>Сообщения борта, накопленные с начала сессии.</summary>
    public IReadOnlyList<StatusTextEvent> Messages
    {
        get
        {
            lock (_inboxLock)
            {
                return _inbox.ToArray();
            }
        }
    }

    /// <summary>Порт, на котором идёт приёмка.</summary>
    public string PortName { get; private set; } = string.Empty;

    /// <summary>Баннер прошивки борта, если он получен.</summary>
    public string? FirmwareBanner => _link.FirmwareBanner;

    /// <summary>Открывает канал и дожидается первого <c>HEARTBEAT</c>.</summary>
    /// <exception cref="VehicleLinkException">Порт занят, борт молчит.</exception>
    public async Task ConnectAsync(string portName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);

        await _link.ConnectAsync(portName, ConnectTimeout, ct).ConfigureAwait(false);
        if (!_link.IsConnected)
        {
            throw new VehicleLinkException($"Порт {portName} открыт, но HEARTBEAT не получен.");
        }

        PortName = portName;
        _connected = true;
    }

    // --- Шаг 1. Сверка конфигурации ---------------------------------------

    /// <summary>
    /// Вычитывает с борта всю таблицу параметров и сравнивает её с эталоном.
    /// </summary>
    /// <remarks>
    /// Сверяется всё, что эталон объявил контролируемым, а не компасный блок:
    /// изделие отличается от изделия каналами приёмника, выходами, режимами и
    /// регуляторами.
    /// </remarks>
    public async Task<ParameterTransferPlan> CompareAsync(
        IReadOnlyDictionary<string, double> reference,
        ParameterRoleMap roles,
        IProgress<string>? progress,
        CancellationToken ct,
        IProgress<ParameterProgress>? detail = null)
    {
        EnsureConnected();

        var board = await _link.ReadAllParamsAsync(progress, detail, ct).ConfigureAwait(false);
        return ParameterTransfer.Plan(reference, board.Values, roles);
    }

    // --- Шаг 2. Внешний компас первым в очереди ----------------------------

    /// <summary>
    /// Ставит внешний компас первым в таблице приоритетов и перезагружает борт.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Приоритеты строятся <b>из собственных</b> <c>COMPASS_DEV_IDx</c>
    /// целевой платы, а не из эталона. Скопированные из эталона, они называют
    /// датчики чужой платы и дают <c>PreArm: Compass N not found</c>.
    /// </para>
    /// <para>
    /// 🔴 Перезагрузка обязательна и обязана быть здесь, до переноса калибровки.
    /// <c>COMPASS_PRIOx_ID</c> помечен <c>@RebootRequired</c>, и при загрузке
    /// прошивка физически переставляет целые блоки параметров между слотами.
    /// Калибровка, записанная до этой перезагрузки, легла бы на слот, который
    /// после неё принадлежит другому датчику.
    /// </para>
    /// </remarks>
    public async Task<StepResult> MakeExternalCompassPrimaryAsync(
        IProgress<string>? progress,
        CancellationToken ct)
    {
        EnsureConnected();

        progress?.Report("Чтение состава компасов…");

        var slots = new List<CompassSlot>(CompassIdentity.MaxSlot);
        for (var slot = CompassIdentity.MinSlot; slot <= CompassIdentity.MaxSlot; slot++)
        {
            slots.Add(await ReadSlotAsync(slot, ct).ConfigureAwait(false));
        }

        var present = slots.Where(static s => s.IsPresent).ToArray();
        if (present.Length == 0)
        {
            return StepResult.Fail(
                "Борт не видит ни одного компаса: все COMPASS_DEV_IDx нулевые. Ставить первым нечего.");
        }

        var external = present.FirstOrDefault(s => CompassIdentity.IsExternal(CompassIdentity.Classify(s)) == true);
        if (external is null)
        {
            var described = string.Join("; ", present.Select(CompassIdentity.Describe));
            return StepResult.Fail(
                "Среди найденных компасов нет ни одного внешнего: " + described
              + ". Технологическая карта требует внешний компас первым в очереди.");
        }

        if (present[0].Slot == external.Slot && slots[0].DeviceId.Raw == external.DeviceId.Raw)
        {
            // Уже первый — писать приоритеты и тратить перезагрузку незачем.
            return StepResult.Pass(
                $"Внешний компас уже первый в очереди: {CompassIdentity.Describe(external.DeviceId)}. "
              + "Перезагрузка не потребовалась.");
        }

        // Порядок: внешний первым, остальные — в прежнем относительном порядке.
        var order = new List<CompassSlot>(present.Length) { external };
        order.AddRange(present.Where(s => s.Slot != external.Slot));

        for (var i = 0; i < order.Count && i < CompassIdentity.MaxSlot; i++)
        {
            var name = CompassIdentity.PriorityIdName(i + CompassIdentity.MinSlot);
            var value = (float)order[i].DeviceId.Raw;

            progress?.Report($"Запись {name}…");

            var before = await _link.ReadParamAsync(name, ct).ConfigureAwait(false);
            await _link.WriteParamAsync(name, value, before.Type, ct).ConfigureAwait(false);

            var readBack = await _link.ReadParamAsync(name, ct).ConfigureAwait(false);
            if (Math.Abs(readBack.Value - value) > 0.5f)
            {
                return StepResult.Fail(
                    $"Борт не принял {name}: записывали {value:F0}, обратное чтение дало {readBack.Value:F0}.");
            }
        }

        progress?.Report("Перезагрузка борта после смены приоритетов…");
        await _link.RebootAndReconnectAsync(RebootTimeout, ct).ConfigureAwait(false);

        if (!_link.IsConnected)
        {
            return StepResult.Fail("Борт не вернулся на связь после перезагрузки.");
        }

        // 🔴 Проверяется состояние ПОСЛЕ перезагрузки, а не подтверждение
        // записи до неё. Записанный COMPASS_PRIOx_ID — это ещё не выполненная
        // перестановка: слоты переставляет прошивка при загрузке, и убедиться,
        // что она это сделала, можно только перечитав их. Отчёт по записи
        // означал бы «мы попросили», а нужен ответ «получилось».
        progress?.Report("Проверка состава компасов после перезагрузки…");

        var firstAfter = await ReadSlotAsync(CompassIdentity.MinSlot, ct).ConfigureAwait(false);
        var kindAfter = CompassIdentity.Classify(firstAfter);

        if (!firstAfter.IsPresent)
        {
            return StepResult.Fail(
                "После перезагрузки первый слот пуст: прошивка не приняла новую очередь компасов.");
        }

        if (CompassIdentity.IsExternal(kindAfter) != true)
        {
            return StepResult.Fail(
                $"После перезагрузки первым остался {CompassIdentity.Describe(firstAfter.DeviceId)} "
              + $"[{CompassIdentity.ExternalKindText(kindAfter)}]. Прошивка не переставила слоты — "
              + "перенос калибровки лёг бы на чужой датчик.");
        }

        return StepResult.Pass(
            $"Первым в очереди: {CompassIdentity.Describe(firstAfter.DeviceId)}. Проверено перечитыванием "
          + "после перезагрузки — прошивка переставила блоки параметров между слотами, и перенос калибровки "
          + "пойдёт на верный слот.");
    }

    // --- Шаг 3. Перенос конфигурации --------------------------------------

    /// <summary>Записывает на борт эталонные значения расходящихся параметров.</summary>
    public async Task<IReadOnlyList<ParamWriteRecord>> ApplyAsync(
        IReadOnlyList<ParameterDifference> differences,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        EnsureConnected();
        return await ParameterTransfer.ApplyAsync(_link, differences, progress, ct).ConfigureAwait(false);
    }

    /// <summary>Перезагружает борт и дожидается его возвращения.</summary>
    public async Task<StepResult> RebootAsync(IProgress<string>? progress, CancellationToken ct)
    {
        EnsureConnected();

        progress?.Report("Перезагрузка борта…");
        await _link.RebootAndReconnectAsync(RebootTimeout, ct).ConfigureAwait(false);

        return _link.IsConnected
            ? StepResult.Pass("Борт вернулся на связь. " + (_link.FirmwareBanner ?? "баннер не получен"))
            : StepResult.Fail("Борт не вернулся на связь после перезагрузки.");
    }

    // --- Шаг 4. Калибровка уровня -----------------------------------------

    /// <summary>
    /// Калибровка уровня платы.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <c>param5 = 2</c> — и только оно. Это TRIM, то есть уровень платы;
    /// <c>param5 = 1</c> — шестипозиционная калибровка акселерометров, которую
    /// на стенде выполнить невозможно, а <c>param5 = 4</c> обнуляет трим.
    /// Ошибка в одном числе меняет операцию на совершенно другую.
    /// </para>
    /// <para>
    /// Борт обязан быть обезврежен: на взведённом прошивка отвечает отказом и
    /// сообщением <c>Disarm to allow calibration</c>. Проверяем заранее, чтобы
    /// назвать причину до команды, а не расшифровывать отказ после.
    /// </para>
    /// </remarks>
    public async Task<StepResult> LevelCalibrationAsync(IProgress<string>? progress, CancellationToken ct)
    {
        EnsureConnected();

        if (_link.LiveState is { Armed: true })
        {
            return StepResult.Fail("Борт взведён. Калибровка уровня на взведённом борту прошивкой запрещена.");
        }

        progress?.Report("Калибровка уровня (PREFLIGHT_CALIBRATION, param5 = 2)…");

        var result = await _link.SendCommandAsync(
            MavCommand.PreflightCalibration,
            0f, 0f, 0f, 0f, 2f, 0f, 0f,
            CommandTimeout,
            ct).ConfigureAwait(false);

        await Task.Delay(PostCommandSettle, ct).ConfigureAwait(false);

        if (result != MavResult.Accepted)
        {
            return StepResult.Fail(
                $"Калибровка уровня отклонена: {result}. " + DescribeRecent("calib", "level", "trim"));
        }

        return StepResult.Pass(
            "Уровень откалиброван: записаны AHRS_TRIM_X и AHRS_TRIM_Y. Плата должна была стоять "
          + "горизонтально и неподвижно.");
    }

    // --- Шаг 5. Калибровка компаса по фиксированному курсу ------------------

    /// <summary>
    /// Калибровка компаса по известному курсу борта.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Курс вводится оператором в момент калибровки и означает <b>истинный
    /// курс борта сейчас</b>, а не азимут оснастки. Свойство положения борта, а
    /// не рабочего места: борт можно повернуть между двумя прогонами, и
    /// настройка стенда об этом не узнает.
    /// </para>
    /// <para>
    /// 🔴 Команда сохраняет результат немедленно, шага подтверждения нет, и она
    /// принудительно ставит <c>COMPASS_DIA*</c> в (1,1,1), а <c>COMPASS_ODI*</c>
    /// в нули — то есть отменяет перенесённую из эталона мягкожелезную
    /// компенсацию. Это записывается в протокол как факт, а не как отказ.
    /// </para>
    /// <para>
    /// 🔴 Команда начинается с <c>_reset_compass_id()</c> и может обнулить
    /// <c>COMPASS_PRIOx_ID</c>, то есть сломать очередь, выставленную на втором
    /// шаге. Поэтому очередь после неё перечитывается, а не считается
    /// сохранившейся.
    /// </para>
    /// </remarks>
    /// <param name="headingDeg">Истинный курс борта, градусы 0…360.</param>
    public async Task<StepResult> CompassCalibrationAsync(
        double headingDeg,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        EnsureConnected();

        if (double.IsNaN(headingDeg) || double.IsInfinity(headingDeg) || headingDeg is < 0 or > 360)
        {
            return StepResult.Fail(
                $"Курс {headingDeg} вне диапазона 0…360°. Калибровка по неверному курсу оставит компас "
              + "«успешно» откалиброванным на чужое направление.");
        }

        var before = await ReadPriorityOrderAsync(ct).ConfigureAwait(false);

        progress?.Report($"Калибровка компаса по курсу {headingDeg:F1}° (FIXED_MAG_CAL_YAW)…");

        var result = await _link.SendCommandAsync(
            MavCommand.FixedMagCalYaw,
            (float)headingDeg, 0f, 0f, 0f, 0f, 0f, 0f,
            CommandTimeout,
            ct).ConfigureAwait(false);

        await Task.Delay(PostCommandSettle, ct).ConfigureAwait(false);

        if (result != MavResult.Accepted)
        {
            return StepResult.Fail(
                $"Калибровка компаса отклонена: {result}. " + DescribeRecent("mag", "compass", "position"));
        }

        var after = await ReadPriorityOrderAsync(ct).ConfigureAwait(false);
        var orderChanged = !before.SequenceEqual(after);

        var note =
            $"Компасы откалиброваны по курсу {headingDeg:F1}°. Команда сохранила смещения немедленно и "
          + "принудительно поставила COMPASS_DIA*=(1,1,1) и COMPASS_ODI*=(0,0,0) — перенесённая из эталона "
          + "мягкожелезная компенсация с этого момента не действует.";

        return orderChanged
            ? StepResult.Fail(
                note + " ВНИМАНИЕ: таблица приоритетов изменилась ("
              + string.Join(", ", before) + " → " + string.Join(", ", after)
              + "). Команда сбросила COMPASS_PRIOx_ID; очередь компасов нужно выставить заново.")
            : StepResult.Pass(note + " Таблица приоритетов не изменилась.");
    }

    // --- Шаг 6. Готовность к взведению -------------------------------------

    /// <summary>
    /// Перезагружает борт и проверяет, готов ли он быть взведённым.
    /// </summary>
    /// <remarks>
    /// 🔴 Причины отказа называются дословно строками прошивки. Пересказ своими
    /// словами здесь недопустим: оператор идёт с этим текстом к схеме и к
    /// документации, и переведённая формулировка в них не находится.
    /// </remarks>
    public async Task<ArmReadiness> CheckArmReadinessAsync(IProgress<string>? progress, CancellationToken ct)
    {
        EnsureConnected();

        progress?.Report("Перезагрузка перед проверкой готовности…");
        await _link.RebootAndReconnectAsync(RebootTimeout, ct).ConfigureAwait(false);

        if (!_link.IsConnected)
        {
            return new ArmReadiness(
                false,
                new[] { "борт не вернулся на связь после перезагрузки" },
                "Проверить готовность не удалось: связи нет.");
        }

        // 🔴 Сразу после HEARTBEAT борт ещё не готов, и его первая жалоба —
        // «System not initialised». Это состояние загрузки, а не дефект
        // изделия: калибруется барометр, поднимается AHRS, инициализируются
        // датчики. Проверка, выполненная в этот момент, забраковала бы каждое
        // изделие подряд, и по протоколу это выглядело бы как настоящий отказ.
        PrearmReport report = await WaitForInitialisationAsync(progress, ct).ConfigureAwait(false);

        var blockers = report.Messages
            .Select(static m => m.Text.Replace("PreArm:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim())
            .Where(static t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (blockers.Length > 0)
        {
            return new ArmReadiness(
                false,
                blockers,
                "Борт взвестись не даст. Прошивка называет причины: " + string.Join("; ", blockers));
        }

        // Молчание предполётных проверок — это не доказательство готовности,
        // если сама команда 401 борту неизвестна: старая прошивка её не знает,
        // и тогда судить не по чему.
        if (report.CommandResult is null)
        {
            return new ArmReadiness(
                false,
                new[] { "прошивка не знает команды предполётных проверок (401)" },
                "Готовность подтвердить нечем: борт не выполняет запрос предполётных проверок. "
              + "Отсутствие жалоб при этом доказательством не является.");
        }

        return new ArmReadiness(
            true,
            Array.Empty<string>(),
            "Предполётные проверки не назвали ни одной причины отказа: борт готов быть взведённым.");
    }

    // --- Служебное ---------------------------------------------------------

    /// <summary>Сколько ждать окончания загрузки борта перед тем, как верить его жалобам.</summary>
    private static readonly TimeSpan InitialisationTimeout = TimeSpan.FromSeconds(40);

    /// <summary>
    /// Ждёт, пока борт закончит инициализацию, и возвращает первый отчёт, в
    /// котором её уже нет.
    /// </summary>
    /// <remarks>
    /// 🔴 «System not initialised» отфильтровывается не потому, что мешает
    /// получить годный вердикт, а потому что это не свойство изделия: сразу
    /// после подачи питания так отвечает любая исправная плата. Если жалоба не
    /// уходит за отведённое время, она остаётся в списке причин — тогда это уже
    /// факт об изделии, и молчать о нём нельзя.
    /// </remarks>
    private async Task<PrearmReport> WaitForInitialisationAsync(IProgress<string>? progress, CancellationToken ct)
    {
        const string NotInitialised = "not initialised";

        var deadline = DateTimeOffset.UtcNow + InitialisationTimeout;
        PrearmReport report;

        while (true)
        {
            progress?.Report("Предполётные проверки…");

            try
            {
                report = await _link.RunPrearmChecksAsync(PrearmWindow, ct).ConfigureAwait(false);
            }
            catch (VehicleLinkException ex)
            {
                return new PrearmReport(null, new[] { new StatusTextEvent(MavSeverity.Error, ex.Message, DateTimeOffset.UtcNow) }, null);
            }

            var initialising = report.Messages.Any(
                static m => m.Text.Contains(NotInitialised, StringComparison.OrdinalIgnoreCase));

            if (!initialising || DateTimeOffset.UtcNow >= deadline)
            {
                return report;
            }

            progress?.Report("Борт ещё инициализируется, жду…");
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
        }
    }

    private async Task<CompassSlot> ReadSlotAsync(int slot, CancellationToken ct)
    {
        var devId = await ReadOrZeroAsync(CompassIdentity.DevIdName(slot), ct).ConfigureAwait(false);
        var external = await ReadOrZeroAsync(CompassIdentity.ExternalName(slot), ct).ConfigureAwait(false);
        var use = await ReadOrZeroAsync(CompassIdentity.UseName(slot), ct).ConfigureAwait(false);
        var orient = await ReadOrZeroAsync(CompassIdentity.OrientName(slot), ct).ConfigureAwait(false);

        return new CompassSlot(
            slot,
            CompassIdentity.ToDeviceId(devId),
            (int)Math.Round(external),
            (int)Math.Round(use),
            (int)Math.Round(orient),
            (0, 0, 0));
    }

    /// <summary>
    /// Читает параметр, считая незнакомое борту имя нулём.
    /// </summary>
    /// <remarks>
    /// Прошивка законно не знает части имён — например <c>COMPASS_EXTERN3</c>
    /// при двух компасах. Это сообщение о составе борта, а не сбой чтения.
    /// </remarks>
    private async Task<double> ReadOrZeroAsync(string name, CancellationToken ct)
    {
        try
        {
            var value = await _link.ReadParamAsync(name, ct).ConfigureAwait(false);
            return value.Value;
        }
        catch (VehicleLinkException)
        {
            return 0;
        }
    }

    private async Task<IReadOnlyList<string>> ReadPriorityOrderAsync(CancellationToken ct)
    {
        var order = new List<string>(CompassIdentity.MaxSlot);
        for (var slot = CompassIdentity.MinSlot; slot <= CompassIdentity.MaxSlot; slot++)
        {
            var raw = await ReadOrZeroAsync(CompassIdentity.PriorityIdName(slot), ct).ConfigureAwait(false);
            order.Add(((uint)Math.Max(0, Math.Round(raw))).ToString(CultureInfo.InvariantCulture));
        }

        return order;
    }

    /// <summary>Последние сообщения борта, относящиеся к теме, — для расшифровки отказа.</summary>
    private string DescribeRecent(params string[] keywords)
    {
        StatusTextEvent[] snapshot;
        lock (_inboxLock)
        {
            snapshot = _inbox.TakeLast(20).ToArray();
        }

        var matched = snapshot
            .Where(m => keywords.Any(k => m.Text.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Select(static m => m.Text)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return matched.Length == 0
            ? "Борт причину не назвал."
            : "Борт сообщил: " + string.Join(" | ", matched);
    }

    private void OnStatusText(object? sender, StatusTextEvent e)
    {
        lock (_inboxLock)
        {
            _inbox.Add(e);
        }
    }

    private void EnsureConnected()
    {
        if (!_connected || !_link.IsConnected)
        {
            throw new VehicleLinkException("Связи с бортом нет: приёмка не начата или канал оборван.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _link.StatusTextReceived -= OnStatusText;
        await _link.DisposeAsync().ConfigureAwait(false);
    }
}
