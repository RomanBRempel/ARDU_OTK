using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ARDU_OTK.Services.Fc;

/// <summary>
/// Технологическая карта серийной калибровки компаса: от подключения до
/// протокола, одним прогоном и без ветвлений «продолжить несмотря на».
/// </summary>
/// <remarks>
/// <para>
/// Главное правило карты: <b>любой FAIL немедленно останавливает процедуру</b>.
/// Ни одна стадия не выполняется после отказа предыдущей, а результат называет
/// стадию и причину. Частичного успеха, поданного как успех, не бывает: борт
/// уходит в брак или на повторную наладку, но не в приёмку.
/// </para>
/// <para>
/// Реестр пишется по ходу дела (<see cref="ICalibrationStore"/> вызывается на
/// каждой стадии), а не одной транзакцией в конце. Убитый процесс обязан
/// оставить восстановимый частичный прогон: оператор должен видеть, на чём
/// именно оборвались, а не пустую строку.
/// </para>
/// <para>
/// Класс не знает про UI. Всё, что должен увидеть оператор, уходит через
/// <see cref="ICalibrationProgress"/>; всё, что должно пережить перезапуск, —
/// через <see cref="ICalibrationStore"/>.
/// </para>
/// <para>
/// Аудит записей (<see cref="CalibrationRunResult.Writes"/>) — журнал, а не
/// снимок: записи добавляются только в конец и упорядочены по
/// <see cref="ParamWriteRecord.AtUtc"/>. Текущее состояние борта по имени
/// параметра — это <b>последняя</b> запись с этим именем. Так сделано потому,
/// что стадия <see cref="CalibrationStage.FixedYaw"/> перетирает часть того,
/// что записала стадия <see cref="CalibrationStage.WriteReference"/>, и
/// протокол обязан показывать то, что борт держит сейчас.
/// </para>
/// </remarks>
public sealed class SerialCompassCalibrationJob
{
    /// <summary>Ожидание первого <c>HEARTBEAT</c> после открытия порта.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Ожидание трёхмерного фикса. Холодный старт приёмника в цеху долгий.</summary>
    private static readonly TimeSpan GpsFixTimeout = TimeSpan.FromMinutes(3);

    /// <summary>Ожидание <c>COMMAND_ACK</c> на обычную команду.</summary>
    private static readonly TimeSpan CommandAckTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Ожидание возвращения борта после ребута. Плата отваливается от шины USB
    /// без корректного detach и перечисляется заново, номер COM может смениться.
    /// </summary>
    private static readonly TimeSpan RebootTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Пауза после принятой команды фиксированного курса. <c>COMMAND_ACK</c>
    /// приходит раньше, чем прошивка досылает свои <c>STATUSTEXT</c> о снятых
    /// компасах, — без этой паузы диагностика теряется.
    /// </summary>
    private static readonly TimeSpan PostCommandSettle = TimeSpan.FromSeconds(2);

    private readonly IVehicleLink _link;
    private readonly ICalibrationStore _store;
    private readonly ICalibrationProgress _progress;
    private readonly CalibrationTolerances _tolerances;

    /// <summary>Создаёт задание. Все зависимости обязательны и не подменяются по ходу прогона.</summary>
    /// <param name="link">Канал связи с бортом.</param>
    /// <param name="store">Реестр прогонов; пишется по ходу процедуры.</param>
    /// <param name="progress">Канал прогресса для оператора — единственный выход наружу.</param>
    /// <param name="tolerances">Допуски технологической карты.</param>
    public SerialCompassCalibrationJob(
        IVehicleLink link,
        ICalibrationStore store,
        ICalibrationProgress progress,
        CalibrationTolerances tolerances)
    {
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _tolerances = tolerances ?? throw new ArgumentNullException(nameof(tolerances));
    }

