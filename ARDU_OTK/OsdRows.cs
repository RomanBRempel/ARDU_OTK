using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARDU_OTK.Services.Fc;
using Microsoft.UI.Xaml;

namespace ARDU_OTK;

/// <summary>
/// Строка настройки OSD, не относящейся к панели: <c>OSD_TYPE</c>,
/// <c>OSD1_CHAN_MIN</c> и прочие.
/// </summary>
/// <remarks>
/// Показываются рядом с панелями намеренно. Выключенный <c>OSD_TYPE</c> означает
/// экран, которого на очках нет вовсе, — и раскладка при этом выглядит
/// безупречно: все панели включены и расставлены. Раскладка без настроек
/// подсистемы отвечает на вопрос «где панели», но не на вопрос «увидит ли их
/// кто-нибудь».
/// </remarks>
public sealed class OsdValueRow
{
    public OsdValueRow(OsdValue value, bool hasReference)
    {
        ArgumentNullException.ThrowIfNull(value);

        Name = value.Name;
        ValueText = value.BoardText;

        ReferenceLineText = value.Reference is null
            ? string.Empty
            : "эталон " + value.ReferenceText;

        var state = OsdRowState.Of(value.Board is not null, value.Differs, value.IsDefect, hasReference);
        MatchVisibility = state.Match;
        DiffVisibility = state.Diff;
        UncontrolledVisibility = state.Uncontrolled;
        ReferenceLineVisibility = state.ShowReferenceLine && value.Reference is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

        Tip = state.Tip(value);
    }

    public string Name { get; }

    public string ValueText { get; }

    public string ReferenceLineText { get; }

    public string Tip { get; }

    public Visibility MatchVisibility { get; }

    public Visibility DiffVisibility { get; }

    public Visibility UncontrolledVisibility { get; }

    public Visibility ReferenceLineVisibility { get; }
}

/// <summary>Строка панели OSD в таблице экрана.</summary>
/// <remarks>
/// 🔴 Признак показа и знакоместо стоят отдельными колонками, а не сводной
/// строкой. Оператор читает таблицу по вертикали: столбик включений говорит,
/// что вообще рисуется, столбики X и Y — не съехала ли раскладка. Слепленные в
/// одну строку, они заставляют разбирать каждую панель по отдельности, и на
/// полусотне панелей это перестают делать вовсе.
/// </remarks>
public sealed class OsdPanelRow
{
    public OsdPanelRow(OsdPanel panel, bool hasReference)
    {
        ArgumentNullException.ThrowIfNull(panel);

        Caption = panel.Caption;

        EnableText = FormatEnable(panel.Enable.Board);
        ColumnText = panel.Column?.BoardText ?? "—";
        RowText = panel.Row?.BoardText ?? "—";

        var state = OsdRowState.Of(
            !panel.AbsentOnBoard,
            panel.DiffersAnywhere,
            panel.IsDefect,
            hasReference);

        MatchVisibility = state.Match;
        DiffVisibility = state.Diff;
        UncontrolledVisibility = state.Uncontrolled;

        ReferenceLineText = BuildReferenceLine(panel);
        ReferenceLineVisibility = state.ShowReferenceLine && ReferenceLineText.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        Tip = BuildTip(panel);
    }

    public string Caption { get; }

    public string EnableText { get; }

    public string ColumnText { get; }

    public string RowText { get; }

    public string ReferenceLineText { get; }

    public string Tip { get; }

    public Visibility MatchVisibility { get; }

    public Visibility DiffVisibility { get; }

    public Visibility UncontrolledVisibility { get; }

    public Visibility ReferenceLineVisibility { get; }

    /// <summary>
    /// Ноль и единица словами.
    /// </summary>
    /// <remarks>
    /// Число здесь не сообщает ничего: столбик из нулей и единиц читается так же
    /// плохо, как столбик номеров функций серворулей, ради чего и заведена
    /// расшифровка перечислений. Значение при этом никуда не девается — оно в
    /// подсказке строки.
    /// </remarks>
    private static string FormatEnable(double? value) => value switch
    {
        null => "—",
        >= 0.5 => "вкл",
        _ => "выкл",
    };

    /// <summary>Строка-поправка: чем эталон отличается от увиденного.</summary>
    private static string BuildReferenceLine(OsdPanel panel)
    {
        var parts = new List<string>(3);

        if (panel.Enable.Differs)
        {
            parts.Add("эталон " + FormatEnable(panel.Enable.Reference));
        }

        if (panel.Column?.Differs == true)
        {
            parts.Add("X " + panel.Column.ReferenceText);
        }

        if (panel.Row?.Differs == true)
        {
            parts.Add("Y " + panel.Row.ReferenceText);
        }

        return string.Join(" · ", parts);
    }

    private static string BuildTip(OsdPanel panel)
    {
        var names = new[] { panel.Enable, panel.Column, panel.Row }
            .Where(static v => v is not null)
            .Select(static v => string.Create(
                CultureInfo.InvariantCulture,
                $"{v!.Name} = {v.BoardText}"));

        return string.Join("\n", names);
    }
}

/// <summary>
/// Как показать состояние строки: сошлось, разошлось либо разошлось вне контроля.
/// </summary>
/// <remarks>
/// 🔴 Три состояния, а не два. Расхождение по имени, которое эталон не сверяет,
/// дефектом не является: технолог вывел его из-под контроля осознанно. Красная
/// строка на таком имени — ложная тревога, а список, где половина тревог
/// ложные, перестаёт читаться целиком, вместе с настоящими.
/// </remarks>
internal readonly record struct OsdRowState(
    Visibility Match,
    Visibility Diff,
    Visibility Uncontrolled,
    bool ShowReferenceLine)
{
    public static OsdRowState Of(bool presentOnBoard, bool differs, bool isDefect, bool hasReference)
    {
        // Без эталона сравнивать не с чем: показывается снимок борта, и любая
        // отметка «сошлось» здесь была бы утверждением о проверке, которой не
        // было.
        if (!hasReference)
        {
            return new OsdRowState(Visibility.Collapsed, Visibility.Collapsed, Visibility.Collapsed, false);
        }

        if (isDefect)
        {
            return new OsdRowState(Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed, true);
        }

        if (differs)
        {
            return new OsdRowState(Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible, true);
        }

        return presentOnBoard
            ? new OsdRowState(Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed, false)
            : new OsdRowState(Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible, true);
    }

    public string Tip(OsdValue value) => string.Create(
        CultureInfo.InvariantCulture,
        $"{value.Name}: борт {value.BoardText}, эталон {value.ReferenceText}");
}
