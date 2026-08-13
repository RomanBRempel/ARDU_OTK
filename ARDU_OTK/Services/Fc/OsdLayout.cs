using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ARDU_OTK.Services.Fc;

/// <summary>Какое поле панели OSD описывает имя параметра.</summary>
public enum OsdFieldKind
{
    /// <summary>Признак показа панели, суффикс <c>_EN</c>.</summary>
    Enable,

    /// <summary>Столбец знакоместа, суффикс <c>_X</c>.</summary>
    Column,

    /// <summary>Строка знакоместа, суффикс <c>_Y</c>.</summary>
    Row,
}

/// <summary>
/// Одно значение: что на борту, что в эталоне и сверяется ли имя вообще.
/// </summary>
/// <remarks>
/// 🔴 «Значения нет» и «значение ноль» разведены типом <c>double?</c>. Ноль в
/// <c>_EN</c> означает выключенную панель, отсутствие имени — что борт такой
/// панели не знает вовсе (прошивка другой версии либо другая сборка). Сведённые
/// к одному прочерку, эти два состояния читаются как «панель выключена», и
/// расхождение прошивок проходит незамеченным.
/// </remarks>
/// <param name="Name">Полное имя параметра, например <c>OSD1_BAT_VOLT_X</c>.</param>
/// <param name="Board">Значение борта; <c>null</c> — борт имени не знает.</param>
/// <param name="Reference">Значение эталона; <c>null</c> — эталон о нём молчит.</param>
/// <param name="Controlled">Имя сверяется этим эталоном (роль не <see cref="ParameterControl.Uncontrolled"/>).</param>
public sealed record OsdValue(string Name, double? Board, double? Reference, bool Controlled)
{
    /// <summary>Борт и эталон разошлись по этому имени.</summary>
    /// <remarks>
    /// 🔴 Сравнение точное, без допуска. Все поля панели — целые: признак показа
    /// и номер знакоместа. У целого «близко» не бывает: столбец 12 и столбец 13 —
    /// это разные места на экране, а не одно с погрешностью. Значения приводятся
    /// к <c>float</c> тем же способом, что и в сверке параметров, — к решётке
    /// binary32, в которой борт эти числа и хранит.
    /// </remarks>
    public bool Differs =>
        Board is { } board && Reference is { } reference
        && ReferenceParamFile.ToBoardFloat(board) != ReferenceParamFile.ToBoardFloat(reference);

    /// <summary>Расхождение, о котором эталон высказался: сверяемое имя разошлось.</summary>
    /// <remarks>
    /// Расхождение по неконтролируемому имени дефектом не является и красным
    /// показано быть не должно: эталон таких имён не сверяет, и красная строка
    /// на нём — ложная тревога, из-за которой оператор перестаёт верить и
    /// настоящим.
    /// </remarks>
    public bool IsDefect => Controlled && Differs;

    /// <summary>Целое значение борта для раскладки; <c>null</c> — значения нет либо оно не целое.</summary>
    public int? BoardCell => ToCell(Board);

    /// <inheritdoc cref="BoardCell"/>
    public int? ReferenceCell => ToCell(Reference);

    /// <summary>Значение борта как его показывают в строке таблицы.</summary>
    public string BoardText => Format(Board);

    /// <inheritdoc cref="BoardText"/>
    public string ReferenceText => Format(Reference);

    /// <summary>
    /// Знакоместо: целое неотрицательное. Дробное значение знакоместом не
    /// является — такое поле показывается числом и на макет не выносится.
    /// </summary>
    private static int? ToCell(double? value) =>
        value is { } v && !double.IsNaN(v) && v == Math.Floor(v) && v is >= 0 and < 1000
            ? (int)v
            : null;

    private static string Format(double? value) =>
        value is { } v ? ReferenceParamFile.FormatCanonical(v) : "—";
}