    /// <summary>
    /// Выполняет процедуру целиком и возвращает её итог.
    /// </summary>
    /// <param name="request">Задание: порт, эталон, азимут стапеля, номер борта, оператор.</param>
    /// <param name="ct">Токен отмены. Учитывается на каждом ожидании.</param>
    /// <returns>
    /// Итог прогона. При отказе <see cref="CalibrationRunResult.FailedStage"/>
    /// называет стадию, на которой остановились, а
    /// <see cref="CalibrationRunResult.FailureReason"/> — причину для оператора.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Прогон отменён. Перед пробросом прогон закрывается в реестре как
    /// отменённый: открытая строка в реестре хуже, чем строка с отказом,
    /// потому что блокирует обновление приложения и врёт про состояние стенда.
    /// </exception>
    /// <remarks>
    /// Никаких других исключений наружу не выходит.
    /// <see cref="VehicleLinkException"/> и любой прочий сбой сворачиваются в
    /// отказ стадии с текстом, понятным оператору у стенда.
    /// </remarks>
    public async Task<CalibrationRunResult> RunAsync(CalibrationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var c = new RunContext(request);

        // Подписка живёт весь прогон: STATUSTEXT прилетают асинхронно, в том
        // числе между стадиями, и терять их нельзя — именно они объясняют,
        // почему прошивка отказала.
        void OnStatusText(object? sender, StatusTextEvent e) => c.Inbox.Enqueue(e);

        _link.StatusTextReceived += OnStatusText;
        try
        {
            var steps = new (CalibrationStage Stage, string Title, Func<RunContext, CancellationToken, Task<string?>> Run)[]
            {
                (CalibrationStage.Connect, "Эталон, реестр и подключение к борту", RunConnectAsync),
                (CalibrationStage.GpsFix, "Ожидание трёхмерного фикса GPS", RunGpsFixAsync),
                (CalibrationStage.VerifyTopology, "Сверка состава компасов с эталоном", RunVerifyTopologyAsync),
                (CalibrationStage.WriteReference, "Перенос калибровочных данных из эталона", RunWriteReferenceAsync),
                (CalibrationStage.VerifyWrites, "Обратное чтение записанных параметров", RunVerifyWritesAsync),
                (CalibrationStage.Reboot, "Перезагрузка борта", RunRebootAsync),
                (CalibrationStage.ReReadControlSample, "Контрольная выборка после перезагрузки", RunReReadControlSampleAsync),
                (CalibrationStage.FixedYaw, "Калибровка по фиксированному курсу стапеля", RunFixedYawAsync),
                (CalibrationStage.Acceptance, "Приёмочные проверки", RunAcceptanceAsync),
            };

            foreach (var step in steps)
            {
                ct.ThrowIfCancellationRequested();
                c.Stage = step.Stage;
                _progress.StageStarted(step.Stage, step.Title);

                var failure = await step.Run(c, ct).ConfigureAwait(false);
                await DrainInboxAsync(c, ct).ConfigureAwait(false);

                if (failure is not null)
                {
                    _progress.StageFinished(step.Stage, CheckOutcome.Fail, failure);
                    return await CompleteAsync(c, passed: false, step.Stage, failure, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                _progress.StageFinished(step.Stage, CheckOutcome.Pass, step.Title + " — норма.");
            }

            // Протокол — отдельная стадия карты: сначала закрываем прогон в
            // реестре, и только потом сообщаем оператору, что он закрыт.
            ct.ThrowIfCancellationRequested();
            c.Stage = CalibrationStage.Protocol;
            _progress.StageStarted(CalibrationStage.Protocol, "Оформление протокола");
            var result = await CompleteAsync(c, passed: true, null, null, CancellationToken.None).ConfigureAwait(false);
            _progress.StageFinished(CalibrationStage.Protocol, CheckOutcome.Pass, "Протокол закрыт, приёмка пройдена.");
            return result;
        }
        catch (OperationCanceledException)
        {
            // Отменённый прогон закрывается как непройденный. Сбой самого
            // реестра здесь не должен подменить собой отмену — иначе
            // вызывающий слой не поймёт, что произошло на самом деле.
            try
            {
                _progress.StageFinished(c.Stage, CheckOutcome.Fail, "Прогон отменён оператором.");
                await CompleteAsync(c, passed: false, c.Stage, "Прогон отменён оператором.", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception storeEx) when (storeEx is not OperationCanceledException)
            {
                _progress.Log(MavSeverity.Critical, $"Не удалось закрыть отменённый прогон в реестре: {storeEx.Message}");
            }

            throw;
        }
        catch (VehicleLinkException ex)
        {
            var reason = $"Отказ канала связи на стадии {c.Stage}: {ex.Message}";
            _progress.StageFinished(c.Stage, CheckOutcome.Fail, reason);
            return await CompleteAsync(c, passed: false, c.Stage, reason, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Сырое исключение наружу не выпускаем: у стенда его никто не
            // прочитает, а прогон останется открытым.
            var reason = $"Непредвиденный сбой на стадии {c.Stage}: {ex.GetType().Name}: {ex.Message}";
            _progress.StageFinished(c.Stage, CheckOutcome.Fail, reason);
            return await CompleteAsync(c, passed: false, c.Stage, reason, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _link.StatusTextReceived -= OnStatusText;
        }
    }

    // ------------------------------------------------------------------
    // Стадия 1. Connect
    // ------------------------------------------------------------------

    private async Task<string?> RunConnectAsync(RunContext c, CancellationToken ct)
    {
        // Эталон читается и хешируется ДО касания железа: негодный эталон
        // обязан остановить процедуру, пока борт ещё не тронут. Иначе плата
        // уедет с наполовину применённой калибровкой.
        ReferenceParamSet reference;
        string hash;
        try
        {
            reference = ReferenceParamFile.Load(c.Request.ReferenceFilePath);
            hash = ComputeReferenceHash(c.Request.ReferenceFilePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Эталонный файл «{c.Request.ReferenceFilePath}» не прочитан: {ex.Message}";
        }

        if (reference.Values.Count == 0)
        {
            return $"Эталонный файл «{c.Request.ReferenceFilePath}» не содержит ни одного параметра.";
        }

        var compassNames = reference.Values.Keys
            .Where(static k => k.StartsWith(CompassPrefix, StringComparison.Ordinal))
            .ToArray();
        if (compassNames.Length == 0)
        {
            return $"В эталоне «{c.Request.ReferenceFilePath}» нет ни одного параметра {CompassPrefix}* — "
                 + "перенос калибровки невозможен.";
        }

        c.Reference = reference;
        c.ReferenceHash = hash;

        c.RunId = await _store.BeginRunAsync(c.Request, hash, AppVersion, ct).ConfigureAwait(false);
        c.RunOpen = true;

        _progress.Log(
            MavSeverity.Info,
            $"Эталон: {reference.FilePath} ({reference.Format}), параметров {reference.Values.Count}, "
          + $"из них {CompassPrefix}* — {compassNames.Length}. SHA-256: {hash}");

        await _link.ConnectAsync(c.Request.PortName, ConnectTimeout, ct).ConfigureAwait(false);
        if (!_link.IsConnected)
        {
            return $"Порт {c.Request.PortName} открыт, но HEARTBEAT не получен.";
        }

        await AddCheckAsync(c, new CheckResult(
            "link.connected",
            "Связь с бортом",
            CheckOutcome.Pass,
            $"Порт {c.Request.PortName}, sysid {_link.TargetSystem}, compid {_link.TargetComponent}, "
          + $"прошивка: {_link.FirmwareBanner ?? "баннер не получен"}"), ct).ConfigureAwait(false);

        return null;
    }

    // ------------------------------------------------------------------
    // Стадия 2. GpsFix
    // ------------------------------------------------------------------

    private async Task<string?> RunGpsFixAsync(RunContext c, CancellationToken ct)
    {
        // Фикс нужен не «для порядка»: MAV_CMD_FIXED_MAG_CAL_YAW вычисляет
        // эталонное поле по всемирной магнитной модели (WMM) в точке борта.
        // Без координат прошивка отвечает FAILED и «Mag: no position available».
        //
        // Формально требование снимается: если передать в param3/param4
        // ненулевые широту и долготу в десятичных градусах, прошивка возьмёт
        // их и GPS ей не понадобится. Карта сознательно этого не делает —
        // живой фикс подтверждает, что приёмник на плате исправен и что
        // координаты соответствуют реальному положению стапеля, а не тому,
        // что кто-то однажды вписал в конфигурацию цеха.
        var fix = await _link.WaitForGpsFixAsync(GpsFixTimeout, ct).ConfigureAwait(false);
        if (!fix.Is3D)
        {
            return $"Нет трёхмерного фикса GPS: fix_type={fix.FixType}, спутников {fix.SatellitesVisible}. "
                 + "Модель магнитного поля не может быть вычислена.";
        }

        c.Fix = fix;

        await AddCheckAsync(c, new CheckResult(
            "gps.fix3d",
            "Трёхмерный фикс GPS",
            CheckOutcome.Pass,
            string.Format(
                CultureInfo.InvariantCulture,
                "fix_type={0}, широта {1:F7}°, долгота {2:F7}°, спутников {3}",
                fix.FixType, fix.LatitudeDeg, fix.LongitudeDeg, fix.SatellitesVisible),
            fix.SatellitesVisible,
            null), ct).ConfigureAwait(false);

        return null;
    }

    // ------------------------------------------------------------------
    // Стадия 3. VerifyTopology
    // ------------------------------------------------------------------

    private async Task<string?> RunVerifyTopologyAsync(RunContext c, CancellationToken ct)
    {
        var reference = c.Reference!;

        var slots = new List<CompassSlot>(MaxSlots);
        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            slots.Add(await ReadSlotAsync(c, slot, ct).ConfigureAwait(false));
        }

        c.TopologyBefore.Clear();
        c.TopologyBefore.AddRange(slots);

        foreach (var s in slots)
        {
            await AddCheckAsync(c, new CheckResult(
                $"topology.slot{s.Slot}",
                $"Слот компаса {s.Slot}",
                CheckOutcome.Pass,
                s.IsPresent
                    ? $"{DescribeDevice(s.DeviceId)}, EXTERNAL={s.ExternalFlag}, USE={s.UseFlag}, "
                    + $"ORIENT={s.Orientation}, |OFS|={s.OffsetMagnitude:F1} мГс"
                    : "слот пуст"), ct).ConfigureAwait(false);
        }

        // Сверка с эталоном. Любое расхождение — отказ: перенос калибровки на
        // другой состав датчиков даёт правдоподобные, но неверные числа.
        foreach (var s in slots)
        {
            var names = NamesFor(s.Slot);

            if (!reference.TryGet(names.DevId, out var refDevRaw))
            {
                if (!s.IsPresent)
                {
                    continue;
                }

                return $"Слот {s.Slot}: борт видит {DescribeDevice(s.DeviceId)}, "
                     + $"а в эталоне нет {names.DevId} — состав компасов эталона неизвестен.";
            }

            var refDev = new CompassDeviceId(ToRawDeviceId(refDevRaw));

            if (refDev.IsEmpty != !s.IsPresent)
            {
                return s.IsPresent
                    ? $"Слот {s.Slot}: борт видит {DescribeDevice(s.DeviceId)}, в эталоне слот пуст."
                    : $"Слот {s.Slot}: эталон ожидает {DescribeDevice(refDev)}, борт слот не видит.";
            }

            if (!s.IsPresent)
            {
                continue;
            }

            // Сравниваются тип шины, адрес и тип датчика. Номер шины намеренно
            // не сравнивается: он зависит от разводки конкретной ревизии платы
            // и меняется без смены датчика.
            var mismatch = CompareDeviceFields(s.Slot, s.DeviceId, refDev);
            if (mismatch is not null)
            {
                return mismatch;
            }

            if (reference.TryGet(names.Orient, out var refOrient)
                && (int)Math.Round(refOrient) != s.Orientation)
            {
                // COMPASS_ORIENT* обязан быть верным ДО калибровки по
                // фиксированному курсу: автоопределение ориентации работает
                // только в обычной калибровке. При неверной ориентации
                // процедура «успешно» оставит компас в негодном состоянии.
                return $"Слот {s.Slot}: поле ORIENT — борт {s.Orientation}, эталон {(int)Math.Round(refOrient)}. "
                     + "Калибровка по фиксированному курсу не исправляет ориентацию.";
            }

            if (reference.TryGet(names.Use, out var refUse)
                && (int)Math.Round(refUse) != s.UseFlag)
            {
                return $"Слот {s.Slot}: поле USE — борт {s.UseFlag}, эталон {(int)Math.Round(refUse)}.";
            }

            if (reference.TryGet(names.External, out var refExternal))
            {
                var refFlag = (int)Math.Round(refExternal);
                var refExternalness = refFlag != 0;
                var boardExternalness = s.ExternalFlag != 0;
                if (refExternalness != boardExternalness)
                {
                    return $"Слот {s.Slot}: поле EXTERNAL — борт {s.ExternalFlag}, эталон {refFlag}.";
                }

                if (refFlag != s.ExternalFlag)
                {
                    // 1 и 2 различаются только правом автоопределения
                    // перекрыть значение (2 — операторская блокировка).
                    // Разрешённая наружу «внешность» одинакова, отказывать не за что.
                    _progress.Log(
                        MavSeverity.Notice,
                        $"Слот {s.Slot}: EXTERNAL борт {s.ExternalFlag}, эталон {refFlag} — "
                      + "различие только в блокировке автоопределения, на калибровку не влияет.");
                }
            }
        }

        // Слот 1 обязан быть внешним компасом: приоритетный компас на
        // серийной сборке — вынесенный, и вся приёмка считает его основным.
        var first = slots[0];
        if (!first.IsPresent)
        {
            return "Слот 1 пуст: приоритетного компаса нет.";
        }

        var external = ClassifyExternal(first);
        if (external is null)
        {
            return $"Слот 1 ({DescribeDevice(first.DeviceId)}): признак внешнего компаса неоднозначен "
                 + $"(EXTERNAL={first.ExternalFlag}, шина {first.DeviceId.BusType}). Требуется внешний компас.";
        }

        if (external == false)
        {
            return $"Слот 1 ({DescribeDevice(first.DeviceId)}) — внутренний компас. "
                 + "Технологическая карта требует внешний компас в первом слоте.";
        }

        await AddCheckAsync(c, new CheckResult(
            "topology.slot1.external",
            "Слот 1 — внешний компас",
            CheckOutcome.Pass,
            $"{DescribeDevice(first.DeviceId)}, EXTERNAL={first.ExternalFlag}"), ct).ConfigureAwait(false);

        return null;
    }

    // ------------------------------------------------------------------
    // Стадия 4. WriteReference
    // ------------------------------------------------------------------

    private async Task<string?> RunWriteReferenceAsync(RunContext c, CancellationToken ct)
    {
        var reference = c.Reference!;

        // Mission Planner помечает COMPASS_DEV_ID* как ReadOnly, но это
        // ограничение самого GCS: PARAM_SET прошивка примет и значение
        // сохранит. Исключаем их не поэтому, а по причине со стороны прошивки:
        // DEV_ID сверяется при загрузке с реально обнаруженным датчиком, и
        // подложенное чужое значение «пересаживает» калибровку не на тот
        // сенсор. Ровно так же COMPASS_PRIO*_ID содержит идентификаторы
        // датчиков ИМЕННО ЭТОЙ платы: копия назовёт несуществующие датчики и
        // даст «PreArm: Compass N not found».
        foreach (var slot in c.TopologyBefore.Where(static s => s.IsPresent))
        {
            var names = NamesFor(slot.Slot);

            foreach (var name in TransferableNamesForSlot(names))
            {
                ct.ThrowIfCancellationRequested();

                if (IsNeverWrite(name))
                {
                    // Страховка от опечатки в списке переносимых имён.
                    _progress.Log(MavSeverity.Warning, $"Параметр {name} в списке запрещённых к записи — пропущен.");
                    continue;
                }

                if (!reference.TryGet(name, out var requested))
                {
                    // Отсутствующее в эталоне имя не подменяется значением по
                    // умолчанию: «по умолчанию» — это не калибровка, а её потеря.
                    c.SkippedNames.Add(name);
                    _progress.Log(MavSeverity.Notice, $"В эталоне нет {name} — параметр не записывается.");
                    continue;
                }

                var before = await ReadAsync(c, name, ct).ConfigureAwait(false);
                var alreadyEqual = ValuesEqual(before.Value, requested, before.Type, _tolerances.ParamVerifyTolerance);

                if (!alreadyEqual)
                {
                    await _link.WriteParamAsync(name, (float)requested, before.Type, ct).ConfigureAwait(false);
                }

                c.Pending.Add(new PendingWrite
                {
                    Name = name,
                    Slot = slot.Slot,
                    Before = before.Value,
                    Requested = requested,
                    Type = before.Type,
                    AlreadyEqual = alreadyEqual,
                });
            }
        }

        if (c.Pending.Count == 0)
        {
            return "Эталон не содержит ни одного переносимого параметра компаса — переносить нечего.";
        }

        await AddCheckAsync(c, new CheckResult(
            "write.reference",
            "Перенос калибровочных данных",
            CheckOutcome.Pass,
            c.SkippedNames.Count == 0
                ? $"Обработано параметров: {c.Pending.Count}. Пропусков нет."
                : $"Обработано параметров: {c.Pending.Count}. Нет в эталоне ({c.SkippedNames.Count}): "
                  + string.Join(", ", c.SkippedNames),
            c.Pending.Count,
            null), ct).ConfigureAwait(false);

        return null;
    }

    // ------------------------------------------------------------------
    // Стадия 5. VerifyWrites
    // ------------------------------------------------------------------

    private async Task<string?> RunVerifyWritesAsync(RunContext c, CancellationToken ct)
    {
        var coalesced = 0;

        foreach (var p in c.Pending)
        {
            ct.ThrowIfCancellationRequested();

            // Эхо PARAM_SET подтверждением не считается: оно широковещательное,
            // асинхронное и приходит с param_index = 0xFFFF. Проверяем
            // независимым чтением ПО ИМЕНИ — индексы параметров нестабильны.
            var readBack = await ReadAsync(c, p.Name, ct).ConfigureAwait(false);

            WriteOutcome outcome;
            if (SameFloat(readBack.Value, p.Before)
                && !SameFloat(p.Requested, p.Before)
                && IsInsideCoalescingBand(p.Before, p.Requested, p.Type))
            {
                // Прошивка сама пропускает запись нецелого параметра, если
                // новое значение отличается от старого меньше чем на 1e-4
                // относительных. Обратное чтение показывает старое значение —
                // это не отказ записи, а схлопывание.
                outcome = WriteOutcome.Coalesced;
                coalesced++;
            }
            else if (ValuesEqual(readBack.Value, p.Requested, readBack.Type, _tolerances.ParamVerifyTolerance))
            {
                outcome = p.AlreadyEqual ? WriteOutcome.AlreadyEqual : WriteOutcome.Verified;
            }
            else
            {
                outcome = WriteOutcome.Mismatch;
            }

            p.Accepted = readBack.Value;

            await AddWriteAsync(c, new ParamWriteRecord(
                p.Name, p.Before, p.Requested, readBack.Value, outcome, DateTimeOffset.UtcNow), ct)
                .ConfigureAwait(false);

            if (outcome == WriteOutcome.Coalesced)
            {
                _progress.Log(
                    MavSeverity.Notice,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: прошивка схлопнула запись ({1:G9} → {2:G9}, разница внутри полосы 1e-4). Не ошибка.",
                        p.Name, p.Before, p.Requested));
            }

            if (outcome == WriteOutcome.Mismatch)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Параметр {0}: записано {1:G9}, обратное чтение дало {2:G9} (было {3:G9}). "
                  + "Это не схлопывание — борт не принял значение.",
                    p.Name, p.Requested, readBack.Value, p.Before);
            }
        }

        await AddCheckAsync(c, new CheckResult(
            "write.verified",
            "Обратное чтение записанных параметров",
            CheckOutcome.Pass,
            $"Проверено {c.Pending.Count}, из них схлопнуто прошивкой {coalesced}.",
            c.Pending.Count,
            null), ct).ConfigureAwait(false);

        return null;
    }

    // ------------------------------------------------------------------
    // Стадия 6. Reboot
    // ------------------------------------------------------------------

    private async Task<string?> RunRebootAsync(RunContext c, CancellationToken ct)
    {
        await _link.RebootAndReconnectAsync(RebootTimeout, ct).ConfigureAwait(false);

        // Всё, что читалось до ребута, объявляется недостоверным. При загрузке
        // прошивка физически переставляет целые блоки параметров между слотами
        // по таблице приоритетов, так что даже совпадение имени ничего не
        // гарантирует. Кэш чтений очищается целиком.
        c.SessionReads.Clear();

        if (!_link.IsConnected)
        {
            return "Борт не вернулся на связь после перезагрузки.";
        }

        await AddCheckAsync(c, new CheckResult(
            "reboot.first",
            "Перезагрузка после переноса",
            CheckOutcome.Pass,
            $"Борт на связи, прошивка: {_link.FirmwareBanner ?? "баннер не получен"}"), ct).ConfigureAwait(false);

        return null;
    }

    // ------------------------------------------------------------------
    // Стадия 7. ReReadControlSample
    // ------------------------------------------------------------------

    private async Task<string?> RunReReadControlSampleAsync(RunContext c, CancellationToken ct)
    {
        // Контрольная выборка: все оси OFS и SCALE каждого слота. Именно они
        // несут перенесённое смещение и масштаб; если они не пережили ребут,
        // всё остальное обсуждать бессмысленно.
        var control = c.Pending
            .Where(static p => IsOffsetName(p.Name) || IsScaleName(p.Name))
            .ToArray();

        if (control.Length == 0)
        {
            return "В контрольной выборке нет ни одного параметра OFS/SCALE — переносить было нечего.";
        }

        foreach (var p in control)
        {
            ct.ThrowIfCancellationRequested();

            var readBack = await ReadAsync(c, p.Name, ct).ConfigureAwait(false);

            // Сравниваем с тем значением, которое борт подтвердил на стадии 5,
            // а не с запрошенным: у схлопнутой записи борт законно держит
            // старое значение, и требовать от него запрошенное — ошибка.
            var expected = p.Accepted ?? p.Requested;
            if (!ValuesEqual(readBack.Value, expected, readBack.Type, _tolerances.ParamVerifyTolerance))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Параметр {0} не пережил перезагрузку: до ребута {1:G9}, после {2:G9}.",
                    p.Name, expected, readBack.Value);
            }
        }

        await AddCheckAsync(c, new CheckResult(
            "persist.control",
            "Контрольная выборка после перезагрузки",
            CheckOutcome.Pass,
            $"Совпало {control.Length} из {control.Length} (OFS по трём осям и SCALE каждого слота).",
            control.Length,
            null), ct).ConfigureAwait(false);

        return null;
    }

