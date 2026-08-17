using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ARDU_OTK.Services.Fc;
using ARDU_OTK.Services.Store;
using Microsoft.UI.Xaml;

namespace ARDU_OTK;

/// <summary>
/// Строка ожидаемого состава компасов в мастере эталона.
/// </summary>
/// <remarks>
/// Пустой слот — это не «плохой» слот, а законное состояние, и выглядеть он
/// обязан иначе, чем слот с неопознанным датчиком: первое нормально, второе
/// требует разбирательства.
/// </remarks>
public sealed class ExpectedCompassSlotRow
{
    public ExpectedCompassSlotRow(ExpectedCompassSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        Slot = slot.Slot;
        SlotText = string.Create(CultureInfo.InvariantCulture, $"Слот {slot.Slot}");
        IsPresent = slot.IsPresent;

        // Флага может не быть вовсе: эталон вправе не содержать COMPASS_EXTERNAL*.
        // Тогда судить о внешности нельзя — и это отдельное состояние, а не
        // «внутренний».
        Kind = slot.ExternalFlag is { } flag
            ? CompassIdentity.Classify(slot.DeviceId, flag)
            : ExternalKind.Ambiguous;

        IsExternal = slot.IsPresent ? CompassIdentity.IsExternal(Kind) : null;

        DeviceText = CompassIdentity.Describe(slot.DeviceId);
        KindText = slot.IsPresent
            ? CompassIdentity.ExternalKindText(Kind)
              + (slot.ExternalFlag is null ? " (COMPASS_EXTERNAL* в эталоне нет)" : string.Empty)
            : "слот не занят";

        PresentVisibility = slot.IsPresent ? Visibility.Visible : Visibility.Collapsed;
        EmptyVisibility = slot.IsPresent ? Visibility.Collapsed : Visibility.Visible;

        // Значок предупреждения только там, где действительно неясно: у пустого
        // слота вопросов нет.
        AmbiguousVisibility = slot.IsPresent && IsExternal is null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public int Slot { get; }

    public string SlotText { get; }

    public string DeviceText { get; }

    public string KindText { get; }

    public bool IsPresent { get; }

    public ExternalKind Kind { get; }

    /// <summary><c>null</c> — судить нельзя; приравнивать к любому из значений запрещено.</summary>
    public bool? IsExternal { get; }

    public Visibility PresentVisibility { get; }

    public Visibility EmptyVisibility { get; }

    public Visibility AmbiguousVisibility { get; }
}

/// <summary>
/// Строка параметра в разделе контроля мастера эталона.
/// </summary>
/// <remarks>
/// Роль и значение показываются вместе намеренно: вопрос «почему этот параметр
/// не контролируется» оператор задаёт, глядя на конкретное число, и ответ
/// должен стоять рядом с ним, а не в справке.
/// </remarks>
public sealed class ParameterRoleRow
{
    /// <summary>
    /// Положение ползунка: не контролируем — контроль — контроль и показ.
    /// </summary>
    /// <remarks>
    /// 🔴 Порядок монотонный, по возрастанию внимания к параметру, а не по
    /// порядку членов <see cref="ParameterControl"/>. Ползунок — это шкала:
    /// положение «не контролируем» между двумя видами контроля читалось бы как
    /// «средняя степень», и оператор промахивался бы мимо нужного деления,
    /// глядя не на подпись, а на положение кружка.
    /// </remarks>
    public static ParameterControl FromSliderIndex(double index) => (int)Math.Round(index) switch
    {
        0 => ParameterControl.Uncontrolled,
        1 => ParameterControl.ControlledHidden,
        _ => ParameterControl.ControlledVisible,
    };

    /// <inheritdoc cref="FromSliderIndex"/>
    public static double ToSliderIndex(ParameterControl control) => control switch
    {
        ParameterControl.Uncontrolled => 0,
        ParameterControl.ControlledHidden => 1,
        _ => 2,
    };

    public ParameterRoleRow(
        ParameterRole role,
        double value,
        ParameterEnums.VehicleClass vehicle = ParameterEnums.VehicleClass.Unknown)
    {
        ArgumentNullException.ThrowIfNull(role);

        Name = role.Name;
        Control = role.Control;
        CannotMatch = role.CannotMatch;
        SliderIndex = ToSliderIndex(role.Control);
        ModeText = ParameterRoleMap.SectionTitle(role.Control);

        // Технолог решает роль параметра по смыслу значения, а не по его коду:
        // «Aileron» отвечает на вопрос «что это», число — нет.
        ValueText = ParameterEnums.Format(
            role.Name, value, vehicle, ReferenceParamFile.FormatCanonical(value));
        ReasonText = role.Reason;

        // Отступление показывается вместе с обоснованием и подписью: строка
        // «изменено» без ответа на «кем и зачем» ничего не объясняет и лишь
        // вызывает вопрос, на который в интерфейсе нет ответа.
        OverrideText = role.Override is { } over
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Отступление от умолчания: {over.Justification} — {DescribeAuthor(over)}")
            : string.Empty;

        IsOverridden = role.IsOverridden;
        OverrideVisibility = role.IsOverridden ? Visibility.Visible : Visibility.Collapsed;

        IsHazardous = role.IsHazardousOverride;
        HazardVisibility = role.IsHazardousOverride ? Visibility.Visible : Visibility.Collapsed;
        HazardText = role.IsHazardousOverride
            ? "Это имя не может совпасть с эталоном ни на одной исправной плате: контроль по нему остановит каждый прогон."
            : string.Empty;
    }