/// <summary>
/// Панель OSD: то, что прошивка рисует на экране одним элементом.
/// </summary>
/// <param name="Screen">Номер экрана из имени параметра.</param>
/// <param name="Token">Имя панели без префикса и суффикса, например <c>BAT_VOLT</c>.</param>
/// <param name="Enable">Признак показа.</param>
/// <param name="Column">Столбец знакоместа; <c>null</c> — у панели нет имени <c>_X</c>.</param>
/// <param name="Row">Строка знакоместа; <c>null</c> — у панели нет имени <c>_Y</c>.</param>
public sealed record OsdPanel(
    int Screen,
    string Token,
    OsdValue Enable,
    OsdValue? Column,
    OsdValue? Row)
{
    /// <summary>Подпись панели: имя из прошивки строчными, как его показывает Mission Planner.</summary>
    public string Caption => Token.ToLowerInvariant();

    /// <summary>Панель показывается на борту.</summary>
    public bool ShownOnBoard => Enable.Board >= 0.5;

    /// <summary>Панель показывается по эталону.</summary>
    public bool ShownInReference => Enable.Reference >= 0.5;

    /// <summary>Хоть одно поле панели разошлось со сверяемым эталоном.</summary>
    public bool IsDefect =>
        Enable.IsDefect || Column?.IsDefect == true || Row?.IsDefect == true;

    /// <summary>Хоть одно поле разошлось — включая имена вне контроля.</summary>
    public bool DiffersAnywhere =>
        Enable.Differs || Column?.Differs == true || Row?.Differs == true;

    /// <summary>Борт не знает ни одного поля этой панели: она пришла из эталона.</summary>
    public bool AbsentOnBoard => Enable.Board is null && Column?.Board is null && Row?.Board is null;

    /// <summary>Место панели на борту; <c>null</c> — знакоместо не задано.</summary>
    public (int Column, int Row)? BoardCell =>
        Column?.BoardCell is { } x && Row?.BoardCell is { } y ? (x, y) : null;

    /// <inheritdoc cref="BoardCell"/>
    public (int Column, int Row)? ReferenceCell =>
        Column?.ReferenceCell is { } x && Row?.ReferenceCell is { } y ? (x, y) : null;
}

/// <summary>
/// Размер знакового поля экрана.
/// </summary>
/// <param name="Columns">Знакомест по горизонтали.</param>
/// <param name="Rows">Знакомест по вертикали.</param>
/// <param name="IsAssumed">
/// Размер не объявлен параметрами борта, а взят по умолчанию. Отличать
/// обязательно: макет, нарисованный по предположению, не должен выглядеть как
/// измеренный — иначе панель, «вылезшая за край», окажется выдумкой стенда.
/// </param>
public sealed record OsdScreenSize(int Columns, int Rows, bool IsAssumed)
{
    /// <summary>Знакоместо помещается на этом экране.</summary>
    public bool Contains(int column, int row) => column < Columns && row < Rows;
}

/// <summary>Один экран OSD: его собственные настройки и панели.</summary>
/// <param name="Number">Номер экрана: цифра в имени <c>OSDn_*</c>.</param>
/// <param name="Size">Размер знакового поля.</param>
/// <param name="Settings">Настройки самого экрана: включение, канал, разрешение.</param>
/// <param name="Panels">Панели в порядке имён.</param>
public sealed record OsdScreen(
    int Number,
    OsdScreenSize Size,
    IReadOnlyList<OsdValue> Settings,
    IReadOnlyList<OsdPanel> Panels)
{
    /// <summary>Сколько панелей показывается на борту.</summary>
    public int ShownCount => Panels.Count(static p => p.ShownOnBoard);

    /// <summary>Сколько панелей разошлось со сверяемым эталоном.</summary>
    public int DefectCount => Panels.Count(static p => p.IsDefect);

    /// <summary>Панели, чьё знакоместо на борту не помещается на экран.</summary>
    /// <remarks>
    /// 🔴 Отдельный список, а не пометка в общем. Панель, вынесенная за край
    /// знакового поля, на очках не видна вовсе, при этом в таблице параметров
    /// она выглядит совершенно исправной: признак показа включён, координаты
    /// заданы числами. Именно такое изделие проходит сверку и уезжает
    /// заказчику с пустым углом экрана.
    /// </remarks>
    public IReadOnlyList<OsdPanel> Overflowing => Panels
        .Where(p => p.ShownOnBoard && p.BoardCell is { } cell && !Size.Contains(cell.Column, cell.Row))
        .ToArray();
}

/// <summary>Раскладка OSD целиком: общие настройки и экраны.</summary>
/// <param name="General">Настройки без номера экрана: <c>OSD_TYPE</c>, <c>OSD_UNITS</c> и прочие.</param>
/// <param name="Screens">Экраны в порядке номеров.</param>
public sealed record OsdConfiguration(
    IReadOnlyList<OsdValue> General,
    IReadOnlyList<OsdScreen> Screens)
{
    /// <summary>Ни борт, ни эталон об OSD ничего не говорят.</summary>
    public bool IsEmpty => General.Count == 0 && Screens.Count == 0;
}

/// <summary>
/// Разбор параметров OSD в экранную раскладку.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 Состав панелей не зашит списком, а выводится из самой таблицы параметров.
/// Прошивка на борту может быть новее приложения: зашитый перечень молча
/// потерял бы новые панели и показал бы пустые строки для тех, которых на этом
/// борту нет. Панелью считается всякое имя вида <c>OSDn_ИМЯ_EN</c>; его соседи
/// <c>_X</c> и <c>_Y</c> дают знакоместо. По той же причине и число экранов
/// берётся из имён, а не считается равным четырём.
/// </para>
/// <para>
/// Класс не знает ни про UI, ни про связь с бортом: на входе два словаря
/// значений и карта ролей, на выходе — раскладка. Разбор проверяется без стенда.
/// </para>
/// </remarks>
public static class OsdLayout
{
    /// <summary>Префикс всех имён подсистемы.</summary>
    public const string Prefix = "OSD";

