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

namespace ARDU_OTK;

/// <summary>Пункт списка эталонов: профиль плюс готовая подпись для списка.</summary>
public sealed record ProfileChoice(CalibrationProfile Profile, string Caption);

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
        Detail = string.Create(
            CultureInfo.InvariantCulture,
            $"X {sample.X}  Y {sample.Y}  Z {sample.Z}");
        FieldText = string.Create(CultureInfo.InvariantCulture, $"{magnitude:0}");

        OkVisibility = inBand ? Visibility.Visible : Visibility.Collapsed;
        WarnVisibility = inBand ? Visibility.Collapsed : Visibility.Visible;

        if (!inBand)
        {
            Detail += magnitude < FieldMin
                ? "  · поле слабее нормы"
                : "  · поле сильнее нормы";
        }
    }

    public string Title { get; }

    public string Detail { get; }

    public string FieldText { get; }

    public Visibility OkVisibility { get; }

    public Visibility WarnVisibility { get; }
}

/// <summary>
/// Заглавная страница рабочего места: выбор текущего полётного контроллера и
/// выбор эталонного профиля.
/// </summary>
/// <remarks>
/// Проверка обновлений сюда намеренно не выведена — она живёт в настройках.
/// </remarks>
public sealed partial class MainPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    private bool _storeReady;
    private bool _busy;

    public MainPage()
    {
        InitializeComponent();

        _services.LinkChanged += OnLinkChanged;
        _hudTimer.Tick += OnHudTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Строки панели компасов.</summary>
    public ObservableCollection<CompassRow> Compasses { get; } = [];

    /// <summary>Журнал операций рабочего места.</summary>
    public ObservableCollection<CalibrationLogRow> LogEntries { get; } = [];

    /// <summary>Выбранный эталон. Читается процедурой прогона.</summary>
    public CalibrationProfile? SelectedProfile => (ProfileCombo.SelectedItem as ProfileChoice)?.Profile;

    /// <summary>Описание выбранного порта.</summary>
    public SerialPortDescription? SelectedPortInfo => PortCombo.SelectedItem as SerialPortDescription;

    /// <summary>Выбранный порт борта.</summary>
    public string? SelectedPort => SelectedPortInfo?.PortName;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ReloadPortsAsync().ConfigureAwait(true);
        RenderLink();

        try
        {
            await _services.InitializeAsync().ConfigureAwait(true);
            _storeReady = true;
        }
        catch (Exception ex)
        {
            ProfileBar.Severity = InfoBarSeverity.Error;
            ProfileBar.Message = "Реестр недоступен, список эталонов не прочитан: " + ex.Message;
            ProfileBar.IsOpen = true;
        }

        if (_storeReady)
        {
            await ReloadProfilesAsync().ConfigureAwait(true);
        }

        RenderReady();

        if (AutoConnectCheck.IsChecked == true)
        {
            await TryAutoConnectAsync().ConfigureAwait(true);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _services.LinkChanged -= OnLinkChanged;
        _hudTimer.Stop();
        _hudTimer.Tick -= OnHudTick;
    }

    private void Log(string message) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            LogEntries.Insert(0, new CalibrationLogRow(MavSeverity.Info, message, DateTimeOffset.Now));

            // Журнал рабочего места не должен расти без границ.
            while (LogEntries.Count > 300)
            {
                LogEntries.RemoveAt(LogEntries.Count - 1);
            }
        });

    private void OnClearLogClick(object sender, RoutedEventArgs e) => LogEntries.Clear();

    // --- HUD ---------------------------------------------------------------

    /// <summary>
    /// Опрос состояния борта для индикатора.
    /// </summary>
    /// <remarks>
    /// Данные обновляются десятки раз в секунду, поэтому индикатор не
    /// подписывается на каждое сообщение, а читает последнее состояние с
    /// частотой обновления экрана: иначе поток интерфейса захлебнётся.
    /// </remarks>
    private void OnHudTick(object? sender, object e) => RenderHud();

    private void RenderHud()
    {
        var state = _services.LiveState;
        if (state is null)
        {
            HudArmedText.Text = "нет данных";
            return;
        }

        if (state.Attitude is { } attitude)
        {
            var rollDeg = attitude.RollRad * 180.0 / Math.PI;
            var pitchDeg = attitude.PitchRad * 180.0 / Math.PI;
            var yawDeg = (attitude.YawRad * 180.0 / Math.PI + 360.0) % 360.0;

            // Горизонт вращается против крена и сдвигается по тангажу.
            RollRotate.Angle = -rollDeg;
            PitchTranslate.Y = pitchDeg * 3.0;

            HudRollText.Text = string.Create(CultureInfo.InvariantCulture, $"{rollDeg,6:0.0}°");
            HudPitchText.Text = string.Create(CultureInfo.InvariantCulture, $"{pitchDeg,6:0.0}°");
            HudYawText.Text = string.Create(CultureInfo.InvariantCulture, $"{yawDeg,6:0.0}°");
        }

        // Сентинелы уже отфильтрованы каналом: null означает «борт не сообщает».
        HudVoltageText.Text = state.VoltageVolts is { } v
            ? string.Create(CultureInfo.InvariantCulture, $"{v,6:0.00} В")
            : "нет данных";

        HudCurrentText.Text = state.CurrentAmperes is { } a
            ? string.Create(CultureInfo.InvariantCulture, $"{a,6:0.00} А")
            : "нет данных";

        HudModeText.Text = state.ModeName;
        HudModeBigText.Text = state.ModeName;
        HudArmedText.Text = state.Armed ? "ВЗВЕДЁН" : "ОБЕЗВРЕЖЕН";

        RenderCompasses(state);
    }

    private void RenderCompasses(VehicleLiveState state)
    {
        // Пустой экземпляр — это IMU без магнитометра (типично для третьего
        // блока на плате с двумя компасами), а не третий компас.
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

        CompassHintText.Text = samples.Count == 0
            ? "Показаний магнитометров пока нет."
            : string.Empty;
    }

    // --- Полётный контроллер ----------------------------------------------

    private async void OnRefreshPortsClick(object sender, RoutedEventArgs e) =>
        await ReloadPortsAsync().ConfigureAwait(true);

    private void OnPortSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PortDetailText.Text = SelectedPortInfo?.Details ?? string.Empty;
        RenderLink();
        RenderReady();
    }

    /// <summary>
    /// Перечитывает порты вместе с описанием устройств. Список не кэшируется:
    /// после перезагрузки борт переподключается к шине и может занять другой
    /// номер порта.
    /// </summary>
    private async Task ReloadPortsAsync()
    {
        var previous = SelectedPort;

        IReadOnlyList<SerialPortDescription> ports;
        try
        {
            ports = await SerialPortCatalog.ListAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowLinkMessage(InfoBarSeverity.Warning, "Не удалось перечислить порты: " + ex.Message);
            return;
        }

        PortCombo.ItemsSource = ports;

        var restored = previous is not null
            ? ports.FirstOrDefault(p => string.Equals(p.PortName, previous, StringComparison.OrdinalIgnoreCase))
            : null;

        // Предвыбор только когда кандидат один: угадывать за оператора, к какому
        // из нескольких бортов подключаться, недопустимо.
        var candidates = PickCandidates(ports);

        PortCombo.SelectedItem = restored
            ?? (candidates.Count == 1 ? candidates[0] : null)
            ?? (ports.Count == 1 ? ports[0] : null);

        PortDetailText.Text = SelectedPortInfo?.Details ?? string.Empty;

        if (ports.Count == 0)
        {
            ShowLinkMessage(InfoBarSeverity.Informational, "COM-портов не найдено — подключите борт кабелем.");
        }
        else if (SelectedPortInfo is { IsBootloader: true })
        {
            ShowLinkMessage(
                InfoBarSeverity.Warning,
                "Устройство представилось загрузчиком: прошивка не запущена, связь по MAVLink не установится.");
        }
        else
        {
            LinkBar.IsOpen = false;
        }

        RenderLink();
        RenderReady();
    }

    private async void OnAutoConnectChanged(object sender, RoutedEventArgs e)
    {
        if (AutoConnectCheck.IsChecked == true && !_services.IsLinkConnected)
        {
            await TryAutoConnectAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Автоподключение работает только при единственном кандидате: выбирать за
    /// оператора, к какому из нескольких бортов подключиться, недопустимо.
    /// </summary>
    private async Task TryAutoConnectAsync()
    {
        var ports = PortCombo.ItemsSource as IReadOnlyList<SerialPortDescription> ?? [];

        if (ports.Count == 0)
        {
            ShowLinkMessage(InfoBarSeverity.Informational, "COM-портов не найдено — подключите борт кабелем.");
            return;
        }

        // Кандидатом считается только опознанная плата ArduPilot и только не в
        // режиме загрузчика. Если таких несколько — выбирает оператор.
        var candidates = PickCandidates(ports);

        if (candidates.Count != 1)
        {
            ShowLinkMessage(
                InfoBarSeverity.Informational,
                candidates.Count == 0
                    ? "Плата ArduPilot среди портов не опознана — выберите порт вручную."
                    : "Опознано несколько плат — выберите нужную вручную.");
            return;
        }

        PortCombo.SelectedItem = candidates[0];
        await ConnectAsync().ConfigureAwait(true);
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e) =>
        await ConnectAsync().ConfigureAwait(true);

    private async Task ConnectAsync()
    {
        if (SelectedPort is not { } port || _busy)
        {
            return;
        }

        SetBusy(true);
        LinkBar.IsOpen = false;
        LinkStateText.Text = "Подключение…";
        LinkDetailText.Text = port;

        try
        {
            await _services.ConnectAsync(port).ConfigureAwait(true);
            Log($"Установлена связь с бортом на {port}.");
        }
        catch (Exception ex)
        {
            ShowLinkMessage(InfoBarSeverity.Error, ex.Message);
            Log($"Подключение к {port} не удалось: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            RenderLink();
            RenderReady();
        }
    }

    private async void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            await _services.DisconnectAsync().ConfigureAwait(true);
            Log("Соединение с бортом закрыто.");
        }
        catch (Exception ex)
        {
            ShowLinkMessage(InfoBarSeverity.Warning, ex.Message);
        }
        finally
        {
            SetBusy(false);
            RenderLink();
            RenderReady();
        }
    }

    private void OnLinkChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        RenderLink();
        RenderReady();
    });

    private void RenderLink()
    {
        var connected = _services.IsLinkConnected;

        LinkIcon.Glyph = connected ? "" : "";
        LinkIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            connected ? "SystemFillColorSuccessBrush" : "TextFillColorTertiaryBrush"];

        LinkStateText.Text = connected ? "Связь с полётником установлена" : "Нет связи";
        LinkDetailText.Text = connected
            ? $"{_services.ConnectedPort} · {_services.ConnectedFirmware ?? "версия прошивки не получена"}"
            : "Выберите порт и подключитесь.";

        ConnectButton.IsEnabled = !connected && !_busy && SelectedPort is not null;
        DisconnectButton.IsEnabled = connected && !_busy;
        PortCombo.IsEnabled = !connected && !_busy;
        RefreshPortsButton.IsEnabled = !connected && !_busy;
    }

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
        RenderLink();
    }

    // --- Эталон ------------------------------------------------------------

    private async void OnRefreshProfilesClick(object sender, RoutedEventArgs e) =>
        await ReloadProfilesAsync().ConfigureAwait(true);

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderProfile();
        RenderReady();
    }

    private async Task ReloadProfilesAsync()
    {
        if (!_storeReady)
        {
            return;
        }

        try
        {
            var profiles = await _services.LoadProfilesAsync().ConfigureAwait(true);

            var previousId = SelectedProfile?.Id;
            var choices = profiles
                .Where(static p => !p.IsRetired)
                .Select(static p => new ProfileChoice(p, Describe(p)))
                .ToList();

            ProfileCombo.ItemsSource = choices;

            if (previousId is { } id)
            {
                ProfileCombo.SelectedItem = choices.FirstOrDefault(c => c.Profile.Id == id);
            }
            else if (choices.Count == 1)
            {
                ProfileCombo.SelectedIndex = 0;
            }

            if (choices.Count == 0)
            {
                ProfileBar.Severity = InfoBarSeverity.Warning;
                ProfileBar.Message = "Действующих эталонных профилей нет. Создайте профиль, считав параметры с эталонного борта.";
                ProfileBar.IsOpen = true;
            }
            else
            {
                ProfileBar.IsOpen = false;
            }
        }
        catch (Exception ex)
        {
            ProfileBar.Severity = InfoBarSeverity.Error;
            ProfileBar.Message = "Не удалось прочитать список эталонов: " + ex.Message;
            ProfileBar.IsOpen = true;
        }

        RenderProfile();
    }

    /// <summary>
    /// Отбирает порты, на которых может отвечать MAVLink.
    /// </summary>
    /// <remarks>
    /// Композитные платы F7/H7 показывают два CDC-порта с одинаковыми VID/PID:
    /// на нулевом интерфейсе живёт MAVLink, на втором — SLCAN. Без этого
    /// уточнения на такой плате кандидатов всегда двое и автоподключение не
    /// срабатывает никогда.
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

    private static string Describe(CalibrationProfile profile) =>
        profile.Description is { Length: > 0 } description
            ? $"{profile.Name} — {description}"
            : profile.Name;

    private void RenderProfile()
    {
        var profile = SelectedProfile;

        ProfileIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            profile is null ? "TextFillColorTertiaryBrush" : "SystemFillColorSuccessBrush"];

        if (profile is null)
        {
            ProfileStateText.Text = "Эталон не выбран";
            ProfileDetailText.Text = "Выберите профиль изделия.";
            ProfileFileText.Text = string.Empty;
            ProfileHashText.Text = string.Empty;
            return;
        }

        ProfileStateText.Text = profile.Name;
        ProfileDetailText.Text = profile.Description is { Length: > 0 } d
            ? d
            : "Описание не задано.";

        ProfileFileText.Text =
            $"Файл: {profile.ReferenceFileName} ({profile.ReferenceFormat}), параметров: {profile.ParamCount}, прогонов: {profile.RunCount}";

        // Хэш показывается целиком: по нему сверяют, что на стенде тот же эталон.
        ProfileHashText.Text = "SHA-256: " + profile.ReferenceHash;
    }

    // --- Готовность --------------------------------------------------------

    private void RenderReady()
    {
        var problems = new List<string>();

        if (!_services.IsLinkConnected)
        {
            problems.Add("нет связи с полётником");
        }

        if (SelectedProfile is null)
        {
            problems.Add("не выбран эталон");
        }

        var ready = problems.Count == 0;

        if (ready)
        {
            ReadyBar.Severity = InfoBarSeverity.Success;
            ReadyBar.Message = "Борт на связи, эталон выбран. Можно вводить идентификатор борта и начинать работу.";
        }
        else
        {
            ReadyBar.Severity = InfoBarSeverity.Informational;
            ReadyBar.Message = "Не готово: " + string.Join(", ", problems) + ".";
        }

        // HUD и панели появляются только после выбора борта и эталона: до этого
        // показывать нечего, а пустой прибор оператор читает как «всё по нулям».
        HudCard.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        PanelsRow.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        NotBuiltBar.IsOpen = ready;

        if (ready && !_hudTimer.IsEnabled)
        {
            _hudTimer.Start();
            RenderHud();
        }
        else if (!ready && _hudTimer.IsEnabled)
        {
            _hudTimer.Stop();
            Compasses.Clear();
        }
    }
}