    // ------------------------------------------------------------------
    // Стадия 8. FixedYaw
    // ------------------------------------------------------------------

    private async Task<string?> RunFixedYawAsync(RunContext c, CancellationToken ct)
    {
        var yawDeg = Wrap360(c.Request.JigAzimuthDeg);

        // Предупреждение идёт ДО команды: после неё отменять нечего —
        // результат сохраняется немедленно.
        _progress.Log(
            MavSeverity.Warning,
            string.Format(
                CultureInfo.InvariantCulture,
                "MAV_CMD_FIXED_MAG_CAL_YAW: азимут стапеля {0:F2}° (ИСТИННЫЙ, не магнитный), маска 0 — все компасы. "
              + "Команда сохраняет результат СРАЗУ, шага подтверждения нет.",
                yawDeg));

        // param1 — азимут, param2 = 0 (все включённые компасы),
        // param3/param4 = 0 — широта и долгота не задаются, борт берёт
        // собственное положение (AHRS, затем GPS). Остальное — нули.
        var result = await _link.SendCommandAsync(
            MavCommand.FixedMagCalYaw,
            (float)yawDeg, 0f, 0f, 0f, 0f, 0f, 0f,
            CommandAckTimeout,
            ct).ConfigureAwait(false);

        var texts = await DrainInboxAsync(c, ct).ConfigureAwait(false);

        if (result != MavResult.Accepted)
        {
            return $"Команда фиксированного курса отклонена: {result}. {DiagnoseFixedYaw(texts)}";
        }

        // ACK приходит раньше, чем прошивка досылает сообщения о снятых
        // компасах, — добираем их.
        await Task.Delay(PostCommandSettle, ct).ConfigureAwait(false);
        var afterTexts = await DrainInboxAsync(c, ct).ConfigureAwait(false);
        var allTexts = texts.Concat(afterTexts).ToArray();

        // Фиксация конфликта карты: команда обнуляет мягкожелезную
        // компенсацию. Это записывается в протокол как факт, а не как отказ.
        await AddCheckAsync(c, new CheckResult(
            "fixedyaw.autosave",
            "Побочные эффекты калибровки по фиксированному курсу",
            CheckOutcome.Pass,
            "Команда сохраняет смещения немедленно (set_and_save_offsets), шага подтверждения нет, "
          + "и принудительно устанавливает COMPASS_DIA*=(1,1,1) и COMPASS_ODI*=(0,0,0). "
          + "Значения DIA/ODI, перенесённые на стадии WriteReference и подтверждённые на стадии VerifyWrites, "
          + "с этого момента НЕ ДЕЙСТВУЮТ. COMPASS_SCALE* команда не трогает."), ct).ConfigureAwait(false);

        _progress.Log(
            MavSeverity.Alert,
            "ВНИМАНИЕ: калибровка по фиксированному курсу перезаписала мягкожелезную компенсацию. "
          + "Перенесённые из эталона COMPASS_DIA* и COMPASS_ODI* заменены на единичные (1,1,1) и нулевые (0,0,0). "
          + "В протокол попадут фактические значения, прочитанные с борта после команды.");

        // Протокол не имеет права показывать значение, которого на борту уже
        // нет: перечитываем DIA/ODI/OFS/SCALE всех слотов и дописываем в
        // журнал фактическое состояние.
        foreach (var slot in c.TopologyBefore.Where(static s => s.IsPresent))
        {
            var names = NamesFor(slot.Slot);
            var actual = new List<string>(10);

            foreach (var name in PostCalibrationNames(names))
            {
                ct.ThrowIfCancellationRequested();

                var previous = c.Pending.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
                var value = await ReadAsync(c, name, ct).ConfigureAwait(false);

                await AddWriteAsync(c, new ParamWriteRecord(
                    Name: name,
                    Before: previous?.Accepted ?? previous?.Requested,
                    Requested: value.Value,
                    ReadBack: value.Value,
                    Outcome: WriteOutcome.Verified,
                    AtUtc: DateTimeOffset.UtcNow), ct).ConfigureAwait(false);

                actual.Add(string.Format(CultureInfo.InvariantCulture, "{0}={1:G9}", name, value.Value));
            }

            await AddCheckAsync(c, new CheckResult(
                $"fixedyaw.actual.slot{slot.Slot}",
                $"Фактические значения после калибровки, слот {slot.Slot}",
                CheckOutcome.Pass,
                string.Join("; ", actual)), ct).ConfigureAwait(false);
        }

        // Команда начинает с _reset_compass_id(): она может обнулить
        // COMPASS_PRIO*_ID и сообщить о снятом компасе.
        var removed = allTexts
            .Where(static t => t.Text.Contains("removed", StringComparison.OrdinalIgnoreCase)
                            && t.Text.Contains("DEVID", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (removed.Length != 0)
        {
            _progress.Log(
                MavSeverity.Alert,
                "Прошивка сняла компас из таблицы приоритетов: " + string.Join(" | ", removed.Select(static t => t.Text)));
        }

        // Таблица приоритетов не должна была измениться: слот 1 обязан
        // остаться внешним компасом.
        var slot1 = await ReadSlotAsync(c, 1, ct).ConfigureAwait(false);
        var before1 = c.TopologyBefore.FirstOrDefault(static s => s.Slot == 1);

        if (before1 is not null && slot1.DeviceId.Raw != before1.DeviceId.Raw)
        {
            return $"Таблица приоритетов изменилась: слот 1 был {DescribeDevice(before1.DeviceId)}, "
                 + $"стал {DescribeDevice(slot1.DeviceId)}. "
                 + (removed.Length != 0 ? string.Join(" | ", removed.Select(static t => t.Text)) : string.Empty);
        }

        if (ClassifyExternal(slot1) != true)
        {
            return $"После калибровки слот 1 ({DescribeDevice(slot1.DeviceId)}) перестал определяться как внешний компас "
                 + $"(EXTERNAL={slot1.ExternalFlag}).";
        }

        // COMPASS_PRIO1_ID появился в 4.1; на более старой прошивке его чтение
        // законно отказывает и это не повод валить прогон.
        try
        {
            var prio1 = await ReadAsync(c, Prio1IdName, ct).ConfigureAwait(false);
            var prioRaw = ToRawDeviceId(prio1.Value);
            if (prioRaw != 0 && prioRaw != slot1.DeviceId.Raw)
            {
                return $"{Prio1IdName}={prioRaw} не соответствует обнаруженному в слоте 1 "
                     + $"{DescribeDevice(slot1.DeviceId)} — таблица приоритетов изменена.";
            }

            if (prioRaw == 0)
            {
                _progress.Log(
                    MavSeverity.Notice,
                    $"{Prio1IdName}=0 — прошивка заполнит его автоматически при следующей загрузке.");
            }
        }
        catch (VehicleLinkException ex)
        {
            _progress.Log(MavSeverity.Notice, $"{Prio1IdName} не прочитан ({ex.Message}) — прошивка старше 4.1.");
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Стадия 9. Acceptance
    // ------------------------------------------------------------------

    private async Task<string?> RunAcceptanceAsync(RunContext c, CancellationToken ct)
    {
        // Второй ребут: результат калибровки оценивается с чистой загрузки,
        // после того как прошивка перечитала параметры и пересобрала таблицу
        // приоритетов. Иначе часть предполётных проверок отвечает «требуется
        // перезагрузка», а не по существу.
        await _link.RebootAndReconnectAsync(RebootTimeout, ct).ConfigureAwait(false);
        c.SessionReads.Clear();

        if (!_link.IsConnected)
        {
            return "Борт не вернулся на связь после второй перезагрузки.";
        }

        c.TopologyAfter.Clear();
        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            c.TopologyAfter.Add(await ReadSlotAsync(c, slot, ct).ConfigureAwait(false));
        }

        var after1 = c.TopologyAfter[0];
        var before1 = c.TopologyBefore.FirstOrDefault(static s => s.Slot == 1);
        if (before1 is not null && after1.DeviceId.Raw != before1.DeviceId.Raw)
        {
            return $"После перезагрузки слот 1 занят другим датчиком: было {DescribeDevice(before1.DeviceId)}, "
                 + $"стало {DescribeDevice(after1.DeviceId)}.";
        }

        if (ClassifyExternal(after1) != true)
        {
            return $"После перезагрузки слот 1 ({DescribeDevice(after1.DeviceId)}) не является внешним компасом "
                 + $"(EXTERNAL={after1.ExternalFlag}).";
        }

        var prearm = await _link.RunPrearmChecksAsync(_tolerances.PrearmWindow, ct).ConfigureAwait(false);
        c.PrearmMessages.Clear();
        c.PrearmMessages.AddRange(prearm.Messages);
        foreach (var m in prearm.Messages)
        {
            ct.ThrowIfCancellationRequested();
            _progress.Log(m.Severity, m.Text);
            if (c.RunOpen)
            {
                await _store.RecordMessageAsync(c.RunId, m, ct).ConfigureAwait(false);
            }
        }

        var telemetry = await _link.CaptureTelemetryAsync(_tolerances.TelemetryWindow, ct).ConfigureAwait(false);

        var checks = InvokeAcceptanceChecks(c, telemetry, prearm);
        foreach (var check in checks)
        {
            await AddCheckAsync(c, check, ct).ConfigureAwait(false);
        }

        var overall = FoldAcceptance(checks);
        if (overall != CheckOutcome.Pass)
        {
            // Повторов нет: технологическая карта говорит «стоп». Повторный
            // прогон — решение оператора, а не автомата.
            var bad = checks.Where(static r => r.Outcome != CheckOutcome.Pass).ToArray();
            var detail = bad.Length == 0
                ? "приёмка не подтверждена"
                : string.Join("; ", bad.Select(static r => $"{r.Title} — {r.Outcome}: {r.Detail}"));
            return $"Приёмочные проверки не пройдены ({overall}): {detail}";
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Общие помощники прогона
    // ------------------------------------------------------------------

    private async Task<CompassSlot> ReadSlotAsync(RunContext c, int slot, CancellationToken ct)
    {
        var names = NamesFor(slot);

        var devRaw = ToRawDeviceId((await ReadAsync(c, names.DevId, ct).ConfigureAwait(false)).Value);
        var deviceId = new CompassDeviceId(devRaw);

        if (deviceId.IsEmpty)
        {
            // Пустой слот: остальные его параметры существуют, но ничего не
            // описывают. Лишние чтения только замедляют прогон.
            return new CompassSlot(slot, deviceId, 0, 0, 0, (0d, 0d, 0d));
        }

        var external = (await ReadAsync(c, names.External, ct).ConfigureAwait(false)).AsInt();
        var use = (await ReadAsync(c, names.Use, ct).ConfigureAwait(false)).AsInt();
        var orient = (await ReadAsync(c, names.Orient, ct).ConfigureAwait(false)).AsInt();

        var ofsX = (await ReadAsync(c, names.Ofs[0], ct).ConfigureAwait(false)).Value;
        var ofsY = (await ReadAsync(c, names.Ofs[1], ct).ConfigureAwait(false)).Value;
        var ofsZ = (await ReadAsync(c, names.Ofs[2], ct).ConfigureAwait(false)).Value;

        return new CompassSlot(slot, deviceId, external, use, orient, (ofsX, ofsY, ofsZ));
    }

    private async Task<ParamValue> ReadAsync(RunContext c, string name, CancellationToken ct)
    {
        var value = await _link.ReadParamAsync(name, ct).ConfigureAwait(false);
        c.SessionReads[name] = value;
        return value;
    }

    private async Task AddCheckAsync(RunContext c, CheckResult result, CancellationToken ct)
    {
        c.Checks.Add(result);
        if (c.RunOpen)
        {
            await _store.RecordCheckAsync(c.RunId, result, ct).ConfigureAwait(false);
        }

        _progress.Log(
            result.Outcome == CheckOutcome.Pass ? MavSeverity.Info : MavSeverity.Warning,
            $"{result.Title}: {result.Detail}");
    }

    private async Task AddWriteAsync(RunContext c, ParamWriteRecord record, CancellationToken ct)
    {
        c.Writes.Add(record);
        if (c.RunOpen)
        {
            await _store.RecordWriteAsync(c.RunId, record, ct).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<StatusTextEvent>> DrainInboxAsync(RunContext c, CancellationToken ct)
    {
        var drained = new List<StatusTextEvent>();
        while (c.Inbox.TryDequeue(out var message))
        {
            drained.Add(message);
            c.Messages.Add(message);
            _progress.Log(message.Severity, message.Text);

            if (c.RunOpen)
            {
                await _store.RecordMessageAsync(c.RunId, message, ct).ConfigureAwait(false);
            }
        }

        return drained;
    }

    private async Task<CalibrationRunResult> CompleteAsync(
        RunContext c,
        bool passed,
        CalibrationStage? failedStage,
        string? reason,
        CancellationToken ct)
    {
        var result = new CalibrationRunResult(
            passed,
            failedStage,
            reason,
            c.Checks.ToArray(),
            c.Writes.ToArray(),
            c.TopologyBefore.ToArray(),
            c.TopologyAfter.ToArray(),
            c.PrearmMessages.ToArray(),
            SafeBanner(),
            c.StartedUtc,
            DateTimeOffset.UtcNow);

        if (c.RunOpen)
        {
            try
            {
                await _store.CompleteRunAsync(c.RunId, result, ct).ConfigureAwait(false);
                c.RunOpen = false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Отказ реестра не должен скрыть от оператора результат замера:
                // деталь уже в приспособлении, и её судьбу решать сейчас.
                // Прогон останется помеченным как брошенный и будет подобран
                // SweepAbandonedRunsAsync при следующем запуске.
                _progress.Log(MavSeverity.Critical, $"Прогон не закрыт в реестре: {ex.Message}");
            }
        }

        return result;
    }

    private string? SafeBanner()
    {
        try
        {
            return _link.FirmwareBanner;
        }
        catch (VehicleLinkException)
        {
            return null;
        }
    }

    private static string DiagnoseFixedYaw(IReadOnlyList<StatusTextEvent> texts)
    {
        // Известные тексты прошивки. Порядок — от самой частой причины к редкой.
        var known = new[]
        {
            "Mag: no position available",
            "Mag: WMM table error",
            "unhealthy",
            "bad uncorrected field",
        };

        var matched = texts
            .Where(t => known.Any(k => t.Text.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Select(static t => t.Text)
            .ToArray();

        if (matched.Length != 0)
        {
            return "Диагностика прошивки: " + string.Join(" | ", matched);
        }

        var severe = texts
            .Where(static t => t.Severity <= MavSeverity.Warning)
            .Select(static t => t.Text)
            .ToArray();

        return severe.Length != 0
            ? "Сообщения борта: " + string.Join(" | ", severe)
            : "Прошивка не прислала пояснения (STATUSTEXT не получены).";
    }

    private static double Wrap360(double deg)
    {
        var wrapped = deg % 360d;
        return wrapped < 0d ? wrapped + 360d : wrapped;
    }

    private static uint ToRawDeviceId(double value) => value <= 0d ? 0u : (uint)Math.Round(value);

    /// <summary>
    /// Побитово-эквивалентное сравнение в решётке binary32. Ноль со знаком
    /// нормализуется: <c>-0f == 0f</c> — прошивка не различает их, и протокол
    /// не должен различать тоже.
    /// </summary>
    private static bool SameFloat(double a, double b) => (float)a == (float)b;

    /// <summary>
    /// Полоса схлопывания прошивки: <c>save_sync()</c> пропускает запись
    /// нецелого параметра, если <c>|v1 - v2| &lt; 1e-4 * |v1|</c>.
    /// </summary>
    private static bool IsInsideCoalescingBand(double before, double requested, MavParamType type)
        => type is not MavParamType.Int32
        && Math.Abs(before - requested) < FirmwareCoalescingBand * Math.Abs(before);

    private const double FirmwareCoalescingBand = 1e-4;

    private static string AppVersion =>
        typeof(SerialCompassCalibrationJob).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(SerialCompassCalibrationJob).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    // ==================================================================
    // Внутреннее состояние прогона
    // ==================================================================

    private const int MaxSlots = 3;
    private const string CompassPrefix = "COMPASS_";
    private const string Prio1IdName = "COMPASS_PRIO1_ID";

    private sealed class RunContext
    {
        public RunContext(CalibrationRequest request) => Request = request;

        public CalibrationRequest Request { get; }

        public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;

        public long RunId { get; set; }

        public bool RunOpen { get; set; }

        public CalibrationStage Stage { get; set; } = CalibrationStage.Connect;

        public ReferenceParamSet? Reference { get; set; }

        public string ReferenceHash { get; set; } = string.Empty;

        public GpsFix? Fix { get; set; }

        public List<CompassSlot> TopologyBefore { get; } = new();

        public List<CompassSlot> TopologyAfter { get; } = new();

        public List<CheckResult> Checks { get; } = new();

        public List<ParamWriteRecord> Writes { get; } = new();

        public List<StatusTextEvent> Messages { get; } = new();

        public List<StatusTextEvent> PrearmMessages { get; } = new();

        public List<string> SkippedNames { get; } = new();

        public List<PendingWrite> Pending { get; } = new();

        /// <summary>Очередь STATUSTEXT: событие приходит из потока канала, разбирается из потока прогона.</summary>
        public ConcurrentQueue<StatusTextEvent> Inbox { get; } = new();

        /// <summary>Чтения текущей сессии. Обнуляется при каждом ребуте — после него ничему верить нельзя.</summary>
        public Dictionary<string, ParamValue> SessionReads { get; } = new(StringComparer.Ordinal);
    }

    private sealed class PendingWrite
    {
        public required string Name { get; init; }

        public int Slot { get; init; }

        public double Before { get; init; }

        public double Requested { get; init; }

        public MavParamType Type { get; init; }

        /// <summary>Значение уже совпадало — запись не отправлялась.</summary>
        public bool AlreadyEqual { get; init; }

        /// <summary>Значение, подтверждённое обратным чтением на стадии VerifyWrites.</summary>
        public double? Accepted { get; set; }
    }

    private sealed record SlotNames(
        int Slot,
        string DevId,
        string External,
        string Use,
        string Orient,
        string Scale,
        IReadOnlyList<string> Ofs,
        IReadOnlyList<string> Dia,
        IReadOnlyList<string> Odi,
        IReadOnlyList<string> Mot);

    // ==================================================================
    // TODO(integration): переходники к соседним типам.
    //
    // Ниже собрано всё, что по технологической карте принадлежит
    // CompassIdentity / ReferenceParamFile / AcceptanceChecks. Точные имена
    // членов этих типов на момент написания неизвестны, поэтому вызовы
    // сведены в одно место: оркестратору достаточно заменить тела методов
    // этого блока, не трогая логику стадий.
    // ==================================================================

    /// <summary>
    /// TODO(integration): ожидается <c>CompassIdentity.ParamNamesForSlot(slot)</c>
    /// с теми же полями. Локальная таблица построена по проверенным именам
    /// ArduPilot; единственное нерегулярное имя — EXTERNAL: слот 1 —
    /// <c>COMPASS_EXTERNAL</c>, слоты 2 и 3 — <c>COMPASS_EXTERN2/EXTERN3</c>
    /// (не <c>EXTERNAL2</c>).
    /// </summary>
    private static SlotNames NamesFor(int slot)
    {
        var suffix = slot == 1 ? string.Empty : slot.ToString(CultureInfo.InvariantCulture);

        return new SlotNames(
            slot,
            DevId: CompassPrefix + "DEV_ID" + suffix,
            External: slot == 1 ? CompassPrefix + "EXTERNAL" : CompassPrefix + "EXTERN" + suffix,
            Use: CompassPrefix + "USE" + suffix,
            Orient: CompassPrefix + "ORIENT" + suffix,
            Scale: CompassPrefix + "SCALE" + suffix,
            Ofs: Axes(CompassPrefix + "OFS" + suffix),
            Dia: Axes(CompassPrefix + "DIA" + suffix),
            Odi: Axes(CompassPrefix + "ODI" + suffix),
            Mot: Axes(CompassPrefix + "MOT" + suffix));

        static string[] Axes(string prefix) => new[] { prefix + "_X", prefix + "_Y", prefix + "_Z" };
    }

    /// <summary>
    /// TODO(integration): ожидается список переносимых имён из
    /// <c>CompassIdentity</c> (тот же набор, что прошивка сама переносит при
    /// смене приоритета). ORIENT/EXTERNAL/USE сюда намеренно не входят: карта
    /// их проверяет, но не правит.
    /// </summary>
    private static IEnumerable<string> TransferableNamesForSlot(SlotNames names)
    {
        foreach (var n in names.Ofs)
        {
            yield return n;
        }

        foreach (var n in names.Dia)
        {
            yield return n;
        }

        foreach (var n in names.Odi)
        {
            yield return n;
        }

        // COMPASS_MOT* — результат CompassMot, он зависит от разводки питания.
        // Перенос допустим только для идентичной сборки, что и есть случай
        // серийной приёмки по эталону этой же модели.
        foreach (var n in names.Mot)
        {
            yield return n;
        }

        yield return names.Scale;
    }

    /// <summary>Имена, перечитываемые после калибровки по фиксированному курсу.</summary>
    private static IEnumerable<string> PostCalibrationNames(SlotNames names)
    {
        foreach (var n in names.Dia)
        {
            yield return n;
        }

        foreach (var n in names.Odi)
        {
            yield return n;
        }

        foreach (var n in names.Ofs)
        {
            yield return n;
        }

        yield return names.Scale;
    }

    /// <summary>
    /// TODO(integration): ожидается список запрещённых к записи имён из
    /// <c>CompassIdentity</c>. Здесь он продублирован как страховка: запись
    /// <c>COMPASS_DEV_ID*</c> пересаживает калибровку не на тот датчик, запись
    /// <c>COMPASS_PRIO*_ID</c> называет датчики чужой платы.
    /// </summary>
    private static bool IsNeverWrite(string name)
        => name.StartsWith(CompassPrefix + "DEV_ID", StringComparison.Ordinal)
        || (name.StartsWith(CompassPrefix + "PRIO", StringComparison.Ordinal)
            && name.EndsWith("_ID", StringComparison.Ordinal));

    private static bool IsOffsetName(string name)
        => name.StartsWith(CompassPrefix + "OFS", StringComparison.Ordinal);

    private static bool IsScaleName(string name)
        => name.StartsWith(CompassPrefix + "SCALE", StringComparison.Ordinal);

    /// <summary>
    /// TODO(integration): ожидается <c>CompassIdentity</c>-рендер
    /// <see cref="CompassDeviceId"/> вида <c>I2C:bus 1:addr 0x0E:IST8310</c>
    /// (с расшифровкой devtype). Здесь devtype печатается числом.
    /// </summary>
    private static string DescribeDevice(CompassDeviceId id)
        => id.IsEmpty
            ? "слот пуст"
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0}:bus {1}:addr 0x{2:X2}:devtype 0x{3:X2} (raw {4})",
                id.BusType, id.Bus, id.Address, id.DevType, id.Raw);

    /// <summary>
    /// TODO(integration): ожидается классификатор <c>CompassIdentity</c>
    /// «внутренний / внешний / неоднозначно». Возвращает <c>true</c> —
    /// внешний, <c>false</c> — внутренний, <c>null</c> — судить нельзя.
    /// </summary>
    /// <remarks>
    /// Флаг <c>COMPASS_EXTERNAL*</c> после загрузки — это уже разрешённая
    /// прошивкой истина: драйвер перезаписывает 0/1 по факту обнаружения, а
    /// значение 2 запрещает автоопределению его трогать. Тип шины служит
    /// подтверждением: DroneCAN, MSP и последовательные компасы всегда внешние,
    /// внутренний SPI — всегда внутренний.
    /// </remarks>
    private static bool? ClassifyExternal(CompassSlot slot)
    {
        if (!slot.IsPresent)
        {
            return null;
        }

        var busSaysExternal = slot.DeviceId.BusType
            is MavBusType.DroneCan or MavBusType.Msp or MavBusType.Serial;

        if (slot.ExternalFlag == 2)
        {
            return true;
        }

        if (slot.ExternalFlag == 1)
        {
            return true;
        }

        if (slot.ExternalFlag == 0)
        {
            // Шина говорит «внешний», а флаг — «внутренний»: разойтись они не
            // должны, судить по одному из них нельзя.
            return busSaysExternal ? null : false;
        }

        return null;
    }

    /// <summary>
    /// TODO(integration): ожидается сравнение слота с эталонной конфигурацией
    /// из <c>CompassIdentity</c> (тип шины + адрес + devtype). Здесь нужен
    /// разбор до подполя, чтобы отказ называл конкретное расхождение.
    /// </summary>
    private static string? CompareDeviceFields(int slot, CompassDeviceId board, CompassDeviceId reference)
    {
        if (board.BusType != reference.BusType)
        {
            return $"Слот {slot}: тип шины — борт {board.BusType}, эталон {reference.BusType}.";
        }

        if (board.Address != reference.Address)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Слот {0}: адрес на шине — борт 0x{1:X2}, эталон 0x{2:X2}.",
                slot, board.Address, reference.Address);
        }

        if (board.DevType != reference.DevType)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Слот {0}: тип датчика (devtype) — борт 0x{1:X2}, эталон 0x{2:X2}.",
                slot, board.DevType, reference.DevType);
        }

        return null;
    }

    /// <summary>
    /// TODO(integration): ожидается типизированное сравнение из
    /// <c>ReferenceParamFile</c>. Целые сравниваются точно, вещественные —
    /// в решётке binary32 с относительным допуском.
    /// </summary>
    private static bool ValuesEqual(double a, double b, MavParamType type, double relativeTolerance)
    {
        if (type is MavParamType.Int8 or MavParamType.Int16 or MavParamType.Int32)
        {
            return (long)Math.Round(a) == (long)Math.Round(b);
        }

        var fa = (float)a;
        var fb = (float)b;
        if (fa.Equals(fb))
        {
            return true;
        }

        var diff = MathF.Abs(fa - fb);
        var scale = MathF.Max(MathF.Abs(fa), MathF.Abs(fb));
        return diff <= (float)relativeTolerance * scale || diff <= 1e-9f;
    }

    /// <summary>
    /// TODO(integration): ожидается SHA-256 содержимого эталона из
    /// <c>ReferenceParamFile</c>. Важно, чтобы хеш совпадал с тем, который
    /// показывают остальные экраны, иначе реестр не свяжется с файлом.
    /// </summary>
    private static string ComputeReferenceHash(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        var hash = SHA256.HashData(stream);

        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>
    /// TODO(integration): ожидается <c>AcceptanceChecks</c> с четырьмя
    /// проверками — здоровье магнитометров по <c>SYS_STATUS</c>, чистота
    /// предполётных проверок, курс борта против азимута стапеля
    /// (<see cref="CalibrationTolerances.HeadingVsJigDeg"/>) и расхождение
    /// курсов между компасами
    /// (<see cref="CalibrationTolerances.InterCompassSpreadDeg"/>).
    /// Здесь ожидается агрегирующий вызов, возвращающий все четыре
    /// <see cref="CheckResult"/>.
    /// </summary>
    private IReadOnlyList<CheckResult> InvokeAcceptanceChecks(
        RunContext c,
        TelemetrySnapshot telemetry,
        PrearmReport prearm)
        // TODO(alpha): предел смещений берётся из значения по умолчанию.
        // Правильнее прочитать COMPASS_OFFS_MAX с борта — на плате он мог быть изменён.
        => AcceptanceChecks.RunAll(
            prearm,
            c.TopologyAfter,
            AcceptanceChecks.DefaultOffsMax,
            telemetry,
            c.Request.JigAzimuthDeg,
            _tolerances);

    /// <summary>
    /// TODO(integration): ожидается свёртка <c>AcceptanceChecks</c> к общему
    /// вердикту. <see cref="CheckOutcome.Inconclusive"/> не считается
    /// прохождением — недостаток данных это не «норма».
    /// </summary>
    private static CheckOutcome FoldAcceptance(IReadOnlyList<CheckResult> checks)
        => AcceptanceChecks.Fold(checks);
}
