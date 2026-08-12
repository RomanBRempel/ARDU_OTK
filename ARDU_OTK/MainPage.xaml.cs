using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI;
using Windows.UI;
using Windows.Foundation;

namespace ARDU_OTK;

/// <summary>Строка панели компасов.</summary>
/// <remarks>
/// 🔴 Строка обновляется на месте, а не пересоздаётся. Показания приходят
/// несколько раз в секунду, и подмена объекта в списке заставляла список
/// пересобирать контейнер на каждом такте: панель мерцала, а добавленные
/// анимации появления это подчёркивали. Уведомления об изменении свойств
/// меняют только текст, оставляя контейнер на месте.
/// </remarks>
public sealed class CompassRow : INotifyPropertyChanged
{
    /// <summary>Норма модуля магнитного поля, мГс, и границы предполётной проверки.</summary>
    public const double FieldExpected = 530;

    public const double FieldMin = 185;

    public const double FieldMax = 875;

    /// <param name="sample">Показания магнитометра из телеметрии.</param>
    /// <param name="identity">
    /// Опознание датчика: внешний или внутренний и что за железо.
    /// <c>null</c> — параметры опознания ещё не прочитаны либо борт их не отдал.
    /// </param>
    /// <remarks>
    /// 🔴 «Внешность» приходит не из телеметрии, а из параметров
    /// <c>COMPASS_DEV_ID*</c> и <c>COMPASS_EXTERNAL*</c>. Судить о ней по
    /// номеру слота запрещено: порядок слотов задаётся таблицей приоритетов и
    /// меняется при перестановке, так что «первый — значит внешний» неверно
    /// ровно на тех бортах, ради которых проверка и существует.
    /// </remarks>
    public CompassRow(MagSample sample, CompassIdentityRow? identity = null)
    {
        Instance = sample.Instance;
        Title = $"Компас {sample.Instance + 1}";
        Update(sample, identity);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Номер экземпляра телеметрии, с нуля. По нему строка опознаётся при перестановке.</summary>
    public int Instance { get; }

    public string Title { get; }

    public string Detail { get; private set; } = string.Empty;

    public string FieldText { get; private set; } = string.Empty;

    /// <summary>Внешний, внутренний либо «судить нельзя» — словом.</summary>
    public string KindText { get; private set; } = string.Empty;

    /// <summary>Что за датчик: шина, адрес, тип. Пусто, если опознание не прочитано.</summary>
    public string DeviceText { get; private set; } = string.Empty;

    public Visibility OkVisibility { get; private set; }

    public Visibility WarnVisibility { get; private set; }

    public Visibility ExternalVisibility { get; private set; }

    public Visibility InternalVisibility { get; private set; }

    public Visibility UnknownKindVisibility { get; private set; }

    /// <summary>
    /// Обновляет показания и опознание, не пересоздавая строку.
    /// </summary>
    /// <remarks>
    /// Уведомление рассылается только по действительно изменившимся свойствам:
    /// показания магнитометра дрожат в последнем разряде, а метка «внешний» и
    /// описание датчика между тактами не меняются вовсе, и перерисовывать их
    /// несколько раз в секунду незачем.
    /// </remarks>
    public void Update(MagSample sample, CompassIdentityRow? identity)
    {
        var magnitude = sample.Magnitude;
        var inBand = magnitude is >= FieldMin and <= FieldMax;

        var detail = string.Create(CultureInfo.InvariantCulture, $"X {sample.X}  Y {sample.Y}  Z {sample.Z}");
        if (!inBand)
        {
            detail += magnitude < FieldMin ? "  · поле слабее нормы" : "  · поле сильнее нормы";
        }

        var field = string.Create(CultureInfo.InvariantCulture, $"{magnitude:0}");
        var kind = identity?.KindText ?? "опознание не прочитано";
        var device = identity?.DeviceText ?? string.Empty;

        if (Detail != detail) { Detail = detail; Raise(nameof(Detail)); }
        if (FieldText != field) { FieldText = field; Raise(nameof(FieldText)); }
        if (KindText != kind) { KindText = kind; Raise(nameof(KindText)); }
        if (DeviceText != device) { DeviceText = device; Raise(nameof(DeviceText)); }

        // Внешний, внутренний и «судить нельзя» окрашены по-разному: последнее
        // не должно выглядеть как исправное состояние.
        SetVisibility(identity?.IsExternal == true, ExternalVisibility,
            v => ExternalVisibility = v, nameof(ExternalVisibility));
        SetVisibility(identity?.IsExternal == false, InternalVisibility,
            v => InternalVisibility = v, nameof(InternalVisibility));
        SetVisibility(identity?.IsExternal is null, UnknownKindVisibility,
            v => UnknownKindVisibility = v, nameof(UnknownKindVisibility));

        SetVisibility(inBand, OkVisibility, v => OkVisibility = v, nameof(OkVisibility));
        SetVisibility(!inBand, WarnVisibility, v => WarnVisibility = v, nameof(WarnVisibility));
    }

    private void SetVisibility(bool visible, Visibility current, Action<Visibility> assign, string name)
    {
        var wanted = visible ? Visibility.Visible : Visibility.Collapsed;
        if (current == wanted)
        {
            return;
        }

        assign(wanted);
        Raise(name);
    }

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Опознание одного компаса: внешний или внутренний и что за железо.
/// </summary>
/// <remarks>
/// Читается с борта один раз на соединение и не пересчитывается по каждому
/// кадру телеметрии: <c>COMPASS_DEV_ID*</c> меняется только при перестановке
/// слотов, а чтение параметров на каждом такте индикатора забило бы канал.
/// </remarks>
/// <param name="Slot">Номер слота 1..3.</param>
/// <param name="IsExternal">Внешний компас. <c>null</c> — судить нельзя.</param>
/// <param name="KindText">Русское название вида.</param>
/// <param name="DeviceText">Описание датчика.</param>
public sealed record CompassIdentityRow(int Slot, bool? IsExternal, string KindText, string DeviceText);

/// <summary>
/// Строка отображаемого параметра: то, что технолог поднял в секцию
/// «контроль и показ» настройками эталона.
/// </summary>
/// <remarks>
/// 🔴 Панель существует затем, чтобы решение технолога было видно на рабочем
/// экране. Раскладка, которую можно задать в мастере эталона и нигде не
/// увидеть при сдаче, — это настройка ни на что не влияющая: оператор о ней не
/// знает, и поднятые имена ничем не отличаются от скрытых.
/// </remarks>
public sealed class WatchedParameterRow
{
    public WatchedParameterRow(
        string name,
        double expected,
        double? actual,
        bool matches,
        ParameterEnums.VehicleClass vehicle = ParameterEnums.VehicleClass.Unknown)
    {
        Name = name;

        // Перечислимые значения — именем, как в Mission Planner. Число при
        // этом остаётся на виду: по нему параметр вводят руками и сверяют с
        // документацией.
        ExpectedText = ParameterEnums.Format(
            name, expected, vehicle, ReferenceParamFile.FormatCanonical(expected));

        ActualText = actual is { } value
            ? ParameterEnums.Format(
                name, value, vehicle, string.Create(CultureInfo.InvariantCulture, $"{value:G9}"))
            : "нет на борту";

        MatchVisibility = matches ? Visibility.Visible : Visibility.Collapsed;
        DiffVisibility = matches ? Visibility.Collapsed : Visibility.Visible;
    }

    public string Name { get; }

    public string ExpectedText { get; }

    public string ActualText { get; }

    public Visibility MatchVisibility { get; }

    public Visibility DiffVisibility { get; }
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

        BuildTickAnimation();

        _services.LinkChanged += OnLinkChanged;
        _hudTimer.Tick += OnHudTick;
        _portsTimer.Tick += OnPortsTick;

        // Закрытие щелчком мимо — это тоже закрытие: без синхронизации флага
        // следующее нажатие на карточку «схлопнуло» бы уже закрытый список.
        PortFlyout.Closed += (_, _) => _portsExpanded = false;
        ReferenceFlyout.Closed += (_, _) => _referencesExpanded = false;
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
        // Проверка связи идёт до всех прочих условий: она нужна и при
        // раскрытом списке портов, и во время приёмки.
        SyncConnectionState();

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

    // --- Приёмка прямо на рабочем экране ------------------------------------

    /// <summary>Расхождения последней сверки.</summary>
    /// <remarks>
    /// Приёмка живёт на рабочем экране, а не в отдельном окне: оператор у
    /// стенда смотрит на приборы, компасы и расхождения одновременно, и
    /// отдельное окно заставляло бы его переключаться между ними в момент,
    /// когда он решает судьбу платы.
    /// </remarks>
    public ObservableCollection<ParameterDifferenceRow> Differences { get; } = new();

    /// <summary>Параметры, которые эталон помечен показывать оператору.</summary>
    public ObservableCollection<WatchedParameterRow> Watched { get; } = new();

    /// <summary>Сверка скриптов изделия.</summary>
    public ObservableCollection<ScriptDifferenceRow> ScriptDiffs { get; } = new();

    /// <summary>
    /// Класс аппарата по HEARTBEAT. Нужен расшифровке полётных режимов.
    /// </summary>
    /// <remarks>
    /// 🔴 Пока борт не отозвался, класс неизвестен, и режимы показываются
    /// числами. Подставить самолётную таблицу «по умолчанию» нельзя: у коптера
    /// нулевой режим — Stabilize, у самолёта — Manual, и уверенная подпись не
    /// того режима хуже честного числа.
    /// </remarks>
    private ParameterEnums.VehicleClass VehicleClass =>
        _services.LiveState is { } state
            ? ParameterEnums.Classify(state.VehicleType)
            : ParameterEnums.VehicleClass.Unknown;

    /// <summary>
    /// Расхождения по каналам и выходам, ждущие своей сводной строки.
    /// </summary>
    /// <remarks>
    /// Заполняется сверкой, разбирается отрисовкой наблюдаемых: канал обязан
    /// попасть на экран одной строкой, а расхождение и совпавшие поля приходят
    /// из разных источников.
    /// </remarks>
    private readonly Dictionary<string, ParameterDifference> _channelDifferences = new(StringComparer.Ordinal);

    private AcceptanceSession? _session;
    private CancellationTokenSource? _acceptanceCts;
    private bool _acceptanceBusy;
    private bool _diffAccepted;

    /// <summary>
    /// Открытый прогон в реестре. <c>0</c> — прогон не открыт.
    /// </summary>
    /// <remarks>
    /// 🔴 Прогон открывается в начале приёмки и закрывается её итогом. Открытая
    /// строка в реестре хуже строки с отказом: она блокирует обновление
    /// приложения и врёт про состояние стенда, поэтому закрытие идёт даже при
    /// сбое и при отмене.
    /// </remarks>
    private long _runId;

    private DateTimeOffset _runStartedUtc;

    private readonly List<CheckResult> _runChecks = new();

    private void OnUnitIdChanged(object sender, TextChangedEventArgs e) => RenderAll();

    private void OnStartAcceptanceClick(object sender, RoutedEventArgs e)
    {
        if (_acceptanceBusy)
        {
            _acceptanceCts?.Cancel();
            return;
        }

        _ = RunAcceptanceAsync();
    }

    /// <summary>
    /// Автоматическая часть приёмки: сверка, очередь компасов, перенос,
    /// перезагрузка, повторная сверка.
    /// </summary>
    /// <remarks>
    /// 🔴 Останавливается на показе расхождений и дальше не идёт. Развилку
    /// «править или принять» проходит человек: автомат, решающий за него, либо
    /// останавливает годное изделие, либо пропускает негодное.
    /// </remarks>
    private async Task RunAcceptanceAsync()
    {
        if (_selectedReference is not { } reference || _services.ConnectedPort is not { } port)
        {
            return;
        }

        SetAcceptanceBusy(true);
        Differences.Clear();
        _diffAccepted = false;

        CompareCountsPanel.Visibility = Visibility.Collapsed;
        CompareActionsPanel.Visibility = Visibility.Collapsed;
        CompareProgress.Visibility = Visibility.Visible;
        CompareProgress.IsIndeterminate = true;

        _acceptanceCts = new CancellationTokenSource();
        var ct = _acceptanceCts.Token;

        var progress = new Progress<string>(text =>
        {
            CompareStateText.Text = text;
            AnimateCompareProgress(text);
        });

        var detail = new Progress<ParameterProgress>(ShowReadTick);
        var aborted = false;

        try
        {
            await CloseSessionAsync().ConfigureAwait(true);

            // Прогон открывается в реестре до касания железа: строка о прогоне,
            // который начали, обязана существовать даже если он оборвётся на
            // первом же шаге.
            await OpenRunAsync(reference).ConfigureAwait(true);

            AppendLog(MavSeverity.Info, "Приёмка: открываю канал связи…");
            _session = await _services.BeginAcceptanceAsync(port, ct).ConfigureAwait(true);

            var expected = reference.ParseParameters().Values;
            var roles = reference.ToRoleMap();

            CompareStateText.Text = "Вычитываю прошивку борта…";
            var plan = await _session.CompareAsync(expected, roles, progress, ct, detail).ConfigureAwait(true);
            AppendLog(MavSeverity.Info,
                $"Сверка: сопоставлено {plan.Compared}, совпало {plan.Matched}, расходится {plan.Differences.Count}.");

            SetCompassBusy(true, "очередь компасов…");
            var primary = await _session.MakeExternalCompassPrimaryAsync(progress, ct).ConfigureAwait(true);
            SetCompassBusy(false);

            AppendLog(
                primary.IsWarning ? MavSeverity.Warning : primary.Ok ? MavSeverity.Info : MavSeverity.Error,
                primary.Message);

            if (!primary.Ok)
            {
                // Останавливаемся только на настоящем отказе: борт не принял
                // приоритеты либо не вернулся после перезагрузки. Отсутствие
                // компасов отказом не считается — оно приходит предупреждением
                // и приёмку не прерывает.
                ShowCompare(plan, "Приёмка остановлена: " + primary.Message);
                return;
            }

            // Опознание компасов после перестановки устарело — перечитываем.
            await LoadCompassIdentityAsync().ConfigureAwait(true);

            // 🔴 Перезагрузка — не обряд, а следствие записи. Если ничего не
            // записано, перезагружать нечего: борт и так в том состоянии, в
            // котором его перечитают. Лишняя перезагрузка стоит полминуты,
            // рвёт связь и заставляет оператора ждать без причины.
            var written = 0;
            if (plan.Writable.Count > 0)
            {
                CompareStateText.Text = $"Переношу расхождения: {plan.Writable.Count}…";
                var records = await _session.ApplyAsync(plan.Writable, progress, ct).ConfigureAwait(true);
                written = records.Count(static r => r.Outcome == WriteOutcome.Verified);
                AppendLog(MavSeverity.Info, $"Перенос: записано {written}, отклонено {records.Count - written}.");
            }
            else
            {
                AppendLog(MavSeverity.Info, "Переносить нечего: расхождений по записываемым именам нет.");
            }

            if (written > 0)
            {
                var reboot = await _session.RebootAsync(progress, ct).ConfigureAwait(true);
                AppendLog(reboot.Ok ? MavSeverity.Info : MavSeverity.Error, reboot.Message);
                if (!reboot.Ok)
                {
                    ShowCompare(plan, "Приёмка остановлена: " + reboot.Message);
                    return;
                }
            }
            else
            {
                AppendLog(MavSeverity.Info, "Перезагрузка пропущена: на борт ничего не записано.");
            }

            CompareStateText.Text = "Повторная сверка после перезагрузки…";
            var after = await _session.CompareAsync(expected, roles, progress, ct, detail).ConfigureAwait(true);
            await LoadCompassIdentityAsync().ConfigureAwait(true);

            ShowCompare(after, after.IsClean
                ? "Борт совпал с эталоном по всем контролируемым параметрам."
                : $"Осталось расхождений: {after.Differences.Count}. Исправьте записью или примите как есть — "
                  + "до этого калибровки заблокированы.");

            AppendLog(MavSeverity.Info, $"Повторная сверка: расхождений {after.Differences.Count}.");

            await CompareScriptsAsync(reference, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            CompareStateText.Text = "Приёмка отменена оператором.";
            AppendLog(MavSeverity.Warning, "Приёмка отменена оператором. Состояние борта могло остаться изменённым.");
            aborted = true;
        }
        catch (Exception ex)
        {
            CompareStateText.Text = "Сбой: " + ex.Message;
            AppendLog(MavSeverity.Error, $"Приёмка прервана: {ex.GetType().Name}: {ex.Message}");
            aborted = true;
        }
        finally
        {
            _acceptanceCts?.Dispose();
            _acceptanceCts = null;
            SetCompassBusy(false);
            SetAcceptanceBusy(false);

            // 🔴 Оборванная приёмка обязана отпустить порт. Иначе сессия держит
            // его монопольно, автоподключение молчит по её вине, и связь не
            // вернётся до перезапуска приложения — при подключённом борте на
            // столе. Успешно прошедшая автоматическая часть порт сохраняет:
            // дальше по нему пойдут команды оператора.
            if (aborted)
            {
                await CloseRunAsync(false, "Приёмка прервана.").ConfigureAwait(true);
                await ReleaseAcceptanceAsync().ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    /// Закрывает приёмку и возвращает наблюдательное соединение.
    /// </summary>
    /// <remarks>
    /// 🔴 Возврат обязателен и не может быть оставлен автоподключению. Порт всю
    /// приёмку принадлежал сессии, автоподключение при этом намеренно молчало,
    /// и после освобождения порта его никто не разбудит: приборы остались бы
    /// мёртвыми, а «Старт приёмки» — заблокированным по причине «нет связи»,
    /// хотя борт стоит на столе подключённым.
    /// </remarks>
    private async Task ReleaseAcceptanceAsync()
    {
        var port = _session?.PortName;
        await CloseSessionAsync().ConfigureAwait(true);

        if (string.IsNullOrEmpty(port))
        {
            return;
        }

        try
        {
            AppendLog(MavSeverity.Info, "Приёмка закрыта, возвращаю наблюдательное соединение…");

            // Имя порта после перезагрузок могло смениться, поэтому сначала
            // пробуем то, на котором приёмка закончилась, а затем общий отбор.
            await _services.ConnectAsync(port).ConfigureAwait(true);
        }
        catch (Exception)
        {
            await ReloadPortsAsync().ConfigureAwait(true);
            await TryAutoConnectAsync().ConfigureAwait(true);
        }

        RenderAll();

        if (_services.IsLinkConnected)
        {
            await LoadCompassIdentityAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Двигает полосу по сообщениям о ходе чтения.
    /// </summary>
    /// <remarks>
    /// Полоса заполняется по числам «принято N из M» из самого канала, а не по
    /// таймеру: выдуманный ход, не связанный с работой, показывает движение
    /// там, где его нет, и это хуже неподвижной полосы.
    /// </remarks>
    private void AnimateCompareProgress(string text)
    {
        var of = text.IndexOf(" из ", StringComparison.Ordinal);
        if (of < 0)
        {
            CompareProgress.IsIndeterminate = true;
            return;
        }

        var head = text[..of];
        var digitsStart = head.LastIndexOf(' ');
        var tail = text[(of + 4)..].TrimEnd('.', ' ');

        if (digitsStart >= 0
            && int.TryParse(head[(digitsStart + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var done)
            && int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)
            && total > 0)
        {
            CompareProgress.IsIndeterminate = false;
            CompareProgress.Value = Math.Clamp((double)done / total, 0, 1);
        }
    }

    /// <summary>
    /// Бегущая галочка: имя только что принятого параметра.
    /// </summary>
    /// <remarks>
    /// 🔴 Отрисовка прорежена. Борт отдаёт тысячу с лишним имён за пять секунд,
    /// то есть по две с половиной сотни в секунду; перерисовывать строку на
    /// каждое — значит занять поток разметки целиком и подвесить весь экран
    /// вместе с приборами. Глаз всё равно не читает быстрее десятка в секунду.
    /// </remarks>
    private void ShowReadTick(ParameterProgress tick)
    {
        var now = Environment.TickCount64;
        if (now - _lastTickAtMs < 90)
        {
            return;
        }

        _lastTickAtMs = now;

        CompareTickPanel.Visibility = Visibility.Visible;
        CompareTickName.Text = tick.Name;
        CompareTickCount.Text = tick.Total > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{tick.Done} / {tick.Total}")
            : tick.Done.ToString(CultureInfo.InvariantCulture);

        // Короткий толчок галочки: движение отличает «идёт» от «замерло»
        // надёжнее, чем меняющиеся цифры, на которые оператор не смотрит.
        _tickPulse.Begin();
    }

    private long _lastTickAtMs;

    private readonly Storyboard _tickPulse = new();

    /// <summary>Собирает анимацию толчка галочки. Вызывается один раз.</summary>
    private void BuildTickAnimation()
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0.6 });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)),
            Value = 1.0,
            EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut },
        });

        Storyboard.SetTarget(animation, CompareTickScale);
        Storyboard.SetTargetProperty(animation, "ScaleX");
        _tickPulse.Children.Add(animation);

        var vertical = new DoubleAnimationUsingKeyFrames();
        vertical.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0.6 });
        vertical.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)),
            Value = 1.0,
            EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut },
        });

        Storyboard.SetTarget(vertical, CompareTickScale);
        Storyboard.SetTargetProperty(vertical, "ScaleY");
        _tickPulse.Children.Add(vertical);
    }

    private void ShowCompare(ParameterTransferPlan plan, string state)
    {
        CompareTickPanel.Visibility = Visibility.Collapsed;

        Differences.Clear();
        _channelDifferences.Clear();

        foreach (var difference in plan.Differences)
        {
            // Расхождение по каналу или выходу не идёт отдельной строкой: оно
            // встанет в сводную строку своего канала, рядом с остальными его
            // полями. Иначе один и тот же канал оказался бы на экране дважды —
            // строкой расхождения и сводной строкой без него.
            if (ChannelOrder.TrySplit(difference.Name, out _, out _))
            {
                _channelDifferences[difference.Name] = difference;
                continue;
            }

            Differences.Add(new ParameterDifferenceRow(difference, VehicleClass));
        }

        CompareStateText.Text = state;
        CompareMatchedText.Text = plan.Matched.ToString(CultureInfo.InvariantCulture);
        CompareDiffText.Text = plan.Differences.Count.ToString(CultureInfo.InvariantCulture);
        CompareSkippedText.Text = plan.SkippedUncontrolled.ToString(CultureInfo.InvariantCulture);

        CompareProgress.Visibility = Visibility.Collapsed;
        CompareCountsPanel.Visibility = Visibility.Visible;
        CompareActionsPanel.Visibility = Visibility.Visible;

        RenderWatched();
        CheckFirmware();
        UpdateAcceptanceCommands();
    }

    /// <summary>
    /// Сверяет прошивку борта с прошивкой образца.
    /// </summary>
    /// <remarks>
    /// 🔴 Одинаковый набор параметров на другой версии прошивки — это уже
    /// другое изделие: у части имён меняется смысл, часть исчезает, часть
    /// появляется со значением по умолчанию и совпадает с эталоном «сама
    /// собой». Сверка при этом сходится, а плата ведёт себя иначе, и объяснить
    /// это потом нечем.
    ///
    /// Расхождение не блокирует приёмку: решение о том, годится ли другая
    /// сборка прошивки, принимает технолог, а не программа. Но замолчать его
    /// нельзя — молчание читается как «прошивка та же».
    /// </remarks>
    private void CheckFirmware()
    {
        if (_selectedReference is not { } reference)
        {
            return;
        }

        var board = _services.ConnectedFirmware;

        if (!reference.HasFirmware)
        {
            AppendLog(
                MavSeverity.Warning,
                "Прошивка образца в эталоне не записана — сверить нечем. "
              + "Эталон снят до того, как версию стали хранить; пересними́те его, чтобы проверка заработала.");
            return;
        }

        if (string.IsNullOrWhiteSpace(board))
        {
            AppendLog(
                MavSeverity.Warning,
                $"Борт не прислал версию прошивки — сверить с эталоном ({reference.Firmware}) нечем.");
            return;
        }

        if (string.Equals(board.Trim(), reference.Firmware.Trim(), StringComparison.Ordinal))
        {
            AppendLog(MavSeverity.Info, $"Прошивка совпала с эталоном: {reference.Firmware}.");
            return;
        }

        AppendLog(
            MavSeverity.Warning,
            $"Прошивка борта отличается от эталонной. Эталон: {reference.Firmware}. Борт: {board.Trim()}. "
          + "Часть параметров могла сменить смысл или появиться со значением по умолчанию — "
          + "совпадение по именам этого не покажет.");
    }

    /// <summary>
    /// Показывает параметры, поднятые эталоном в секцию «контроль и показ».
    /// </summary>
    /// <remarks>
    /// Значения берутся из таблицы, прочитанной сверкой, а не отдельным
    /// чтением: повторный проход по тысяче имён ради одной панели — это пять
    /// секунд занятого канала на каждое обновление экрана.
    /// </remarks>
    /// <summary>
    /// Дописывает в список наблюдаемые имена со значениями эталона.
    /// </summary>
    /// <remarks>
    /// 🔴 Показываются с эталонными значениями сразу после выбора эталона, до
    /// всякой сверки. Эталон — это и есть то, каким изделие должно быть; ждать
    /// подключения борта, чтобы узнать, что от него требуется, оператору
    /// незачем. Значение борта подставляется рядом, когда сверка выполнена.
    /// </remarks>
    private void RenderWatched()
    {
        if (_selectedReference is not { } reference)
        {
            return;
        }

        IReadOnlyDictionary<string, double> expected;
        ParameterRoleMap roles;
        try
        {
            expected = reference.ParseParameters().Values;
            roles = reference.ToRoleMap();
        }
        catch (Exception)
        {
            // Неразбираемый эталон — забота мастера эталона, а не рабочего
            // экрана: здесь он просто не даёт наблюдаемого списка.
            return;
        }

        var board = _session?.LastBoard ?? _previewBoard;
        var shown = Differences.Select(static d => d.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in _channelDifferences.Keys)
        {
            shown.Add(name);
        }
        var added = 0;

        // Каналы и выходы собираются в сводные строки, остальное идёт как есть.
        var groups = new SortedDictionary<string, List<ChannelField>>(
            Comparer<string>.Create(ChannelOrder.Compare));

        // Расхождения по каналам попадают в сводные строки независимо от того,
        // помечен ли канал к показу: расхождение показывается всегда.
        foreach (var difference in _channelDifferences.Values)
        {
            if (!ChannelOrder.TrySplit(difference.Name, out var diffTitle, out var diffLabel))
            {
                continue;
            }

            if (!groups.TryGetValue(diffTitle, out var diffFields))
            {
                diffFields = new List<ChannelField>();
                groups[diffTitle] = diffFields;
            }

            diffFields.Add(new ChannelField(
                diffLabel,
                difference.Name,
                difference.Expected is { } exp
                    ? ParameterEnums.Format(
                        difference.Name, exp, VehicleClass, ReferenceParamFile.FormatCanonical(exp))
                    : "—",
                difference.Actual is { } act
                    ? ParameterEnums.Format(
                        difference.Name,
                        act,
                        VehicleClass,
                        string.Create(CultureInfo.InvariantCulture, $"{act:G9}"))
                    : "нет",
                difference));

            added++;
        }

        foreach (var pair in expected.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            if (roles.Classify(pair.Key).Control != ParameterControl.ControlledVisible
                || shown.Contains(pair.Key))
            {
                // Разошедшееся имя уже в списке строкой расхождения — вторая
                // строка о том же имени только сбивала бы счёт.
                continue;
            }

            double? actual = null;
            if (board is not null && board.Values.TryGetValue(pair.Key, out var value))
            {
                actual = value.Value;
            }

            if (ChannelOrder.TrySplit(pair.Key, out var title, out var label))
            {
                if (!groups.TryGetValue(title, out var fields))
                {
                    fields = new List<ChannelField>();
                    groups[title] = fields;
                }

                fields.Add(new ChannelField(
                    label,
                    pair.Key,
                    ParameterEnums.Format(
                        pair.Key,
                        pair.Value,
                        VehicleClass,
                        ReferenceParamFile.FormatCanonical(pair.Value)),
                    actual is { } a
                        ? ParameterEnums.Format(
                            pair.Key, a, VehicleClass, string.Create(CultureInfo.InvariantCulture, $"{a:G9}"))
                        : null,
                    Source: null));

                added++;
                continue;
            }

            Differences.Add(new ParameterDifferenceRow(pair.Key, pair.Value, actual, VehicleClass));
            added++;
        }

        foreach (var group in groups)
        {
            group.Value.Sort(static (x, y) => ChannelOrder.CompareField(x.Label, y.Label));
            Differences.Add(new ParameterDifferenceRow(group.Key, group.Value));
        }

        if (added == 0)
        {
            return;
        }

        CompareStateText.Text = board is null
            ? $"Эталон помечен показывать {added} имён — их эталонные значения ниже. "
              + "Значения борта появятся после сверки."
            : CompareStateText.Text + $" Ниже расхождений — {added} наблюдаемых имён, они сошлись.";
    }

    // --- Скрипты изделия ----------------------------------------------------

    /// <summary>
    /// Сверяет скрипты борта с эталоном.
    /// </summary>
    /// <remarks>
    /// 🔴 Скрипты — часть конфигурации изделия наравне с параметрами. Прошивка
    /// исполняет всё, что лежит в каталоге скриптов: лишний файл делает изделие
    /// непохожим на эталонное при полностью совпавших параметрах, а
    /// недостающий отнимает часть поведения.
    /// </remarks>
    private async Task CompareScriptsAsync(CalibrationReference reference, CancellationToken ct)
    {
        if (_session is null)
        {
            return;
        }

        ScriptDiffs.Clear();

        try
        {
            var expected = await _services.Store.GetReferenceScriptsAsync(reference.Id, ct)
                .ConfigureAwait(true);

            var progress = new Progress<string>(text => ScriptsStateText.Text = text);
            var differences = await _session.CompareScriptsAsync(expected, progress, ct)
                .ConfigureAwait(true);

            foreach (var difference in differences)
            {
                ScriptDiffs.Add(new ScriptDifferenceRow(difference));
            }

            // 🔴 Совпавшие показываются наравне с разошедшимися и с теми же
            // подробностями: путь, имя, размер, хеш. Слово «совпали» без них
            // ничего не доказывает — по нему не понять, что именно сравнивали.
            var problematic = differences.Select(static d => d.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var script in expected.Where(sc => !problematic.Contains(sc.Path)))
            {
                ScriptDiffs.Add(new ScriptDifferenceRow(script));
            }

            ScriptsStateText.Text = expected.Count == 0 && differences.Count == 0
                ? "Эталон без скриптов, и на борту их нет."
                : differences.Count == 0
                    ? $"Скрипты совпали с эталоном: {expected.Count}."
                    : $"Скриптов в эталоне {expected.Count}, расходится {differences.Count}.";

            AppendLog(MavSeverity.Info, "Сверка скриптов: " + ScriptsStateText.Text);
        }
        catch (Exception ex)
        {
            ScriptsStateText.Text = "Скрипты не сверены: " + ex.Message;
            AppendLog(MavSeverity.Error, "Сверка скриптов не выполнена: " + ex.Message);
        }
        finally
        {
            UpdateAcceptanceCommands();
        }
    }

    private async void OnApplyScriptClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            var row = ScriptDiffs.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.Ordinal));
            if (row is not null)
            {
                await ApplyScriptsAsync(new[] { row }).ConfigureAwait(true);
            }
        }
    }

    private async void OnApplyAllScriptsClick(object sender, RoutedEventArgs e) =>
        await ApplyScriptsAsync(ScriptDiffs.Where(static r => r.CanApply).ToArray()).ConfigureAwait(true);

    private async Task ApplyScriptsAsync(IReadOnlyList<ScriptDifferenceRow> rows)
    {
        if (_session is null || rows.Count == 0 || _acceptanceBusy)
        {
            return;
        }

        SetAcceptanceBusy(true);
        try
        {
            var progress = new Progress<string>(text => ScriptsStateText.Text = text);
            var outcomes = await _session
                .ApplyScriptsAsync(rows.Select(static r => r.Source).ToArray(), progress, CancellationToken.None)
                .ConfigureAwait(true);

            foreach (var (path, failure) in outcomes)
            {
                var row = rows.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.Ordinal));
                row?.ApplyOutcome(failure);
            }

            var ok = outcomes.Count(static o => o.Failure is null);
            ScriptsStateText.Text =
                $"Выполнено {ok} из {outcomes.Count}. Изменения вступят в силу после перезагрузки борта: "
              + "Lua подхватывается при старте.";

            AppendLog(MavSeverity.Info, "Скрипты: " + ScriptsStateText.Text);
        }
        catch (Exception ex)
        {
            ScriptsStateText.Text = "Не выполнено: " + ex.Message;
            AppendLog(MavSeverity.Error, "Работа со скриптами не выполнена: " + ex.Message);
        }
        finally
        {
            SetAcceptanceBusy(false);
        }
    }

    private async void OnWriteOneClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
        {
            var row = Differences.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal));
            if (row is not null)
            {
                await WriteDifferencesAsync(new[] { row }).ConfigureAwait(true);
            }
        }
    }

    private async void OnWriteAllClick(object sender, RoutedEventArgs e) =>
        await WriteDifferencesAsync(Differences.Where(static r => r.CanWrite).ToArray()).ConfigureAwait(true);

    private async Task WriteDifferencesAsync(IReadOnlyList<ParameterDifferenceRow> rows)
    {
        if (_session is null || rows.Count == 0 || _acceptanceBusy)
        {
            return;
        }

        SetAcceptanceBusy(true);
        CompareProgress.Visibility = Visibility.Visible;
        CompareProgress.IsIndeterminate = true;

        try
        {
            var progress = new Progress<string>(text => CompareStateText.Text = text);
            var records = await _session
                .ApplyAsync(
                    rows.SelectMany(static r => r.Sources).ToArray(),
                    progress,
                    CancellationToken.None)
                .ConfigureAwait(true);

            foreach (var record in records)
            {
                // Строка ищется по своим источникам, а не по заголовку: у
                // сводной строки канала заголовок «RC5», а записывались
                // RC5_MIN и RC5_MAX.
                var row = rows.FirstOrDefault(r => r.Sources.Any(
                    s => string.Equals(s.Name, record.Name, StringComparison.Ordinal)));

                row?.ApplyOutcome(record);
            }

            var ok = records.Count(static r => r.Outcome == WriteOutcome.Verified);
            CompareStateText.Text = $"Записано {ok}, отклонено {records.Count - ok}. "
                + "Часть значений вступит в силу после перезагрузки борта.";
            AppendLog(MavSeverity.Info, $"Запись по требованию: принято {ok}, отклонено {records.Count - ok}.");
        }
        catch (Exception ex)
        {
            CompareStateText.Text = "Запись не выполнена: " + ex.Message;
            AppendLog(MavSeverity.Error, "Запись не выполнена: " + ex.Message);
        }
        finally
        {
            CompareProgress.Visibility = Visibility.Collapsed;
            SetAcceptanceBusy(false);
        }
    }

    /// <remarks>
    /// 🔴 Принятые расхождения перечисляются в журнале поимённо. «Принято как
    /// есть» без списка имён через полгода неотличимо от «сошлось само».
    /// </remarks>
    private void OnAcceptDiffClick(object sender, RoutedEventArgs e)
    {
        var outstanding = Differences
            .Where(static d => d.OutcomeVisibility == Visibility.Collapsed)
            .Select(static d => d.Name)
            .ToArray();

        _diffAccepted = true;

        AppendLog(MavSeverity.Warning, outstanding.Length == 0
            ? "Оператор принял состояние борта: неразобранных расхождений нет."
            : "Оператор принял расхождения как есть: " + string.Join(", ", outstanding));

        CompareStateText.Text = outstanding.Length == 0
            ? "Расхождения разобраны. Калибровки разблокированы."
            : $"Принято как есть: {outstanding.Length}. Имена перечислены в журнале. Калибровки разблокированы.";

        UpdateAcceptanceCommands();
    }

    // --- Команды оператора --------------------------------------------------

    private async void OnLevelClick(object sender, RoutedEventArgs e)
    {
        if (_session is null || _acceptanceBusy)
        {
            return;
        }

        SetAcceptanceBusy(true);
        try
        {
            var progress = new Progress<string>(text => CompareStateText.Text = text);
            var result = await _session.LevelCalibrationAsync(progress, CancellationToken.None)
                .ConfigureAwait(true);

            AppendLog(result.Ok ? MavSeverity.Info : MavSeverity.Error, result.Message);
            CompareStateText.Text = result.Message;
        }
        catch (Exception ex)
        {
            AppendLog(MavSeverity.Error, "Калибровка уровня не выполнена: " + ex.Message);
        }
        finally
        {
            SetAcceptanceBusy(false);
        }
    }

    private async void OnCompassCalClick(object sender, RoutedEventArgs e)
    {
        if (_session is null || _acceptanceBusy)
        {
            return;
        }

        var heading = HeadingBox.Value;
        if (double.IsNaN(heading))
        {
            AppendLog(MavSeverity.Error,
                "Курс борта не задан. Калибровка по неверному курсу оставит компас «успешно» откалиброванным "
              + "на чужое направление, и это не покажет ни одна проверка.");
            return;
        }

        SetAcceptanceBusy(true);
        SetCompassBusy(true, "калибровка…");
        try
        {
            var progress = new Progress<string>(text => CompassBusyText.Text = text);
            // Координаты рабочего места нужны, когда у борта нет фикса GPS:
            // без них команда не может вычислить эталонное магнитное поле.
            var result = await _session.CompassCalibrationAsync(
                heading,
                _settings.StandLatitudeDeg,
                _settings.StandLongitudeDeg,
                progress,
                CancellationToken.None).ConfigureAwait(true);

            AppendLog(result.Ok ? MavSeverity.Info : MavSeverity.Error, result.Message);
            CompareStateText.Text = result.Message;

            await LoadCompassIdentityAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppendLog(MavSeverity.Error, "Калибровка компаса не выполнена: " + ex.Message);
        }
        finally
        {
            SetCompassBusy(false);
            SetAcceptanceBusy(false);
        }
    }

    private async void OnFinishClick(object sender, RoutedEventArgs e)
    {
        if (_session is null || _acceptanceBusy)
        {
            return;
        }

        SetAcceptanceBusy(true);
        try
        {
            var progress = new Progress<string>(text => CompareStateText.Text = text);
            var readiness = await _session.CheckArmReadinessAsync(progress, CancellationToken.None)
                .ConfigureAwait(true);

            AppendLog(readiness.Ready ? MavSeverity.Info : MavSeverity.Error, readiness.Detail);

            // 🔴 Слово прошивки о калибровке важнее нашего вывода по
            // параметрам. Наш вывод — это признаки калиброванности, а прошивка
            // вдобавок сравнивает экземпляры между собой: борт с ненулевыми
            // смещениями и правильным масштабом законно получает отказ
            // «Accels inconsistent». Зелёный значок рядом с таким отказом —
            // прямая ложь, поэтому значок повторяет причину дословно.
            _firmwareImuComplaint = FindComplaint(readiness.Blockers, "accel", "imu", "gyro");
            _firmwareMagComplaint = FindComplaint(readiness.Blockers, "compass", "mag");
            RenderCalibrationState();

            foreach (var blocker in readiness.Blockers)
            {
                AppendLog(MavSeverity.Warning, "Мешает взведению: " + blocker);
            }

            ReadyBar.Severity = readiness.Ready ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            ReadyBar.Message = readiness.Ready
                ? "ГОДЕН: борт готов быть взведённым."
                : readiness.Detail;

            await RecordCheckAsync(
                "arm.readiness",
                "Готовность к взведению",
                readiness.Ready ? CheckOutcome.Pass : CheckOutcome.Fail,
                readiness.Detail).ConfigureAwait(true);

            // Проверка готовности — последний шаг приёмки, её итог и есть
            // вердикт прогона.
            await CloseRunAsync(readiness.Ready, readiness.Detail).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppendLog(MavSeverity.Error, "Проверка готовности не выполнена: " + ex.Message);
        }
        finally
        {
            SetAcceptanceBusy(false);

            // Проверка готовности — последний шаг приёмки. Порт освобождается
            // здесь, иначе он остался бы занятым сессией до перезапуска
            // приложения, а приборы — мёртвыми.
            await ReleaseAcceptanceAsync().ConfigureAwait(true);
        }
    }

    // --- Состояние приёмки --------------------------------------------------

    private void SetAcceptanceBusy(bool busy)
    {
        _acceptanceBusy = busy;

        StartAcceptanceButton.Content = busy ? "Отменить приёмку" : "Старт приёмки";
        StartAcceptanceButton.IsEnabled = busy
            || (_services.IsLinkConnected && _selectedReference is not null);

        UpdateAcceptanceCommands();
    }

    /// <summary>Движение на панели компасов, пока идёт работа с ними.</summary>
    private void SetCompassBusy(bool busy, string text = "")
    {
        CompassBusyRing.IsActive = busy;
        CompassBusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CompassBusyText.Text = text;
    }

    /// <remarks>
    /// 🔴 Калибровки разблокированы только после разбора расхождений.
    /// Калибровать борт, конфигурация которого расходится с эталоном и не
    /// разобрана, значит подтвердить не то изделие, которое сдаётся.
    /// </remarks>
    private void UpdateAcceptanceCommands()
    {
        var live = _session is not null && !_acceptanceBusy;

        // 🔴 Разобранными считаются только настоящие расхождения. В списке
        // вперемешку лежат и совпавшие наблюдаемые имена — у них исхода не
        // бывает никогда, и требование «исход у каждой строки» держало бы
        // калибровки заблокированными вечно.
        var outstanding = Differences
            .Where(static d => d.Source is not null && d.OutcomeVisibility == Visibility.Collapsed)
            .ToArray();

        var resolved = _diffAccepted || outstanding.Length == 0;

        WriteAllButton.IsEnabled = live && Differences.Any(static d => d.CanWrite);
        AcceptDiffButton.IsEnabled = live && outstanding.Length > 0 && !_diffAccepted;
        ApplyAllScriptsButton.IsEnabled = live && ScriptDiffs.Any(static r => r.CanApply);

        LevelButton.IsEnabled = live && resolved;
        CompassCalButton.IsEnabled = live && resolved;
        FinishButton.IsEnabled = live && resolved;
    }

    private async Task CloseSessionAsync()
    {
        await _services.EndAcceptanceAsync().ConfigureAwait(true);
        _session = null;
    }

    // --- Прогон в реестре ---------------------------------------------------

    /// <summary>
    /// Открывает прогон в реестре и привязывает его к эталону.
    /// </summary>
    /// <remarks>
    /// 🔴 Открывается до касания железа: строка о прогоне, который начали,
    /// обязана существовать, даже если он оборвётся на первом же шаге.
    /// Прерванный прогон в истории борта — это факт, а его отсутствие — ложь.
    /// </remarks>
    private async Task OpenRunAsync(CalibrationReference reference)
    {
        await CloseRunAsync(passed: false, "Прогон не завершён.").ConfigureAwait(true);

        _runChecks.Clear();
        _runStartedUtc = DateTimeOffset.UtcNow;

        var request = new CalibrationRequest(
            PortName: _services.ConnectedPort ?? string.Empty,
            ReferenceFilePath: reference.SourceName,
            JigAzimuthDeg: double.IsNaN(HeadingBox.Value) ? 0 : HeadingBox.Value,
            UnitId: UnitIdBox.Text.Trim(),
            Operator: _settings.DefaultOperator,
            Roles: reference.ToRoleMap());

        try
        {
            _runId = await _services.Store
                .BeginRunAsync(request, reference.ParamHash, _services.Updates.CurrentVersion ?? "0.0.0-dev")
                .ConfigureAwait(true);

            await _services.Store
                .AttachReferenceToRunAsync(_runId, reference.Id, reference.Name)
                .ConfigureAwait(true);

            AppendLog(MavSeverity.Info,
                $"Прогон {_runId} открыт в реестре: борт {request.UnitId}, эталон «{reference.Name}».");
        }
        catch (Exception ex)
        {
            // Отказ реестра не должен помешать сдать плату: оператор у стенда,
            // деталь в приспособлении. Но молчать об этом нельзя — прогон
            // останется незаписанным.
            _runId = 0;
            AppendLog(MavSeverity.Error,
                "Прогон не открыт в реестре: " + ex.Message + " Приёмка пойдёт, но в историю не попадёт.");
        }
    }

    /// <summary>Дописывает проверку и в реестр, и в итог прогона.</summary>
    private async Task RecordCheckAsync(string id, string title, CheckOutcome outcome, string detail)
    {
        var check = new CheckResult(id, title, outcome, detail);
        _runChecks.Add(check);

        if (_runId == 0)
        {
            return;
        }

        try
        {
            await _services.Store.RecordCheckAsync(_runId, check).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppendLog(MavSeverity.Error, $"Проверка «{title}» не записана в реестр: {ex.Message}");
        }
    }

    /// <summary>
    /// Закрывает прогон итогом.
    /// </summary>
    /// <remarks>
    /// 🔴 Закрывается при любом исходе — успехе, отказе, отмене, сбое.
    /// Открытая строка в реестре хуже строки с отказом: она блокирует
    /// обновление приложения и врёт про состояние стенда.
    /// </remarks>
    private async Task CloseRunAsync(bool passed, string reason)
    {
        if (_runId == 0)
        {
            return;
        }

        var runId = _runId;
        _runId = 0;

        var result = new CalibrationRunResult(
            passed,
            passed ? null : CalibrationStage.Acceptance,
            passed ? null : reason,
            _runChecks.ToArray(),
            Array.Empty<ParamWriteRecord>(),
            Array.Empty<CompassSlot>(),
            Array.Empty<CompassSlot>(),
            Array.Empty<StatusTextEvent>(),
            _session?.FirmwareBanner,
            _runStartedUtc,
            DateTimeOffset.UtcNow)
        {
            RunId = runId,
            PortName = _services.ConnectedPort ?? string.Empty,
        };

        try
        {
            await _services.Store.CompleteRunAsync(runId, result).ConfigureAwait(true);
            AppendLog(passed ? MavSeverity.Info : MavSeverity.Warning,
                $"Прогон {runId} закрыт в реестре: {(passed ? "ГОДЕН" : "не пройден")}. {reason}");
        }
        catch (Exception ex)
        {
            AppendLog(MavSeverity.Error, $"Прогон {runId} не закрыт в реестре: {ex.Message}");
        }
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
        ShowSelector(_portsExpanded, PortFlyout, PortFlyoutRoot, PortCard);
        ShowSelector(_referencesExpanded, ReferenceFlyout, ReferenceFlyoutRoot, ReferenceCard);
    }

    /// <summary>
    /// Ставит всплывающий слой так, чтобы карточка осталась на своём месте:
    /// плашки «выше» уходят над ней, «ниже» — под неё, а прозрачная прокладка
    /// занимает ровно место самой карточки.
    /// </summary>
    /// <summary>
    /// Открывает список выбора под плашкой.
    /// </summary>
    /// <remarks>
    /// 🔴 Размещением занимается Flyout, а не приложение. Прежний Popup
    /// отсчитывал смещение от собственного места в разметке, координаты
    /// приходилось вычислять руками, и любое изменение состава панели или
    /// размера шрифта сдвигало список — он открывался под чужой карточкой.
    /// Здесь остаётся только ширина: список обязан совпасть с плашкой,
    /// иначе он читается как отдельный элемент, а не как её раскрытие.
    /// </remarks>
    private static void ShowSelector(bool open, Flyout flyout, FrameworkElement root, FrameworkElement card)
    {
        if (!open)
        {
            flyout.Hide();
            return;
        }

        var width = card.ActualWidth;
        if (width <= 0)
        {
            // Карточка ещё не измерена — раскрывать нечего.
            flyout.Hide();
            return;
        }

        // Отступы презентера съедают часть ширины, поэтому вычитаются.
        root.Width = Math.Max(0, width - FlyoutPresenterPadding);
        flyout.ShowAt(card);
    }

    /// <summary>Горизонтальные отступы штатного презентера Flyout.</summary>
    private const double FlyoutPresenterPadding = 24;

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
        await TryAutoConnectAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Подключает или отключает наблюдательное соединение по команде оператора.
    /// </summary>
    /// <remarks>
    /// Ручное подключение идёт на выбранный порт и не спрашивает флаг
    /// автоподключения: это прямая команда, а не политика. Ручное отключение
    /// освобождает порт — им пользуются, когда плату надо отдать Mission
    /// Planner, не закрывая стенд.
    /// </remarks>
    private async void OnLinkToggleClick(object sender, RoutedEventArgs e)
    {
        if (_services.IsAcceptanceRunning)
        {
            ShowLinkMessage(
                InfoBarSeverity.Warning,
                "Идёт приёмка: порт занят ею. Остановите приёмку, потом управляйте связью.");
            return;
        }

        if (_services.IsLinkConnected)
        {
            await _services.DisconnectAsync().ConfigureAwait(true);
            Log("Связь разорвана по команде оператора, порт свободен.");
            RenderAll();
            return;
        }

        if (_selectedPort is null)
        {
            // Порт не выбран — берём однозначно опознанную плату, если она
            // одна. Молча не подключаемся ни к чему: у композитной платы
            // портов несколько, и «какой-нибудь» из них — это тот самый
            // SLCAN, на котором HEARTBEAT не будет никогда.
            var candidates = PickCandidates(_ports);
            if (candidates.Count != 1)
            {
                ShowLinkMessage(
                    InfoBarSeverity.Informational,
                    candidates.Count == 0
                        ? "Плата ArduPilot среди портов не опознана — выберите порт на плашке."
                        : "Опознано несколько плат — выберите нужную на плашке.");
                return;
            }

            _selectedPort = candidates[0];
        }

        await ConnectAsync().ConfigureAwait(true);
        RenderAll();
    }

    /// <summary>
    /// Подключает наблюдательное соединение, если борт опознан однозначно.
    /// </summary>
    /// <remarks>
    /// 🔴 Во время приёмки не подключается никогда. COM-порт открывается
    /// монопольно и на всю приёмку принадлежит сессии; наблюдательное
    /// соединение, полезшее туда после перезагрузки борта, отбирает порт у
    /// процедуры либо получает отказ и засоряет журнал сообщением «порт занят
    /// другой программой» — про самого себя.
    /// </remarks>
    private async Task TryAutoConnectAsync()
    {
        // 🔴 Флаг проверяется здесь, а не у каждого вызывающего. Раньше это
        // решал сам вызывающий, и один из них — возврат наблюдательного
        // соединения после приёмки — проверку не делал: снятая галка ничего
        // не значила, приложение продолжало занимать порт само.
        if (AutoConnectCheck.IsChecked != true)
        {
            return;
        }

        if (_services.IsAcceptanceRunning || _services.IsLinkConnected || _busy)
        {
            return;
        }

        if (_ports.Count == 0)
        {
            ShowLinkMessage(InfoBarSeverity.Informational, "COM-портов не найдено — подключите борт кабелем.");
            return;
        }

        // 🔴 Выбор оператора важнее отбора по признакам. Плашка показывала
        // один порт, а автоподключение лезло в другой: у композитной платы
        // MAVLink и SLCAN отличаются только номером интерфейса, и отбор,
        // сделанный заново, законно приходил к другому ответу. Порт, названный
        // на плашке, и порт, в который стучимся, обязаны быть одним и тем же.
        if (_selectedPort is { } chosen
            && _ports.Any(p => string.Equals(p.PortName, chosen.PortName, StringComparison.OrdinalIgnoreCase)))
        {
            await ConnectAsync().ConfigureAwait(true);
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

    private void OnLinkChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        RenderAll();

        // Опознание компасов привязано к соединению, а не к такту индикатора:
        // при обрыве оно устаревает, при новом подключении читается заново.
        if (_services.IsLinkConnected)
        {
            _ = LoadCompassIdentityAsync();
        }
        else
        {
            _compassIdentity.Clear();
            Compasses.Clear();
        }
    });

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

        // Кнопка называет действие, а не состояние: «Отключить» на живой связи
        // и «Подключить» на мёртвой.
        LinkToggleButton.Content = connected ? "Отключить" : "Подключить";
        LinkToggleButton.IsEnabled = !_services.IsAcceptanceRunning && !_busy;
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

        // 🔴 Без номера борта прогон не привязать к изделию, и история сдачи
        // превращается в список безымянных записей, по которому нельзя
        // ответить на единственный вопрос, ради которого реестр и ведётся.
        if (UnitIdBox.Text.Trim().Length == 0)
        {
            problems.Add("не указан номер борта");
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

        // Наблюдаемые имена показываются сразу после выбора эталона: пока
        // сверки не было, в списке стоят эталонные значения. Пересобирается
        // только когда никакой сверки ещё не выполнялось — иначе затёрло бы
        // её итог.
        if (_session is null && _previewBoard is null && !_previewBusy)
        {
            Differences.Clear();
            RenderWatched();
        }

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

    private void Log(string message) => AppendLog(MavSeverity.Info, message);

    /// <summary>
    /// Строка журнала с явной важностью.
    /// </summary>
    /// <remarks>
    /// Важность задаётся вызывающим, а не выводится из текста: отказ приёмки и
    /// сообщение о ходе выглядят в журнале одинаково, если оба записаны как
    /// «сведения», и оператор пролистывает отказ не заметив.
    /// </remarks>
    private void AppendLog(MavSeverity severity, string message) => DispatcherQueue.TryEnqueue(() =>
    {
        LogEntries.Insert(0, new CalibrationLogRow(severity, message, DateTimeOffset.Now));
        while (LogEntries.Count > 300)
        {
            LogEntries.RemoveAt(LogEntries.Count - 1);
        }
    });

    private void OnClearLogClick(object sender, RoutedEventArgs e) => LogEntries.Clear();


    /// <summary>Состояние связи, отражённое на экране последней перерисовкой.</summary>
    private bool? _renderedConnected;

    private void OnHudTick(object? sender, object e) => RenderHud();

    /// <summary>
    /// Замечает возвращение связи и обновляет экран.
    /// </summary>
    /// <remarks>
    /// 🔴 Сверка живёт на часах опроса портов, а не на часах индикатора. Часы
    /// индикатора сами останавливаются, когда связи нет, — и получается, что
    /// единственное, что могло бы заметить возвращение борта, выключается
    /// ровно в тот момент, когда оно понадобилось. Экран навсегда застревал с
    /// надписью «нет связи с полётником» после первой же перезагрузки борта.
    ///
    /// Часы опроса портов работают всегда, поэтому проверка стоит здесь.
    /// </remarks>
    private void SyncConnectionState()
    {
        var connected = _services.IsLinkConnected;
        if (_renderedConnected == connected)
        {
            return;
        }

        _renderedConnected = connected;
        RenderAll();

        if (connected)
        {
            _ = LoadCompassIdentityAsync();
            _ = PreviewCompareAsync();
        }
    }

    /// <summary>
    /// Сверяет борт с эталоном сразу после подключения, не дожидаясь запуска
    /// приёмки.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Сверка ничего на борту не меняет: это одно чтение таблицы параметров.
    /// Поэтому ждать «Старта», чтобы узнать, чем плата отличается от эталона,
    /// незачем — оператор видит это сразу, как только положил её на стенд, и
    /// решает, стоит ли вообще запускать процедуру.
    /// </para>
    /// <para>
    /// Во время приёмки не выполняется: там сверка идёт своим чередом и
    /// повторное чтение тысячи имён отняло бы у процедуры канал.
    /// </para>
    /// </remarks>
    private async Task PreviewCompareAsync()
    {
        if (_selectedReference is not { } reference
            || _acceptanceBusy
            || _session is not null
            || _previewBusy)
        {
            return;
        }

        _previewBusy = true;
        try
        {
            CompareStateText.Text = "Вычитываю прошивку борта…";
            CompareProgress.Visibility = Visibility.Visible;
            CompareProgress.IsIndeterminate = true;

            var progress = new Progress<string>(text =>
            {
                CompareStateText.Text = text;
                AnimateCompareProgress(text);
            });

            var detail = new Progress<ParameterProgress>(ShowReadTick);

            var board = await _services.ReadAllParametersAsync(progress, detail).ConfigureAwait(true);

            var plan = ParameterTransfer.Plan(
                reference.ParseParameters().Values,
                board.Values,
                reference.ToRoleMap());

            _previewBoard = board;
            ShowCompare(plan, plan.IsClean
                ? "Борт совпал с эталоном. Приёмку можно запускать."
                : $"Расходится параметров: {plan.Differences.Count}. Это предварительная сверка — "
                  + "борт не тронут. Запись пойдёт только по «Старту приёмки».");

            AppendLog(MavSeverity.Info,
                $"Предварительная сверка: совпало {plan.Matched}, расходится {plan.Differences.Count}.");
        }
        catch (Exception ex)
        {
            CompareStateText.Text = "Предварительная сверка не выполнена: " + ex.Message;
        }
        finally
        {
            CompareProgress.Visibility = Visibility.Collapsed;
            CompareTickPanel.Visibility = Visibility.Collapsed;
            _previewBusy = false;
        }
    }

    private bool _previewBusy;

    /// <summary>Оператор задал курс сам — подстановка с борта прекращается.</summary>
    private bool _headingEdited;

    private void OnHeadingEdited(object sender, RoutedEventArgs e) => _headingEdited = true;

    /// <summary>Таблица борта из предварительной сверки — по ней показываются наблюдаемые значения.</summary>
    private FullParameterSet? _previewBoard;

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

            // Шкала тангажа рисуется кодом, поэтому размер берётся из того же
            // масштаба, что и разметка: иначе на увеличенном интерфейсе цифры
            // на приборе остались бы единственным мелким текстом на экране.
            FontSize = 13 * Services.UiScale.Current,
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

            // 🔴 Курс для калибровки подставляется с борта и следует за ним,
            // пока оператор не взялся за поле руками. Вводить вручную то, что
            // борт measures сам, — лишний повод ошибиться в цифре, а ошибка в
            // курсе оставляет компас «успешно» откалиброванным на чужое
            // направление, и этого не покажет ни одна проверка.
            //
            // Как только поле получило ввод, подстановка прекращается: борт
            // может стоять не так, как думает его же AHRS, и последнее слово
            // за человеком.
            if (HeadingBox.FocusState == FocusState.Unfocused && !_headingEdited)
            {
                HeadingBox.Value = Math.Round(yawDeg, 1);
            }
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

        RenderAir(state);
        RenderGps(state);
        RenderCalibrationState();
        RenderCompasses(state);
    }

    /// <summary>
    /// Показывает, требуется ли калибровка акселерометров и компасов.
    /// </summary>
    /// <remarks>
    /// Судим по уже прочитанной таблице параметров: она снимается сверкой на
    /// каждом подключении, и второй проход по тысяче имён ради двух панелей —
    /// это лишние секунды занятого канала. Пока таблицы нет, панели говорят
    /// «нет данных», а не «всё в порядке»: непрочитанное и проверенное — разные
    /// вещи, и зелёная панель без чтения была бы выдуманным результатом.
    /// </remarks>
    /// <summary>Жалоба прошивки на инерциальные датчики; пусто — жалоб не было.</summary>
    private string _firmwareImuComplaint = string.Empty;

    /// <summary>Жалоба прошивки на компасы.</summary>
    private string _firmwareMagComplaint = string.Empty;

    /// <summary>Первая предполётная жалоба, где встретилось любое из слов.</summary>
    private static string FindComplaint(IEnumerable<string> blockers, params string[] words)
    {
        foreach (var blocker in blockers)
        {
            foreach (var word in words)
            {
                if (blocker.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    return blocker;
                }
            }
        }

        return string.Empty;
    }

    private void RenderCalibrationState()
    {
        var values = (_session?.LastBoard ?? _previewBoard)?.Values;
        IReadOnlyDictionary<string, double>? plain = null;

        if (values is not null)
        {
            var map = new Dictionary<string, double>(values.Count, StringComparer.Ordinal);
            foreach (var pair in values)
            {
                map[pair.Key] = pair.Value.Value;
            }

            plain = map;
        }

        ApplyCalibrationCard(
            Override(CalibrationStatus.Imu(plain), _firmwareImuComplaint), ImuCalCard, ImuCalText);

        ApplyCalibrationCard(
            Override(CalibrationStatus.Mag(plain), _firmwareMagComplaint), MagCalCard, MagCalText);
    }

    /// <summary>Жалоба прошивки перекрывает вывод по параметрам.</summary>
    private static CalibrationVerdict Override(CalibrationVerdict verdict, string complaint) =>
        complaint.Length == 0
            ? verdict
            : CalibrationVerdict.Needed("прошивка: " + complaint);

    /// <summary>
    /// Красит значок калибровки. Подробности уходят в подсказку: на приборах
    /// нужен ответ «надо или нет», а не рассуждение.
    /// </summary>
    private void ApplyCalibrationCard(CalibrationVerdict verdict, Border card, TextBlock text)
    {
        ToolTipService.SetToolTip(card, verdict.Detail);

        if (!verdict.Known)
        {
            text.Text = "нет данных";
            text.Foreground = InkDim;
            card.BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
            return;
        }

        text.Text = verdict.Required ? "требуется" : "не требуется";
        text.Foreground = (Brush)Application.Current.Resources[verdict.Required
            ? "SystemFillColorCriticalBrush"
            : "SystemFillColorSuccessBrush"];

        card.BorderBrush = text.Foreground;
    }

    /// <summary>
    /// Скорости и газ из <c>VFR_HUD</c>.
    /// </summary>
    /// <remarks>
    /// Ноль воздушной скорости на стенде — норма и показывается как ноль:
    /// прочерк означал бы «борт молчит», а он отвечает. Отличить «трубки нет»
    /// от «трубка есть и показывает ноль» по одному этому числу нельзя, и
    /// подменять его прочерком — значит скрыть от оператора сам факт ответа.
    /// </remarks>
    private void RenderAir(VehicleLiveState state)
    {
        if (state.Air is not { } air)
        {
            HudAirspeedText.Text = "—";
            HudGroundspeedText.Text = "—";
            HudThrottleText.Text = "—";
            return;
        }

        HudAirspeedText.Text = string.Create(CultureInfo.InvariantCulture, $"{air.AirspeedMs:0.0} м/с");
        HudGroundspeedText.Text = string.Create(CultureInfo.InvariantCulture, $"{air.GroundspeedMs:0.0} м/с");
        HudThrottleText.Text = string.Create(CultureInfo.InvariantCulture, $"{air.ThrottlePercent} %");
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
        RenderGpsBlock(
            state.Gps,
            HudGpsFixText,
            HudGpsSatsText,
            HudGpsHdopText,
            HudGpsAltText,
            HudGpsPositionText,
            "сообщений не было");

        // Для второго приёмника молчание GPS2_RAW — это не «рано судить», а
        // содержательный ответ: второго приёмника нет либо он не настроен.
        RenderGpsBlock(
            state.Gps2,
            HudGps2FixText,
            HudGps2SatsText,
            HudGps2HdopText,
            HudGps2AltText,
            HudGps2PositionText,
            "не подключён");
    }

    /// <summary>Заполняет одну панель приёмника.</summary>
    private void RenderGpsBlock(
        GpsFix? fix,
        TextBlock fixText,
        TextBlock satsText,
        TextBlock hdopText,
        TextBlock altText,
        TextBlock positionText,
        string silenceText)
    {
        if (fix is not { } gps)
        {
            fixText.Text = silenceText;
            fixText.Foreground = InkDim;
            satsText.Text = "—";
            hdopText.Text = "—";
            altText.Text = "—";
            positionText.Text = "координат нет";
            return;
        }

        fixText.Text = gps.FixTypeText;
        fixText.Foreground = gps.Is3D
            ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
            : (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];

        satsText.Text = gps.SatellitesVisible.ToString(CultureInfo.InvariantCulture);

        // Неизвестная геометрия показывается словом. Приведённая к числу, она
        // выглядела бы либо идеальной, либо чудовищной — и то и другое ложь.
        hdopText.Text = gps.Hdop is { } hdop
            ? string.Create(CultureInfo.InvariantCulture, $"{hdop:0.00}")
            : "нет";

        altText.Text = gps.Is3D
            ? string.Create(CultureInfo.InvariantCulture, $"{gps.AltitudeMeters:0} м")
            : "нет";

        // Координаты без решения — это последнее известное или ноль; выдавать
        // их за положение борта нельзя.
        positionText.Text = gps.Is3D
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{gps.LatitudeDeg:F7}, {gps.LongitudeDeg:F7}")
            : "координат нет: решение не трёхмерное";
    }

    /// <summary>
    /// Опознание компасов, прочитанное с борта. Ключ — номер слота.
    /// </summary>
    /// <remarks>
    /// Читается один раз на соединение: <c>COMPASS_DEV_ID*</c> меняется только
    /// при перестановке слотов, а чтение параметров на каждом такте индикатора
    /// забило бы канал телеметрии.
    /// </remarks>
    private readonly Dictionary<int, CompassIdentityRow> _compassIdentity = new();

    private bool _compassIdentityLoading;

    /// <summary>
    /// Читает с борта, какой компас внешний, а какой внутренний.
    /// </summary>
    /// <remarks>
    /// 🔴 Судить о внешности по номеру слота нельзя. Порядок слотов задаётся
    /// таблицей приоритетов и меняется при перестановке, поэтому «первый —
    /// значит внешний» неверно ровно на тех бортах, ради которых проверка и
    /// существует. Вывод делается по типу шины и флагу <c>COMPASS_EXTERNAL*</c>.
    /// </remarks>
    private async Task LoadCompassIdentityAsync()
    {
        if (_compassIdentityLoading || !_services.IsLinkConnected)
        {
            return;
        }

        _compassIdentityLoading = true;
        try
        {
            var found = new Dictionary<int, CompassIdentityRow>();

            for (var slot = CompassIdentity.MinSlot; slot <= CompassIdentity.MaxSlot; slot++)
            {
                double devId;
                try
                {
                    devId = await _services.ReadParameterAsync(CompassIdentity.DevIdName(slot))
                        .ConfigureAwait(true);
                }
                catch (Exception)
                {
                    // Прошивка законно не знает имени при меньшем числе
                    // компасов — это сообщение о составе, а не сбой.
                    continue;
                }

                var id = CompassIdentity.ToDeviceId(devId);
                if (id.IsEmpty)
                {
                    continue;
                }

                double externalFlag;
                try
                {
                    externalFlag = await _services.ReadParameterAsync(CompassIdentity.ExternalName(slot))
                        .ConfigureAwait(true);
                }
                catch (Exception)
                {
                    externalFlag = double.NaN;
                }

                var kind = double.IsNaN(externalFlag)
                    ? ExternalKind.Ambiguous
                    : CompassIdentity.Classify(id, (int)Math.Round(externalFlag));

                // 🔴 Флаг со значением 2 запрещает прошивке переопределить
                // вывод своим обнаружением. На шине, которая сама по себе не
                // делает компас внешним, такой флаг мог попасть на плату только
                // извне — и тогда он держит на датчике чужую метку, которую
                // борт исправить не вправе. Об этом говорится прямо: подпись
                // «внешний» на внутреннем компасе иначе выглядит как факт.
                var forced = !double.IsNaN(externalFlag) && (int)Math.Round(externalFlag) == 2;
                var busDecides = id.BusType is MavBusType.DroneCan or MavBusType.Msp or MavBusType.Serial;

                var device = CompassIdentity.Describe(id);
                if (forced && !busDecides)
                {
                    device += " · метка задана вручную (флаг 2), автоопределение выключено";
                    AppendLog(
                        MavSeverity.Warning,
                        $"{CompassIdentity.ExternalName(slot)} = 2: вид компаса в слоте {slot} задан жёстко, "
                      + "и прошивка не вправе его исправить. Если метка неверна, обнулите этот параметр "
                      + "и перезагрузите борт — при обнаружении датчика прошивка проставит вид сама.");
                }

                found[slot] = new CompassIdentityRow(
                    slot,
                    CompassIdentity.IsExternal(kind),
                    CompassIdentity.ExternalKindText(kind),
                    device);
            }

            _compassIdentity.Clear();
            foreach (var pair in found)
            {
                _compassIdentity[pair.Key] = pair.Value;
            }

            // Строки пересобираются: у них опознание задаётся при создании.
            Compasses.Clear();
            RenderHud();
        }
        catch (Exception)
        {
            // Индикатор без опознания хуже, чем с ним, но лучше, чем упавший
            // рабочий экран: строки покажут «опознание не прочитано».
        }
        finally
        {
            _compassIdentityLoading = false;
        }
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
                Compasses.Add(new CompassRow(sample, IdentityFor(sample)));
            }
        }
        else
        {
            // 🔴 Порядок правится перестановкой (Move), а содержимое —
            // обновлением строки на месте. Подмена объекта заставляла список
            // пересобирать контейнер несколько раз в секунду: панель мерцала.
            // Move при этом сохраняется — он и даёт анимацию переезда строки
            // при смене очереди компасов.
            for (var i = 0; i < samples.Count; i++)
            {
                var wanted = samples[i];

                if (Compasses[i].Instance != wanted.Instance)
                {
                    var from = -1;
                    for (var j = i + 1; j < Compasses.Count; j++)
                    {
                        if (Compasses[j].Instance == wanted.Instance)
                        {
                            from = j;
                            break;
                        }
                    }

                    if (from > 0)
                    {
                        Compasses.Move(from, i);
                    }
                }

                Compasses[i].Update(wanted, IdentityFor(wanted));
            }
        }

        CompassHintText.Text = samples.Count == 0 ? "Показаний магнитометров пока нет." : string.Empty;
    }

    /// <summary>
    /// Опознание для показаний магнитометра.
    /// </summary>
    /// <remarks>
    /// 🔴 Экземпляр телеметрии нумеруется с нуля, слот компаса — с единицы.
    /// Сдвиг на единицу здесь не косметика: без него внешний компас подписался
    /// бы данными соседнего, и оператор читал бы «внешний» под внутренним.
    /// </remarks>
    private CompassIdentityRow? IdentityFor(MagSample sample) =>
        _compassIdentity.TryGetValue(sample.Instance + CompassIdentity.MinSlot, out var identity)
            ? identity
            : null;
}

