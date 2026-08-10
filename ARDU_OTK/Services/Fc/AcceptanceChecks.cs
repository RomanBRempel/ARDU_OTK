using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ARDU_OTK.Services.Fc;

/// <summary>
/// К чему сводится претензия предполётной проверки к компасу — то есть чем
/// оператор её закроет.
/// </summary>
/// <remarks>
/// Классификация нужна протоколу: «перезагрузить», «откалибровать» и «убрать
/// железо со стапеля» — три разных действия, и по голому тексту сообщения
/// оператор их путает.
/// </remarks>
public enum CompassComplaintKind
{
    /// <summary>Снимается перезагрузкой борта: значение записано, но ещё не применено.</summary>
    Reboot,

    /// <summary>Снимается калибровкой (или корректным переносом калибровочного блока).</summary>
    Calibration,

    /// <summary>Внешняя причина: магнитная обстановка стапеля, близкое железо, ток в проводах.</summary>
    Environment,

    /// <summary>Железо: датчик не отвечает или его вовсе нет по заявленному <c>DEV_ID</c>.</summary>
    Hardware,

    /// <summary>Калибровка прямо сейчас идёт — судить рано, надо дождаться конца.</summary>
    Running,

    /// <summary>Строка про компас, не совпавшая ни с одним известным шаблоном.</summary>
    Unknown,
}

/// <summary>Разобранная претензия предполётной проверки, относящаяся к компасу.</summary>
/// <param name="Message">Исходное сообщение борта, как оно пришло.</param>
/// <param name="Text">Текст без префикса <c>PreArm:</c>, по нему шло сопоставление.</param>
/// <param name="Kind">Чем эта претензия закрывается.</param>
/// <param name="Advice">Готовая подсказка оператору.</param>
public sealed record CompassComplaint(
    StatusTextEvent Message,
    string Text,
    CompassComplaintKind Kind,
    string Advice);

/// <summary>
/// Приёмочные критерии серийной калибровки компаса: чистые функции над
/// контрактными типами.
/// </summary>
/// <remarks>
/// Здесь нет ни MAVLink, ни ввода-вывода, ни UI — всё измеренное приходит
/// параметрами. Это сделано ради проверяемости: приёмка должна прогоняться
/// юнит-тестами без борта на столе, иначе её нельзя изменить, не рискуя
/// серийным выпуском. Все функции детерминированы и без побочных эффектов,
/// числа форматируются инвариантной культурой, чтобы результат не зависел от
/// языка системы.
/// </remarks>
public static class AcceptanceChecks
{
    /// <summary>
    /// Имя параметра порога смещений. Именно <c>COMPASS_OFFS_MAX</c>, с двумя «S»:
    /// в технологической карте опечатка <c>COMPASS_OFS_MAX</c>, такого параметра
    /// в прошивке нет (<c>COMPASS_OFS_*</c> — это сами смещения по осям).
    /// </summary>
    public const string OffsMaxParamName = "COMPASS_OFFS_MAX";

    /// <summary>Значение <see cref="OffsMaxParamName"/> по умолчанию в прошивке, мГс.</summary>
    /// <remarks>
    /// Документация на сайте всё ещё называет порог 600 — это устаревшая цифра,
    /// код сравнивает с <c>COMPASS_OFFS_MAX</c> (диапазон 500…3000, по умолчанию 1800).
    /// </remarks>
    public const double DefaultOffsMax = 1800.0;

    /// <summary>Ожидаемый модуль магнитного поля, мГс.</summary>
    public const double MagFieldExpectedMilligauss = 530.0;

    /// <summary>Нижняя граница допустимого модуля поля, мГс (ниже борт ругается <c>Check mag field</c>).</summary>
    public const double MagFieldMinMilligauss = 185.0;

    /// <summary>Верхняя граница допустимого модуля поля, мГс.</summary>
    public const double MagFieldMaxMilligauss = 875.0;

    /// <summary>
    /// Сколько отсчётов <c>ATTITUDE</c> должно попасть в окно, чтобы курсу можно было верить.
    /// </summary>
    /// <remarks>
    /// Один-два кадра — это ещё не установившееся значение оценщика; при пустом
    /// окне поля <c>AttitudeSample</c> просто нулевые, и без этого порога нулевой
    /// курс выглядел бы как честно измеренный.
    /// </remarks>
    public const int DefaultMinAttitudeSamples = 5;

    /// <summary>Стабильные идентификаторы проверок для реестра и протокола.</summary>
    public static class CheckIds
    {
        /// <summary>Предполётные проверки не жалуются на компас.</summary>
        public const string PrearmCompass = "acceptance.prearm.compass";

        /// <summary>Модуль смещений слота против <c>COMPASS_OFFS_MAX</c>. Дополняется <c>.slotN</c>.</summary>
        public const string CompassOffsets = "acceptance.compass.offsets";

        /// <summary>Курс борта против азимута стапеля.</summary>
        public const string HeadingVsJig = "acceptance.heading.jig";

        /// <summary>Расхождение курсов между компасами.</summary>
        public const string InterCompassSpread = "acceptance.compass.spread";

