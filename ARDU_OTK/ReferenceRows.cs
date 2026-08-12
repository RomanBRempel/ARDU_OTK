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

        OverrideVisibility = role.IsOverridden ? Visibility.Visible : Visibility.Collapsed;

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
/// Секция раздела контроля: контролируем и показываем, контролируем и не
/// показываем, не контролируем.
/// </summary>
/// <remarks>
/// Секция существует как объект, а не как три отдельные плашки в разметке,
/// потому что заголовок, пояснение и счётчик обязаны быть выведены из одного
/// значения <see cref="ParameterControl"/>. Три копии этих текстов в XAML
/// разошлись бы при первой же правке формулировки.
/// </remarks>
public sealed class ParameterRoleSectionRow
{
    public ParameterRoleSectionRow(
        ParameterControl control,
        IReadOnlyList<ParameterRoleRow> rows,
        int totalCount,
        bool isExpanded)
    {
        ArgumentNullException.ThrowIfNull(rows);

        Control = control;
        Title = ParameterRoleMap.SectionTitle(control);
        Hint = ParameterRoleMap.SectionHint(control);
        IsExpanded = isExpanded;

        Rows = new ObservableCollection<ParameterRoleRow>(rows);

        // Счётчик показывает и отфильтрованное, и полное число: иначе фильтр
        // «COMPASS» создаёт впечатление, что эталон состоит из двадцати
        // параметров, и оператор судит о нём по обрезанной картине.
        CountText = rows.Count == totalCount
            ? totalCount.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{rows.Count} из {totalCount}");

        EmptyVisibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListVisibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyText = totalCount == 0
            ? "В этой секции нет ни одного параметра эталона."
            : "Под фильтр в этой секции ничего не подошло.";
    }

    public ParameterControl Control { get; }

    public string Title { get; }

    public string Hint { get; }

    public string CountText { get; }

    public bool IsExpanded { get; }

    public ObservableCollection<ParameterRoleRow> Rows { get; }

    public Visibility EmptyVisibility { get; }

    public Visibility ListVisibility { get; }

    public string EmptyText { get; }
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
