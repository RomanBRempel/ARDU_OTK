using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ARDU_OTK.Services.Fc.Mavlink;
using ARDU_OTK.Services.Store;

namespace ARDU_OTK.Services.Fc;

/// <summary>Итог одного шага приёмки.</summary>
/// <param name="Ok">Шаг выполнен.</param>
/// <param name="Message">Что именно произошло — для оператора и протокола.</param>
public sealed record StepResult(bool Ok, string Message)
{
    /// <summary>
    /// Шаг не выполнен, но приёмку это не останавливает.
    /// </summary>
    /// <remarks>
    /// 🔴 Третий исход существует затем, чтобы отсутствие условия не выдавалось
    /// за отказ изделия. Нет компасов — работать по-прежнему можно: параметры
    /// сверяются и переносятся, а очередь компасов выставлять просто не на чем.
    /// Останавливать за это всю приёмку значит запрещать оператору делать то,
    /// что делать можно, и прятать от него настоящую причину за словом «отказ».
    /// </remarks>
    public bool IsWarning { get; init; }

    public static StepResult Pass(string message) => new(true, message);

    public static StepResult Warn(string message) => new(true, message) { IsWarning = true };

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

    /// <summary>Канал сессии жив.</summary>
    public bool IsConnected => _connected && _link.IsConnected;

    /// <summary>
    /// Состояние борта для индикатора — по каналу самой приёмки.
    /// </summary>
    /// <remarks>
    /// 🔴 Приборы обязаны работать всю приёмку. COM-порт открывается монопольно,
    /// и наблюдательное соединение на время процедуры закрыто; если не отдавать
    /// состояние отсюда, оператор на несколько минут остаётся без горизонта,
    /// напряжения и компасов — ровно тогда, когда борт перезагружается и по
    /// приборам видно, вернулся он или нет.
    /// </remarks>
    public VehicleLiveState? LiveState => _link.LiveState;

