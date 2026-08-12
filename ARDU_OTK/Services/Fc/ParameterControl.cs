using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ARDU_OTK.Services.Fc;

/// <summary>
/// Роль параметра в эталоне: контролируем ли мы его и показываем ли оператору.
/// </summary>
/// <remarks>
/// 🔴 Единственная классификация параметров в приложении. До её появления роль
/// была размазана по четырём независимым местам — списку запрещённых к записи
/// имён, спискам переносимых имён, флагу «решающее подполе» в сверке топологии
/// и жёстко зашитым условиям внутри стадий процедуры. Пока роль не была
/// объявлена как понятие, параметр, который физически не может совпасть с
/// эталоном, либо случайно не проверялся (и оператор об этом не знал), либо
/// проверялся и заваливал исправное изделие.
/// </remarks>
public enum ParameterControl
{
    /// <summary>Сверяем с эталоном и показываем оператору в карточке эталона.</summary>
    ControlledVisible,

    /// <summary>Сверяем с эталоном, но в карточке не показываем: значимо для процедуры, не значимо для глаз.</summary>
    ControlledHidden,

    /// <summary>
    /// Не сверяем и не переносим. Либо параметр называет конкретный экземпляр
    /// железа, либо прошивка переписывает его сама — совпасть с эталоном он не
    /// может ни на одной исправной плате.
    /// </summary>
    Uncontrolled,
}

