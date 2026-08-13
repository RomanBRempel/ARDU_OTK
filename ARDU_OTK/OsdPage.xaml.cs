using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ARDU_OTK.Services;
using ARDU_OTK.Services.Fc;
using ARDU_OTK.Services.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ARDU_OTK;

/// <summary>Эталон в списке выбора: имя, короткий хеш и пометка о выводе из обращения.</summary>
public sealed class OsdReferenceChoice
{
    public OsdReferenceChoice(CalibrationReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        Reference = reference;
        Caption = reference.IsRetired
            ? reference.ShortCaption + " · выведен из обращения"
            : reference.ShortCaption;
    }

    public CalibrationReference Reference { get; }

    public string Caption { get; }
}

/// <summary>
/// Просмотр настроек OSD по экранам: таблица панелей и макет знакового поля.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 Раздел заведён потому, что раскладка OSD физически не читается по списку
/// параметров. Одна панель — это три имени (<c>_EN</c>, <c>_X</c>, <c>_Y</c>),
/// панелей на экране до полусотни, экранов несколько; в общем списке сверки они
/// идут вперемешку с тысячей прочих имён и отвечают на вопрос «какое число
/// записано», но не на вопрос «как выглядит экран». Строка
/// <c>OSD1_BAT_VOLT_X = 24</c> не говорит ничего о том, видно ли напряжение
/// пилоту.
/// </para>
/// <para>
/// Страница ничего не пишет на борт. Приведение OSD к эталону делает та же
/// сверка, что и для остальных параметров: второй путь записи означал бы второе
/// место, где решается, что на изделии должно стоять, и оно молча разошлось бы
/// с первым.
/// </para>
/// </remarks>
public sealed partial class OsdPage : Page
{
    /// <summary>Отношение высоты знакоместа к ширине у наложения MAX7456 (12×18 точек).</summary>
    private const double CellAspect = 1.5;

    /// <summary>Через сколько столбцов ставится направляющая линия макета.</summary>
    private const int ColumnGuideStep = 5;

    /// <summary>Через сколько строк ставится направляющая линия макета.</summary>
    private const int RowGuideStep = 4;

    private readonly AppServices _services = AppServices.Instance;

    private OsdConfiguration _configuration = new(Array.Empty<OsdValue>(), Array.Empty<OsdScreen>());

    private IReadOnlyDictionary<string, double>? _boardValues;

    private IReadOnlyDictionary<string, double>? _referenceValues;

    private ParameterRoleMap? _roles;

    private bool _loadingReferences;

    private bool _reading;

    public OsdPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    public ObservableCollection<OsdValueRow> GeneralRows { get; } = new();

    public ObservableCollection<OsdPanelRow> PanelRows { get; } = new();

    public ObservableCollection<OsdValueRow> SettingRows { get; } = new();

    private bool HasReference => _referenceValues is not null;

    private async Task LoadAsync()
    {
        // Снимок борта берётся тот же, что уже прочитан на этом запуске:
        // повторное вычитывание тысячи имён ради взгляда на раскладку — это
        // двадцать секунд ожидания за вопрос «а как там OSD».
        if (_services.LastParameters is { } snapshot)
        {
            _boardValues = OsdLayout.ToValues(snapshot.Set);
            BoardStateText.Text = DescribeSnapshot(snapshot);
        }

        await LoadReferencesAsync().ConfigureAwait(true);
        Rebuild();
    }

    private async Task LoadReferencesAsync()
    {
        try
        {
            var references = await _services.LoadReferencesAsync().ConfigureAwait(true);
            var settings = await _services.LoadSettingsAsync().ConfigureAwait(true);

            var choices = references.Select(static r => new OsdReferenceChoice(r)).ToArray();

            _loadingReferences = true;
            ReferenceBox.ItemsSource = choices;
            ReferenceBox.DisplayMemberPath = nameof(OsdReferenceChoice.Caption);

            // Раздел открывается на том же эталоне, что и стенд: два экрана,
            // показывающие разные эталоны без объявления об этом, — это способ
            // сверить изделие не с тем требованием.
            var selected = choices.FirstOrDefault(c => c.Reference.Id == settings.LastReferenceId)
                ?? choices.FirstOrDefault(static c => !c.Reference.IsRetired);

            ReferenceBox.SelectedItem = selected;
            _loadingReferences = false;

            ApplyReference(selected?.Reference);
        }
        catch (Exception ex)
        {
            _loadingReferences = false;
            Report("Список эталонов не прочитан: " + ex.Message, InfoBarSeverity.Error);
        }
    }