    public string Name { get; }

    public ParameterControl Control { get; }

    /// <summary>Положение ползунка 0..2. Привязка односторонняя: применение идёт через обработчик страницы.</summary>
    public double SliderIndex { get; }

    /// <summary>Подпись текущего режима под ползунком.</summary>
    public string ModeText { get; }

    public string ValueText { get; }

    public string ReasonText { get; }

    public string OverrideText { get; }

    public string HazardText { get; }

    /// <summary>Параметр не может совпасть с эталоном по устройству прошивки, а не по решению технолога.</summary>
    public bool CannotMatch { get; }

    /// <summary>Роль отличается от умолчания технологической карты.</summary>
    public bool IsOverridden { get; }

    /// <summary>Под контролем имя, которое совпасть не может: прогон остановится всегда.</summary>
    public bool IsHazardous { get; }

    public Visibility OverrideVisibility { get; }

    public Visibility HazardVisibility { get; }

    private static string DescribeAuthor(ParameterRoleOverride over)
    {
        var who = string.IsNullOrWhiteSpace(over.By) ? "автор не записан" : over.By;
        return over.AtUtc == DateTimeOffset.MinValue
            ? who
            : string.Create(CultureInfo.InvariantCulture, $"{who}, {over.AtUtc.ToLocalTime():dd.MM.yyyy}");
    }
}

/// <summary>
/// Узел дерева параметров: все имена с одним префиксом — текстом до первого
/// подчёркивания.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 Дерево, а не плоский список, потому что полный дамп прошивки — больше
/// тысячи имён. Плоский список отвечает только на вопрос «покажи параметр,
/// имя которого я уже знаю»; вопрос «что здесь относится к компасам, к
/// серво-выходам, к батарее» он оставляет на перебор глазами, а именно этот
/// вопрос оператор и задаёт, разбирая чужой эталон.
/// </para>
/// <para>
/// Разбиение ровно как в Mission Planner — по тексту до первого «_». Своё,
/// «улучшенное» разбиение заставило бы держать в голове два дерева сразу:
/// одно в наземной станции, другое здесь.
/// </para>
/// </remarks>
public sealed class ParameterGroupRow
{
    /// <summary>Узел для имён без подчёркивания: это не префикс, а остаток.</summary>
    public const string NoPrefixKey = "без префикса";

    public ParameterGroupRow(
        string prefix,
        IReadOnlyList<ParameterRoleRow> rows,
        int totalCount,
        bool isExpanded)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(rows);

        Prefix = prefix;
        Title = prefix == NoPrefixKey ? NoPrefixKey : prefix + "_*";
        IsExpanded = isExpanded;

        Rows = new ObservableCollection<ParameterRoleRow>(rows);

        // Счётчик показывает и отфильтрованное, и полное число: иначе фильтр
        // «OFS» создаёт впечатление, что в узле COMPASS шесть параметров, и
        // оператор судит об эталоне по обрезанной картине.
        CountText = rows.Count == totalCount
            ? totalCount.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{rows.Count} из {totalCount}");

        // Раскладка узла видна на свёрнутом узле. Иначе, чтобы узнать, есть ли
        // в узле хоть что-то контролируемое, его пришлось бы раскрыть — то есть
        // раскрыть все сто узлов подряд, ради чего дерево и не заводят.
        var visible = rows.Count(static r => r.Control == ParameterControl.ControlledVisible);
        var hidden = rows.Count(static r => r.Control == ParameterControl.ControlledHidden);
        var off = rows.Count - visible - hidden;