/// <summary>
/// Правило умолчания: маска имени, роль и причина, которую увидит оператор.
/// </summary>
/// <param name="Pattern">Маска имени. <c>*</c> — любая последовательность, <c>?</c> — один знак.</param>
/// <param name="Control">Роль, которую правило назначает.</param>
/// <param name="Reason">Почему именно эта роль. Показывается в мастере эталона строкой под именем.</param>
/// <param name="CannotMatch">
/// Параметр не может совпасть с эталоном по устройству прошивки, а не по
/// решению технолога. Отличать обязательно: перевести такое имя под контроль
/// оператор вправе, но предупредить его нужно — контроль по нему завалит
/// каждый прогон, а не только неисправный борт.
/// </param>
public sealed record ParameterRoleRule(
    string Pattern,
    ParameterControl Control,
    string Reason,
    bool CannotMatch = false)
{
    private readonly Regex _regex = GlobToRegex(Pattern);

    /// <summary>Подходит ли имя под маску. Регистр не учитывается: имена приводятся к верхнему.</summary>
    public bool Matches(string name) => _regex.IsMatch(name);

    /// <summary>
    /// Маска в регулярное выражение.
    /// </summary>
    /// <remarks>
    /// <c>Regex.Escape</c> обязателен: без него <c>COMPASS_PRIO?_ID</c> — это
    /// «необязательный знак O», а не «любой знак», и правило молча захватывает
    /// не те имена.
    /// </remarks>
    private static Regex GlobToRegex(string glob) => new(
        "^" + Regex.Escape(glob)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
}

/// <summary>
/// Отступление технолога от умолчания: имя, новая роль и обоснование.
/// </summary>
/// <remarks>
/// Отступление хранится в эталоне и попадает в протокол вместе с подписью и
/// временем. Обоснование необязательно: раскладка задаётся ползунком в строке,
/// а требование писать текст на каждое из сотен переключений привело бы к
/// отпискам вида «надо» — они не объясняют ничего и лишь создают видимость
/// разбора. Что действительно требует подтверждения — возврат под контроль
/// имени, которое совпасть не может; за это отвечает
/// <see cref="ParameterRole.IsHazardousOverride"/>.
/// </remarks>
/// <param name="Name">Точное имя параметра, без масок.</param>
/// <param name="Control">Роль, назначенная оператором.</param>
/// <param name="Justification">Пояснение технолога; может быть пустым.</param>
/// <param name="By">Кто отступил.</param>
/// <param name="AtUtc">Когда.</param>
public sealed record ParameterRoleOverride(
    string Name,
    ParameterControl Control,
    string Justification,
    string By,
    DateTimeOffset AtUtc);

/// <summary>Разрешённая роль одного имени: что назначено, чем назначено и почему.</summary>
/// <param name="Name">Имя параметра в верхнем регистре.</param>
/// <param name="Control">Действующая роль.</param>
/// <param name="DefaultControl">Роль по умолчанию — что назначила бы технологическая карта.</param>
/// <param name="Reason">Причина умолчания.</param>
/// <param name="CannotMatch">Параметр не может совпасть с эталоном по устройству прошивки.</param>
/// <param name="Override">Отступление оператора; <c>null</c> — действует умолчание.</param>
public sealed record ParameterRole(
    string Name,
    ParameterControl Control,
    ParameterControl DefaultControl,
    string Reason,
    bool CannotMatch,
    ParameterRoleOverride? Override)
{
    /// <summary>Действует отступление, а не умолчание.</summary>
    public bool IsOverridden => Override is not null;

    /// <summary>Сверяем ли параметр с эталоном и переносим ли его на борт.</summary>
    public bool IsControlled => Control != ParameterControl.Uncontrolled;

    /// <summary>
    /// Отступление вернуло под контроль имя, которое совпасть не может.
    /// </summary>
    /// <remarks>
    /// Не запрещено, но обязано быть видно: такой контроль остановит каждый
    /// прогон, включая прогон исправного изделия.
    /// </remarks>
    public bool IsHazardousOverride => IsOverridden && CannotMatch && IsControlled;
}

/// <summary>
/// Роли параметров эталона: умолчания технологической карты плюс отступления
/// технолога.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 Единственный источник истины о том, какие параметры сверяются с эталоном.
/// И мастер эталона, и стадии процедуры спрашивают роль здесь; второго списка
/// «что проверять» в приложении быть не должно — он неминуемо разойдётся с
/// первым, и разойдётся молча.
/// </para>
/// <para>
/// Роль управляет <b>сверкой и переносом</b>, но не отменяет запрет записи:
/// <see cref="CompassIdentity.NeverWriteNames"/> — отдельная и не перекрываемая
/// оператором защита. Перевести <c>COMPASS_DEV_ID</c> под контроль можно (борт
/// начнёт сверяться по нему и завалит прогон), записать его на борт — нельзя
/// никогда.
/// </para>
/// </remarks>
public sealed class ParameterRoleMap
{
    /// <summary>
    /// Умолчания в порядке разбора: побеждает первое подошедшее правило.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Порядок значим и обратен привычному: узкие правила стоят выше широких,
    /// иначе <c>COMPASS_*</c> перехватило бы всё компасное ещё до того, как до
    /// <c>COMPASS_DEV_ID*</c> дошла бы очередь.
    /// </para>
    /// <para>
    /// Первый блок — то, что не может совпасть с эталоном никогда. Он делится
    /// надвое: идентичность железа (параметр называет конкретный датчик
    /// конкретной платы) и величины, которые прошивка переписывает сама
    /// (счётчики, наземное давление, температура калибровки). Всё остальное —
    /// вся прошивка целиком, а не только компас — контролируется.
    /// </para>
    /// <para>
    /// 🔴 По умолчанию под контролем, но <b>без показа</b> оператору не только
    /// прочие параметры, но и компасный блок. Эталон — это конфигурация изделия
    /// целиком, в ней тысяча с лишним имён, и любой заранее отобранный список
    /// «важного» — это чужое мнение о том, за чем следить на конкретной модели.
    /// Что поднять в видимую секцию, решает технолог ползунком в строке, и его
    /// решение записывается в эталон.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ParameterRoleRule> DefaultRules { get; } =
    [
        // --- Идентичность железа: называет датчик именно той платы ----------
        new ParameterRoleRule(
            "COMPASS_DEV_ID*",
            ParameterControl.Uncontrolled,
            "@ReadOnly, определяется прошивкой при загрузке. Внутри — номер шины, "
          + "который на физически одинаковых платах законно разный. Решающие подполя "
          + "(тип шины, адрес, devtype) сверяются отдельно, на стадии состава компасов.",
            CannotMatch: true),
        new ParameterRoleRule(
            "COMPASS_PRIO?_ID",
            ParameterControl.Uncontrolled,
            "Идентификаторы датчиков платы-образца. Перенесённые на другой борт, они "
          + "называют несуществующее железо: PreArm: Compass N not found. Приоритеты "
          + "строятся из собственных COMPASS_DEV_IDx целевой платы.",
            CannotMatch: true),
        new ParameterRoleRule(
            "INS_ACC*_ID",
            ParameterControl.Uncontrolled,
            "Идентичность акселерометра этой платы. INS_ACC1_ID не существует — у первого экземпляра имя без цифры.",
            CannotMatch: true),
        new ParameterRoleRule(
            "INS_GYR*_ID",
            ParameterControl.Uncontrolled,
            "Идентичность гироскопа этой платы.",
            CannotMatch: true),
        new ParameterRoleRule(
            "*_DEVID",
            ParameterControl.Uncontrolled,
            "Идентификатор устройства этой платы (барометр, приёмник воздушного давления и прочее).",
            CannotMatch: true),
        new ParameterRoleRule(
            "*_DEVID?",
            ParameterControl.Uncontrolled,
            "Идентификатор устройства этой платы.",
            CannotMatch: true),

        // --- Величины, которые прошивка переписывает сама -------------------
        new ParameterRoleRule(
            "STAT_*",
            ParameterControl.Uncontrolled,
            "Счётчик наработки платы: число загрузок, время полёта, время работы. Растёт сам.",
            CannotMatch: true),
        new ParameterRoleRule(
            "*_CALTEMP",
            ParameterControl.Uncontrolled,
            "Температура на момент калибровки конкретного экземпляра.",
            CannotMatch: true),
        new ParameterRoleRule(
            "INS_GYROFFS*",
            ParameterControl.Uncontrolled,
            "Смещения гироскопа пересчитываются при каждой загрузке. Расхождение с эталоном — норма, а не дефект.",
            CannotMatch: true),

        // Список Mission Planner, применяемый им при восстановлении из файла:
        // счётчики заданий, наземные давление и температура, служебные записи.
        new ParameterRoleRule("SYSID_SW_MREV", ParameterControl.Uncontrolled, VolatileReason, CannotMatch: true),
        new ParameterRoleRule("WP_TOTAL", ParameterControl.Uncontrolled, VolatileReason, CannotMatch: true),
        new ParameterRoleRule("CMD_TOTAL", ParameterControl.Uncontrolled, VolatileReason, CannotMatch: true),
        new ParameterRoleRule("CMD_INDEX", ParameterControl.Uncontrolled, VolatileReason, CannotMatch: true),
        new ParameterRoleRule("FENCE_TOTAL", ParameterControl.Uncontrolled, VolatileReason, CannotMatch: true),
        new ParameterRoleRule("SYS_NUM_RESETS", ParameterControl.Uncontrolled, VolatileReason, CannotMatch: true),
        new ParameterRoleRule("ARSPD_OFFSET", ParameterControl.Uncontrolled, VolatileReason, CannotMatch: true),
        new ParameterRoleRule("GND_ABS_PRESS", ParameterControl.Uncontrolled, VolatileReason, CannotMatch: true),
        new ParameterRoleRule("GND_TEMP", ParameterControl.Uncontrolled, VolatileReason, CannotMatch: true),
        new ParameterRoleRule("BARO?_GND_PRESS", ParameterControl.Uncontrolled, VolatileReason, CannotMatch: true),
        new ParameterRoleRule("BARO_GND_TEMP", ParameterControl.Uncontrolled, VolatileReason, CannotMatch: true),
        new ParameterRoleRule("LOG_LASTFILE", ParameterControl.Uncontrolled, VolatileReason, CannotMatch: true),
        new ParameterRoleRule("FORMAT_VERSION", ParameterControl.Uncontrolled, VolatileReason, CannotMatch: true),

        // --- Компасный блок процедуры: то, ради чего эталон и заводится -----
        new ParameterRoleRule(
            "COMPASS_OFS*",
            ParameterControl.ControlledHidden,
            "Жёсткое железо: смещения магнитометра. Переносятся из эталона и подтверждаются обратным чтением."),
        new ParameterRoleRule(
            "COMPASS_DIA*",
            ParameterControl.ControlledHidden,
            "Мягкое железо, диагональ. Переносится из эталона; калибровка по фиксированному курсу её затем сбрасывает в (1,1,1) — "
          + "это записывается в протокол как факт."),
        new ParameterRoleRule(
            "COMPASS_ODI*",
            ParameterControl.ControlledHidden,
            "Мягкое железо, внедиагональ. Переносится из эталона; калибровка по фиксированному курсу её затем обнуляет."),
        new ParameterRoleRule(
            "COMPASS_SCALE*",
            ParameterControl.ControlledHidden,
            "Масштабный коэффициент. Калибровка по фиксированному курсу его не трогает."),
        new ParameterRoleRule(
            "COMPASS_MOT*",
            ParameterControl.ControlledHidden,
            "Результат CompassMot: зависит от силовой проводки изделия. Переносится только при включённом переключателе."),
        new ParameterRoleRule(
            "COMPASS_ORIENT*",
            ParameterControl.ControlledHidden,
            "Ориентация датчика. Сверяется, но не переносится: калибровка по фиксированному курсу ориентацию не исправляет, "
          + "и неверное значение даёт «успешно» откалиброванный негодный компас."),
        new ParameterRoleRule(
            "COMPASS_USE*",
            ParameterControl.ControlledHidden,
            "Применяется ли компас. Сверяется, не переносится."),
        // 🔴 «Внешний или внутренний» — свойство железа этой платы, а не
        // настройка изделия, и переносу не подлежит. Флаг привязан к номеру
        // экземпляра, а какой датчик каким экземпляром окажется, решает порядок
        // обнаружения при загрузке — он у двух плат законно разный. Перенесённый
        // с образца флаг садится на чужой датчик и объявляет внутренний компас
        // внешним; значение 2 вдобавок запрещает прошивке это исправить, и
        // ошибка остаётся на плате навсегда. Прошивка выставляет флаг сама,
        // когда находит датчик, — этому и надо верить.
        new ParameterRoleRule(
            "COMPASS_EXTERNAL",
            ParameterControl.Uncontrolled,
            "Внешний или внутренний компас: прошивка определяет это сама при обнаружении датчика. "
          + "Флаг привязан к номеру экземпляра, а порядок экземпляров у двух плат законно разный, "
          + "поэтому перенос с образца пометил бы чужой датчик.",
            CannotMatch: true),
        new ParameterRoleRule(
            "COMPASS_EXTERN?",
            ParameterControl.Uncontrolled,
            "То же для второго и третьего экземпляров. Обратите внимание на имя: EXTERN2, а не EXTERNAL2.",
            CannotMatch: true),

        // --- Что оператор держит перед глазами -------------------------------
        //
        // Единственный блок, попадающий в видимую секцию по умолчанию. Это не
        // «самые важные параметры вообще», а то, по чему на стенде опознают
        // перепутанную сборку: пять первых каналов приёмника, пять первых
        // выходов на исполнительные механизмы и таблица полётных режимов.
        // Расхождение здесь означает не сбитую настройку, а другое изделие.
        //
        // 🔴 Маски перечислены поимённо, а не как RC?_* и SERVO?_*. Знак «?» —
        // это любой знак, и такая маска взяла бы SERVO_RNG_ENABLE и подобные
        // имена, где на месте цифры стоит буква.
        .. ChannelRules("RC", RcChannelCount, ShownChannelCount, RcChannelReason),
        .. ChannelRules("SERVO", ServoOutputCount, ShownChannelCount, ServoOutputReason),

        // 🔴 Пределы отклонения — это границы, за которые автопилот изделие не
        // пустит. Расхождение с эталоном здесь не сбивает настройку, а меняет
        // поведение в воздухе, и заметить это на стенде можно только глазами:
        // борт с чужими пределами взлетает, проходит все проверки и ведёт себя
        // не так. Имена перечислены и в нынешнем, и в прежнем написании:
        // прошивки на изделиях разных лет называют одно и то же по-разному, и
        // молчание о переименованном параметре читалось бы как его отсутствие.
        new ParameterRoleRule("ROLL_LIMIT_DEG", ParameterControl.ControlledVisible, AngleLimitReason),
        new ParameterRoleRule("PTCH_LIM_MAX_DEG", ParameterControl.ControlledVisible, AngleLimitReason),
        new ParameterRoleRule("PTCH_LIM_MIN_DEG", ParameterControl.ControlledVisible, AngleLimitReason),
        new ParameterRoleRule("LIM_ROLL_CD", ParameterControl.ControlledVisible, AngleLimitReason),
        new ParameterRoleRule("LIM_PITCH_MAX", ParameterControl.ControlledVisible, AngleLimitReason),
        new ParameterRoleRule("LIM_PITCH_MIN", ParameterControl.ControlledVisible, AngleLimitReason),
        new ParameterRoleRule("ANGLE_MAX", ParameterControl.ControlledVisible, AngleLimitReason),
        new ParameterRoleRule("ATC_ANGLE_MAX", ParameterControl.ControlledVisible, AngleLimitReason),
        new ParameterRoleRule("Q_ANGLE_MAX", ParameterControl.ControlledVisible, AngleLimitReason),

        new ParameterRoleRule(
            "FLTMODE*",
            ParameterControl.ControlledVisible,
            "Таблица полётных режимов и канал их переключения."),
        new ParameterRoleRule(
            "MODE?",
            ParameterControl.ControlledVisible,
            "Полётный режим по положению переключателя (наименование Rover и Sub)."),
        new ParameterRoleRule(
            "MODE_CH",
            ParameterControl.ControlledVisible,
            "Канал переключения полётных режимов (наименование Rover и Sub)."),

        // --- Всё остальное --------------------------------------------------
        new ParameterRoleRule(
            "*",
            ParameterControl.ControlledHidden,
            "Настройка изделия: переносится с эталона и сверяется, но у стенда оператору читать её не о чем."),
    ];

    /// <summary>
    /// Сколько каналов приёмника и выходов названы поимённо. Это ровно то, что
    /// показывает Mission Planner на экранах Servo Output и Radio Calibration.
    /// </summary>
    /// <remarks>
    /// Под контролем они были и раньше — их забирало общее правило в конце
    /// списка. Названы поимённо они затем, чтобы в карточке эталона у каждого
    /// стояло осмысленное обоснование, а не «у стенда читать её не о чем»:
    /// технолог решает роль параметра по обоснованию, и отговорка вместо
    /// причины — это решение вслепую.
    /// </remarks>
    private const int ServoOutputCount = 16;

    private const int RcChannelCount = 16;

    /// <summary>
    /// Сколько каналов и выходов показывать оператору по умолчанию.
    /// </summary>
    /// <remarks>
    /// 🔴 Пять, а не все шестнадцать. Контроль и показ — разные вещи: под
    /// контролем всё, а на экране только та короткая выборка, по которой
    /// перепутанную сборку опознают с одного взгляда. Шестнадцать каналов
    /// вместе с шестнадцатью выходами дают под две сотни строк, и список
    /// перестаёт быть выборкой — в нём уже нельзя ничего заметить. Что поднять
    /// в показ сверх этого, решает технолог ползунком в карточке эталона: это
    /// его решение, а не умолчание кода.
    /// </remarks>
    private const int ShownChannelCount = 5;

    /// <summary>
    /// Строит правила вида <c>RC1_*</c>…<c>RC16_*</c> — по одному на канал.
    /// </summary>
    /// <remarks>
    /// Правила порождаются, а не выписываются: тридцать две почти одинаковые
    /// строки расходятся при первой же правке одной из них, и расхождение
    /// проявится как «на одном канале роль другая» без всякой причины.
    /// </remarks>
    private static IEnumerable<ParameterRoleRule> ChannelRules(
        string prefix,
        int count,
        int shown,
        string reason)
    {
        for (int i = 1; i <= count; i++)
        {
            yield return new ParameterRoleRule(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}{i}_*"),
                i <= shown ? ParameterControl.ControlledVisible : ParameterControl.ControlledHidden,
                reason);
        }
    }

    private const string RcChannelReason =
        "Канал приёмника. Границы, середина и назначение задают управление изделием: "
      + "расхождение с эталоном означает другую раскладку пульта, а не сбитую настройку.";

    private const string ServoOutputReason =
        "Выход на исполнительный механизм. Назначение и границы задают, что именно к нему "
      + "подключено: расхождение — это другая сборка, а не другая настройка.";

    private const string AngleLimitReason =
        "Предел отклонения, за который автопилот изделие не пустит. Это не настройка отзывчивости, "
      + "а граница поведения в воздухе: чужое значение проходит все наземные проверки и проявляется "
      + "только в полёте.";

    private const string VolatileReason =
        "Прошивка ведёт это значение сама: счётчик, наземная опора или служебная запись. "
      + "На двух платах оно совпасть не обязано и о качестве изделия ничего не говорит.";

    private static readonly ParameterRoleRule MotorCompDisabledRule = new(
        "COMPASS_MOT*",
        ParameterControl.Uncontrolled,
        "Перенос COMPASS_MOT* выключен в этом эталоне: компенсация тока двигателей зависит от силовой проводки "
      + "и переносима только между полностью одинаковыми сборками.",
        CannotMatch: false);

    private readonly IReadOnlyList<ParameterRoleRule> _rules;
    private readonly Dictionary<string, ParameterRoleOverride> _overrides;

    private ParameterRoleMap(
        bool transferMotorComp,
        IReadOnlyList<ParameterRoleRule> rules,
        Dictionary<string, ParameterRoleOverride> overrides)
    {
        TransferMotorComp = transferMotorComp;
        _rules = rules;
        _overrides = overrides;
    }

    /// <summary>Умолчания технологической карты без единого отступления, перенос COMPASS_MOT* включён.</summary>
    public static ParameterRoleMap Default { get; } = Create(transferMotorComp: true, overrides: null);

    /// <summary>Переносится ли <c>COMPASS_MOT*</c>. Выключенный перенос выводит эти имена из-под контроля.</summary>
    public bool TransferMotorComp { get; }

    /// <summary>Отступления от умолчаний, в порядке имён.</summary>
    public IReadOnlyList<ParameterRoleOverride> Overrides =>
        _overrides.Values.OrderBy(static o => o.Name, StringComparer.Ordinal).ToArray();

    /// <summary>Есть ли хоть одно отступление от технологической карты.</summary>
    public bool HasOverrides => _overrides.Count > 0;

    /// <summary>
    /// Собирает карту ролей эталона.
    /// </summary>
    /// <param name="transferMotorComp">Значение переключателя переноса <c>COMPASS_MOT*</c> в эталоне.</param>
    /// <param name="overrides">Отступления технолога; <c>null</c> или пусто — только умолчания.</param>
    public static ParameterRoleMap Create(
        bool transferMotorComp,
        IEnumerable<ParameterRoleOverride>? overrides)
    {
        // Выключенный перенос COMPASS_MOT* — это не отступление технолога, а
        // следствие уже принятого им решения. Поэтому он вставляется правилом
        // умолчания, а не отступлением: иначе один и тот же выбор был бы
        // записан в эталоне дважды, в двух разных полях.
        var rules = transferMotorComp
            ? DefaultRules
            : new[] { MotorCompDisabledRule }.Concat(DefaultRules).ToArray();

        var map = new Dictionary<string, ParameterRoleOverride>(StringComparer.Ordinal);
        if (overrides is not null)
        {
            foreach (var item in overrides)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Name))
                {
                    continue;
                }

                var name = Normalize(item.Name);
                map[name] = item with { Name = name };
            }
        }

        return new ParameterRoleMap(transferMotorComp, rules, map);
    }

    /// <summary>Роль одного имени.</summary>
    /// <exception cref="ArgumentException">Имя пусто.</exception>
    public ParameterRole Classify(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalized = Normalize(name);

        // Хвостовое правило "*" гарантирует совпадение, но полагаться на это
        // молча нельзя: правку таблицы, которая его уронит, обязано поймать
        // осмысленное исключение, а не NullReferenceException где-то в UI.
        var rule = _rules.FirstOrDefault(r => r.Matches(normalized))
            ?? throw new InvalidOperationException(
                $"Таблица ролей не покрывает имя «{normalized}»: в ней потеряно замыкающее правило «*».");

        _overrides.TryGetValue(normalized, out var over);

        return new ParameterRole(
            Name: normalized,
            Control: over?.Control ?? rule.Control,
            DefaultControl: rule.Control,
            Reason: rule.Reason,
            CannotMatch: rule.CannotMatch,
            Override: over);
    }

    /// <summary>Сверяется ли параметр с эталоном. Короткая форма для стадий процедуры.</summary>
    public bool IsControlled(string name) => Classify(name).IsControlled;

    /// <summary>Карта с добавленным или заменённым отступлением.</summary>
    public ParameterRoleMap With(ParameterRoleOverride item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Name);

        var name = Normalize(item.Name);
        var next = new Dictionary<string, ParameterRoleOverride>(_overrides, StringComparer.Ordinal)
        {
            [name] = item with { Name = name, Justification = (item.Justification ?? string.Empty).Trim() },
        };

        // Отступление, совпавшее с умолчанием, отступлением не является:
        // хранить его значит показывать оператору пометку «изменено» там, где
        // ничего не изменено.
        var rule = _rules.First(r => r.Matches(name));
        if (rule.Control == item.Control)
        {
            next.Remove(name);
        }

        return new ParameterRoleMap(TransferMotorComp, _rules, next);
    }

    /// <summary>Карта без отступления по этому имени — возврат к умолчанию.</summary>
    public ParameterRoleMap Without(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var next = new Dictionary<string, ParameterRoleOverride>(_overrides, StringComparer.Ordinal);
        next.Remove(Normalize(name));
        return new ParameterRoleMap(TransferMotorComp, _rules, next);
    }

    /// <summary>Та же карта с другим значением переключателя переноса <c>COMPASS_MOT*</c>.</summary>
    public ParameterRoleMap WithMotorCompTransfer(bool transferMotorComp) =>
        transferMotorComp == TransferMotorComp
            ? this
            : Create(transferMotorComp, _overrides.Values);

    /// <summary>
    /// Отступления в виде текста для хранения в эталоне.
    /// </summary>
    /// <remarks>
    /// Хранятся только отступления, а не полная раскладка. Полная раскладка в
    /// базе означала бы, что эталон, заведённый до правки технологической карты,
    /// навсегда остался бы с прежними умолчаниями и молча разошёлся бы с картой.
    /// Пустая строка — отступлений нет.
    /// </remarks>
    public string SerializeOverrides()
    {
        if (_overrides.Count == 0)
        {
            return string.Empty;
        }

        var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var item in Overrides)
            {
                writer.WriteStartObject();
                writer.WriteString("name", item.Name);
                writer.WriteString("control", item.Control.ToString());
                writer.WriteString("justification", item.Justification);
                writer.WriteString("by", item.By);
                writer.WriteString("atUtc", item.AtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Разбирает хранимые отступления.
    /// </summary>
    /// <remarks>
    /// 🔴 Непонятная запись отбрасывается, а не приводит к отказу: эталон с
    /// испорченным полем отступлений всё равно годен — он просто работает по
    /// умолчаниям технологической карты, и это заведомо безопаснее, чем
    /// невозможность открыть эталон вообще. Число отброшенных записей
    /// возвращается, чтобы мастер эталона мог о них сказать вслух.
    /// </remarks>
    public static IReadOnlyList<ParameterRoleOverride> DeserializeOverrides(string? json, out int discarded)
    {
        discarded = 0;

        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ParameterRoleOverride>();
        }

        List<ParameterRoleOverride> result = new();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                discarded = 1;
                return Array.Empty<ParameterRoleOverride>();
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (TryReadOverride(element, out var item))
                {
                    result.Add(item);
                }
                else
                {
                    discarded++;
                }
            }
        }
        catch (JsonException)
        {
            discarded++;
            return Array.Empty<ParameterRoleOverride>();
        }

        return result;
    }

    /// <summary>Человеческое имя секции — для мастера эталона и для протокола.</summary>
    public static string SectionTitle(ParameterControl control) => control switch
    {
        ParameterControl.ControlledVisible => "Контролируем и показываем",
        ParameterControl.ControlledHidden => "Контролируем, не показываем",
        _ => "Не контролируем",
    };

    /// <summary>Чем секция отличается от соседней — подпись под заголовком.</summary>
    public static string SectionHint(ParameterControl control) => control switch
    {
        ParameterControl.ControlledVisible =>
            "Сверяются, переносятся, и их значения оператор видит на рабочем экране и в протоколе. "
          + "По умолчанию сюда не попадает ничего: что держать перед глазами на этой модели, решает "
          + "технолог — заранее отобранный список «важного» был бы чужим мнением о его изделии.",
        ParameterControl.ControlledHidden =>
            "Сверяются с бортом и переносятся на него. Это основной режим для всей прошивки целиком. "
          + "На рабочем экране не показываются: тысяча с лишним строк вытеснила бы с него то, ради чего "
          + "оператор на него смотрит.",
        _ =>
            "Из сверки и переноса исключены. Это параметры, которые называют конкретный экземпляр железа "
          + "или которые прошивка ведёт сама: совпасть с эталоном они не могут ни на одной исправной плате, "
          + "и контроль по ним останавливал бы каждый прогон.",
    };

    /// <summary>Приведение имени к канону хранения: обрезка и верхний регистр.</summary>
    /// <remarks>
    /// Инвариантная культура обязательна: в турецкой раскладке
    /// <c>ToUpper</c> отображает <c>i</c> в <c>İ</c>, и отступление, заведённое
    /// на такой машине, перестало бы находиться на любой другой.
    /// </remarks>
    private static string Normalize(string name) => name.Trim().ToUpperInvariant();

    private static bool TryReadOverride(JsonElement element, out ParameterRoleOverride item)
    {
        item = null!;

        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String
            || !element.TryGetProperty("control", out var controlElement)
            || controlElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var name = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(name)
            || !Enum.TryParse(controlElement.GetString(), ignoreCase: false, out ParameterControl control))
        {
            return false;
        }

        var justification = ReadString(element, "justification");

        var atUtc = DateTimeOffset.TryParse(
            ReadString(element, "atUtc"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

        item = new ParameterRoleOverride(
            Normalize(name),
            control,
            justification,
            ReadString(element, "by"),
            atUtc);

        return true;
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