        /// <summary>Модуль поля одного компаса против ожидаемой полосы. Дополняется <c>.instN</c>.</summary>
        public const string MagFieldMagnitude = "acceptance.compass.field";
    }

    private sealed record ComplaintRule(Regex Pattern, CompassComplaintKind Kind, string Advice);

    private const RegexOptions RuleOptions = RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;

    /// <summary>
    /// Шаблоны сообщений компаса. Сопоставление идёт по устойчивому началу
    /// строки, а не по равенству: прошивка подставляет в текст номера и
    /// измеренные значения (<c>Compass 2 not healthy</c>, <c>Check mag field: 1204, max 875, min 185</c>),
    /// поэтому сравнение целых строк не поймало бы ни одного реального случая.
    /// Порядок важен: частные варианты <c>Check mag field (…)</c> стоят раньше общего.
    /// </summary>
    private static readonly ComplaintRule[] Rules =
    [
        new(new Regex(@"^Compass calibration running", RuleOptions),
            CompassComplaintKind.Running,
            "Калибровка компаса выполняется прямо сейчас — дождитесь её окончания и повторите приёмку."),

        new(new Regex(@"^Compass calibrated requires reboot", RuleOptions),
            CompassComplaintKind.Reboot,
            "Калибровка записана, но не применена: перезагрузите борт и повторите проверку."),

        new(new Regex(@"^Compass order change requires reboot", RuleOptions),
            CompassComplaintKind.Reboot,
            "Изменён порядок компасов (COMPASS_PRIOx_ID): перезагрузите борт — блоки параметров переставляются только при загрузке."),

        new(new Regex(@"^Compass not calibrated", RuleOptions),
            CompassComplaintKind.Calibration,
            "Компас не считается откалиброванным: смещения нулевые либо COMPASS_DEV_IDx не совпадает с обнаруженным датчиком. Выполните калибровку на этом борту."),

        new(new Regex(@"^Compass offsets too high", RuleOptions),
            CompassComplaintKind.Calibration,
            $"Модуль смещений превышает {OffsMaxParamName}: калибровка негодная, повторите её вдали от железа."),

        new(new Regex(@"^Compasses inconsistent", RuleOptions),
            CompassComplaintKind.Environment,
            "Компасы расходятся между собой (порог 90° xyz / 60° xy / 200 мГс по длине): проверьте магнитную обстановку стапеля и ориентацию COMPASS_ORIENT*, затем перекалибруйте."),

        new(new Regex(@"^Check mag field \(xy diff:", RuleOptions),
            CompassComplaintKind.Environment,
            "Расхождение горизонтальной составляющей поля между компасами: уберите со стапеля ферромагнетики и источники тока."),

        new(new Regex(@"^Check mag field \(z diff:", RuleOptions),
            CompassComplaintKind.Environment,
            "Расхождение вертикальной составляющей поля между компасами: уберите со стапеля ферромагнетики и источники тока."),

        new(new Regex(@"^Check mag field", RuleOptions),
            CompassComplaintKind.Environment,
            "Модуль магнитного поля вне полосы 185…875 мГс (норма 530): рядом железо или магнит, либо датчик врёт."),

        new(new Regex(@"^Compass \d+ not healthy", RuleOptions),
            CompassComplaintKind.Hardware,
            "Компас не отвечает: проверьте шлейф, шину и питание датчика."),

        new(new Regex(@"^Compass \d+ not found", RuleOptions),
            CompassComplaintKind.Hardware,
            "Заявленный компас не обнаружен: скорее всего в COMPASS_PRIOx_ID перенесён DEV_ID чужого борта. Переприсвойте приоритеты по собственным COMPASS_DEV_IDx и перезагрузитесь."),
    ];

    /// <summary>Убирает префикс <c>PreArm:</c>, если он остался в строке, и обрезает пробелы.</summary>
    private static string StripPrearmPrefix(string text)
    {
        var t = text.Trim();
        const string prefix = "PreArm:";
        if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            t = t[prefix.Length..].Trim();
        }