    /// <summary>Знакомест по горизонтали у аналогового наложения MAX7456.</summary>
    public const int DefaultColumns = 30;

    /// <summary>Знакомест по вертикали у аналогового наложения MAX7456 (PAL).</summary>
    public const int DefaultRows = 16;

    /// <summary>Имя параметра разрешения текста: <c>OSDn_TXT_RES</c>.</summary>
    private const string TextResolutionSuffix = "_TXT_RES";

    /// <summary>
    /// Размеры знакового поля по значению <c>OSDn_TXT_RES</c>.
    /// </summary>
    /// <remarks>
    /// Значение, которого здесь нет, размером не подменяется: прошивка новее
    /// таблицы — законный случай, и нарисовать по догадке поле не того размера
    /// значит показать оператору выдуманное расположение панелей.
    /// </remarks>
    private static readonly Dictionary<int, (int Columns, int Rows)> TextResolutions = new()
    {
        [0] = (30, 16),
        [1] = (50, 18),
    };

    /// <summary>Приводит снимок борта к виду, в котором работает разбор.</summary>
    public static IReadOnlyDictionary<string, double> ToValues(FullParameterSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        var result = new Dictionary<string, double>(set.Values.Count, StringComparer.Ordinal);
        foreach (var pair in set.Values)
        {
            result[pair.Key] = pair.Value.Value;
        }

        return result;
    }

    /// <summary>
    /// Собирает раскладку из значений борта и эталона.
    /// </summary>
    /// <param name="board">Снимок борта; <c>null</c> — борт не читали.</param>
    /// <param name="reference">Значения эталона; <c>null</c> — эталон не выбран.</param>
    /// <param name="roles">
    /// Роли параметров эталона. <c>null</c> — эталона нет, и вопрос «сверяется ли
    /// имя» не имеет смысла: расхождений всё равно не показывается.
    /// </param>
    public static OsdConfiguration Build(
        IReadOnlyDictionary<string, double>? board,
        IReadOnlyDictionary<string, double>? reference,
        ParameterRoleMap? roles)
    {
        // Имена берутся из обоих источников: панель, которой на борту нет, а в
        // эталоне есть, — это не пустое место, а расхождение, и увидеть его надо.
        var names = new SortedSet<string>(StringComparer.Ordinal);
        Collect(board, names);
        Collect(reference, names);

        var general = new List<OsdValue>();
        var screenSettings = new Dictionary<int, List<OsdValue>>();
        var screenPanels = new Dictionary<int, Dictionary<string, PanelBuilder>>();

        foreach (var name in names)
        {
            var value = Read(name, board, reference, roles);

            if (!TrySplitScreen(name, out var screen, out var tail))
            {
                general.Add(value);
                continue;
            }

            if (!TrySplitField(tail, out var token, out var field))
            {
                if (!screenSettings.TryGetValue(screen, out var settings))
                {
                    settings = new List<OsdValue>();
                    screenSettings[screen] = settings;
                }

                settings.Add(value);
                continue;
            }

            if (!screenPanels.TryGetValue(screen, out var panels))
            {
                panels = new Dictionary<string, PanelBuilder>(StringComparer.Ordinal);
                screenPanels[screen] = panels;
            }

            if (!panels.TryGetValue(token, out var builder))
            {
                builder = new PanelBuilder();
                panels[token] = builder;
            }

            builder.Set(field, value);
        }

        var numbers = new SortedSet<int>(screenSettings.Keys);
        numbers.UnionWith(screenPanels.Keys);

        var screens = new List<OsdScreen>(numbers.Count);
        foreach (var number in numbers)
        {
            var settings = screenSettings.TryGetValue(number, out var list)
                ? list
                : new List<OsdValue>();

            var panels = screenPanels.TryGetValue(number, out var map)
                ? map
                    // 🔴 Без имени _EN панелью считать нечего: одинокие _X и _Y
                    // бывают у настроек, которые панелями не являются, и строка
                    // «панель без признака показа» сообщала бы о несуществующем.
                    .Where(static p => p.Value.Enable is not null)
                    .OrderBy(static p => p.Key, StringComparer.Ordinal)
                    .Select(p => p.Value.Build(number, p.Key))
                    .ToArray()
                : Array.Empty<OsdPanel>();

            screens.Add(new OsdScreen(number, ResolveSize(number, board, reference), settings, panels));
        }

        return new OsdConfiguration(general, screens);
    }

