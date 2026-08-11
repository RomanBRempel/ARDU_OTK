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
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI;
using Windows.UI;
using Windows.Foundation;

namespace ARDU_OTK;

/// <summary>Строка панели компасов.</summary>
public sealed class CompassRow
{
    /// <summary>Норма модуля магнитного поля, мГс, и границы предполётной проверки.</summary>
    public const double FieldExpected = 530;

    public const double FieldMin = 185;

    public const double FieldMax = 875;

    public CompassRow(MagSample sample)
    {
        var magnitude = sample.Magnitude;
        var inBand = magnitude is >= FieldMin and <= FieldMax;

        Title = $"Компас {sample.Instance + 1}";
        Detail = string.Create(CultureInfo.InvariantCulture, $"X {sample.X}  Y {sample.Y}  Z {sample.Z}");
        FieldText = string.Create(CultureInfo.InvariantCulture, $"{magnitude:0}");

        OkVisibility = inBand ? Visibility.Visible : Visibility.Collapsed;
        WarnVisibility = inBand ? Visibility.Collapsed : Visibility.Visible;

        if (!inBand)
        {
            Detail += magnitude < FieldMin ? "  · поле слабее нормы" : "  · поле сильнее нормы";
        }
    }

    public string Title { get; }

    public string Detail { get; }

    public string FieldText { get; }

    public Visibility OkVisibility { get; }

    public Visibility WarnVisibility { get; }
}