        return t;
    }

    private static string Fmt(double value, int digits) =>
        value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>
    /// Разбирает сообщения предполётной проверки и оставляет только относящиеся
    /// к компасу, снабдив каждое классом и подсказкой.
    /// </summary>
    /// <param name="messages">Собранные строки; <c>null</c> считается пустым набором.</param>
    /// <returns>Претензии в том порядке, в каком пришли сообщения.</returns>
    public static IReadOnlyList<CompassComplaint> ClassifyCompassComplaints(IEnumerable<StatusTextEvent>? messages)
    {
        if (messages is null)
        {
            return Array.Empty<CompassComplaint>();
        }

        var result = new List<CompassComplaint>();
        foreach (var message in messages)
        {
            if (message is null || string.IsNullOrWhiteSpace(message.Text))
            {
                continue;
            }

            var text = StripPrearmPrefix(message.Text);
            var rule = Array.Find(Rules, r => r.Pattern.IsMatch(text));
            if (rule is not null)
            {
                result.Add(new CompassComplaint(message, text, rule.Kind, rule.Advice));
                continue;
            }

            // Незнакомая строка про компас — не повод её проглотить: прошивка
            // могла добавить новый текст, и молчаливый пропуск превратился бы в
            // ложную приёмку.
            var mentionsCompass = text.Contains("ompass", StringComparison.Ordinal)
                                  || text.Contains("mag field", StringComparison.OrdinalIgnoreCase);
            if (mentionsCompass)
            {
                result.Add(new CompassComplaint(
                    message,
                    text,
                    CompassComplaintKind.Unknown,
                    "Неизвестное сообщение о компасе — разберите вручную и при необходимости дополните список шаблонов."));
            }
        }

        return result;
    }

    /// <summary>
    /// Проверка 1: предполётные проверки не имеют претензий к компасу.
    /// </summary>
    /// <param name="report">Итог команды <c>MAV_CMD_RUN_PREARM_CHECKS</c> (401).</param>
    /// <returns>Один <see cref="CheckResult"/> с перечнем найденных претензий.</returns>
    /// <remarks>
    /// Отсутствие сообщений само по себе ничего не доказывает: борт мог не
    /// понять команду 401, мог не прислать ни одного <c>SYS_STATUS</c> за окно,
    /// или бит <c>PREARM_CHECK</c> мог быть не заявлен. Во всех трёх случаях
    /// исход <see cref="CheckOutcome.Inconclusive"/>, а не <see cref="CheckOutcome.Pass"/>.
    /// Бит <c>3D_MAG</c> для этого не годится вовсе: он отражает только
    /// «данные идут» (<c>compass.healthy()</c>), и некалиброванный борт
    /// показывает его исправным.
    /// </remarks>
    public static CheckResult CheckPrearmCompassClean(PrearmReport? report)
    {
        const string title = "Предполётные проверки: жалобы на компас";

        if (report is null)
        {
            return new CheckResult(
                CheckIds.PrearmCompass,
                title,
                CheckOutcome.Inconclusive,
                "Отчёт предполётных проверок отсутствует — судить не о чем.");
        }

        var complaints = ClassifyCompassComplaints(report.Messages);

        if (report.CommandResult is null)
        {
            return new CheckResult(
                CheckIds.PrearmCompass,
                title,
                CheckOutcome.Inconclusive,
                "Борт не подтвердил команду MAV_CMD_RUN_PREARM_CHECKS (401): ответа нет, прошивка её, вероятно, не знает (нужна 4.1+). "
                + DescribeComplaints(complaints, "Собранные сообщения о компасе: ", "Сообщений о компасе за окно не было, но это не доказательство исправности."),
                complaints.Count,
                0);
        }

        if (report.CommandResult != MavResult.Accepted)
        {
            return new CheckResult(
                CheckIds.PrearmCompass,
                title,
                CheckOutcome.Inconclusive,
                $"Команда 401 вернула {report.CommandResult}, а не Accepted: проверки не выполнялись (борт мог быть заармлен). "
                + DescribeComplaints(complaints, "Собранные сообщения о компасе: ", "Сообщений о компасе за окно не было, но это не доказательство исправности."),
                complaints.Count,
                0);
        }

        if (report.Health is null)
        {
            return new CheckResult(
                CheckIds.PrearmCompass,
                title,
                CheckOutcome.Inconclusive,
                "За окно наблюдения не пришло ни одного SYS_STATUS: состояние бита PREARM_CHECK неизвестно. "
                + DescribeComplaints(complaints, "Собранные сообщения о компасе: ", "Сообщений о компасе не было, но подтвердить это состоянием борта нечем."),
                complaints.Count,
                0);
        }

        var health = report.Health.Value;
        if (!health.IsReported(SysStatusSensor.PrearmCheck))
        {
            return new CheckResult(
                CheckIds.PrearmCompass,
                title,
                CheckOutcome.Inconclusive,
                "Бит PREARM_CHECK (0x10000000) не заявлен как present+enabled в SYS_STATUS — прошивка о результате предполётных проверок не сообщает. "
                + DescribeComplaints(complaints, "Собранные сообщения о компасе: ", "Сообщений о компасе не было, но подтвердить это состоянием борта нечем."),
                complaints.Count,
                0);
        }

        if (complaints.Count > 0)
        {
            return new CheckResult(
                CheckIds.PrearmCompass,
                title,
                CheckOutcome.Fail,
                $"Претензий к компасу: {complaints.Count} (допустимо 0). "
                + DescribeComplaints(complaints, string.Empty, string.Empty),
                complaints.Count,
                0);
        }

        var prearmOk = health.IsOk(SysStatusSensor.PrearmCheck);
        var detail = prearmOk
            ? "Претензий к компасу нет (допустимо 0), команда 401 принята, бит PREARM_CHECK выставлен — предполётные проверки проходят целиком."
            : "Претензий к компасу нет (допустимо 0), но бит PREARM_CHECK снят: предполётные проверки не проходят по причинам, не связанным с компасом — разберитесь с ними до сдачи борта.";

        return new CheckResult(CheckIds.PrearmCompass, title, CheckOutcome.Pass, detail, 0, 0);
    }

    private static string DescribeComplaints(
        IReadOnlyList<CompassComplaint> complaints,
        string prefixWhenAny,
        string textWhenNone)
    {
        if (complaints.Count == 0)
        {
            return textWhenNone;
        }

        var sb = new StringBuilder(prefixWhenAny);
        for (var i = 0; i < complaints.Count; i++)
        {
            if (i > 0)
            {
                sb.Append("; ");
            }

            var c = complaints[i];
            sb.Append('«').Append(c.Text).Append("» [").Append(DescribeKind(c.Kind)).Append("] — ").Append(c.Advice);
        }

        return sb.ToString();
    }

    /// <summary>Человекочитаемое название класса претензии.</summary>
    public static string DescribeKind(CompassComplaintKind kind) => kind switch
    {
        CompassComplaintKind.Reboot => "снимается перезагрузкой",
        CompassComplaintKind.Calibration => "снимается калибровкой",
        CompassComplaintKind.Environment => "причина во внешней обстановке",
        CompassComplaintKind.Hardware => "неисправность или отсутствие датчика",
        CompassComplaintKind.Running => "калибровка выполняется",
        _ => "класс не определён",
    };

    /// <summary>
    /// Проверка 2: модуль смещений каждого занятого слота не превышает
    /// <see cref="OffsMaxParamName"/>.
    /// </summary>
    /// <param name="slots">Слоты компасов, как они прочитаны из параметров борта.</param>
    /// <param name="offsMax">Значение <c>COMPASS_OFFS_MAX</c>, мГс (в прошивке по умолчанию 1800).</param>
    /// <returns>По одному результату на каждый занятый слот.</returns>
    /// <remarks>
    /// Нулевые смещения по всем трём осям — не «идеальный» результат, а подпись
    /// некалиброванного компаса: <c>Compass::configured()</c> считает такой слот
    /// ненастроенным, и борт ответит <c>PreArm: Compass not calibrated</c>.
    /// Поэтому нулевой вектор даёт <see cref="CheckOutcome.Inconclusive"/>, а не
    /// «прошло с запасом».
    /// </remarks>
    public static IReadOnlyList<CheckResult> CheckOffsetMagnitudes(
        IReadOnlyList<CompassSlot>? slots,
        double offsMax)
    {
        var present = slots?.Where(static s => s is { IsPresent: true }).ToList() ?? new List<CompassSlot>();

        if (present.Count == 0)
        {
            return
            [
                new CheckResult(
                    CheckIds.CompassOffsets,
                    "Смещения компасов",
                    CheckOutcome.Inconclusive,
                    "Ни одного занятого слота компаса (все COMPASS_DEV_IDx нулевые) — измерять нечего."),
            ];
        }

        if (double.IsNaN(offsMax) || double.IsInfinity(offsMax) || offsMax <= 0)
        {
            return present
                .Select(slot => new CheckResult(
                    $"{CheckIds.CompassOffsets}.slot{slot.Slot.ToString(CultureInfo.InvariantCulture)}",
                    $"Смещения компаса, слот {slot.Slot.ToString(CultureInfo.InvariantCulture)}",
                    CheckOutcome.Inconclusive,
                    $"Порог {OffsMaxParamName} не прочитан или некорректен ({Fmt(offsMax, 1)}) — сравнивать не с чем. Измеренный модуль смещений: {Fmt(slot.OffsetMagnitude, 1)} мГс.",
                    slot.OffsetMagnitude))
                .ToList();
        }

        // Прошивка ограничивает параметр диапазоном 500…3000; выход за него —
        // повод усомниться в эталонном файле, но не повод не мерить.
        var rangeNote = offsMax is < 500 or > 3000
            ? $" Внимание: {OffsMaxParamName} = {Fmt(offsMax, 0)} вне штатного диапазона 500…3000 мГс."
            : string.Empty;

        var results = new List<CheckResult>(present.Count);
        foreach (var slot in present)
        {
            var id = $"{CheckIds.CompassOffsets}.slot{slot.Slot.ToString(CultureInfo.InvariantCulture)}";
            var title = $"Смещения компаса, слот {slot.Slot.ToString(CultureInfo.InvariantCulture)}";
            var magnitude = slot.OffsetMagnitude;
            var axes = $"COMPASS_OFS X={Fmt(slot.Offsets.X, 1)}, Y={Fmt(slot.Offsets.Y, 1)}, Z={Fmt(slot.Offsets.Z, 1)} мГс";

            var isZero = slot.Offsets.X == 0 && slot.Offsets.Y == 0 && slot.Offsets.Z == 0;
            if (isZero)
            {
                results.Add(new CheckResult(
                    id,
                    title,
                    CheckOutcome.Inconclusive,
                    $"Смещения нулевые по всем осям ({axes}). Формально 0.0 мГс < {Fmt(offsMax, 0)} мГс, но нулевой вектор — признак того, что компас не калиброван: прошивка считает такой слот ненастроенным и выдаст PreArm: Compass not calibrated.{rangeNote}",
                    magnitude,
                    offsMax));
                continue;
            }

            var outcome = magnitude > offsMax ? CheckOutcome.Fail : CheckOutcome.Pass;
            var verdict = outcome == CheckOutcome.Fail ? "превышает" : "в пределах";
            results.Add(new CheckResult(
                id,
                title,
                outcome,
                $"Модуль смещений {Fmt(magnitude, 1)} мГс {verdict} допуск {OffsMaxParamName} = {Fmt(offsMax, 0)} мГс. {axes}.{rangeNote}",
                magnitude,
                offsMax));
        }

        return results;
    }

    /// <summary>
    /// Проверка 3: курс борта совпадает с азимутом стапеля в пределах допуска.
    /// </summary>
    /// <param name="snapshot">Срез телеметрии за окно наблюдения.</param>
    /// <param name="jigAzimuthDeg">Азимут стапеля, градусы, ИСТИННЫЙ.</param>
    /// <param name="toleranceDeg">Допуск, градусы (по технологической карте ±3°).</param>
    /// <param name="minAttitudeSamples">Минимум отсчётов <c>ATTITUDE</c>, при котором курсу можно верить.</param>
    /// <remarks>
    /// Склонение здесь НЕ вводится. Рыскание из <c>ATTITUDE</c> уже отсчитано от
    /// ИСТИННОГО севера: прошивка возвращает <c>wrap_PI(heading + _declination)</c>,
    /// а склонение берёт из встроенной модели WMM при <c>COMPASS_AUTODEC = 1</c>.
    /// Азимут стапеля по контракту тоже истинный. Значит величины сравнимы
    /// напрямую, и любая «поправка на склонение» внесла бы ошибку дважды.
    /// </remarks>
    public static CheckResult CheckHeadingAgainstJig(
        TelemetrySnapshot? snapshot,
        double jigAzimuthDeg,
        double toleranceDeg = 3.0,
        int minAttitudeSamples = DefaultMinAttitudeSamples)
    {
        const string title = "Курс борта против азимута стапеля";

        if (snapshot is null)
        {
            return new CheckResult(
                CheckIds.HeadingVsJig,
                title,
                CheckOutcome.Inconclusive,
                "Срез телеметрии отсутствует — курс не измерен.",
                null,
                toleranceDeg);
        }

        if (double.IsNaN(jigAzimuthDeg) || double.IsInfinity(jigAzimuthDeg))
        {
            return new CheckResult(
                CheckIds.HeadingVsJig,
                title,
                CheckOutcome.Inconclusive,
                $"Азимут стапеля задан некорректно ({jigAzimuthDeg}) — сравнивать не с чем.",
                null,
                toleranceDeg);
        }

        if (snapshot.AttitudeSampleCount < minAttitudeSamples)
        {
            return new CheckResult(
                CheckIds.HeadingVsJig,
                title,
                CheckOutcome.Inconclusive,
                $"В окне наблюдения только {snapshot.AttitudeSampleCount.ToString(CultureInfo.InvariantCulture)} отсчётов ATTITUDE при необходимых {minAttitudeSamples.ToString(CultureInfo.InvariantCulture)} — курсу верить нельзя. Проверьте поток сообщения 30 (на Copter группы SRn_* по умолчанию молчат).",
                null,
                toleranceDeg);
        }

        var yawRad = snapshot.Attitude.YawRad;
        if (float.IsNaN(yawRad) || float.IsInfinity(yawRad))
        {
            return new CheckResult(
                CheckIds.HeadingVsJig,
                title,
                CheckOutcome.Inconclusive,
                "Рыскание в срезе телеметрии не является числом — оценщик ориентации не сошёлся.",
                null,
                toleranceDeg);
        }

        var yawDeg = Wrap360(RadToDeg(yawRad));
        var jigDeg = Wrap360(jigAzimuthDeg);
        var errorDeg = Wrap180(yawDeg - jigDeg);
        var absError = Math.Abs(errorDeg);

        var outcome = absError > toleranceDeg ? CheckOutcome.Fail : CheckOutcome.Pass;
        var sign = errorDeg >= 0 ? "+" : "-";
        var detail =
            $"Курс борта {Fmt(yawDeg, 2)}° (истинный, из ATTITUDE.yaw), азимут стапеля {Fmt(jigDeg, 2)}° (истинный). "
            + $"Ошибка {sign}{Fmt(absError, 2)}° при допуске ±{Fmt(toleranceDeg, 2)}°. "
            + $"Усреднено по {snapshot.AttitudeSampleCount.ToString(CultureInfo.InvariantCulture)} отсчётам. "
            + "Поправка на магнитное склонение не вводится: обе величины истинные.";

        return new CheckResult(CheckIds.HeadingVsJig, title, outcome, detail, errorDeg, toleranceDeg);
    }

    /// <summary>
    /// Магнитный курс одного компаса с компенсацией наклона, градусы 0…360.
    /// </summary>
    /// <param name="sample">Поле компаса, мГс, в осях борта.</param>
    /// <param name="rollRad">Крен, радианы.</param>
    /// <param name="pitchRad">Тангаж, радианы.</param>
    /// <remarks>
    /// Формулы те же, что в <c>AP_AHRS::calculate_heading</c>, записанные через
    /// углы вместо строки матрицы направляющих косинусов
    /// (<c>c = (-sin θ, sin φ·cos θ, cos φ·cos θ)</c>):
    /// <code>
    /// Hy = my·(cos φ·cos θ) − mz·(sin φ·cos θ)
    /// Hx = mx·cos²θ + sin θ·(my·sin φ·cos θ + mz·cos φ·cos θ)
    /// heading = atan2(−Hy, Hx)
    /// </code>
    /// где φ — крен, θ — тангаж. Классическая запись
    /// <c>Xh = mx·cos θ + my·sin φ·sin θ + mz·cos φ·sin θ</c>,
    /// <c>Yh = my·cos φ − mz·sin φ</c> даёт тот же угол — она отличается
    /// множителем cos θ, который в <c>atan2</c> сокращается.
    /// Результат МАГНИТНЫЙ: склонение здесь не добавляется, потому что оно
    /// неизвестно (при <c>COMPASS_AUTODEC = 1</c> прошивка не публикует его
    /// параметром). Для сравнения компасов между собой этого достаточно.
    /// </remarks>
    public static double MagneticHeadingDeg(MagSample sample, double rollRad, double pitchRad)
    {
        double mx = sample.X;
        double my = sample.Y;
        double mz = sample.Z;

        var sinRoll = Math.Sin(rollRad);
        var cosRoll = Math.Cos(rollRad);
        var sinPitch = Math.Sin(pitchRad);
        var cosPitch = Math.Cos(pitchRad);

        // Нижняя строка матрицы поворота «борт → NED»: проекция оси Z земли на оси борта.
        var cx = -sinPitch;
        var cy = sinRoll * cosPitch;
        var cz = cosRoll * cosPitch;
        var cosPitchSq = 1.0 - cx * cx;

        var headY = my * cz - mz * cy;
        var headX = mx * cosPitchSq - cx * (my * cy + mz * cz);

        var headingRad = Math.Atan2(-headY, headX);
        return Wrap360(RadToDeg(headingRad));
    }

    /// <summary>
    /// Проверка 4: расхождение курсов между компасами меньше допуска.
    /// </summary>
    /// <param name="snapshot">Срез телеметрии: поля компасов и наклон борта.</param>
    /// <param name="toleranceDeg">Допуск, градусы (по технологической карте &lt; 5°).</param>
    /// <param name="minAttitudeSamples">Минимум отсчётов <c>ATTITUDE</c> — наклон нужен для компенсации.</param>
    /// <remarks>
    /// Абсолютный курс по магнитометру потребовал бы местного склонения, а его
    /// прошивка при включённом <c>COMPASS_AUTODEC</c> параметром не отдаёт. Но в
    /// РАЗНОСТИ курсов двух компасов склонение сокращается: оно одинаково для
    /// обоих и входит в оба курса аддитивно. Поэтому курсы считаются в общей
    /// магнитной системе отсчёта и сравниваются попарно — именно это и делает
    /// проверку выполнимой без модели поля.
    /// Экземпляры с нулевым полем пропускаются: на борту с тремя ИНС и двумя
    /// компасами <c>SCALED_IMU3</c> всё равно приходит, но с обнулённым
    /// магнитным полем, и «курс» такого фантома дал бы бессмысленный разброс.
    /// </remarks>
    public static CheckResult CheckInterCompassSpread(
        TelemetrySnapshot? snapshot,
        double toleranceDeg = 5.0,
        int minAttitudeSamples = DefaultMinAttitudeSamples)
    {
        const string title = "Расхождение курсов между компасами";

        if (snapshot is null)
        {
            return new CheckResult(
                CheckIds.InterCompassSpread,
                title,
                CheckOutcome.Inconclusive,
                "Срез телеметрии отсутствует — поля компасов не измерены.",
                null,
                toleranceDeg);
        }

        if (snapshot.AttitudeSampleCount < minAttitudeSamples)
        {
            return new CheckResult(
                CheckIds.InterCompassSpread,
                title,
                CheckOutcome.Inconclusive,
                $"В окне наблюдения только {snapshot.AttitudeSampleCount.ToString(CultureInfo.InvariantCulture)} отсчётов ATTITUDE при необходимых {minAttitudeSamples.ToString(CultureInfo.InvariantCulture)}: крен и тангаж неизвестны, компенсировать наклон нечем.",
                null,
                toleranceDeg);
        }

        var usable = UsableMags(snapshot);
        if (usable.Count < 2)
        {
            var seen = snapshot.Mags?.Count ?? 0;
            return new CheckResult(
                CheckIds.InterCompassSpread,
                title,
                CheckOutcome.Inconclusive,
                $"Годных компасов {usable.Count.ToString(CultureInfo.InvariantCulture)} из {seen.ToString(CultureInfo.InvariantCulture)} принятых сообщений (экземпляры с нулевым полем не в счёт) — сравнивать не с чем, нужно не меньше двух.",
                null,
                toleranceDeg);
        }

        var roll = snapshot.Attitude.RollRad;
        var pitch = snapshot.Attitude.PitchRad;
        if (float.IsNaN(roll) || float.IsInfinity(roll) || float.IsNaN(pitch) || float.IsInfinity(pitch))
        {
            return new CheckResult(
                CheckIds.InterCompassSpread,
                title,
                CheckOutcome.Inconclusive,
                "Крен или тангаж в срезе телеметрии не являются числом — компенсация наклона невозможна.",
                null,
                toleranceDeg);
        }

        var headings = usable
            .Select(m => (m.Instance, Heading: MagneticHeadingDeg(m, roll, pitch), m.Magnitude))
            .ToList();

        if (headings.Any(static h => double.IsNaN(h.Heading)))
        {
            return new CheckResult(
                CheckIds.InterCompassSpread,
                title,
                CheckOutcome.Inconclusive,
                "Курс хотя бы одного компаса не вычислим (вырожденный вектор поля) — измерение непригодно.",
                null,
                toleranceDeg);
        }

        var maxSpread = 0.0;
        var worstA = headings[0].Instance;
        var worstB = headings[1].Instance;
        for (var i = 0; i < headings.Count; i++)
        {
            for (var j = i + 1; j < headings.Count; j++)
            {
                var diff = Math.Abs(Wrap180(headings[i].Heading - headings[j].Heading));
                if (diff > maxSpread)
                {
                    maxSpread = diff;
                    worstA = headings[i].Instance;
                    worstB = headings[j].Instance;
                }
            }
        }

        var list = string.Join(
            ", ",
            headings.Select(h =>
                $"экз. {h.Instance.ToString(CultureInfo.InvariantCulture)} (слот {(h.Instance + 1).ToString(CultureInfo.InvariantCulture)}) — {Fmt(h.Heading, 2)}°, |B| {Fmt(h.Magnitude, 0)} мГс"));

        var outcome = maxSpread >= toleranceDeg ? CheckOutcome.Fail : CheckOutcome.Pass;
        var detail =
            $"Максимальное расхождение {Fmt(maxSpread, 2)}° между экземплярами {worstA.ToString(CultureInfo.InvariantCulture)} и {worstB.ToString(CultureInfo.InvariantCulture)} при допуске < {Fmt(toleranceDeg, 2)}°. "
            + $"Курсы магнитные, с компенсацией наклона (крен {Fmt(RadToDeg(roll), 2)}°, тангаж {Fmt(RadToDeg(pitch), 2)}°): {list}. "
            + "Склонение не вводится — в разности курсов оно сокращается.";

        return new CheckResult(CheckIds.InterCompassSpread, title, outcome, detail, maxSpread, toleranceDeg);
    }

    /// <summary>
    /// Дополнение к проверке 4: модуль поля каждого компаса против ожидаемой полосы.
    /// </summary>
    /// <param name="snapshot">Срез телеметрии.</param>
    /// <returns>По одному результату на каждый компас с ненулевым полем.</returns>
    /// <remarks>
    /// Компас может уложиться в допуск по расхождению курсов и при этом
    /// показывать физически невозможный модуль поля — такое расхождение
    /// «одинаково врут оба» надо показать оператору. Полоса та же, по которой
    /// прошивка выдаёт <c>PreArm: Check mag field</c>: норма 530 мГс,
    /// минимум 185, максимум 875.
    /// </remarks>
    public static IReadOnlyList<CheckResult> DescribeFieldMagnitudes(TelemetrySnapshot? snapshot)
    {
        var usable = snapshot is null ? new List<MagSample>() : UsableMags(snapshot);
        if (usable.Count == 0)
        {
            return
            [
                new CheckResult(
                    CheckIds.MagFieldMagnitude,
                    "Модуль магнитного поля",
                    CheckOutcome.Inconclusive,
                    $"Ни одного компаса с ненулевым полем в срезе телеметрии — измерять нечего (ожидаемая полоса {Fmt(MagFieldMinMilligauss, 0)}…{Fmt(MagFieldMaxMilligauss, 0)} мГс).",
                    null,
                    MagFieldExpectedMilligauss),
            ];
        }

        var results = new List<CheckResult>(usable.Count);
        foreach (var mag in usable)
        {
            var magnitude = mag.Magnitude;
            var inBand = magnitude >= MagFieldMinMilligauss && magnitude <= MagFieldMaxMilligauss;
            var verdict = inBand ? "в пределах" : "вне";
            results.Add(new CheckResult(
                $"{CheckIds.MagFieldMagnitude}.inst{mag.Instance.ToString(CultureInfo.InvariantCulture)}",
                $"Модуль магнитного поля, экз. {mag.Instance.ToString(CultureInfo.InvariantCulture)} (слот {(mag.Instance + 1).ToString(CultureInfo.InvariantCulture)})",
                inBand ? CheckOutcome.Pass : CheckOutcome.Fail,
                $"|B| = {Fmt(magnitude, 0)} мГс {verdict} полосы {Fmt(MagFieldMinMilligauss, 0)}…{Fmt(MagFieldMaxMilligauss, 0)} мГс (норма {Fmt(MagFieldExpectedMilligauss, 0)} мГс). "
                + $"Компоненты X={mag.X.ToString(CultureInfo.InvariantCulture)}, Y={mag.Y.ToString(CultureInfo.InvariantCulture)}, Z={mag.Z.ToString(CultureInfo.InvariantCulture)} мГс."
                + (inBand ? string.Empty : " Борт на такое поле отвечает PreArm: Check mag field — уберите железо со стапеля или замените датчик."),
                magnitude,
                MagFieldExpectedMilligauss));
        }

        return results;
    }

    /// <summary>
    /// Компасы, по которым вообще можно судить: непустое поле, по одному на экземпляр,
    /// в порядке возрастания номера экземпляра.
    /// </summary>
    private static List<MagSample> UsableMags(TelemetrySnapshot snapshot)
    {
        if (snapshot.Mags is null)
        {
            return new List<MagSample>();
        }

        return snapshot.Mags
            .Where(static m => !m.IsEmpty)
            .GroupBy(static m => m.Instance)
            .OrderBy(static g => g.Key)
            .Select(static g => g.First())
            .ToList();
    }

    /// <summary>Прогон всех приёмочных критериев одним вызовом.</summary>
    /// <param name="prearm">Отчёт предполётных проверок; <c>null</c> даёт <see cref="CheckOutcome.Inconclusive"/>.</param>
    /// <param name="slots">Слоты компасов после калибровки и перезагрузки.</param>
    /// <param name="compassOffsMax">Значение <c>COMPASS_OFFS_MAX</c>, прочитанное с борта.</param>
    /// <param name="telemetry">Срез телеметрии за окно наблюдения.</param>
    /// <param name="jigAzimuthDeg">Азимут стапеля, градусы, истинный.</param>
    /// <param name="tolerances">Допуски процедуры; <c>null</c> — значения по умолчанию.</param>
    /// <returns>Результаты в порядке технологической карты.</returns>
    public static IReadOnlyList<CheckResult> RunAll(
        PrearmReport? prearm,
        IReadOnlyList<CompassSlot>? slots,
        double compassOffsMax,
        TelemetrySnapshot? telemetry,
        double jigAzimuthDeg,
        CalibrationTolerances? tolerances = null)
    {
        var t = tolerances ?? new CalibrationTolerances();

        var results = new List<CheckResult> { CheckPrearmCompassClean(prearm) };
        results.AddRange(CheckOffsetMagnitudes(slots, compassOffsMax));
        results.Add(CheckHeadingAgainstJig(telemetry, jigAzimuthDeg, t.HeadingVsJigDeg));
        results.Add(CheckInterCompassSpread(telemetry, t.InterCompassSpreadDeg));
        results.AddRange(DescribeFieldMagnitudes(telemetry));
        return results;
    }

    /// <summary>
    /// Сводит набор результатов в один исход.
    /// </summary>
    /// <remarks>
    /// <see cref="CheckOutcome.Inconclusive"/> НЕ считается прохождением: пустой
    /// набор и любая неопределённость дают неопределённость, любой отказ —
    /// отказ. Иначе «не смогли измерить» тихо превратилось бы в «годен».
    /// </remarks>
    public static CheckOutcome Fold(IEnumerable<CheckResult>? results)
    {
        if (results is null)
        {
            return CheckOutcome.Inconclusive;
        }

        var any = false;
        var inconclusive = false;
        foreach (var r in results)
        {
            if (r is null)
            {
                inconclusive = true;
                continue;
            }

            any = true;
            switch (r.Outcome)
            {
                case CheckOutcome.Fail:
                    return CheckOutcome.Fail;
                case CheckOutcome.Inconclusive:
                    inconclusive = true;
                    break;
            }
        }

        if (!any || inconclusive)
        {
            return CheckOutcome.Inconclusive;
        }

        return CheckOutcome.Pass;
    }

    /// <summary>Приёмка пройдена только при <see cref="CheckOutcome.Pass"/> по всем проверкам.</summary>
    public static bool IsAccepted(IEnumerable<CheckResult>? results) => Fold(results) == CheckOutcome.Pass;

    /// <summary>Причина, по которой свод не «годен»: перечень непрошедших проверок.</summary>
    /// <returns><c>null</c>, если всё прошло.</returns>
    public static string? DescribeFailure(IEnumerable<CheckResult>? results)
    {
        if (results is null)
        {
            return "Результаты проверок отсутствуют.";
        }

        var bad = results
            .Where(static r => r is not null && r.Outcome != CheckOutcome.Pass)
            .ToList();

        if (bad.Count == 0)
        {
            return null;
        }

        return string.Join(
            " | ",
            bad.Select(static r => $"{r.Title}: {(r.Outcome == CheckOutcome.Fail ? "не годен" : "не определено")} — {r.Detail}"));
    }

    /// <summary>Радианы в градусы.</summary>
    public static double RadToDeg(double rad) => rad * (180.0 / Math.PI);

    /// <summary>Приводит угол к диапазону [0; 360).</summary>
    public static double Wrap360(double deg)
    {
        if (double.IsNaN(deg) || double.IsInfinity(deg))
        {
            return deg;
        }

        var v = deg % 360.0;
        if (v < 0)
        {
            v += 360.0;
        }

        return v;
    }

    /// <summary>Приводит угол к диапазону (−180; 180] — так разность курсов не «перескакивает» через север.</summary>
    public static double Wrap180(double deg)
    {
        if (double.IsNaN(deg) || double.IsInfinity(deg))
        {
            return deg;
        }

        var v = Wrap360(deg);
        if (v > 180.0)
        {
            v -= 360.0;
        }

        return v;
    }
}