        var parts = new List<string>(3);
        if (visible > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"показ {visible}"));
        }

        if (hidden > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"контроль {hidden}"));
        }

        if (off > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"вне контроля {off}"));
        }

        ModeText = string.Join(" · ", parts);

        HazardVisibility = rows.Any(static r => r.IsHazardous) ? Visibility.Visible : Visibility.Collapsed;
        OverrideVisibility = rows.Any(static r => r.IsOverridden) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Ключ узла: то, по чему группировали. Не для показа — для сопоставления.</summary>
    public string Prefix { get; }

    /// <summary>Заголовок узла: <c>COMPASS_*</c>.</summary>
    public string Title { get; }

    public string CountText { get; }

    /// <summary>Раскладка контроля внутри узла, видимая на свёрнутом узле.</summary>
    public string ModeText { get; }

    /// <summary>
    /// Узел раскрыт.
    /// </summary>
    /// <remarks>
    /// 🔴 Свойство изменяемое, хотя привязка односторонняя и разовая. Контейнеры
    /// списка переиспользуются при прокрутке: если оставить здесь состояние на
    /// момент сборки, раскрытый узел, уехавший за край экрана и вернувшийся,
    /// пришёл бы обратно свёрнутым — а оператор считает это потерей своего места
    /// в списке.
    /// </remarks>
    public bool IsExpanded { get; set; }

    public ObservableCollection<ParameterRoleRow> Rows { get; }

    /// <summary>В узле есть имя под контролем, которое совпасть не может.</summary>
    public Visibility HazardVisibility { get; }

    /// <summary>В узле есть отступление от технологической карты.</summary>
    public Visibility OverrideVisibility { get; }

    /// <summary>Префикс имени: текст до первого подчёркивания.</summary>
    /// <remarks>
    /// Правило живёт здесь, а не на странице: по нему строится дерево и по нему
    /// же запоминается, какие узлы раскрыты. Две копии правила разошлись бы, и
    /// раскрытие перестало бы попадать в свой узел.
    /// </remarks>
    public static string PrefixOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var cut = name.IndexOf('_', StringComparison.Ordinal);
        return cut > 0 ? name[..cut] : NoPrefixKey;
    }

    /// <summary>
    /// Порядок узлов: <c>SERVO2</c> раньше <c>SERVO10</c>.
    /// </summary>
    /// <remarks>
    /// Побайтовое сравнение поставило бы SERVO10 между SERVO1 и SERVO2, и
    /// оператор, ищущий девятый выход, пролистал бы мимо него: глаз в дереве
    /// ведут по номеру, а не по коду символа. Остаток без префикса уходит в
    /// конец — он не место в алфавите, а мусорная корзина имён.
    /// </remarks>
    public static int Compare(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a == NoPrefixKey || b == NoPrefixKey)
        {
            return a == b ? 0 : a == NoPrefixKey ? 1 : -1;
        }

        var (headA, numberA) = SplitTrailingNumber(a);
        var (headB, numberB) = SplitTrailingNumber(b);

        var head = string.CompareOrdinal(headA, headB);
        return head != 0 ? head : numberA.CompareTo(numberB);
    }

    /// <summary>Делит «SERVO10» на «SERVO» и 10. Без хвостовых цифр номер равен −1.</summary>
    private static (string Head, int Number) SplitTrailingNumber(string prefix)
    {
        var cut = prefix.Length;
        while (cut > 0 && char.IsAsciiDigit(prefix[cut - 1]))
        {
            cut--;
        }

        if (cut == prefix.Length)
        {
            return (prefix, -1);
        }

        // Длинный хвост цифр — не номер экземпляра, а чужеродное имя: сравнивать
        // его как число нельзя, но и ронять разбор списка из-за него незачем.
        return (
            prefix[..cut],
            int.TryParse(prefix[cut..], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number
                : -1);
    }
}

/// <summary>Строка скрипта в мастере эталона.</summary>
/// <remarks>
/// Хеш показывается рядом с именем намеренно: два скрипта с одним именем и
/// разным содержимым — самая частая причина расхождения, и различить их можно
/// только по хешу.
/// </remarks>
public sealed class ReferenceScriptRow
{
    public ReferenceScriptRow(ReferenceScript script, bool canRemove)
    {
        ArgumentNullException.ThrowIfNull(script);

        Source = script;
        Path = script.Path;
        FileName = script.FileName;

        DetailText = string.Create(
            CultureInfo.InvariantCulture,
            $"{script.Path} · {script.ByteCount} байт · SHA-256 {ReferenceParameters.ShortHash(script.Hash)}");

        // Первые строки скрипта — это его шапка с назначением и версией; по ней
        // оператор опознаёт скрипт быстрее, чем по хешу.
        PreviewText = Preview(script.Text);

        RemoveVisibility = canRemove ? Visibility.Visible : Visibility.Collapsed;
    }

    public ReferenceScript Source { get; }

    public string Path { get; }

    public string FileName { get; }

    public string DetailText { get; }

    public string PreviewText { get; }

    public Visibility RemoveVisibility { get; }

    private static string Preview(string text)
    {
        var lines = text.Split('\n', 4, StringSplitOptions.None);
        var head = string.Join(
            " ⏎ ",
            lines.Take(3).Select(static l => l.TrimEnd('\r').Trim()).Where(static l => l.Length > 0));

        return head.Length <= 160 ? head : head[..160] + "…";
    }
}

/// <summary>Подпись эталона под заголовком плашки.</summary>
/// <remarks>
/// Отдельный метод, а не свойство записи: плашки строятся кодом, и подпись
/// нужна одной строкой, которая читается в колонке шириной 30 % экрана.
/// </remarks>
public static class ReferenceCaption
{
    /// <summary>Чем эталон опознаётся: происхождение, объём набора и короткий хеш.</summary>
    public static string Describe(CalibrationReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{reference.SourceName} · параметров {reference.ParamCount} · {ReferenceParameters.ShortHash(reference.ParamHash)}");
    }
}