/// <summary>
/// Рабочий экран стенда: выбор борта и эталона плашками, индикатор и панели.
/// </summary>
/// <remarks>
/// Выпадающих списков здесь нет намеренно: выбор — это плашки, которые
/// раскрываются одна вверх и остальные вниз от текущей, а нажатие на плашку
/// сразу выполняет действие, без отдельной кнопки подтверждения.
/// </remarks>
public sealed partial class MainPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    /// <summary>
    /// Слежение за составом COM-портов: плату могут воткнуть и вынуть при
    /// работающем приложении, и список обязан меняться сам.
    /// </summary>
    /// <remarks>
    /// Опрашивается дешёвый <c>GetPortNames</c>; полное перечисление с описанием
    /// устройств через WMI запускается только когда набор имён изменился.
    /// </remarks>
    private readonly DispatcherTimer _portsTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    private string[] _knownPortNames = [];
    private bool _portsScanBusy;

    private IReadOnlyList<SerialPortDescription> _ports = [];
    private IReadOnlyList<CalibrationReference> _references = [];

    private SerialPortDescription? _selectedPort;
    private CalibrationReference? _selectedReference;

    private WorkstationSettings _settings = new();

    private bool _portsExpanded;
    private bool _referencesExpanded;
    private bool _storeReady;
    private bool _busy;

    public MainPage()
    {
        InitializeComponent();

        _services.LinkChanged += OnLinkChanged;
        _hudTimer.Tick += OnHudTick;
        _portsTimer.Tick += OnPortsTick;

        // Закрытие щелчком мимо — это тоже закрытие: без синхронизации флага
        // следующее нажатие на карточку «схлопнуло» бы уже закрытый список.
        PortPopup.Closed += (_, _) => _portsExpanded = false;
        ReferencePopup.Closed += (_, _) => _referencesExpanded = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ObservableCollection<CompassRow> Compasses { get; } = [];

    public ObservableCollection<CalibrationLogRow> LogEntries { get; } = [];

    /// <summary>Выбранный эталон.</summary>
    public CalibrationReference? SelectedReference => _selectedReference;

    /// <summary>Выбранный порт борта.</summary>
    public string? SelectedPort => _selectedPort?.PortName;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        RenderHud();
        await ReloadPortsAsync().ConfigureAwait(true);

        _knownPortNames = [.. _ports.Select(static p => p.PortName).Order(StringComparer.OrdinalIgnoreCase)];
        _portsTimer.Start();

        try
        {
            await _services.InitializeAsync().ConfigureAwait(true);
            _storeReady = true;
        }
        catch (Exception ex)
        {
            ReferenceBar.Severity = InfoBarSeverity.Error;
            ReferenceBar.Message = "Реестр недоступен: " + ex.Message;
            ReferenceBar.IsOpen = true;
        }

        if (_storeReady)
        {
            // Настройки рабочего места живут в реестре, а не в памяти страницы:
            // переход между вкладками пересоздаёт страницу, и флаг, хранимый
            // только в поле, каждый раз терялся бы.
            try
            {
                _settings = await _services.LoadSettingsAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log("Не удалось прочитать настройки рабочего места: " + ex.Message);
            }

            AutoConnectCheck.IsChecked = _settings.AutoConnect;

            if (_settings.LastPortName is { Length: > 0 } lastPort)
            {
                _selectedPort = _ports.FirstOrDefault(p =>
                    string.Equals(p.PortName, lastPort, StringComparison.OrdinalIgnoreCase)) ?? _selectedPort;
            }

            await ReloadReferencesAsync().ConfigureAwait(true);
        }

        RenderAll();

        // Проверка связи обязательна: страница пересоздаётся при каждом возврате
        // с экрана эталонов, а соединение живёт в AppServices и переживает это.
        // Без проверки возврат рвал бы живой канал и поднимал его заново.
        if (AutoConnectCheck.IsChecked == true && !_services.IsLinkConnected)
        {
            await TryAutoConnectAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Сохраняет настройки стенда, не роняя экран при отказе реестра.</summary>
    private async Task PersistSettingsAsync()
    {
        if (!_storeReady)
        {
            return;
        }

        _settings = _settings with
        {
            AutoConnect = AutoConnectCheck.IsChecked == true,
            LastPortName = _selectedPort?.PortName ?? string.Empty,
            LastReferenceId = _selectedReference?.Id,
        };

        try
        {
            await _services.SaveSettingsAsync(_settings).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log("Не удалось сохранить настройки: " + ex.Message);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _services.LinkChanged -= OnLinkChanged;
        _hudTimer.Stop();
        _hudTimer.Tick -= OnHudTick;
        _portsTimer.Stop();
        _portsTimer.Tick -= OnPortsTick;
    }

    /// <summary>Замечает появление и пропажу устройств, пока приложение открыто.</summary>
    private async void OnPortsTick(object? sender, object e)
    {
        if (_portsScanBusy || _portsExpanded)
        {
            // Пока список раскрыт, не подменяем плашки под рукой оператора.
            return;
        }

        _portsScanBusy = true;
        try
        {
            var names = await Task.Run(System.IO.Ports.SerialPort.GetPortNames).ConfigureAwait(true);
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);

            if (names.AsSpan().SequenceEqual(_knownPortNames))
            {
                return;
            }

            var appeared = names.Except(_knownPortNames, StringComparer.OrdinalIgnoreCase).ToList();
            var vanished = _knownPortNames.Except(names, StringComparer.OrdinalIgnoreCase).ToList();
            _knownPortNames = names;

            await ReloadPortsAsync().ConfigureAwait(true);

            foreach (var port in appeared)
            {
                Log($"Обнаружено устройство: {port}.");
            }

            foreach (var port in vanished)
            {
                Log($"Устройство отключено: {port}.");
            }

            // Пропал порт, на котором держалась связь, — сообщаем прямо: борт
            // выдернули или он ушёл в перезагрузку.
            if (_services.ConnectedPort is { } active
                && vanished.Contains(active, StringComparer.OrdinalIgnoreCase))
            {
                ShowLinkMessage(InfoBarSeverity.Warning, $"Порт {active} исчез — связь с бортом потеряна.");
                await _services.DisconnectAsync().ConfigureAwait(true);
            }
            else if (AutoConnectCheck.IsChecked == true && !_services.IsLinkConnected && appeared.Count > 0)
            {
                await TryAutoConnectAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Log("Опрос портов не удался: " + ex.Message);
        }
        finally
        {
            _portsScanBusy = false;
        }
    }

    // --- Плашки ------------------------------------------------------------

    /// <summary>
    /// Строит плашки вокруг выбранной: одна выше, остальные ниже.
    /// </summary>
    /// <param name="putPreviousAbove">
    /// Отдавать ли верхнюю позицию предыдущей по списку плашке. У эталонов она
    /// занята действием «Добавить эталон», поэтому там <c>false</c>: действие и
    /// выбор не смешиваются в одном столбце.
    /// </param>
    private void BuildTiles<T>(
        StackPanel above,
        StackPanel below,
        IReadOnlyList<T> items,
        T? selected,
        Func<T, string> title,
        Func<T, string> detail,
        Action<T> pick,
        bool putPreviousAbove = true)
        where T : class
    {
        above.Children.Clear();
        below.Children.Clear();

        var index = selected is null ? -1 : items.ToList().IndexOf(selected);

        for (var i = 0; i < items.Count; i++)
        {
            if (i == index)
            {
                continue;
            }

            var item = items[i];
            var tile = CreateTile(title(item), detail(item), () => pick(item));

            // Одна плашка раскрывается вверх — это предыдущая по списку.
            if (putPreviousAbove && index > 0 && i == index - 1)
            {
                above.Children.Add(tile);
            }
            else
            {
                below.Children.Add(tile);
            }
        }
    }

    /// <summary>
    /// Плашка «Добавить эталон»: то же оформление, что у эталонов, плюс
    /// подсветка снизу и акцентный кант.
    /// </summary>
    /// <remarks>
    /// Выглядит как панель намеренно — это не кнопка в отдельном ряду, а такой
    /// же элемент раскрытого столбца. Но кант и свечение отличают действие от
    /// выбора: нажатие уводит на экран заведения, а не меняет текущий эталон.
    /// </remarks>
    private UIElement CreateAddReferenceTile()
    {
        var tile = CreateTile(
            "Добавить эталон",
            _services.IsLinkConnected
                ? "выбрать эталон в файле или снять с подключённого борта"
                : "выбрать эталон в файле",
            OpenReferenceEditor);

        tile.Style = (Style)Resources["ActiveTileStyle"];

        var glow = new Border
        {
            Height = 10,
            Margin = new Thickness(8, 0, 8, 0),
            Background = (Brush)Resources["GlowBrush"],
            CornerRadius = new CornerRadius(0, 0, 6, 6),
        };

        var stack = new StackPanel();
        stack.Children.Add(tile);
        stack.Children.Add(glow);
        return stack;
    }

    /// <summary>
    /// Уводит на экран заведения эталона.
    /// </summary>
    /// <remarks>
    /// Живое соединение при этом не рвётся: снимок эталона читается по тому же
    /// каналу, и подключённый образец обязан остаться подключённым.
    /// </remarks>
    private void OpenReferenceEditor()
    {
        _referencesExpanded = false;
        RenderExpansion();

        Frame.Navigate(typeof(ReferenceEditorPage), new ReferenceEditorArgs(_services, null));
    }

    /// <summary>
    /// Правка текущего эталона.
    /// </summary>
    /// <remarks>
    /// Здесь правятся только имя, описание и — пока по эталону не сдавали плат —
    /// допуски. Сам эталон неизменяем, а вывод из обращения и прочая работа со
    /// списком живут в разделе настроек: на рабочем экране им не место, оператор
    /// у стенда занят бортом, а не ведением справочника.
    /// </remarks>
    private void OnEditReferenceClick(object sender, RoutedEventArgs e)
    {
        if (_selectedReference is not { } profile)
        {
            return;
        }

        _referencesExpanded = false;
        RenderExpansion();

        Frame.Navigate(typeof(ReferenceEditorPage), new ReferenceEditorArgs(_services, profile));
    }

    /// <summary>
    /// Уводит на экран приёмки.
    /// </summary>
    /// <remarks>
    /// Порт передаётся явно: приёмка держит его монопольно всю процедуру, и
    /// наблюдательное соединение рабочего экрана она закроет сама. Азимут не
    /// передаётся — курс борта вводится там же, в момент калибровки компаса.
    /// </remarks>
    private void OnStartAcceptanceClick(object sender, RoutedEventArgs e)
    {
        if (_selectedReference is not { } reference || _services.ConnectedPort is not { } port)
        {
            return;
        }

        Frame.Navigate(typeof(AcceptancePage), new AcceptanceArgs(_services, reference, port));
    }

    private Button CreateTile(string title, string detail, Action onClick)
    {
        var captionStyle = (Style)Application.Current.Resources["CaptionTextBlockStyle"];

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Style = captionStyle,
        });

        if (!string.IsNullOrWhiteSpace(detail))
        {
            text.Children.Add(new TextBlock
            {
                Text = detail,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                Style = captionStyle,
            });
        }

        var button = new Button
        {
            Content = text,
            Style = (Style)Resources["TileStyle"],
        };

        button.Click += (_, _) => onClick();
        return button;
    }

    private void OnPortCardClick(object sender, RoutedEventArgs e)
    {
        _portsExpanded = !_portsExpanded;

        if (_portsExpanded)
        {
            _referencesExpanded = false;
            BuildTiles(
                PortsAbove,
                PortsBelow,
                _ports,
                _selectedPort,
                static p => p.Caption,
                static p => p.Details,
                async p => await PickPortAsync(p).ConfigureAwait(true));
        }

        RenderExpansion();
    }

    private async Task PickPortAsync(SerialPortDescription port)
    {
        _selectedPort = port;
        _portsExpanded = false;
        RenderExpansion();
        RenderAll();

        await PersistSettingsAsync().ConfigureAwait(true);

        // Нажатие на плашку — это уже действие: отдельной кнопки «подключить» нет.
        await ConnectAsync().ConfigureAwait(true);
    }

    private void OnReferenceCardClick(object sender, RoutedEventArgs e)
    {
        _referencesExpanded = !_referencesExpanded;

        if (_referencesExpanded)
        {
            _portsExpanded = false;
            BuildTiles(
                ReferencesAbove,
                ReferencesBelow,
                _references,
                _selectedReference,
                static p => p.Name,
                static p => ReferenceCaption.Describe(p),
                PickReference,
                putPreviousAbove: false);

            ReferencesAbove.Children.Add(CreateAddReferenceTile());
        }

        RenderExpansion();
    }

    private async void PickReference(CalibrationReference profile)
    {
        _selectedReference = profile;
        _referencesExpanded = false;
        Log($"Выбран эталон «{profile.Name}».");
        RenderExpansion();
        RenderAll();
        await PersistSettingsAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Открывает или закрывает раскрытия. Списки живут в <c>Popup</c>, поэтому
    /// не занимают места в разметке и не двигают остальные панели окна.
    /// </summary>
    private void RenderExpansion()
    {
        PlacePopup(_portsExpanded, PortPopup, PortPopupRoot, PortCard, FcSelector, PortsAbove, PortSpacer);
        PlacePopup(_referencesExpanded, ReferencePopup, ReferencePopupRoot, ReferenceCard, ReferenceSelector, ReferencesAbove, ReferenceSpacer);
    }

    /// <summary>
    /// Ставит всплывающий слой так, чтобы карточка осталась на своём месте:
    /// плашки «выше» уходят над ней, «ниже» — под неё, а прозрачная прокладка
    /// занимает ровно место самой карточки.
    /// </summary>
    private static void PlacePopup(
        bool open,
        Popup popup,
        FrameworkElement root,
        FrameworkElement card,
        UIElement origin,
        FrameworkElement above,
        FrameworkElement spacer)
    {
        if (!open)
        {
            popup.IsOpen = false;
            return;
        }

        var width = card.ActualWidth;
        if (width <= 0)
        {
            // Карточка ещё не измерена — открывать нечего и не от чего считать.
            popup.IsOpen = false;
            return;
        }

        root.Width = width;
        spacer.Height = card.ActualHeight;

        // Высота верхней части нужна до показа: на неё поднимается весь слой.
        above.Measure(new Size(width, double.PositiveInfinity));

        var offset = card.TransformToVisual(origin).TransformPoint(new Point(0, 0));
        popup.HorizontalOffset = offset.X;
        popup.VerticalOffset = offset.Y - above.DesiredSize.Height;
        popup.IsOpen = true;
    }

    // --- Порты и связь -----------------------------------------------------

    private async Task ReloadPortsAsync()
    {
        try
        {
            _ports = await SerialPortCatalog.ListAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowLinkMessage(InfoBarSeverity.Warning, "Не удалось перечислить порты: " + ex.Message);
            return;
        }

        if (_selectedPort is not null)
        {
            _selectedPort = _ports.FirstOrDefault(p =>
                string.Equals(p.PortName, _selectedPort.PortName, StringComparison.OrdinalIgnoreCase));
        }

        _selectedPort ??= PickCandidates(_ports) is [var only] ? only : null;

        RenderAll();
    }

    private async void OnAutoConnectChanged(object sender, RoutedEventArgs e)
    {
        await PersistSettingsAsync().ConfigureAwait(true);

        if (AutoConnectCheck.IsChecked == true && !_services.IsLinkConnected)
        {
            await TryAutoConnectAsync().ConfigureAwait(true);
        }
    }

    private async Task TryAutoConnectAsync()
    {
        if (_ports.Count == 0)
        {
            ShowLinkMessage(InfoBarSeverity.Informational, "COM-портов не найдено — подключите борт кабелем.");
            return;
        }

        var candidates = PickCandidates(_ports);
        if (candidates.Count != 1)
        {
            ShowLinkMessage(
                InfoBarSeverity.Informational,
                candidates.Count == 0
                    ? "Плата ArduPilot среди портов не опознана — выберите порт вручную."
                    : "Опознано несколько плат — выберите нужную вручную.");
            return;
        }

        _selectedPort = candidates[0];
        await ConnectAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Отбирает порты, на которых может отвечать MAVLink.
    /// </summary>
    /// <remarks>
    /// Композитные платы F7/H7 показывают два CDC-порта с одинаковыми VID/PID:
    /// на нулевом интерфейсе живёт MAVLink, на втором — SLCAN.
    /// </remarks>
    private static List<SerialPortDescription> PickCandidates(IReadOnlyList<SerialPortDescription> ports)
    {
        var candidates = ports.Where(static p => p.LooksLikeArduPilot && !p.IsBootloader).ToList();

        if (candidates.Count > 1)
        {
            var primary = candidates.Where(static p => p.UsbInterface is null or 0).ToList();
            if (primary.Count > 0)
            {
                return primary;
            }
        }

        return candidates;
    }

    private async Task ConnectAsync()
    {
        if (_selectedPort is not { } port || _busy)
        {
            return;
        }

        SetBusy(true);
        LinkBar.IsOpen = false;

        try
        {
            await _services.ConnectAsync(port.PortName).ConfigureAwait(true);
            Log($"Установлена связь с бортом на {port.PortName}.");
        }
        catch (Exception ex)
        {
            ShowLinkMessage(InfoBarSeverity.Error, ex.Message);
            Log($"Подключение к {port.PortName} не удалось: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            RenderAll();
        }
    }

    private void OnLinkChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(RenderAll);

    private void ShowLinkMessage(InfoBarSeverity severity, string message)
    {
        LinkBar.Severity = severity;
        LinkBar.Message = message;
        LinkBar.IsOpen = true;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        LinkRing.IsActive = busy;
    }

    // --- Эталоны -----------------------------------------------------------

    private async Task ReloadReferencesAsync()
    {
        try
        {
            var profiles = await _services.LoadReferencesAsync().ConfigureAwait(true);
            _references = profiles.Where(static p => !p.IsRetired).ToList();

            if (_selectedReference is not null)
            {
                _selectedReference = _references.FirstOrDefault(p => p.Id == _selectedReference.Id);
            }

            _selectedReference ??= _settings.LastReferenceId is { } lastId
                ? _references.FirstOrDefault(p => p.Id == lastId)
                : null;

            _selectedReference ??= _references.Count == 1 ? _references[0] : null;

            if (_references.Count == 0)
            {
                ReferenceBar.Severity = InfoBarSeverity.Warning;
                ReferenceBar.Message =
                    "Эталонов нет — запустить калибровку нечем. Нажмите панель эталона, чтобы завести первый.";
                ReferenceBar.IsOpen = true;
            }
            else
            {
                ReferenceBar.IsOpen = false;
            }
        }
        catch (Exception ex)
        {
            ReferenceBar.Severity = InfoBarSeverity.Error;
            ReferenceBar.Message = "Не удалось прочитать список эталонов: " + ex.Message;
            ReferenceBar.IsOpen = true;
        }
    }

    // --- Отрисовка ---------------------------------------------------------

    private void RenderAll()
    {
        var connected = _services.IsLinkConnected;

        PortCardTitle.Text = _selectedPort?.Caption ?? "Порт не выбран";
        PortCardDetail.Text = connected
            ? $"связь есть · {_services.ConnectedFirmware ?? "версия прошивки не получена"}"
            : _selectedPort?.Details is { Length: > 0 } d
                ? d
                : "Нажмите, чтобы выбрать";

        // Связь показывается подсветкой снизу и акцентным кантом слева. Два
        // признака, а не один: подсветка — мягкий градиент, на светлой теме и на
        // дешёвой цеховой матрице она видна хуже, чем хотелось бы.
        // Подсветка снизу горит всегда: зелёная — готово, красная — не заполнено.
        // Погашенная панель читалась как «ещё не дошли руки», хотя на деле это
        // незавершённая подготовка стенда.
        PortGlow.Visibility = Visibility.Visible;
        PortGlow.Background = (Brush)Resources[connected ? "GlowBrush" : "GlowDangerBrush"];
        PortCard.Style = (Style)Resources[connected ? "ActiveTileStyle" : "DangerTileStyle"];

        ReferenceCardTitle.Text = _selectedReference?.Name ?? "Эталон не выбран";
        ReferenceCardDetail.Text = _selectedReference is { } profile
            ? ReferenceCaption.Describe(profile)
            : _references.Count == 0
                ? "Нажмите, чтобы добавить первый"
                : "Нажмите, чтобы выбрать";

        var hasProfile = _selectedReference is not null;
        ReferenceGlow.Visibility = Visibility.Visible;
        ReferenceGlow.Background = (Brush)Resources[hasProfile ? "GlowBrush" : "GlowDangerBrush"];
        ReferenceCard.Style = (Style)Resources[hasProfile ? "ActiveTileStyle" : "DangerTileStyle"];

        // Править нечего, пока эталон не выбран: карандаш без цели только
        // предлагает нажать и получить отказ.
        EditReferenceButton.Visibility = hasProfile ? Visibility.Visible : Visibility.Collapsed;

        var problems = new List<string>();
        if (!connected)
        {
            problems.Add("нет связи с полётником");
        }

        if (_selectedReference is null)
        {
            problems.Add("не выбран эталон");
        }

        if (problems.Count == 0)
        {
            ReadyBar.Severity = InfoBarSeverity.Success;
            ReadyBar.Message = "Борт на связи, эталон выбран. Можно запускать приёмку.";
        }
        else
        {
            ReadyBar.Severity = InfoBarSeverity.Informational;
            ReadyBar.Message = "Не готово: " + string.Join(", ", problems) + ".";
        }

        StartAcceptanceButton.IsEnabled = problems.Count == 0;

        // Индикатор работает от связи с бортом и не ждёт выбора эталона:
        // приборы нужны оператору раньше, чем эталон.
        if (connected && !_hudTimer.IsEnabled)
        {
            _hudTimer.Start();
        }
        else if (!connected && _hudTimer.IsEnabled)
        {
            _hudTimer.Stop();
            Compasses.Clear();
        }

        RenderHud();
    }

    private void Log(string message) => DispatcherQueue.TryEnqueue(() =>
    {
        LogEntries.Insert(0, new CalibrationLogRow(MavSeverity.Info, message, DateTimeOffset.Now));
        while (LogEntries.Count > 300)
        {
            LogEntries.RemoveAt(LogEntries.Count - 1);
        }
    });

    private void OnClearLogClick(object sender, RoutedEventArgs e) => LogEntries.Clear();


    private void OnHudTick(object? sender, object e) => RenderHud();

    // --- Указатель положения -----------------------------------------------

    /// <summary>Масштаб шкалы тангажа, пикселей на градус.</summary>
    /// <remarks>
    /// Крупный масштаб выбран намеренно: в окне высотой 170 точек он оставляет
    /// в поле зрения ±21°, то есть три-четыре ступени. Мелкий масштаб набивал
    /// поле десятком линий, и шкала переставала читаться.
    /// </remarks>
    private const double PitchScale = 4.0;

    private static Brush Ink => (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];

    private static Brush InkDim => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    private void OnHudSizeChanged(object sender, SizeChangedEventArgs e) => BuildHudGeometry();

    /// <summary>
    /// Перестраивает шкалу под текущий размер. Вызывается на изменение размера,
    /// а не на каждый кадр: по таймеру меняются только два преобразования.
    /// </summary>
    private void BuildHudGeometry()
    {
        var w = HudViewport.ActualWidth;
        var h = HudViewport.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        HudClip.Rect = new Rect(0, 0, w, h);
        BuildPitchLadder(w, h);
        BuildMarker(w, h);
    }

    /// <summary>Шкала тангажа: горизонт и ступени через 10°, подписи слева.</summary>
    private void BuildPitchLadder(double w, double h)
    {
        LadderHost.Children.Clear();

        var cx = w / 2;
        var cy = h / 2;

        // Крен вращает шкалу вокруг точки визирования, а не вокруг угла холста.
        RollRotate.CenterX = cx;
        RollRotate.CenterY = cy;

        const double Gap = 40;
        const double BarLength = 46;

        for (var deg = -40; deg <= 40; deg += 10)
        {
            var y = cy - (deg * PitchScale);

            if (deg == 0)
            {
                // Горизонт — сплошная линия во всю ширину с разрывом под маркером.
                AddLine(cx - Math.Max(cx, 0), y, cx - 22, y, Ink, 2);
                AddLine(cx + 22, y, cx + Math.Max(cx, 0) + w, y, Ink, 2);
                continue;
            }

            AddLine(cx - Gap - BarLength, y, cx - Gap, y, InkDim, 1.6, dashed: deg < 0);
            AddLine(cx + Gap, y, cx + Gap + BarLength, y, InkDim, 1.6, dashed: deg < 0);

            // Засечки на внутренних концах смотрят к горизонту.
            var tickDir = deg > 0 ? 6 : -6;
            AddLine(cx - Gap, y, cx - Gap, y + tickDir, InkDim, 1.6);
            AddLine(cx + Gap, y, cx + Gap, y + tickDir, InkDim, 1.6);

            AddLadderLabel(cx - Gap - BarLength - 26, y, deg);
        }
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness, bool dashed = false)
    {
        var line = new Line
        {
            X1 = x1,
            X2 = x2,
            Y1 = y1,
            Y2 = y2,
            Stroke = stroke,
            StrokeThickness = thickness,
        };

        // Пикирование — пунктиром: отличается от набора не только положением.
        if (dashed)
        {
            line.StrokeDashArray = [4, 3];
        }

        LadderHost.Children.Add(line);
    }

    private void AddLadderLabel(double x, double y, int deg)
    {
        var label = new TextBlock
        {
            Text = Math.Abs(deg).ToString(CultureInfo.InvariantCulture),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Foreground = InkDim,
        };

        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y - 10);
        LadderHost.Children.Add(label);
    }

    /// <summary>Неподвижный маркер положения: крупный, чтобы читался сразу.</summary>
    private void BuildMarker(double w, double h)
    {
        FixedHost.Children.Clear();

        var cx = w / 2;
        var cy = h / 2;
        var accent = Ink;

        foreach (var sign in new[] { -1, 1 })
        {
            FixedHost.Children.Add(new Polyline
            {
                Points =
                [
                    new Point(cx + (sign * 14), cy),
                    new Point(cx + (sign * 40), cy),
                    new Point(cx + (sign * 40), cy + 10),
                ],
                Stroke = accent,
                StrokeThickness = 2.5,
            });
        }

        FixedHost.Children.Add(new Ellipse
        {
            Width = 5,
            Height = 5,
            Fill = accent,
            Margin = new Thickness(cx - 2.5, cy - 2.5, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        });
    }

    private void RenderHud()
    {
        var state = _services.LiveState;
        if (state is null)
        {
            HudArmedText.Text = "Нет данных: борт не на связи";
            HudArmedText.Foreground = InkDim;
            return;
        }

        if (state.Attitude is { } attitude)
        {
            var rollDeg = attitude.RollRad * 180.0 / Math.PI;
            var pitchDeg = attitude.PitchRad * 180.0 / Math.PI;
            var yawDeg = ((attitude.YawRad * 180.0 / Math.PI) + 360.0) % 360.0;

            RollRotate.Angle = -rollDeg;
            PitchTranslate.Y = pitchDeg * PitchScale;

            HudRollText.Text = string.Create(CultureInfo.InvariantCulture, $"{rollDeg:+0.0;-0.0;0.0}°");
            HudPitchText.Text = string.Create(CultureInfo.InvariantCulture, $"{pitchDeg:+0.0;-0.0;0.0}°");
            HudYawText.Text = string.Create(CultureInfo.InvariantCulture, $"{yawDeg:000}°");
        }

        // Сентинелы уже отсеяны каналом: пусто означает «борт не сообщает».
        HudVoltageText.Text = state.VoltageVolts is { } v
            ? string.Create(CultureInfo.InvariantCulture, $"{v:0.00} В")
            : "нет";

        HudCurrentText.Text = state.CurrentAmperes is { } a
            ? string.Create(CultureInfo.InvariantCulture, $"{a:0.00} А")
            : "нет";

        HudModeText.Text = state.ModeName;

        // Взведённые двигатели — предупреждающим цветом и словом, не одним цветом.
        HudArmedText.Text = state.Armed ? "ВЗВЕДЁН" : "Обезврежен";
        HudArmedText.Foreground = state.Armed
            ? (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            : InkDim;

        RenderGps(state);
        RenderCompasses(state);
    }

    /// <summary>
    /// Состояние GPS: вид решения, спутники, геометрия, координаты.
    /// </summary>
    /// <remarks>
    /// 🔴 Три состояния разведены и не сливаются в один прочерк: «сообщений не
    /// было» (судить рано), «приёмника нет» (изделие негодно) и «решения нет»
    /// (ждать или искать небо). Одинаковый прочерк на все три заставлял бы
    /// оператора гадать, что делать дальше.
    /// </remarks>
    private void RenderGps(VehicleLiveState state)
    {
        if (state.Gps is not { } gps)
        {
            HudGpsFixText.Text = "сообщений не было";
            HudGpsFixText.Foreground = InkDim;
            HudGpsSatsText.Text = "—";
            HudGpsHdopText.Text = "—";
            HudGpsAltText.Text = "—";
            HudGpsPositionText.Text = "координат нет";
            return;
        }

        HudGpsFixText.Text = gps.FixTypeText;
        HudGpsFixText.Foreground = gps.Is3D
            ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
            : (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];

        HudGpsSatsText.Text = gps.SatellitesVisible.ToString(CultureInfo.InvariantCulture);

        // Неизвестная геометрия показывается словом. Приведённая к числу, она
        // выглядела бы либо идеальной, либо чудовищной — и то и другое ложь.
        HudGpsHdopText.Text = gps.Hdop is { } hdop
            ? string.Create(CultureInfo.InvariantCulture, $"{hdop:0.00}")
            : "нет";

        HudGpsAltText.Text = gps.Is3D
            ? string.Create(CultureInfo.InvariantCulture, $"{gps.AltitudeMeters:0} м")
            : "нет";

        // Координаты без решения — это последнее известное или ноль; выдавать
        // их за положение борта нельзя.
        HudGpsPositionText.Text = gps.Is3D
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{gps.LatitudeDeg:F7}, {gps.LongitudeDeg:F7}")
            : "координат нет: решение не трёхмерное";
    }

    private void RenderCompasses(VehicleLiveState state)
    {
        var samples = state.Mags
            .Where(static m => !m.IsEmpty)
            .OrderBy(static m => m.Instance)
            .ToList();

        if (samples.Count != Compasses.Count)
        {
            Compasses.Clear();
            foreach (var sample in samples)
            {
                Compasses.Add(new CompassRow(sample));
            }
        }
        else
        {
            for (var i = 0; i < samples.Count; i++)
            {
                Compasses[i] = new CompassRow(samples[i]);
            }
        }

        CompassHintText.Text = samples.Count == 0 ? "Показаний магнитометров пока нет." : string.Empty;
    }
}