    private void ApplyReference(CalibrationReference? reference)
    {
        if (reference is null)
        {
            _referenceValues = null;
            _roles = null;
            ReferenceStateText.Text = "Без эталона показывается только снимок борта: сверять не с чем.";
            return;
        }

        try
        {
            _referenceValues = reference.ParseParameters().Values;
            _roles = reference.ToRoleMap(out var discarded);

            var osdCount = _referenceValues.Keys.Count(
                static k => k.StartsWith(OsdLayout.Prefix, StringComparison.Ordinal));

            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"{reference.Name}: имён OSD в эталоне {osdCount}.");

            if (osdCount == 0)
            {
                // 🔴 Молчание эталона об OSD — не «всё сошлось». Эталон, снятый
                // с платы без наложения, не описывает раскладку вовсе, и зелёные
                // галочки на её строках были бы утверждением о проверке,
                // которой не было.
                text += " Раскладку этот эталон не описывает: сверять её не с чем.";
            }

            if (discarded > 0)
            {
                text += string.Create(
                    CultureInfo.InvariantCulture,
                    $" Отступлений по ролям не разобралось: {discarded} — эталон работает по умолчаниям.");
            }

            ReferenceStateText.Text = text;
        }
        catch (Exception ex)
        {
            _referenceValues = null;
            _roles = null;
            ReferenceStateText.Text = "Эталон не разобран.";
            Report("Параметры эталона не разобраны: " + ex.Message, InfoBarSeverity.Error);
        }
    }

    private void OnReferenceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingReferences)
        {
            return;
        }

        ApplyReference((ReferenceBox.SelectedItem as OsdReferenceChoice)?.Reference);
        Rebuild();
    }

    private async void OnReadBoardClick(object sender, RoutedEventArgs e)
    {
        if (_reading)
        {
            return;
        }

        // Порт открывается монопольно, и во время приёмки он принадлежит сессии.
        // Сказать об этом прямо обязательно: отказ «нет связи» на подключённом
        // борту читается как неисправность стенда.
        if (_services.IsAcceptanceRunning)
        {
            Report(
                "Идёт приёмка: порт занят ею. Раскладку можно прочитать после её завершения.",
                InfoBarSeverity.Warning);
            return;
        }

        if (!_services.IsLinkConnected)
        {
            Report(
                "Нет связи с бортом. Подключитесь к плате на экране «Стенд» — читать раскладку не с чего.",
                InfoBarSeverity.Warning);
            return;
        }

        _reading = true;
        ReadBoardButton.IsEnabled = false;
        ReadBoardRing.IsActive = true;
        ReadBoardRing.Visibility = Visibility.Visible;
        BoardStateText.Text = "Чтение таблицы параметров…";

        try
        {
            var detail = new Progress<ParameterProgress>(p =>
                BoardStateText.Text = p.Total > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"Принято {p.Done} из {p.Total}: {p.Name}")
                    : string.Create(CultureInfo.InvariantCulture, $"Принято {p.Done}: {p.Name}"));

            var set = await _services.ReadAllParametersAsync(null, detail).ConfigureAwait(true);

            _boardValues = OsdLayout.ToValues(set);
            BoardStateText.Text = _services.LastParameters is { } snapshot
                ? DescribeSnapshot(snapshot)
                : string.Create(CultureInfo.InvariantCulture, $"Прочитано имён: {set.Values.Count}.");

            if (!set.IsComplete)
            {
                // Неполная таблица и полная — разные вещи. Раскладка, собранная
                // из недочитанной таблицы, выглядит так же убедительно, как
                // полная, и молчание об этом означало бы показ выдумки.
                var shortfall = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Борт отдал не всю таблицу: не получено имён {set.Missing.Count} из {set.Declared}. ");

                Report(
                    shortfall + "Панели, чьи параметры не пришли, показаны как отсутствующие.",
                    InfoBarSeverity.Warning);
            }
            else
            {
                PageBar.IsOpen = false;
            }

            Rebuild();
        }
        catch (Exception ex)
        {
            BoardStateText.Text = "Таблица параметров не прочитана.";
            Report("Чтение не выполнено: " + ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _reading = false;
            ReadBoardButton.IsEnabled = true;
            ReadBoardRing.IsActive = false;
            ReadBoardRing.Visibility = Visibility.Collapsed;
        }
    }

    private static string DescribeSnapshot(BoardParameterSnapshot snapshot)
    {
        var osdCount = snapshot.Set.Values.Keys.Count(
            static k => k.StartsWith(OsdLayout.Prefix, StringComparison.Ordinal));

        var when = snapshot.ReadUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        var where = snapshot.Port.Length == 0 ? "борт" : snapshot.Port;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{where}, снято {when}: имён всего {snapshot.Set.Values.Count}, из них OSD {osdCount}.");
    }

    /// <summary>Пересобирает раскладку и вкладки экранов.</summary>
    private void Rebuild()
    {
        _configuration = OsdLayout.Build(_boardValues, _referenceValues, _roles);

        var previous = (ScreenSelector.SelectedItem as SelectorBarItem)?.Tag as int?;

        ScreenSelector.Items.Clear();

        if (_configuration.IsEmpty)
        {
            EmptyText.Text = _boardValues is null && _referenceValues is null
                ? "Прочитайте таблицу с борта либо выберите эталон."
                : "Ни борт, ни эталон не содержат ни одного имени OSD: наложения на этом изделии нет.";
            EmptyText.Visibility = Visibility.Visible;
            GeneralPanel.Visibility = Visibility.Collapsed;
            ScreenPanel.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;

        if (_configuration.General.Count > 0)
        {
            ScreenSelector.Items.Add(new SelectorBarItem { Text = "Общие", Tag = -1 });
        }

        foreach (var screen in _configuration.Screens)
        {
            ScreenSelector.Items.Add(new SelectorBarItem
            {
                Text = string.Create(CultureInfo.InvariantCulture, $"Экран {screen.Number}"),
                Tag = screen.Number,
            });
        }

        if (ScreenSelector.Items.Count == 0)
        {
            GeneralPanel.Visibility = Visibility.Collapsed;
            ScreenPanel.Visibility = Visibility.Collapsed;
            return;
        }

        // Вкладка сохраняется между пересборками: оператор, прочитавший борт
        // при открытом «Экране 2», должен остаться на нём, а не быть
        // выброшенным в начало списка.
        var restored = ScreenSelector.Items.FirstOrDefault(i => (i.Tag as int?) == previous);
        ScreenSelector.SelectedItem = restored ?? ScreenSelector.Items[0];

        ShowSelected();
    }

    private void OnScreenChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => ShowSelected();

    private void ShowSelected()
    {
        if ((ScreenSelector.SelectedItem as SelectorBarItem)?.Tag is not int tag)
        {
            return;
        }

        if (tag < 0)
        {
            GeneralPanel.Visibility = Visibility.Visible;
            ScreenPanel.Visibility = Visibility.Collapsed;

            GeneralRows.Clear();
            foreach (var value in _configuration.General)
            {
                GeneralRows.Add(new OsdValueRow(value, HasReference));
            }

            return;
        }

        var screen = _configuration.Screens.FirstOrDefault(s => s.Number == tag);
        if (screen is null)
        {
            return;
        }

        GeneralPanel.Visibility = Visibility.Collapsed;
        ScreenPanel.Visibility = Visibility.Visible;

        PanelRows.Clear();
        foreach (var panel in screen.Panels)
        {
            PanelRows.Add(new OsdPanelRow(panel, HasReference));
        }

        SettingRows.Clear();
        foreach (var value in screen.Settings)
        {
            SettingRows.Add(new OsdValueRow(value, HasReference));
        }

        PanelsTitleText.Text = string.Create(CultureInfo.InvariantCulture, $"Панели экрана {screen.Number}");
        PanelsSummaryText.Text = DescribeScreen(screen);

        var size = string.Create(
            CultureInfo.InvariantCulture,
            $"{screen.Size.Columns}×{screen.Size.Rows}");

        MockSizeText.Text = screen.Size.IsAssumed ? size + " (по умолчанию)" : size;

        RenderMock(screen);
    }

    private string DescribeScreen(OsdScreen screen)
    {
        var parts = new List<string>(3)
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"панелей {screen.Panels.Count}, показывается {screen.ShownCount}"),
        };

        if (HasReference)
        {
            parts.Add(screen.DefectCount == 0
                ? "с эталоном сходится"
                : string.Create(CultureInfo.InvariantCulture, $"расходится с эталоном: {screen.DefectCount}"));
        }
        else
        {
            parts.Add("эталон не выбран — показан только борт");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Рисует знаковое поле экрана.
    /// </summary>
    /// <remarks>
    /// 🔴 Макет показывает <b>привязку</b> панели — знакоместо её левого края и
    /// имя из прошивки, — а не кадр, который увидит пилот: ширина настоящей
    /// надписи зависит от значения в полёте и на стенде неизвестна. Разница
    /// названа вслух подписью под макетом: картинка, выданная за кадр с очков,
    /// породила бы жалобы на «перекрытие», которого нет, и веру в отсутствие
    /// перекрытия там, где оно есть.
    /// </remarks>
    private void RenderMock(OsdScreen screen)
    {
        MockHost.Children.Clear();
        MockHost.ColumnDefinitions.Clear();
        MockHost.RowDefinitions.Clear();

        for (var i = 0; i < screen.Size.Columns; i++)
        {
            MockHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var i = 0; i < screen.Size.Rows; i++)
        {
            MockHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        AddGuides(screen.Size);

        // Сначала то, чего на борту не рисуется, потом то, что рисуется: живая
        // панель обязана перекрывать след эталона, а не прятаться под ним.
        foreach (var panel in screen.Panels.Where(p => !p.ShownOnBoard && p.ShownInReference))
        {
            if (panel.ReferenceCell is { } cell && screen.Size.Contains(cell.Column, cell.Row))
            {
                MockHost.Children.Add(BuildChip(panel, cell, screen.Size, ghost: true));
            }
        }

        foreach (var panel in screen.Panels.Where(static p => p.ShownOnBoard))
        {
            if (panel.BoardCell is { } cell && screen.Size.Contains(cell.Column, cell.Row))
            {
                MockHost.Children.Add(BuildChip(panel, cell, screen.Size, ghost: false));
            }
        }

        UpdateMockHeight();
        UpdateMockNote(screen);
    }

    private void AddGuides(OsdScreenSize size)
    {
        var brush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];

        for (var column = ColumnGuideStep; column < size.Columns; column += ColumnGuideStep)
        {
            var line = new Border
            {
                BorderBrush = brush,
                BorderThickness = new Thickness(1, 0, 0, 0),
                IsHitTestVisible = false,
            };

            Grid.SetColumn(line, column);
            Grid.SetRowSpan(line, size.Rows);
            MockHost.Children.Add(line);
        }

        for (var row = RowGuideStep; row < size.Rows; row += RowGuideStep)
        {
            var line = new Border
            {
                BorderBrush = brush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                IsHitTestVisible = false,
            };

            Grid.SetRow(line, row);
            Grid.SetColumnSpan(line, size.Columns);
            MockHost.Children.Add(line);
        }
    }

    /// <summary>Плашка панели на макете.</summary>
    /// <param name="ghost">
    /// Панель показывается по эталону, но на борту выключена. Рисуется контуром
    /// без заливки: место, которое на изделии пустует, не должно выглядеть
    /// занятым.
    /// </param>
    private static Border BuildChip(OsdPanel panel, (int Column, int Row) cell, OsdScreenSize size, bool ghost)
    {
        var span = Math.Clamp(panel.Caption.Length, 1, size.Columns - cell.Column);

        var (background, border, foreground) = ghost
            ? ("SystemFillColorCautionBackgroundBrush", "SystemFillColorCautionBrush", "TextFillColorSecondaryBrush")
            : panel.IsDefect
                ? ("SystemFillColorCriticalBackgroundBrush", "SystemFillColorCriticalBrush", "TextFillColorPrimaryBrush")
                : panel.DiffersAnywhere
                    ? ("CardBackgroundFillColorSecondaryBrush", "TextFillColorTertiaryBrush", "TextFillColorSecondaryBrush")
                    : ("CardBackgroundFillColorDefaultBrush", "SystemFillColorSuccessBrush", "TextFillColorPrimaryBrush");

        var chip = new Border
        {
            Background = ghost ? null : (Brush)Application.Current.Resources[background],
            BorderBrush = (Brush)Application.Current.Resources[border],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new TextBlock
            {
                Text = panel.Caption,
                FontFamily = new FontFamily("Consolas"),
                FontSize = (double)Application.Current.Resources["AppTinyFontSize"],
                Foreground = (Brush)Application.Current.Resources[foreground],
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(2, 0, 2, 0),
            },
        };

        ToolTipService.SetToolTip(
            chip,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{panel.Caption}: X {cell.Column}, Y {cell.Row}{(ghost ? " — по эталону, на борту выключена" : string.Empty)}"));

        Grid.SetColumn(chip, cell.Column);
        Grid.SetColumnSpan(chip, span);
        Grid.SetRow(chip, cell.Row);

        return chip;
    }

    private void UpdateMockNote(OsdScreen screen)
    {
        var notes = new List<string>(2);

        var overflowing = screen.Overflowing;
        if (overflowing.Count > 0)
        {
            var head = string.Create(
                CultureInfo.InvariantCulture,
                $"Не помещаются на экран {screen.Size.Columns}×{screen.Size.Rows}: ");

            notes.Add(head
              + string.Join(", ", overflowing.Select(static p => p.Caption))
              + ". На изделии их не видно.");
        }

        if (screen.Size.IsAssumed && screen.Panels.Count > 0)
        {
            notes.Add(
                "Размер поля не объявлен параметром OSD"
              + screen.Number.ToString(CultureInfo.InvariantCulture)
              + "_TXT_RES: взято аналоговое 30×16.");
        }

        MockNoteText.Text = string.Join("\n", notes);
        MockNoteText.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMockSizeChanged(object sender, SizeChangedEventArgs e) => UpdateMockHeight();

    /// <summary>
    /// Держит знакоместо прямоугольным.
    /// </summary>
    /// <remarks>
    /// Высота считается от ширины, а не задаётся числом: знакоместо наложения
    /// выше своей ширины в полтора раза, и поле с квадратными клетками показало
    /// бы раскладку сплюснутой — по такому макету расстояния между панелями
    /// оцениваются неверно.
    /// </remarks>
    private void UpdateMockHeight()
    {
        var columns = MockHost.ColumnDefinitions.Count;
        var rows = MockHost.RowDefinitions.Count;

        if (columns == 0 || rows == 0 || MockHost.ActualWidth <= 0)
        {
            return;
        }

        var height = MockHost.ActualWidth / columns * CellAspect * rows;

        // Присвоение высоты внутри обработчика её же изменения зациклилось бы
        // без этой проверки.
        if (Math.Abs(MockHost.Height - height) > 0.5)
        {
            MockHost.Height = height;
        }
    }

    private void Report(string message, InfoBarSeverity severity)
    {
        PageBar.Severity = severity;
        PageBar.Message = message;
        PageBar.IsOpen = true;
    }
}