    /// <summary>
    /// Размер знакового поля экрана.
    /// </summary>
    /// <remarks>
    /// Спрашивается у борта, а при его отсутствии — у эталона. Если ни один не
    /// назвал разрешения либо назвал незнакомое, берётся аналоговое поле 30×16 и
    /// помечается предположением: показать «панель за краем экрана», не зная
    /// размера экрана, — это тревога о том, чего никто не измерял.
    /// </remarks>
    private static OsdScreenSize ResolveSize(
        int screen,
        IReadOnlyDictionary<string, double>? board,
        IReadOnlyDictionary<string, double>? reference)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"{Prefix}{screen}{TextResolutionSuffix}");

        double? declared = board is not null && board.TryGetValue(name, out var fromBoard)
            ? fromBoard
            : reference is not null && reference.TryGetValue(name, out var fromReference)
                ? fromReference
                : null;

        if (declared is { } value
            && value == Math.Floor(value)
            && TextResolutions.TryGetValue((int)value, out var size))
        {
            return new OsdScreenSize(size.Columns, size.Rows, IsAssumed: false);
        }

        return new OsdScreenSize(DefaultColumns, DefaultRows, IsAssumed: true);
    }

    private static void Collect(IReadOnlyDictionary<string, double>? values, SortedSet<string> names)
    {
        if (values is null)
        {
            return;
        }

        foreach (var name in values.Keys)
        {
            if (name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                names.Add(name);
            }
        }
    }

    private static OsdValue Read(
        string name,
        IReadOnlyDictionary<string, double>? board,
        IReadOnlyDictionary<string, double>? reference,
        ParameterRoleMap? roles) => new(
            name,
            board is not null && board.TryGetValue(name, out var actual) ? actual : null,
            reference is not null && reference.TryGetValue(name, out var expected) ? expected : null,
            roles?.IsControlled(name) ?? false);

    /// <summary>
    /// Отделяет номер экрана: <c>OSD1_BAT_VOLT_X</c> — экран 1, хвост <c>BAT_VOLT_X</c>.
    /// </summary>
    /// <remarks>
    /// Имя без цифры (<c>OSD_TYPE</c>) экрану не принадлежит: это настройка всей
    /// подсистемы, и приписать её первому экрану значило бы утверждать, будто
    /// остальные работают иначе.
    /// </remarks>
    private static bool TrySplitScreen(string name, out int screen, out string tail)
    {
        screen = 0;
        tail = string.Empty;

        if (!name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = Prefix.Length;
        while (digits < name.Length && char.IsAsciiDigit(name[digits]))
        {
            digits++;
        }

        if (digits == Prefix.Length || digits >= name.Length || name[digits] != '_')
        {
            return false;
        }

        if (!int.TryParse(
                name.AsSpan(Prefix.Length, digits - Prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out screen))
        {
            return false;
        }

        tail = name[(digits + 1)..];
        return tail.Length > 0;
    }

    /// <summary>
    /// Отделяет поле панели: <c>BAT_VOLT_X</c> — панель <c>BAT_VOLT</c>, поле «столбец».
    /// </summary>
    /// <remarks>
    /// 🔴 Суффикс отделяется вместе с подчёркиванием. Проверка «имя кончается на
    /// EN» захватила бы <c>OSD1_ENABLE</c> — включение всего экрана — и завела бы
    /// панель с пустым именем поверх настройки экрана. Разделитель здесь не
    /// украшение, а единственное, чем поле отличается от хвоста слова.
    /// </remarks>
    private static bool TrySplitField(string tail, out string token, out OsdFieldKind field)
    {
        token = string.Empty;
        field = OsdFieldKind.Enable;

        foreach (var (suffix, kind) in Suffixes)
        {
            if (tail.Length > suffix.Length && tail.EndsWith(suffix, StringComparison.Ordinal))
            {
                token = tail[..^suffix.Length];
                field = kind;
                return true;
            }
        }

        return false;
    }

    private static readonly (string Suffix, OsdFieldKind Kind)[] Suffixes =
    [
        ("_EN", OsdFieldKind.Enable),
        ("_X", OsdFieldKind.Column),
        ("_Y", OsdFieldKind.Row),
    ];

    /// <summary>Накопитель полей одной панели: они приходят тремя отдельными именами.</summary>
    private sealed class PanelBuilder
    {
        public OsdValue? Enable { get; private set; }

        public OsdValue? Column { get; private set; }

        public OsdValue? Row { get; private set; }

        public void Set(OsdFieldKind kind, OsdValue value)
        {
            switch (kind)
            {
                case OsdFieldKind.Enable:
                    Enable = value;
                    break;
                case OsdFieldKind.Column:
                    Column = value;
                    break;
                default:
                    Row = value;
                    break;
            }
        }

        public OsdPanel Build(int screen, string token) =>
            new(screen, token, Enable!, Column, Row);
    }
}