    /// <summary>Читает параметр по каналу приёмки.</summary>
    public Task<ParamValue> ReadParamAsync(string name, CancellationToken ct)
    {
        EnsureConnected();
        return _link.ReadParamAsync(name, ct);
    }

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
        LastBoard = board;
        return ParameterTransfer.Plan(reference, board.Values, roles);
    }

    /// <summary>
    /// Таблица борта, прочитанная последней сверкой.
    /// </summary>
    /// <remarks>
    /// Хранится, чтобы показать оператору значения отображаемых параметров, не
    /// вычитывая борт второй раз: повторное чтение тысячи имён ради панели —
    /// это пять секунд занятого канала на каждое обновление экрана.
    /// </remarks>
    public FullParameterSet? LastBoard { get; private set; }

    // --- Шаг 1б. Сверка скриптов -------------------------------------------

    /// <summary>
    /// Сверяет скрипты борта с эталоном.
    /// </summary>
    /// <remarks>
    /// 🔴 Скрипты — часть конфигурации изделия наравне с параметрами. Прошивка
    /// исполняет всё, что лежит в каталоге скриптов: лишний файл на карте
    /// делает изделие непохожим на эталонное при полностью совпавших
    /// параметрах, а недостающий — отнимает у него часть поведения.
    /// </remarks>
    public async Task<IReadOnlyList<ScriptDifference>> CompareScriptsAsync(
        IReadOnlyList<ReferenceScript> expected,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expected);
        EnsureConnected();

        var unreadable = new List<string>();
        var actual = await ScriptTransfer.ReadAllAsync(_link, progress, unreadable, ct).ConfigureAwait(false);
        return ScriptTransfer.Compare(expected, actual, unreadable);
    }

    /// <summary>
    /// Приводит скрипты борта к эталону: дописывает недостающие, переписывает
    /// разошедшиеся, удаляет лишние.
    /// </summary>
    /// <remarks>
    /// 🔴 Записанный скрипт начинает исполняться только после перезагрузки
    /// борта: Lua подхватывается при старте. Сказать об этом обязан вызывающий —
    /// молчание означало бы «применено», чего не произошло.
    /// </remarks>
    public async Task<IReadOnlyList<(string Path, string? Failure)>> ApplyScriptsAsync(
        IReadOnlyList<ScriptDifference> differences,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(differences);
        EnsureConnected();

        var outcomes = new List<(string Path, string? Failure)>(differences.Count);

        foreach (var difference in differences)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (difference.Outcome == ScriptComparison.ExtraOnBoard)
                {
                    progress?.Report($"Удаление {difference.Path}…");
                    await _link.RemoveFileAsync(difference.Path, ct).ConfigureAwait(false);
                    outcomes.Add((difference.Path, null));
                    continue;
                }

                if (difference.Expected is not { } script)
                {
                    outcomes.Add((difference.Path, "в эталоне этого скрипта нет — записывать нечего"));
                    continue;
                }

                progress?.Report($"Запись {difference.Path}…");
                await _link.WriteFileAsync(script.Path, script.ToBytes(), null, ct).ConfigureAwait(false);

                // Обратное чтение обязательно: подтверждение записи означает,
                // что борт принял байты, но не что на карте лежит тот файл.
                progress?.Report($"Сверка {difference.Path}…");
                var readBack = await _link.ReadFileAsync(script.Path, null, ct).ConfigureAwait(false);
                var actual = ReferenceScript.ComputeHash(readBack);

                outcomes.Add(string.Equals(actual, script.Hash, StringComparison.Ordinal)
                    ? (difference.Path, null)
                    : (difference.Path, "обратное чтение дало другой файл"));
            }
            catch (Exception ex) when (ex is VehicleLinkException or System.IO.InvalidDataException)
            {
                outcomes.Add((difference.Path, ex.Message));
            }
        }

        return outcomes;
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
    /// <summary>
    /// Переносит калибровку компасов с эталона на текущий борт по опознанию
    /// датчика, а не по номеру слота.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Слот — это номер, под которым прошивка нашла датчик при загрузке, а
    /// не сам датчик. Порядок обнаружения у двух плат законно разный: внешний
    /// компас на образце может быть первым, а на целевой плате вторым. Перенос
    /// по совпадению имён — <c>COMPASS_OFS2</c> в <c>COMPASS_OFS2</c> — в этом
    /// случае кладёт калибровку внешнего компаса на внутренний и наоборот.
    /// Плата после такого «совпадает с эталоном» по всем именам и уверенно
    /// показывает неверный курс. Поэтому слоты сопоставляются по решающим
    /// подполям <c>DEV_ID</c>: тип шины, адрес и тип датчика.
    /// </para>
    /// <para>
    /// Что именно делает прошивку убеждённой в калиброванности: ненулевые
    /// смещения при том, что <c>COMPASS_DEV_ID</c> слота совпадает с реально
    /// найденным датчиком. Идентификаторы мы не трогаем — их прошивка
    /// проставляет сама, — а смещения приходят из эталона, и этого достаточно:
    /// <c>Compass::configured()</c> проверяет ровно эти два условия.
    /// </para>
    /// </remarks>
    public async Task<StepResult> TransferCompassCalibrationAsync(
        ReferenceParamSet reference,
        bool transferMotorComp,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reference);
        EnsureConnected();

        progress?.Report("Сопоставление компасов с эталоном…");

        var boardSlots = new List<CompassSlot>(CompassIdentity.MaxSlot);
        for (var slot = CompassIdentity.MinSlot; slot <= CompassIdentity.MaxSlot; slot++)
        {
            boardSlots.Add(await ReadSlotAsync(slot, ct).ConfigureAwait(false));
        }

        var present = boardSlots.Where(static s => s.IsPresent).ToArray();
        if (present.Length == 0)
        {
            return StepResult.Warn(
                "Борт не видит ни одного компаса — переносить калибровку некуда. Шаг пропущен.");
        }

        var expected = CompassIdentity.ReadExpectedSlots(reference)
            .Where(static s => !s.DeviceId.IsEmpty)
            .ToArray();

        if (expected.Length == 0)
        {
            return StepResult.Warn(
                "В эталоне нет ни одного компаса: переносить нечего. Эталон снят с борта без компасов "
              + "либо без их идентификаторов.");
        }

        var differences = new List<ParameterDifference>();
        var mapped = new List<string>();
        var unmatched = new List<string>();
        var missing = new List<string>();

        foreach (var target in present)
        {
            var source = expected.FirstOrDefault(
                e => CompassIdentity.IsSameSensor(e.DeviceId, target.DeviceId));

            if (source is null)
            {
                unmatched.Add(
                    $"слот {target.Slot}: {CompassIdentity.Describe(target.DeviceId)} — такого датчика в эталоне нет");
                continue;
            }

            var sourceNames = transferMotorComp
                ? CompassIdentity.TransferableCalibrationNames(source.Slot)
                : CompassIdentity.CoreCalibrationNames(source.Slot);

            var targetNames = transferMotorComp
                ? CompassIdentity.TransferableCalibrationNames(target.Slot)
                : CompassIdentity.CoreCalibrationNames(target.Slot);

            var copied = 0;
            for (var i = 0; i < sourceNames.Count; i++)
            {
                if (!reference.Values.TryGetValue(sourceNames[i], out var value))
                {
                    // Эталон молчит об этом имени — записывать нечего. Молчание
                    // не заменяется нулём: ноль здесь означал бы «калибровки
                    // нет», а не «сведений нет».
                    missing.Add(sourceNames[i]);
                    continue;
                }

                differences.Add(new ParameterDifference(
                    targetNames[i],
                    ParameterDiffKind.Differs,
                    value,
                    null,
                    MavParamType.Real32,
                    Visible: false,
                    Writable: true,
                    $"перенос калибровки компаса: {sourceNames[i]} эталона в слот {target.Slot} борта"));

                copied++;
            }

            mapped.Add(
                $"эталон слот {source.Slot} → борт слот {target.Slot} "
              + $"({CompassIdentity.Describe(target.DeviceId)}), значений {copied}");
        }

        if (differences.Count == 0)
        {
            return StepResult.Warn(
                "Ни один компас борта не опознан в эталоне: " + string.Join("; ", unmatched)
              + ". Калибровка не перенесена — сверьте состав датчиков с образцом.");
        }

        progress?.Report($"Перенос калибровки компасов: значений {differences.Count}…");

        var records = await ApplyAsync(differences, progress, ct).ConfigureAwait(false);
        var written = records.Count(static r => r.Outcome == WriteOutcome.Verified);
        var rejected = records.Count - written;

        var report = "Перенос калибровки компасов: " + string.Join("; ", mapped)
          + $". Записано и подтверждено: {written}";

        if (rejected > 0)
        {
            report += $", отклонено бортом: {rejected}";
        }

        if (unmatched.Count > 0)
        {
            report += ". Без пары остались: " + string.Join("; ", unmatched);
        }

        if (missing.Count > 0)
        {
            report += $". В эталоне не оказалось значений: {missing.Count}";
        }

        // 🔴 Убеждённость прошивки проверяется по её же признаку: ненулевые
        // смещения в слоте. Сообщить «перенесено», не убедившись в этом,
        // значит выдать за результат сам факт записи.
        var convinced = new List<string>();
        foreach (var target in present)
        {
            var x = await ReadOrZeroAsync(CompassIdentity.OffsetName(target.Slot, MagAxis.X), ct).ConfigureAwait(false);
            var y = await ReadOrZeroAsync(CompassIdentity.OffsetName(target.Slot, MagAxis.Y), ct).ConfigureAwait(false);
            var z = await ReadOrZeroAsync(CompassIdentity.OffsetName(target.Slot, MagAxis.Z), ct).ConfigureAwait(false);

            if (x == 0 && y == 0 && z == 0)
            {
                convinced.Add($"слот {target.Slot}: смещения остались нулевыми");
            }
        }

        if (convinced.Count > 0)
        {
            return StepResult.Fail(
                report + ". Прошивка такой компас калиброванным не считает — " + string.Join("; ", convinced));
        }

        return rejected > 0 || unmatched.Count > 0
            ? StepResult.Warn(report)
            : StepResult.Pass(report + ". Прошивка считает компасы откалиброванными.");
    }

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

        // 🔴 Отсутствие компасов приёмку не останавливает. Очередь выставлять
        // не на чем — значит, шаг пропускается вместе с его перезагрузкой, а
        // не объявляется отказом изделия: параметры и скрипты сверяются и
        // переносятся по-прежнему. Сказать об этом обязательно: изделие,
        // сданное без компасов, летать не будет, и оператор должен узнать об
        // этом от программы, а не от прошивки при первом взлёте.
        var present = slots.Where(static s => s.IsPresent).ToArray();
        if (present.Length == 0)
        {
            return StepResult.Warn(
                "Борт не видит ни одного компаса: все COMPASS_DEV_IDx нулевые. Очередь выставлять не на чем — "
              + "шаг пропущен вместе с перезагрузкой. Остальная приёмка идёт своим чередом, но изделие в таком "
              + "виде негодно: перед сдачей компас должен быть подключён.");
        }

        var external = present.FirstOrDefault(s => CompassIdentity.IsExternal(CompassIdentity.Classify(s)) == true);
        if (external is null)
        {
            var described = string.Join("; ", present.Select(CompassIdentity.Describe));
            return StepResult.Warn(
                "Среди найденных компасов нет ни одного внешнего: " + described
              + ". Ставить первым нечего — шаг пропущен вместе с перезагрузкой. Технологическая карта требует "
              + "внешний компас первым в очереди, и без него приёмку завершать нельзя.");
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
    /// <param name="standLatitudeDeg">Широта рабочего места; <c>null</c> — не задана.</param>
    /// <param name="standLongitudeDeg">Долгота рабочего места; <c>null</c> — не задана.</param>
    public async Task<StepResult> CompassCalibrationAsync(
        double headingDeg,
        double? standLatitudeDeg,
        double? standLongitudeDeg,
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

        // 🔴 Команда вычисляет эталонное магнитное поле по всемирной магнитной
        // модели В ТОЧКЕ БОРТА, и точку эту стенд обязан назвать сам.
        //
        // Прежде стенд передавал нули, если видел у борта фикс, — то есть
        // поручал борту найти собственные координаты. Это поручение борт
        // выполняет не тем путём, каким стенд его проверял: стенд смотрит на
        // GPS_RAW_INT первого приёмника, борт спрашивает сперва решение EKF,
        // затем основной приёмник — а основным при двух приёмниках оказывается
        // и тот, у которого решения нет. Предикаты расходятся, борт отвечает
        // «Mag: no position available», и шаг падает при координатах, которые
        // стенд в эту самую секунду показывает оператору на приборах.
        //
        // Поэтому координаты передаются всегда и берутся из того же фикса,
        // который стенд показывает: живой фикс борта, иначе координаты рабочего
        // места. Гадать за борт больше не о чем.
        var fix = _link.LiveState?.Gps;
        var liveFix = fix is { Is3D: true } live ? live : (GpsFix?)null;

        var source = liveFix is { } known
            ? (Lat: known.LatitudeDeg, Lon: known.LongitudeDeg, FromBoard: true)
            : standLatitudeDeg is { } standLat && standLongitudeDeg is { } standLon
                ? (Lat: standLat, Lon: standLon, FromBoard: false)
                : default((double Lat, double Lon, bool FromBoard)?);

        if (source is not { } point)
        {
            return StepResult.Fail(
                "Калибровать нечем: у борта нет трёхмерного фикса GPS, а координаты рабочего места не заданы. "
              + "Команда вычисляет эталонное магнитное поле по всемирной магнитной модели в точке борта, и без "
              + "координат ей считать не по чему. Задайте широту и долготу стенда в настройках либо выведите "
              + "борт под открытое небо.");
        }

        progress?.Report(
            $"Калибровка компаса по курсу {headingDeg:F1}° в точке {point.Lat:F5}, {point.Lon:F5} "
          + $"({(point.FromBoard ? "фикс борта" : "координаты стенда")}, FIXED_MAG_CAL_YAW)…");

        var result = await _link.SendCommandAsync(
            MavCommand.FixedMagCalYaw,
            (float)headingDeg, 0f, (float)point.Lat, (float)point.Lon, 0f, 0f, 0f,
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
            $"Компасы откалиброваны по курсу {headingDeg:F1}° в точке {point.Lat:F5}, {point.Lon:F5} "
          + (point.FromBoard
                ? "по живому фиксу борта. "
                : "по координатам рабочего места — фикса GPS у борта нет, и исправность приёмника "
                  + "этой калибровкой не подтверждена. ")
            + "Команда сохранила смещения немедленно и принудительно поставила COMPASS_DIA*=(1,1,1) и "
            + "COMPASS_ODI*=(0,0,0) — перенесённая из эталона мягкожелезная компенсация с этого момента "
            + "не действует.";

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
