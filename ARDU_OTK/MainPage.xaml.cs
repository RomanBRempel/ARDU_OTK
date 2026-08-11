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
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI;
using Windows.UI;
using Windows.Foundation;

namespace ARDU_OTK;

/// <summary>РЎС‚СЂРѕРєР° РїР°РЅРµР»Рё РєРѕРјРїР°СЃРѕРІ.</summary>
public sealed class CompassRow
{
    /// <summary>РќРѕСЂРјР° РјРѕРґСѓР»СЏ РјР°РіРЅРёС‚РЅРѕРіРѕ РїРѕР»СЏ, РјР“СЃ, Рё РіСЂР°РЅРёС†С‹ РїСЂРµРґРїРѕР»С‘С‚РЅРѕР№ РїСЂРѕРІРµСЂРєРё.</summary>
    public const double FieldExpected = 530;

    public const double FieldMin = 185;

    public const double FieldMax = 875;

    /// <param name="sample">РџРѕРєР°Р·Р°РЅРёСЏ РјР°РіРЅРёС‚РѕРјРµС‚СЂР° РёР· С‚РµР»РµРјРµС‚СЂРёРё.</param>
    /// <param name="identity">
    /// РћРїРѕР·РЅР°РЅРёРµ РґР°С‚С‡РёРєР°: РІРЅРµС€РЅРёР№ РёР»Рё РІРЅСѓС‚СЂРµРЅРЅРёР№ Рё С‡С‚Рѕ Р·Р° Р¶РµР»РµР·Рѕ.
    /// <c>null</c> вЂ” РїР°СЂР°РјРµС‚СЂС‹ РѕРїРѕР·РЅР°РЅРёСЏ РµС‰С‘ РЅРµ РїСЂРѕС‡РёС‚Р°РЅС‹ Р»РёР±Рѕ Р±РѕСЂС‚ РёС… РЅРµ РѕС‚РґР°Р».
    /// </param>
    /// <remarks>
    /// рџ”ґ В«Р’РЅРµС€РЅРѕСЃС‚СЊВ» РїСЂРёС…РѕРґРёС‚ РЅРµ РёР· С‚РµР»РµРјРµС‚СЂРёРё, Р° РёР· РїР°СЂР°РјРµС‚СЂРѕРІ
    /// <c>COMPASS_DEV_ID*</c> Рё <c>COMPASS_EXTERNAL*</c>. РЎСѓРґРёС‚СЊ Рѕ РЅРµР№ РїРѕ
    /// РЅРѕРјРµСЂСѓ СЃР»РѕС‚Р° Р·Р°РїСЂРµС‰РµРЅРѕ: РїРѕСЂСЏРґРѕРє СЃР»РѕС‚РѕРІ Р·Р°РґР°С‘С‚СЃСЏ С‚Р°Р±Р»РёС†РµР№ РїСЂРёРѕСЂРёС‚РµС‚РѕРІ Рё
    /// РјРµРЅСЏРµС‚СЃСЏ РїСЂРё РїРµСЂРµСЃС‚Р°РЅРѕРІРєРµ, С‚Р°Рє С‡С‚Рѕ В«РїРµСЂРІС‹Р№ вЂ” Р·РЅР°С‡РёС‚ РІРЅРµС€РЅРёР№В» РЅРµРІРµСЂРЅРѕ
    /// СЂРѕРІРЅРѕ РЅР° С‚РµС… Р±РѕСЂС‚Р°С…, СЂР°РґРё РєРѕС‚РѕСЂС‹С… РїСЂРѕРІРµСЂРєР° Рё СЃСѓС‰РµСЃС‚РІСѓРµС‚.
    /// </remarks>
    public CompassRow(MagSample sample, CompassIdentityRow? identity = null)
    {
        var magnitude = sample.Magnitude;
        var inBand = magnitude is >= FieldMin and <= FieldMax;

        Title = $"РљРѕРјРїР°СЃ {sample.Instance + 1}";

        KindText = identity?.KindText ?? "РѕРїРѕР·РЅР°РЅРёРµ РЅРµ РїСЂРѕС‡РёС‚Р°РЅРѕ";
        DeviceText = identity?.DeviceText ?? string.Empty;

        // Р’РЅРµС€РЅРёР№, РІРЅСѓС‚СЂРµРЅРЅРёР№ Рё В«СЃСѓРґРёС‚СЊ РЅРµР»СЊР·СЏВ» РѕРєСЂР°С€РµРЅС‹ РїРѕ-СЂР°Р·РЅРѕРјСѓ: РїРѕСЃР»РµРґРЅРµРµ
        // РЅРµ РґРѕР»Р¶РЅРѕ РІС‹РіР»СЏРґРµС‚СЊ РєР°Рє РёСЃРїСЂР°РІРЅРѕРµ СЃРѕСЃС‚РѕСЏРЅРёРµ.
        ExternalVisibility = identity?.IsExternal == true ? Visibility.Visible : Visibility.Collapsed;
        InternalVisibility = identity?.IsExternal == false ? Visibility.Visible : Visibility.Collapsed;
        UnknownKindVisibility = identity?.IsExternal is null ? Visibility.Visible : Visibility.Collapsed;

        Detail = string.Create(CultureInfo.InvariantCulture, $"X {sample.X}  Y {sample.Y}  Z {sample.Z}");
        FieldText = string.Create(CultureInfo.InvariantCulture, $"{magnitude:0}");

        OkVisibility = inBand ? Visibility.Visible : Visibility.Collapsed;
        WarnVisibility = inBand ? Visibility.Collapsed : Visibility.Visible;

        if (!inBand)
        {
            Detail += magnitude < FieldMin ? "  В· РїРѕР»Рµ СЃР»Р°Р±РµРµ РЅРѕСЂРјС‹" : "  В· РїРѕР»Рµ СЃРёР»СЊРЅРµРµ РЅРѕСЂРјС‹";
        }
    }

    public string Title { get; }

    public string Detail { get; }

    public string FieldText { get; }

    /// <summary>Р’РЅРµС€РЅРёР№, РІРЅСѓС‚СЂРµРЅРЅРёР№ Р»РёР±Рѕ В«СЃСѓРґРёС‚СЊ РЅРµР»СЊР·СЏВ» вЂ” СЃР»РѕРІРѕРј.</summary>
    public string KindText { get; }

    /// <summary>Р§С‚Рѕ Р·Р° РґР°С‚С‡РёРє: С€РёРЅР°, Р°РґСЂРµСЃ, С‚РёРї. РџСѓСЃС‚Рѕ, РµСЃР»Рё РѕРїРѕР·РЅР°РЅРёРµ РЅРµ РїСЂРѕС‡РёС‚Р°РЅРѕ.</summary>
    public string DeviceText { get; }

    public Visibility OkVisibility { get; }

    public Visibility WarnVisibility { get; }

    public Visibility ExternalVisibility { get; }

    public Visibility InternalVisibility { get; }

    public Visibility UnknownKindVisibility { get; }
}

/// <summary>
/// РћРїРѕР·РЅР°РЅРёРµ РѕРґРЅРѕРіРѕ РєРѕРјРїР°СЃР°: РІРЅРµС€РЅРёР№ РёР»Рё РІРЅСѓС‚СЂРµРЅРЅРёР№ Рё С‡С‚Рѕ Р·Р° Р¶РµР»РµР·Рѕ.
/// </summary>
/// <remarks>
/// Р§РёС‚Р°РµС‚СЃСЏ СЃ Р±РѕСЂС‚Р° РѕРґРёРЅ СЂР°Р· РЅР° СЃРѕРµРґРёРЅРµРЅРёРµ Рё РЅРµ РїРµСЂРµСЃС‡РёС‚С‹РІР°РµС‚СЃСЏ РїРѕ РєР°Р¶РґРѕРјСѓ
/// РєР°РґСЂСѓ С‚РµР»РµРјРµС‚СЂРёРё: <c>COMPASS_DEV_ID*</c> РјРµРЅСЏРµС‚СЃСЏ С‚РѕР»СЊРєРѕ РїСЂРё РїРµСЂРµСЃС‚Р°РЅРѕРІРєРµ
/// СЃР»РѕС‚РѕРІ, Р° С‡С‚РµРЅРёРµ РїР°СЂР°РјРµС‚СЂРѕРІ РЅР° РєР°Р¶РґРѕРј С‚Р°РєС‚Рµ РёРЅРґРёРєР°С‚РѕСЂР° Р·Р°Р±РёР»Рѕ Р±С‹ РєР°РЅР°Р».
/// </remarks>
/// <param name="Slot">РќРѕРјРµСЂ СЃР»РѕС‚Р° 1..3.</param>
/// <param name="IsExternal">Р’РЅРµС€РЅРёР№ РєРѕРјРїР°СЃ. <c>null</c> вЂ” СЃСѓРґРёС‚СЊ РЅРµР»СЊР·СЏ.</param>
/// <param name="KindText">Р СѓСЃСЃРєРѕРµ РЅР°Р·РІР°РЅРёРµ РІРёРґР°.</param>
/// <param name="DeviceText">РћРїРёСЃР°РЅРёРµ РґР°С‚С‡РёРєР°.</param>
public sealed record CompassIdentityRow(int Slot, bool? IsExternal, string KindText, string DeviceText);

/// <summary>
/// Р Р°Р±РѕС‡РёР№ СЌРєСЂР°РЅ СЃС‚РµРЅРґР°: РІС‹Р±РѕСЂ Р±РѕСЂС‚Р° Рё СЌС‚Р°Р»РѕРЅР° РїР»Р°С€РєР°РјРё, РёРЅРґРёРєР°С‚РѕСЂ Рё РїР°РЅРµР»Рё.
/// </summary>
/// <remarks>
/// Р’С‹РїР°РґР°СЋС‰РёС… СЃРїРёСЃРєРѕРІ Р·РґРµСЃСЊ РЅРµС‚ РЅР°РјРµСЂРµРЅРЅРѕ: РІС‹Р±РѕСЂ вЂ” СЌС‚Рѕ РїР»Р°С€РєРё, РєРѕС‚РѕСЂС‹Рµ
/// СЂР°СЃРєСЂС‹РІР°СЋС‚СЃСЏ РѕРґРЅР° РІРІРµСЂС… Рё РѕСЃС‚Р°Р»СЊРЅС‹Рµ РІРЅРёР· РѕС‚ С‚РµРєСѓС‰РµР№, Р° РЅР°Р¶Р°С‚РёРµ РЅР° РїР»Р°С€РєСѓ
/// СЃСЂР°Р·Сѓ РІС‹РїРѕР»РЅСЏРµС‚ РґРµР№СЃС‚РІРёРµ, Р±РµР· РѕС‚РґРµР»СЊРЅРѕР№ РєРЅРѕРїРєРё РїРѕРґС‚РІРµСЂР¶РґРµРЅРёСЏ.
/// </remarks>
public sealed partial class MainPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    /// <summary>
    /// РЎР»РµР¶РµРЅРёРµ Р·Р° СЃРѕСЃС‚Р°РІРѕРј COM-РїРѕСЂС‚РѕРІ: РїР»Р°С‚Сѓ РјРѕРіСѓС‚ РІРѕС‚РєРЅСѓС‚СЊ Рё РІС‹РЅСѓС‚СЊ РїСЂРё
    /// СЂР°Р±РѕС‚Р°СЋС‰РµРј РїСЂРёР»РѕР¶РµРЅРёРё, Рё СЃРїРёСЃРѕРє РѕР±СЏР·Р°РЅ РјРµРЅСЏС‚СЊСЃСЏ СЃР°Рј.
    /// </summary>
    /// <remarks>
    /// РћРїСЂР°С€РёРІР°РµС‚СЃСЏ РґРµС€С‘РІС‹Р№ <c>GetPortNames</c>; РїРѕР»РЅРѕРµ РїРµСЂРµС‡РёСЃР»РµРЅРёРµ СЃ РѕРїРёСЃР°РЅРёРµРј
    /// СѓСЃС‚СЂРѕР№СЃС‚РІ С‡РµСЂРµР· WMI Р·Р°РїСѓСЃРєР°РµС‚СЃСЏ С‚РѕР»СЊРєРѕ РєРѕРіРґР° РЅР°Р±РѕСЂ РёРјС‘РЅ РёР·РјРµРЅРёР»СЃСЏ.
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

        // Р—Р°РєСЂС‹С‚РёРµ С‰РµР»С‡РєРѕРј РјРёРјРѕ вЂ” СЌС‚Рѕ С‚РѕР¶Рµ Р·Р°РєСЂС‹С‚РёРµ: Р±РµР· СЃРёРЅС…СЂРѕРЅРёР·Р°С†РёРё С„Р»Р°РіР°
        // СЃР»РµРґСѓСЋС‰РµРµ РЅР°Р¶Р°С‚РёРµ РЅР° РєР°СЂС‚РѕС‡РєСѓ В«СЃС…Р»РѕРїРЅСѓР»РѕВ» Р±С‹ СѓР¶Рµ Р·Р°РєСЂС‹С‚С‹Р№ СЃРїРёСЃРѕРє.
        PortPopup.Closed += (_, _) => _portsExpanded = false;
        ReferencePopup.Closed += (_, _) => _referencesExpanded = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ObservableCollection<CompassRow> Compasses { get; } = [];

    public ObservableCollection<CalibrationLogRow> LogEntries { get; } = [];

    /// <summary>Р’С‹Р±СЂР°РЅРЅС‹Р№ СЌС‚Р°Р»РѕРЅ.</summary>
    public CalibrationReference? SelectedReference => _selectedReference;

    /// <summary>Р’С‹Р±СЂР°РЅРЅС‹Р№ РїРѕСЂС‚ Р±РѕСЂС‚Р°.</summary>
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
            ReferenceBar.Message = "Р РµРµСЃС‚СЂ РЅРµРґРѕСЃС‚СѓРїРµРЅ: " + ex.Message;
            ReferenceBar.IsOpen = true;
        }

        if (_storeReady)
        {
            // РќР°СЃС‚СЂРѕР№РєРё СЂР°Р±РѕС‡РµРіРѕ РјРµСЃС‚Р° Р¶РёРІСѓС‚ РІ СЂРµРµСЃС‚СЂРµ, Р° РЅРµ РІ РїР°РјСЏС‚Рё СЃС‚СЂР°РЅРёС†С‹:
            // РїРµСЂРµС…РѕРґ РјРµР¶РґСѓ РІРєР»Р°РґРєР°РјРё РїРµСЂРµСЃРѕР·РґР°С‘С‚ СЃС‚СЂР°РЅРёС†Сѓ, Рё С„Р»Р°Рі, С…СЂР°РЅРёРјС‹Р№
            // С‚РѕР»СЊРєРѕ РІ РїРѕР»Рµ, РєР°Р¶РґС‹Р№ СЂР°Р· С‚РµСЂСЏР»СЃСЏ Р±С‹.
            try
            {
                _settings = await _services.LoadSettingsAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log("РќРµ СѓРґР°Р»РѕСЃСЊ РїСЂРѕС‡РёС‚Р°С‚СЊ РЅР°СЃС‚СЂРѕР№РєРё СЂР°Р±РѕС‡РµРіРѕ РјРµСЃС‚Р°: " + ex.Message);
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

        // РџСЂРѕРІРµСЂРєР° СЃРІСЏР·Рё РѕР±СЏР·Р°С‚РµР»СЊРЅР°: СЃС‚СЂР°РЅРёС†Р° РїРµСЂРµСЃРѕР·РґР°С‘С‚СЃСЏ РїСЂРё РєР°Р¶РґРѕРј РІРѕР·РІСЂР°С‚Рµ
        // СЃ СЌРєСЂР°РЅР° СЌС‚Р°Р»РѕРЅРѕРІ, Р° СЃРѕРµРґРёРЅРµРЅРёРµ Р¶РёРІС‘С‚ РІ AppServices Рё РїРµСЂРµР¶РёРІР°РµС‚ СЌС‚Рѕ.
        // Р‘РµР· РїСЂРѕРІРµСЂРєРё РІРѕР·РІСЂР°С‚ СЂРІР°Р» Р±С‹ Р¶РёРІРѕР№ РєР°РЅР°Р» Рё РїРѕРґРЅРёРјР°Р» РµРіРѕ Р·Р°РЅРѕРІРѕ.
        if (AutoConnectCheck.IsChecked == true && !_services.IsLinkConnected)
        {
            await TryAutoConnectAsync().ConfigureAwait(true);
        }
    }

    /// <summary>РЎРѕС…СЂР°РЅСЏРµС‚ РЅР°СЃС‚СЂРѕР№РєРё СЃС‚РµРЅРґР°, РЅРµ СЂРѕРЅСЏСЏ СЌРєСЂР°РЅ РїСЂРё РѕС‚РєР°Р·Рµ СЂРµРµСЃС‚СЂР°.</summary>
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
            Log("РќРµ СѓРґР°Р»РѕСЃСЊ СЃРѕС…СЂР°РЅРёС‚СЊ РЅР°СЃС‚СЂРѕР№РєРё: " + ex.Message);
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

    /// <summary>Р—Р°РјРµС‡Р°РµС‚ РїРѕСЏРІР»РµРЅРёРµ Рё РїСЂРѕРїР°Р¶Сѓ СѓСЃС‚СЂРѕР№СЃС‚РІ, РїРѕРєР° РїСЂРёР»РѕР¶РµРЅРёРµ РѕС‚РєСЂС‹С‚Рѕ.</summary>
    private async void OnPortsTick(object? sender, object e)
    {
        if (_portsScanBusy || _portsExpanded)
        {
            // РџРѕРєР° СЃРїРёСЃРѕРє СЂР°СЃРєСЂС‹С‚, РЅРµ РїРѕРґРјРµРЅСЏРµРј РїР»Р°С€РєРё РїРѕРґ СЂСѓРєРѕР№ РѕРїРµСЂР°С‚РѕСЂР°.
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
                Log($"РћР±РЅР°СЂСѓР¶РµРЅРѕ СѓСЃС‚СЂРѕР№СЃС‚РІРѕ: {port}.");
            }

            foreach (var port in vanished)
            {
                Log($"РЈСЃС‚СЂРѕР№СЃС‚РІРѕ РѕС‚РєР»СЋС‡РµРЅРѕ: {port}.");
            }

            // РџСЂРѕРїР°Р» РїРѕСЂС‚, РЅР° РєРѕС‚РѕСЂРѕРј РґРµСЂР¶Р°Р»Р°СЃСЊ СЃРІСЏР·СЊ, вЂ” СЃРѕРѕР±С‰Р°РµРј РїСЂСЏРјРѕ: Р±РѕСЂС‚
            // РІС‹РґРµСЂРЅСѓР»Рё РёР»Рё РѕРЅ СѓС€С‘Р» РІ РїРµСЂРµР·Р°РіСЂСѓР·РєСѓ.
            if (_services.ConnectedPort is { } active
                && vanished.Contains(active, StringComparer.OrdinalIgnoreCase))
            {
                ShowLinkMessage(InfoBarSeverity.Warning, $"РџРѕСЂС‚ {active} РёСЃС‡РµР· вЂ” СЃРІСЏР·СЊ СЃ Р±РѕСЂС‚РѕРј РїРѕС‚РµСЂСЏРЅР°.");
                await _services.DisconnectAsync().ConfigureAwait(true);
            }
            else if (AutoConnectCheck.IsChecked == true && !_services.IsLinkConnected && appeared.Count > 0)
            {
                await TryAutoConnectAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Log("РћРїСЂРѕСЃ РїРѕСЂС‚РѕРІ РЅРµ СѓРґР°Р»СЃСЏ: " + ex.Message);
        }
        finally
        {
            _portsScanBusy = false;
        }
    }

    // --- РџР»Р°С€РєРё ------------------------------------------------------------

    /// <summary>
    /// РЎС‚СЂРѕРёС‚ РїР»Р°С€РєРё РІРѕРєСЂСѓРі РІС‹Р±СЂР°РЅРЅРѕР№: РѕРґРЅР° РІС‹С€Рµ, РѕСЃС‚Р°Р»СЊРЅС‹Рµ РЅРёР¶Рµ.
    /// </summary>
    /// <param name="putPreviousAbove">
    /// РћС‚РґР°РІР°С‚СЊ Р»Рё РІРµСЂС…РЅСЋСЋ РїРѕР·РёС†РёСЋ РїСЂРµРґС‹РґСѓС‰РµР№ РїРѕ СЃРїРёСЃРєСѓ РїР»Р°С€РєРµ. РЈ СЌС‚Р°Р»РѕРЅРѕРІ РѕРЅР°
    /// Р·Р°РЅСЏС‚Р° РґРµР№СЃС‚РІРёРµРј В«Р”РѕР±Р°РІРёС‚СЊ СЌС‚Р°Р»РѕРЅВ», РїРѕСЌС‚РѕРјСѓ С‚Р°Рј <c>false</c>: РґРµР№СЃС‚РІРёРµ Рё
    /// РІС‹Р±РѕСЂ РЅРµ СЃРјРµС€РёРІР°СЋС‚СЃСЏ РІ РѕРґРЅРѕРј СЃС‚РѕР»Р±С†Рµ.
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

            // РћРґРЅР° РїР»Р°С€РєР° СЂР°СЃРєСЂС‹РІР°РµС‚СЃСЏ РІРІРµСЂС… вЂ” СЌС‚Рѕ РїСЂРµРґС‹РґСѓС‰Р°СЏ РїРѕ СЃРїРёСЃРєСѓ.
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
    /// РџР»Р°С€РєР° В«Р”РѕР±Р°РІРёС‚СЊ СЌС‚Р°Р»РѕРЅВ»: С‚Рѕ Р¶Рµ РѕС„РѕСЂРјР»РµРЅРёРµ, С‡С‚Рѕ Сѓ СЌС‚Р°Р»РѕРЅРѕРІ, РїР»СЋСЃ
    /// РїРѕРґСЃРІРµС‚РєР° СЃРЅРёР·Сѓ Рё Р°РєС†РµРЅС‚РЅС‹Р№ РєР°РЅС‚.
    /// </summary>
    /// <remarks>
    /// Р’С‹РіР»СЏРґРёС‚ РєР°Рє РїР°РЅРµР»СЊ РЅР°РјРµСЂРµРЅРЅРѕ вЂ” СЌС‚Рѕ РЅРµ РєРЅРѕРїРєР° РІ РѕС‚РґРµР»СЊРЅРѕРј СЂСЏРґСѓ, Р° С‚Р°РєРѕР№
    /// Р¶Рµ СЌР»РµРјРµРЅС‚ СЂР°СЃРєСЂС‹С‚РѕРіРѕ СЃС‚РѕР»Р±С†Р°. РќРѕ РєР°РЅС‚ Рё СЃРІРµС‡РµРЅРёРµ РѕС‚Р»РёС‡Р°СЋС‚ РґРµР№СЃС‚РІРёРµ РѕС‚
    /// РІС‹Р±РѕСЂР°: РЅР°Р¶Р°С‚РёРµ СѓРІРѕРґРёС‚ РЅР° СЌРєСЂР°РЅ Р·Р°РІРµРґРµРЅРёСЏ, Р° РЅРµ РјРµРЅСЏРµС‚ С‚РµРєСѓС‰РёР№ СЌС‚Р°Р»РѕРЅ.
    /// </remarks>
    private UIElement CreateAddReferenceTile()
    {
        var tile = CreateTile(
            "Р”РѕР±Р°РІРёС‚СЊ СЌС‚Р°Р»РѕРЅ",
            _services.IsLinkConnected
                ? "РІС‹Р±СЂР°С‚СЊ СЌС‚Р°Р»РѕРЅ РІ С„Р°Р№Р»Рµ РёР»Рё СЃРЅСЏС‚СЊ СЃ РїРѕРґРєР»СЋС‡С‘РЅРЅРѕРіРѕ Р±РѕСЂС‚Р°"
                : "РІС‹Р±СЂР°С‚СЊ СЌС‚Р°Р»РѕРЅ РІ С„Р°Р№Р»Рµ",
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
    /// РЈРІРѕРґРёС‚ РЅР° СЌРєСЂР°РЅ Р·Р°РІРµРґРµРЅРёСЏ СЌС‚Р°Р»РѕРЅР°.
    /// </summary>
    /// <remarks>
    /// Р–РёРІРѕРµ СЃРѕРµРґРёРЅРµРЅРёРµ РїСЂРё СЌС‚РѕРј РЅРµ СЂРІС‘С‚СЃСЏ: СЃРЅРёРјРѕРє СЌС‚Р°Р»РѕРЅР° С‡РёС‚Р°РµС‚СЃСЏ РїРѕ С‚РѕРјСѓ Р¶Рµ
    /// РєР°РЅР°Р»Сѓ, Рё РїРѕРґРєР»СЋС‡С‘РЅРЅС‹Р№ РѕР±СЂР°Р·РµС† РѕР±СЏР·Р°РЅ РѕСЃС‚Р°С‚СЊСЃСЏ РїРѕРґРєР»СЋС‡С‘РЅРЅС‹Рј.
    /// </remarks>
    private void OpenReferenceEditor()
    {
        _referencesExpanded = false;
        RenderExpansion();

        Frame.Navigate(typeof(ReferenceEditorPage), new ReferenceEditorArgs(_services, null));
    }

    /// <summary>
    /// РџСЂР°РІРєР° С‚РµРєСѓС‰РµРіРѕ СЌС‚Р°Р»РѕРЅР°.
    /// </summary>
    /// <remarks>
    /// Р—РґРµСЃСЊ РїСЂР°РІСЏС‚СЃСЏ С‚РѕР»СЊРєРѕ РёРјСЏ, РѕРїРёСЃР°РЅРёРµ Рё вЂ” РїРѕРєР° РїРѕ СЌС‚Р°Р»РѕРЅСѓ РЅРµ СЃРґР°РІР°Р»Рё РїР»Р°С‚ вЂ”
    /// РґРѕРїСѓСЃРєРё. РЎР°Рј СЌС‚Р°Р»РѕРЅ РЅРµРёР·РјРµРЅСЏРµРј, Р° РІС‹РІРѕРґ РёР· РѕР±СЂР°С‰РµРЅРёСЏ Рё РїСЂРѕС‡Р°СЏ СЂР°Р±РѕС‚Р° СЃРѕ
    /// СЃРїРёСЃРєРѕРј Р¶РёРІСѓС‚ РІ СЂР°Р·РґРµР»Рµ РЅР°СЃС‚СЂРѕРµРє: РЅР° СЂР°Р±РѕС‡РµРј СЌРєСЂР°РЅРµ РёРј РЅРµ РјРµСЃС‚Рѕ, РѕРїРµСЂР°С‚РѕСЂ
    /// Сѓ СЃС‚РµРЅРґР° Р·Р°РЅСЏС‚ Р±РѕСЂС‚РѕРј, Р° РЅРµ РІРµРґРµРЅРёРµРј СЃРїСЂР°РІРѕС‡РЅРёРєР°.
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

    // --- РџСЂРёС‘РјРєР° РїСЂСЏРјРѕ РЅР° СЂР°Р±РѕС‡РµРј СЌРєСЂР°РЅРµ ------------------------------------

    /// <summary>Р Р°СЃС…РѕР¶РґРµРЅРёСЏ РїРѕСЃР»РµРґРЅРµР№ СЃРІРµСЂРєРё.</summary>
    /// <remarks>
    /// РџСЂРёС‘РјРєР° Р¶РёРІС‘С‚ РЅР° СЂР°Р±РѕС‡РµРј СЌРєСЂР°РЅРµ, Р° РЅРµ РІ РѕС‚РґРµР»СЊРЅРѕРј РѕРєРЅРµ: РѕРїРµСЂР°С‚РѕСЂ Сѓ
    /// СЃС‚РµРЅРґР° СЃРјРѕС‚СЂРёС‚ РЅР° РїСЂРёР±РѕСЂС‹, РєРѕРјРїР°СЃС‹ Рё СЂР°СЃС…РѕР¶РґРµРЅРёСЏ РѕРґРЅРѕРІСЂРµРјРµРЅРЅРѕ, Рё
    /// РѕС‚РґРµР»СЊРЅРѕРµ РѕРєРЅРѕ Р·Р°СЃС‚Р°РІР»СЏР»Рѕ Р±С‹ РµРіРѕ РїРµСЂРµРєР»СЋС‡Р°С‚СЊСЃСЏ РјРµР¶РґСѓ РЅРёРјРё РІ РјРѕРјРµРЅС‚,
    /// РєРѕРіРґР° РѕРЅ СЂРµС€Р°РµС‚ СЃСѓРґСЊР±Сѓ РїР»Р°С‚С‹.
    /// </remarks>
    public ObservableCollection<ParameterDifferenceRow> Differences { get; } = new();

    private AcceptanceSession? _session;
    private CancellationTokenSource? _acceptanceCts;
    private bool _acceptanceBusy;
    private bool _diffAccepted;

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
    /// РђРІС‚РѕРјР°С‚РёС‡РµСЃРєР°СЏ С‡Р°СЃС‚СЊ РїСЂРёС‘РјРєРё: СЃРІРµСЂРєР°, РѕС‡РµСЂРµРґСЊ РєРѕРјРїР°СЃРѕРІ, РїРµСЂРµРЅРѕСЃ,
    /// РїРµСЂРµР·Р°РіСЂСѓР·РєР°, РїРѕРІС‚РѕСЂРЅР°СЏ СЃРІРµСЂРєР°.
    /// </summary>
    /// <remarks>
    /// рџ”ґ РћСЃС‚Р°РЅР°РІР»РёРІР°РµС‚СЃСЏ РЅР° РїРѕРєР°Р·Рµ СЂР°СЃС…РѕР¶РґРµРЅРёР№ Рё РґР°Р»СЊС€Рµ РЅРµ РёРґС‘С‚. Р Р°Р·РІРёР»РєСѓ
    /// В«РїСЂР°РІРёС‚СЊ РёР»Рё РїСЂРёРЅСЏС‚СЊВ» РїСЂРѕС…РѕРґРёС‚ С‡РµР»РѕРІРµРє: Р°РІС‚РѕРјР°С‚, СЂРµС€Р°СЋС‰РёР№ Р·Р° РЅРµРіРѕ, Р»РёР±Рѕ
    /// РѕСЃС‚Р°РЅР°РІР»РёРІР°РµС‚ РіРѕРґРЅРѕРµ РёР·РґРµР»РёРµ, Р»РёР±Рѕ РїСЂРѕРїСѓСЃРєР°РµС‚ РЅРµРіРѕРґРЅРѕРµ.
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

        try
        {
            await CloseSessionAsync().ConfigureAwait(true);

            AppendLog(MavSeverity.Info, "РџСЂРёС‘РјРєР°: РѕС‚РєСЂС‹РІР°СЋ РєР°РЅР°Р» СЃРІСЏР·РёвЂ¦");
            _session = await _services.BeginAcceptanceAsync(port, ct).ConfigureAwait(true);

            var expected = reference.ParseParameters().Values;
            var roles = reference.ToRoleMap();

            CompareStateText.Text = "Р’С‹С‡РёС‚С‹РІР°СЋ РїСЂРѕС€РёРІРєСѓ Р±РѕСЂС‚Р°вЂ¦";
            var plan = await _session.CompareAsync(expected, roles, progress, ct, detail).ConfigureAwait(true);
            AppendLog(MavSeverity.Info,
                $"РЎРІРµСЂРєР°: СЃРѕРїРѕСЃС‚Р°РІР»РµРЅРѕ {plan.Compared}, СЃРѕРІРїР°Р»Рѕ {plan.Matched}, СЂР°СЃС…РѕРґРёС‚СЃСЏ {plan.Differences.Count}.");

            SetCompassBusy(true, "РѕС‡РµСЂРµРґСЊ РєРѕРјРїР°СЃРѕРІвЂ¦");
            var primary = await _session.MakeExternalCompassPrimaryAsync(progress, ct).ConfigureAwait(true);
            SetCompassBusy(false);

            AppendLog(primary.Ok ? MavSeverity.Info : MavSeverity.Error, primary.Message);
            if (!primary.Ok)
            {
                ShowCompare(plan, "РџСЂРёС‘РјРєР° РѕСЃС‚Р°РЅРѕРІР»РµРЅР°: " + primary.Message);
                return;
            }

            // РћРїРѕР·РЅР°РЅРёРµ РєРѕРјРїР°СЃРѕРІ РїРѕСЃР»Рµ РїРµСЂРµСЃС‚Р°РЅРѕРІРєРё СѓСЃС‚Р°СЂРµР»Рѕ вЂ” РїРµСЂРµС‡РёС‚С‹РІР°РµРј.
            await LoadCompassIdentityAsync().ConfigureAwait(true);

            if (plan.Writable.Count > 0)
            {
                CompareStateText.Text = $"РџРµСЂРµРЅРѕС€Сѓ СЂР°СЃС…РѕР¶РґРµРЅРёСЏ: {plan.Writable.Count}вЂ¦";
                var records = await _session.ApplyAsync(plan.Writable, progress, ct).ConfigureAwait(true);
                var ok = records.Count(static r => r.Outcome == WriteOutcome.Verified);
                AppendLog(MavSeverity.Info, $"РџРµСЂРµРЅРѕСЃ: Р·Р°РїРёСЃР°РЅРѕ {ok}, РѕС‚РєР»РѕРЅРµРЅРѕ {records.Count - ok}.");
            }

            var reboot = await _session.RebootAsync(progress, ct).ConfigureAwait(true);
            AppendLog(reboot.Ok ? MavSeverity.Info : MavSeverity.Error, reboot.Message);
            if (!reboot.Ok)
            {
                ShowCompare(plan, "РџСЂРёС‘РјРєР° РѕСЃС‚Р°РЅРѕРІР»РµРЅР°: " + reboot.Message);
                return;
            }

            CompareStateText.Text = "РџРѕРІС‚РѕСЂРЅР°СЏ СЃРІРµСЂРєР° РїРѕСЃР»Рµ РїРµСЂРµР·Р°РіСЂСѓР·РєРёвЂ¦";
            var after = await _session.CompareAsync(expected, roles, progress, ct, detail).ConfigureAwait(true);
            await LoadCompassIdentityAsync().ConfigureAwait(true);

            ShowCompare(after, after.IsClean
                ? "Р‘РѕСЂС‚ СЃРѕРІРїР°Р» СЃ СЌС‚Р°Р»РѕРЅРѕРј РїРѕ РІСЃРµРј РєРѕРЅС‚СЂРѕР»РёСЂСѓРµРјС‹Рј РїР°СЂР°РјРµС‚СЂР°Рј."
                : $"РћСЃС‚Р°Р»РѕСЃСЊ СЂР°СЃС…РѕР¶РґРµРЅРёР№: {after.Differences.Count}. РСЃРїСЂР°РІСЊС‚Рµ Р·Р°РїРёСЃСЊСЋ РёР»Рё РїСЂРёРјРёС‚Рµ РєР°Рє РµСЃС‚СЊ вЂ” "
                  + "РґРѕ СЌС‚РѕРіРѕ РєР°Р»РёР±СЂРѕРІРєРё Р·Р°Р±Р»РѕРєРёСЂРѕРІР°РЅС‹.");

            AppendLog(MavSeverity.Info, $"РџРѕРІС‚РѕСЂРЅР°СЏ СЃРІРµСЂРєР°: СЂР°СЃС…РѕР¶РґРµРЅРёР№ {after.Differences.Count}.");
        }
        catch (OperationCanceledException)
        {
            CompareStateText.Text = "РџСЂРёС‘РјРєР° РѕС‚РјРµРЅРµРЅР° РѕРїРµСЂР°С‚РѕСЂРѕРј.";
            AppendLog(MavSeverity.Warning, "РџСЂРёС‘РјРєР° РѕС‚РјРµРЅРµРЅР° РѕРїРµСЂР°С‚РѕСЂРѕРј. РЎРѕСЃС‚РѕСЏРЅРёРµ Р±РѕСЂС‚Р° РјРѕРіР»Рѕ РѕСЃС‚Р°С‚СЊСЃСЏ РёР·РјРµРЅС‘РЅРЅС‹Рј.");
        }
        catch (Exception ex)
        {
            CompareStateText.Text = "РЎР±РѕР№: " + ex.Message;
            AppendLog(MavSeverity.Error, $"РџСЂРёС‘РјРєР° РїСЂРµСЂРІР°РЅР°: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _acceptanceCts?.Dispose();
            _acceptanceCts = null;
            SetCompassBusy(false);
            SetAcceptanceBusy(false);
        }
    }

    /// <summary>
    /// Р—Р°РєСЂС‹РІР°РµС‚ РїСЂРёС‘РјРєСѓ Рё РІРѕР·РІСЂР°С‰Р°РµС‚ РЅР°Р±Р»СЋРґР°С‚РµР»СЊРЅРѕРµ СЃРѕРµРґРёРЅРµРЅРёРµ.
    /// </summary>
    /// <remarks>
    /// рџ”ґ Р’РѕР·РІСЂР°С‚ РѕР±СЏР·Р°С‚РµР»РµРЅ Рё РЅРµ РјРѕР¶РµС‚ Р±С‹С‚СЊ РѕСЃС‚Р°РІР»РµРЅ Р°РІС‚РѕРїРѕРґРєР»СЋС‡РµРЅРёСЋ. РџРѕСЂС‚ РІСЃСЋ
    /// РїСЂРёС‘РјРєСѓ РїСЂРёРЅР°РґР»РµР¶Р°Р» СЃРµСЃСЃРёРё, Р°РІС‚РѕРїРѕРґРєР»СЋС‡РµРЅРёРµ РїСЂРё СЌС‚РѕРј РЅР°РјРµСЂРµРЅРЅРѕ РјРѕР»С‡Р°Р»Рѕ,
    /// Рё РїРѕСЃР»Рµ РѕСЃРІРѕР±РѕР¶РґРµРЅРёСЏ РїРѕСЂС‚Р° РµРіРѕ РЅРёРєС‚Рѕ РЅРµ СЂР°Р·Р±СѓРґРёС‚: РїСЂРёР±РѕСЂС‹ РѕСЃС‚Р°Р»РёСЃСЊ Р±С‹
    /// РјС‘СЂС‚РІС‹РјРё, Р° В«РЎС‚Р°СЂС‚ РїСЂРёС‘РјРєРёВ» вЂ” Р·Р°Р±Р»РѕРєРёСЂРѕРІР°РЅРЅС‹Рј РїРѕ РїСЂРёС‡РёРЅРµ В«РЅРµС‚ СЃРІСЏР·РёВ»,
    /// С…РѕС‚СЏ Р±РѕСЂС‚ СЃС‚РѕРёС‚ РЅР° СЃС‚РѕР»Рµ РїРѕРґРєР»СЋС‡С‘РЅРЅС‹Рј.
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
            AppendLog(MavSeverity.Info, "РџСЂРёС‘РјРєР° Р·Р°РєСЂС‹С‚Р°, РІРѕР·РІСЂР°С‰Р°СЋ РЅР°Р±Р»СЋРґР°С‚РµР»СЊРЅРѕРµ СЃРѕРµРґРёРЅРµРЅРёРµвЂ¦");

            // РРјСЏ РїРѕСЂС‚Р° РїРѕСЃР»Рµ РїРµСЂРµР·Р°РіСЂСѓР·РѕРє РјРѕРіР»Рѕ СЃРјРµРЅРёС‚СЊСЃСЏ, РїРѕСЌС‚РѕРјСѓ СЃРЅР°С‡Р°Р»Р°
            // РїСЂРѕР±СѓРµРј С‚Рѕ, РЅР° РєРѕС‚РѕСЂРѕРј РїСЂРёС‘РјРєР° Р·Р°РєРѕРЅС‡РёР»Р°СЃСЊ, Р° Р·Р°С‚РµРј РѕР±С‰РёР№ РѕС‚Р±РѕСЂ.
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
    /// Р”РІРёРіР°РµС‚ РїРѕР»РѕСЃСѓ РїРѕ СЃРѕРѕР±С‰РµРЅРёСЏРј Рѕ С…РѕРґРµ С‡С‚РµРЅРёСЏ.
    /// </summary>
    /// <remarks>
    /// РџРѕР»РѕСЃР° Р·Р°РїРѕР»РЅСЏРµС‚СЃСЏ РїРѕ С‡РёСЃР»Р°Рј В«РїСЂРёРЅСЏС‚Рѕ N РёР· MВ» РёР· СЃР°РјРѕРіРѕ РєР°РЅР°Р»Р°, Р° РЅРµ РїРѕ
    /// С‚Р°Р№РјРµСЂСѓ: РІС‹РґСѓРјР°РЅРЅС‹Р№ С…РѕРґ, РЅРµ СЃРІСЏР·Р°РЅРЅС‹Р№ СЃ СЂР°Р±РѕС‚РѕР№, РїРѕРєР°Р·С‹РІР°РµС‚ РґРІРёР¶РµРЅРёРµ
    /// С‚Р°Рј, РіРґРµ РµРіРѕ РЅРµС‚, Рё СЌС‚Рѕ С…СѓР¶Рµ РЅРµРїРѕРґРІРёР¶РЅРѕР№ РїРѕР»РѕСЃС‹.
    /// </remarks>
    private void AnimateCompareProgress(string text)
    {
        var of = text.IndexOf(" РёР· ", StringComparison.Ordinal);
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
        foreach (var difference in plan.Differences)
        {
            Differences.Add(new ParameterDifferenceRow(difference));
        }

        CompareStateText.Text = state;
        CompareMatchedText.Text = plan.Matched.ToString(CultureInfo.InvariantCulture);
        CompareDiffText.Text = plan.Differences.Count.ToString(CultureInfo.InvariantCulture);
        CompareSkippedText.Text = plan.SkippedUncontrolled.ToString(CultureInfo.InvariantCulture);

        CompareProgress.Visibility = Visibility.Collapsed;
        CompareCountsPanel.Visibility = Visibility.Visible;
        CompareActionsPanel.Visibility = Visibility.Visible;

        UpdateAcceptanceCommands();
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
                .ApplyAsync(rows.Select(static r => r.Source).ToArray(), progress, CancellationToken.None)
                .ConfigureAwait(true);

            foreach (var record in records)
            {
                var row = rows.FirstOrDefault(r => string.Equals(r.Name, record.Name, StringComparison.Ordinal));
                row?.ApplyOutcome(record);
            }

            var ok = records.Count(static r => r.Outcome == WriteOutcome.Verified);
            CompareStateText.Text = $"Р—Р°РїРёСЃР°РЅРѕ {ok}, РѕС‚РєР»РѕРЅРµРЅРѕ {records.Count - ok}. "
                + "Р§Р°СЃС‚СЊ Р·РЅР°С‡РµРЅРёР№ РІСЃС‚СѓРїРёС‚ РІ СЃРёР»Сѓ РїРѕСЃР»Рµ РїРµСЂРµР·Р°РіСЂСѓР·РєРё Р±РѕСЂС‚Р°.";
            AppendLog(MavSeverity.Info, $"Р—Р°РїРёСЃСЊ РїРѕ С‚СЂРµР±РѕРІР°РЅРёСЋ: РїСЂРёРЅСЏС‚Рѕ {ok}, РѕС‚РєР»РѕРЅРµРЅРѕ {records.Count - ok}.");
        }
        catch (Exception ex)
        {
            CompareStateText.Text = "Р—Р°РїРёСЃСЊ РЅРµ РІС‹РїРѕР»РЅРµРЅР°: " + ex.Message;
            AppendLog(MavSeverity.Error, "Р—Р°РїРёСЃСЊ РЅРµ РІС‹РїРѕР»РЅРµРЅР°: " + ex.Message);
        }
        finally
        {
            CompareProgress.Visibility = Visibility.Collapsed;
            SetAcceptanceBusy(false);
        }
    }

    /// <remarks>
    /// рџ”ґ РџСЂРёРЅСЏС‚С‹Рµ СЂР°СЃС…РѕР¶РґРµРЅРёСЏ РїРµСЂРµС‡РёСЃР»СЏСЋС‚СЃСЏ РІ Р¶СѓСЂРЅР°Р»Рµ РїРѕРёРјС‘РЅРЅРѕ. В«РџСЂРёРЅСЏС‚Рѕ РєР°Рє
    /// РµСЃС‚СЊВ» Р±РµР· СЃРїРёСЃРєР° РёРјС‘РЅ С‡РµСЂРµР· РїРѕР»РіРѕРґР° РЅРµРѕС‚Р»РёС‡РёРјРѕ РѕС‚ В«СЃРѕС€Р»РѕСЃСЊ СЃР°РјРѕВ».
    /// </remarks>
    private void OnAcceptDiffClick(object sender, RoutedEventArgs e)
    {
        var outstanding = Differences
            .Where(static d => d.OutcomeVisibility == Visibility.Collapsed)
            .Select(static d => d.Name)
            .ToArray();

        _diffAccepted = true;

        AppendLog(MavSeverity.Warning, outstanding.Length == 0
            ? "РћРїРµСЂР°С‚РѕСЂ РїСЂРёРЅСЏР» СЃРѕСЃС‚РѕСЏРЅРёРµ Р±РѕСЂС‚Р°: РЅРµСЂР°Р·РѕР±СЂР°РЅРЅС‹С… СЂР°СЃС…РѕР¶РґРµРЅРёР№ РЅРµС‚."
            : "РћРїРµСЂР°С‚РѕСЂ РїСЂРёРЅСЏР» СЂР°СЃС…РѕР¶РґРµРЅРёСЏ РєР°Рє РµСЃС‚СЊ: " + string.Join(", ", outstanding));

        CompareStateText.Text = outstanding.Length == 0
            ? "Р Р°СЃС…РѕР¶РґРµРЅРёСЏ СЂР°Р·РѕР±СЂР°РЅС‹. РљР°Р»РёР±СЂРѕРІРєРё СЂР°Р·Р±Р»РѕРєРёСЂРѕРІР°РЅС‹."
            : $"РџСЂРёРЅСЏС‚Рѕ РєР°Рє РµСЃС‚СЊ: {outstanding.Length}. РРјРµРЅР° РїРµСЂРµС‡РёСЃР»РµРЅС‹ РІ Р¶СѓСЂРЅР°Р»Рµ. РљР°Р»РёР±СЂРѕРІРєРё СЂР°Р·Р±Р»РѕРєРёСЂРѕРІР°РЅС‹.";

        UpdateAcceptanceCommands();
    }

    // --- РљРѕРјР°РЅРґС‹ РѕРїРµСЂР°С‚РѕСЂР° --------------------------------------------------

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
            AppendLog(MavSeverity.Error, "РљР°Р»РёР±СЂРѕРІРєР° СѓСЂРѕРІРЅСЏ РЅРµ РІС‹РїРѕР»РЅРµРЅР°: " + ex.Message);
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
                "РљСѓСЂСЃ Р±РѕСЂС‚Р° РЅРµ Р·Р°РґР°РЅ. РљР°Р»РёР±СЂРѕРІРєР° РїРѕ РЅРµРІРµСЂРЅРѕРјСѓ РєСѓСЂСЃСѓ РѕСЃС‚Р°РІРёС‚ РєРѕРјРїР°СЃ В«СѓСЃРїРµС€РЅРѕВ» РѕС‚РєР°Р»РёР±СЂРѕРІР°РЅРЅС‹Рј "
              + "РЅР° С‡СѓР¶РѕРµ РЅР°РїСЂР°РІР»РµРЅРёРµ, Рё СЌС‚Рѕ РЅРµ РїРѕРєР°Р¶РµС‚ РЅРё РѕРґРЅР° РїСЂРѕРІРµСЂРєР°.");
            return;
        }

        SetAcceptanceBusy(true);
        SetCompassBusy(true, "РєР°Р»РёР±СЂРѕРІРєР°вЂ¦");
        try
        {
            var progress = new Progress<string>(text => CompassBusyText.Text = text);
            var result = await _session.CompassCalibrationAsync(heading, progress, CancellationToken.None)
                .ConfigureAwait(true);

            AppendLog(result.Ok ? MavSeverity.Info : MavSeverity.Error, result.Message);
            CompareStateText.Text = result.Message;

            await LoadCompassIdentityAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppendLog(MavSeverity.Error, "РљР°Р»РёР±СЂРѕРІРєР° РєРѕРјРїР°СЃР° РЅРµ РІС‹РїРѕР»РЅРµРЅР°: " + ex.Message);
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
            foreach (var blocker in readiness.Blockers)
            {
                AppendLog(MavSeverity.Warning, "РњРµС€Р°РµС‚ РІР·РІРµРґРµРЅРёСЋ: " + blocker);
            }

            ReadyBar.Severity = readiness.Ready ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            ReadyBar.Message = readiness.Ready
                ? "Р“РћР”Р•Рќ: Р±РѕСЂС‚ РіРѕС‚РѕРІ Р±С‹С‚СЊ РІР·РІРµРґС‘РЅРЅС‹Рј."
                : readiness.Detail;
        }
        catch (Exception ex)
        {
            AppendLog(MavSeverity.Error, "РџСЂРѕРІРµСЂРєР° РіРѕС‚РѕРІРЅРѕСЃС‚Рё РЅРµ РІС‹РїРѕР»РЅРµРЅР°: " + ex.Message);
        }
        finally
        {
            SetAcceptanceBusy(false);

            // РџСЂРѕРІРµСЂРєР° РіРѕС‚РѕРІРЅРѕСЃС‚Рё вЂ” РїРѕСЃР»РµРґРЅРёР№ С€Р°Рі РїСЂРёС‘РјРєРё. РџРѕСЂС‚ РѕСЃРІРѕР±РѕР¶РґР°РµС‚СЃСЏ
            // Р·РґРµСЃСЊ, РёРЅР°С‡Рµ РѕРЅ РѕСЃС‚Р°Р»СЃСЏ Р±С‹ Р·Р°РЅСЏС‚С‹Рј СЃРµСЃСЃРёРµР№ РґРѕ РїРµСЂРµР·Р°РїСѓСЃРєР°
            // РїСЂРёР»РѕР¶РµРЅРёСЏ, Р° РїСЂРёР±РѕСЂС‹ вЂ” РјС‘СЂС‚РІС‹РјРё.
            await ReleaseAcceptanceAsync().ConfigureAwait(true);
        }
    }

    // --- РЎРѕСЃС‚РѕСЏРЅРёРµ РїСЂРёС‘РјРєРё --------------------------------------------------

    private void SetAcceptanceBusy(bool busy)
    {
        _acceptanceBusy = busy;

        StartAcceptanceButton.Content = busy ? "РћС‚РјРµРЅРёС‚СЊ РїСЂРёС‘РјРєСѓ" : "РЎС‚Р°СЂС‚ РїСЂРёС‘РјРєРё";
        StartAcceptanceButton.IsEnabled = busy
            || (_services.IsLinkConnected && _selectedReference is not null);

        UpdateAcceptanceCommands();
    }

    /// <summary>Р”РІРёР¶РµРЅРёРµ РЅР° РїР°РЅРµР»Рё РєРѕРјРїР°СЃРѕРІ, РїРѕРєР° РёРґС‘С‚ СЂР°Р±РѕС‚Р° СЃ РЅРёРјРё.</summary>
    private void SetCompassBusy(bool busy, string text = "")
    {
        CompassBusyRing.IsActive = busy;
        CompassBusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CompassBusyText.Text = text;
    }

    /// <remarks>
    /// рџ”ґ РљР°Р»РёР±СЂРѕРІРєРё СЂР°Р·Р±Р»РѕРєРёСЂРѕРІР°РЅС‹ С‚РѕР»СЊРєРѕ РїРѕСЃР»Рµ СЂР°Р·Р±РѕСЂР° СЂР°СЃС…РѕР¶РґРµРЅРёР№.
    /// РљР°Р»РёР±СЂРѕРІР°С‚СЊ Р±РѕСЂС‚, РєРѕРЅС„РёРіСѓСЂР°С†РёСЏ РєРѕС‚РѕСЂРѕРіРѕ СЂР°СЃС…РѕРґРёС‚СЃСЏ СЃ СЌС‚Р°Р»РѕРЅРѕРј Рё РЅРµ
    /// СЂР°Р·РѕР±СЂР°РЅР°, Р·РЅР°С‡РёС‚ РїРѕРґС‚РІРµСЂРґРёС‚СЊ РЅРµ С‚Рѕ РёР·РґРµР»РёРµ, РєРѕС‚РѕСЂРѕРµ СЃРґР°С‘С‚СЃСЏ.
    /// </remarks>
    private void UpdateAcceptanceCommands()
    {
        var live = _session is not null && !_acceptanceBusy;
        var resolved = _diffAccepted
            || (Differences.Count > 0 && Differences.All(static d => d.OutcomeVisibility == Visibility.Visible))
            || (Differences.Count == 0 && _session is not null);

        WriteAllButton.IsEnabled = live && Differences.Any(static d => d.CanWrite);
        AcceptDiffButton.IsEnabled = live && Differences.Count > 0 && !_diffAccepted;

        LevelButton.IsEnabled = live && resolved;
        CompassCalButton.IsEnabled = live && resolved;
        FinishButton.IsEnabled = live && resolved;
    }

    private async Task CloseSessionAsync()
    {
        await _services.EndAcceptanceAsync().ConfigureAwait(true);
        _session = null;
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

        // РќР°Р¶Р°С‚РёРµ РЅР° РїР»Р°С€РєСѓ вЂ” СЌС‚Рѕ СѓР¶Рµ РґРµР№СЃС‚РІРёРµ: РѕС‚РґРµР»СЊРЅРѕР№ РєРЅРѕРїРєРё В«РїРѕРґРєР»СЋС‡РёС‚СЊВ» РЅРµС‚.
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
        Log($"Р’С‹Р±СЂР°РЅ СЌС‚Р°Р»РѕРЅ В«{profile.Name}В».");
        RenderExpansion();
        RenderAll();
        await PersistSettingsAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// РћС‚РєСЂС‹РІР°РµС‚ РёР»Рё Р·Р°РєСЂС‹РІР°РµС‚ СЂР°СЃРєСЂС‹С‚РёСЏ. РЎРїРёСЃРєРё Р¶РёРІСѓС‚ РІ <c>Popup</c>, РїРѕСЌС‚РѕРјСѓ
    /// РЅРµ Р·Р°РЅРёРјР°СЋС‚ РјРµСЃС‚Р° РІ СЂР°Р·РјРµС‚РєРµ Рё РЅРµ РґРІРёРіР°СЋС‚ РѕСЃС‚Р°Р»СЊРЅС‹Рµ РїР°РЅРµР»Рё РѕРєРЅР°.
    /// </summary>
    private void RenderExpansion()
    {
        PlacePopup(_portsExpanded, PortPopup, PortPopupRoot, PortCard, FcSelector, PortsAbove, PortSpacer);
        PlacePopup(_referencesExpanded, ReferencePopup, ReferencePopupRoot, ReferenceCard, ReferenceSelector, ReferencesAbove, ReferenceSpacer);
    }

    /// <summary>
    /// РЎС‚Р°РІРёС‚ РІСЃРїР»С‹РІР°СЋС‰РёР№ СЃР»РѕР№ С‚Р°Рє, С‡С‚РѕР±С‹ РєР°СЂС‚РѕС‡РєР° РѕСЃС‚Р°Р»Р°СЃСЊ РЅР° СЃРІРѕС‘Рј РјРµСЃС‚Рµ:
    /// РїР»Р°С€РєРё В«РІС‹С€РµВ» СѓС…РѕРґСЏС‚ РЅР°Рґ РЅРµР№, В«РЅРёР¶РµВ» вЂ” РїРѕРґ РЅРµС‘, Р° РїСЂРѕР·СЂР°С‡РЅР°СЏ РїСЂРѕРєР»Р°РґРєР°
    /// Р·Р°РЅРёРјР°РµС‚ СЂРѕРІРЅРѕ РјРµСЃС‚Рѕ СЃР°РјРѕР№ РєР°СЂС‚РѕС‡РєРё.
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
            // РљР°СЂС‚РѕС‡РєР° РµС‰С‘ РЅРµ РёР·РјРµСЂРµРЅР° вЂ” РѕС‚РєСЂС‹РІР°С‚СЊ РЅРµС‡РµРіРѕ Рё РЅРµ РѕС‚ С‡РµРіРѕ СЃС‡РёС‚Р°С‚СЊ.
            popup.IsOpen = false;
            return;
        }

        root.Width = width;
        spacer.Height = card.ActualHeight;

        // Р’С‹СЃРѕС‚Р° РІРµСЂС…РЅРµР№ С‡Р°СЃС‚Рё РЅСѓР¶РЅР° РґРѕ РїРѕРєР°Р·Р°: РЅР° РЅРµС‘ РїРѕРґРЅРёРјР°РµС‚СЃСЏ РІРµСЃСЊ СЃР»РѕР№.
        above.Measure(new Size(width, double.PositiveInfinity));

        var offset = card.TransformToVisual(origin).TransformPoint(new Point(0, 0));
        popup.HorizontalOffset = offset.X;
        popup.VerticalOffset = offset.Y - above.DesiredSize.Height;
        popup.IsOpen = true;
    }

    // --- РџРѕСЂС‚С‹ Рё СЃРІСЏР·СЊ -----------------------------------------------------

    private async Task ReloadPortsAsync()
    {
        try
        {
            _ports = await SerialPortCatalog.ListAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowLinkMessage(InfoBarSeverity.Warning, "РќРµ СѓРґР°Р»РѕСЃСЊ РїРµСЂРµС‡РёСЃР»РёС‚СЊ РїРѕСЂС‚С‹: " + ex.Message);
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

    /// <summary>
    /// РџРѕРґРєР»СЋС‡Р°РµС‚ РЅР°Р±Р»СЋРґР°С‚РµР»СЊРЅРѕРµ СЃРѕРµРґРёРЅРµРЅРёРµ, РµСЃР»Рё Р±РѕСЂС‚ РѕРїРѕР·РЅР°РЅ РѕРґРЅРѕР·РЅР°С‡РЅРѕ.
    /// </summary>
    /// <remarks>
    /// рџ”ґ Р’Рѕ РІСЂРµРјСЏ РїСЂРёС‘РјРєРё РЅРµ РїРѕРґРєР»СЋС‡Р°РµС‚СЃСЏ РЅРёРєРѕРіРґР°. COM-РїРѕСЂС‚ РѕС‚РєСЂС‹РІР°РµС‚СЃСЏ
    /// РјРѕРЅРѕРїРѕР»СЊРЅРѕ Рё РЅР° РІСЃСЋ РїСЂРёС‘РјРєСѓ РїСЂРёРЅР°РґР»РµР¶РёС‚ СЃРµСЃСЃРёРё; РЅР°Р±Р»СЋРґР°С‚РµР»СЊРЅРѕРµ
    /// СЃРѕРµРґРёРЅРµРЅРёРµ, РїРѕР»РµР·С€РµРµ С‚СѓРґР° РїРѕСЃР»Рµ РїРµСЂРµР·Р°РіСЂСѓР·РєРё Р±РѕСЂС‚Р°, РѕС‚Р±РёСЂР°РµС‚ РїРѕСЂС‚ Сѓ
    /// РїСЂРѕС†РµРґСѓСЂС‹ Р»РёР±Рѕ РїРѕР»СѓС‡Р°РµС‚ РѕС‚РєР°Р· Рё Р·Р°СЃРѕСЂСЏРµС‚ Р¶СѓСЂРЅР°Р» СЃРѕРѕР±С‰РµРЅРёРµРј В«РїРѕСЂС‚ Р·Р°РЅСЏС‚
    /// РґСЂСѓРіРѕР№ РїСЂРѕРіСЂР°РјРјРѕР№В» вЂ” РїСЂРѕ СЃР°РјРѕРіРѕ СЃРµР±СЏ.
    /// </remarks>
    private async Task TryAutoConnectAsync()
    {
        if (_services.IsAcceptanceRunning)
        {
            return;
        }

        if (_ports.Count == 0)
        {
            ShowLinkMessage(InfoBarSeverity.Informational, "COM-РїРѕСЂС‚РѕРІ РЅРµ РЅР°Р№РґРµРЅРѕ вЂ” РїРѕРґРєР»СЋС‡РёС‚Рµ Р±РѕСЂС‚ РєР°Р±РµР»РµРј.");
            return;
        }

        var candidates = PickCandidates(_ports);
        if (candidates.Count != 1)
        {
            ShowLinkMessage(
                InfoBarSeverity.Informational,
                candidates.Count == 0
                    ? "РџР»Р°С‚Р° ArduPilot СЃСЂРµРґРё РїРѕСЂС‚РѕРІ РЅРµ РѕРїРѕР·РЅР°РЅР° вЂ” РІС‹Р±РµСЂРёС‚Рµ РїРѕСЂС‚ РІСЂСѓС‡РЅСѓСЋ."
                    : "РћРїРѕР·РЅР°РЅРѕ РЅРµСЃРєРѕР»СЊРєРѕ РїР»Р°С‚ вЂ” РІС‹Р±РµСЂРёС‚Рµ РЅСѓР¶РЅСѓСЋ РІСЂСѓС‡РЅСѓСЋ.");
            return;
        }

        _selectedPort = candidates[0];
        await ConnectAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// РћС‚Р±РёСЂР°РµС‚ РїРѕСЂС‚С‹, РЅР° РєРѕС‚РѕСЂС‹С… РјРѕР¶РµС‚ РѕС‚РІРµС‡Р°С‚СЊ MAVLink.
    /// </summary>
    /// <remarks>
    /// РљРѕРјРїРѕР·РёС‚РЅС‹Рµ РїР»Р°С‚С‹ F7/H7 РїРѕРєР°Р·С‹РІР°СЋС‚ РґРІР° CDC-РїРѕСЂС‚Р° СЃ РѕРґРёРЅР°РєРѕРІС‹РјРё VID/PID:
    /// РЅР° РЅСѓР»РµРІРѕРј РёРЅС‚РµСЂС„РµР№СЃРµ Р¶РёРІС‘С‚ MAVLink, РЅР° РІС‚РѕСЂРѕРј вЂ” SLCAN.
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
            Log($"РЈСЃС‚Р°РЅРѕРІР»РµРЅР° СЃРІСЏР·СЊ СЃ Р±РѕСЂС‚РѕРј РЅР° {port.PortName}.");
        }
        catch (Exception ex)
        {
            ShowLinkMessage(InfoBarSeverity.Error, ex.Message);
            Log($"РџРѕРґРєР»СЋС‡РµРЅРёРµ Рє {port.PortName} РЅРµ СѓРґР°Р»РѕСЃСЊ: {ex.Message}");
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

        // РћРїРѕР·РЅР°РЅРёРµ РєРѕРјРїР°СЃРѕРІ РїСЂРёРІСЏР·Р°РЅРѕ Рє СЃРѕРµРґРёРЅРµРЅРёСЋ, Р° РЅРµ Рє С‚Р°РєС‚Сѓ РёРЅРґРёРєР°С‚РѕСЂР°:
        // РїСЂРё РѕР±СЂС‹РІРµ РѕРЅРѕ СѓСЃС‚Р°СЂРµРІР°РµС‚, РїСЂРё РЅРѕРІРѕРј РїРѕРґРєР»СЋС‡РµРЅРёРё С‡РёС‚Р°РµС‚СЃСЏ Р·Р°РЅРѕРІРѕ.
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

    // --- Р­С‚Р°Р»РѕРЅС‹ -----------------------------------------------------------

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
                    "Р­С‚Р°Р»РѕРЅРѕРІ РЅРµС‚ вЂ” Р·Р°РїСѓСЃС‚РёС‚СЊ РєР°Р»РёР±СЂРѕРІРєСѓ РЅРµС‡РµРј. РќР°Р¶РјРёС‚Рµ РїР°РЅРµР»СЊ СЌС‚Р°Р»РѕРЅР°, С‡С‚РѕР±С‹ Р·Р°РІРµСЃС‚Рё РїРµСЂРІС‹Р№.";
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
            ReferenceBar.Message = "РќРµ СѓРґР°Р»РѕСЃСЊ РїСЂРѕС‡РёС‚Р°С‚СЊ СЃРїРёСЃРѕРє СЌС‚Р°Р»РѕРЅРѕРІ: " + ex.Message;
            ReferenceBar.IsOpen = true;
        }
    }

    // --- РћС‚СЂРёСЃРѕРІРєР° ---------------------------------------------------------

    private void RenderAll()
    {
        var connected = _services.IsLinkConnected;

        PortCardTitle.Text = _selectedPort?.Caption ?? "РџРѕСЂС‚ РЅРµ РІС‹Р±СЂР°РЅ";
        PortCardDetail.Text = connected
            ? $"СЃРІСЏР·СЊ РµСЃС‚СЊ В· {_services.ConnectedFirmware ?? "РІРµСЂСЃРёСЏ РїСЂРѕС€РёРІРєРё РЅРµ РїРѕР»СѓС‡РµРЅР°"}"
            : _selectedPort?.Details is { Length: > 0 } d
                ? d
                : "РќР°Р¶РјРёС‚Рµ, С‡С‚РѕР±С‹ РІС‹Р±СЂР°С‚СЊ";

        // РЎРІСЏР·СЊ РїРѕРєР°Р·С‹РІР°РµС‚СЃСЏ РїРѕРґСЃРІРµС‚РєРѕР№ СЃРЅРёР·Сѓ Рё Р°РєС†РµРЅС‚РЅС‹Рј РєР°РЅС‚РѕРј СЃР»РµРІР°. Р”РІР°
        // РїСЂРёР·РЅР°РєР°, Р° РЅРµ РѕРґРёРЅ: РїРѕРґСЃРІРµС‚РєР° вЂ” РјСЏРіРєРёР№ РіСЂР°РґРёРµРЅС‚, РЅР° СЃРІРµС‚Р»РѕР№ С‚РµРјРµ Рё РЅР°
        // РґРµС€С‘РІРѕР№ С†РµС…РѕРІРѕР№ РјР°С‚СЂРёС†Рµ РѕРЅР° РІРёРґРЅР° С…СѓР¶Рµ, С‡РµРј С…РѕС‚РµР»РѕСЃСЊ Р±С‹.
        // РџРѕРґСЃРІРµС‚РєР° СЃРЅРёР·Сѓ РіРѕСЂРёС‚ РІСЃРµРіРґР°: Р·РµР»С‘РЅР°СЏ вЂ” РіРѕС‚РѕРІРѕ, РєСЂР°СЃРЅР°СЏ вЂ” РЅРµ Р·Р°РїРѕР»РЅРµРЅРѕ.
        // РџРѕРіР°С€РµРЅРЅР°СЏ РїР°РЅРµР»СЊ С‡РёС‚Р°Р»Р°СЃСЊ РєР°Рє В«РµС‰С‘ РЅРµ РґРѕС€Р»Рё СЂСѓРєРёВ», С…РѕС‚СЏ РЅР° РґРµР»Рµ СЌС‚Рѕ
        // РЅРµР·Р°РІРµСЂС€С‘РЅРЅР°СЏ РїРѕРґРіРѕС‚РѕРІРєР° СЃС‚РµРЅРґР°.
        PortGlow.Visibility = Visibility.Visible;
        PortGlow.Background = (Brush)Resources[connected ? "GlowBrush" : "GlowDangerBrush"];
        PortCard.Style = (Style)Resources[connected ? "ActiveTileStyle" : "DangerTileStyle"];

        ReferenceCardTitle.Text = _selectedReference?.Name ?? "Р­С‚Р°Р»РѕРЅ РЅРµ РІС‹Р±СЂР°РЅ";
        ReferenceCardDetail.Text = _selectedReference is { } profile
            ? ReferenceCaption.Describe(profile)
            : _references.Count == 0
                ? "РќР°Р¶РјРёС‚Рµ, С‡С‚РѕР±С‹ РґРѕР±Р°РІРёС‚СЊ РїРµСЂРІС‹Р№"
                : "РќР°Р¶РјРёС‚Рµ, С‡С‚РѕР±С‹ РІС‹Р±СЂР°С‚СЊ";

        var hasProfile = _selectedReference is not null;
        ReferenceGlow.Visibility = Visibility.Visible;
        ReferenceGlow.Background = (Brush)Resources[hasProfile ? "GlowBrush" : "GlowDangerBrush"];
        ReferenceCard.Style = (Style)Resources[hasProfile ? "ActiveTileStyle" : "DangerTileStyle"];

        // РџСЂР°РІРёС‚СЊ РЅРµС‡РµРіРѕ, РїРѕРєР° СЌС‚Р°Р»РѕРЅ РЅРµ РІС‹Р±СЂР°РЅ: РєР°СЂР°РЅРґР°С€ Р±РµР· С†РµР»Рё С‚РѕР»СЊРєРѕ
        // РїСЂРµРґР»Р°РіР°РµС‚ РЅР°Р¶Р°С‚СЊ Рё РїРѕР»СѓС‡РёС‚СЊ РѕС‚РєР°Р·.
        EditReferenceButton.Visibility = hasProfile ? Visibility.Visible : Visibility.Collapsed;

        var problems = new List<string>();
        if (!connected)
        {
            problems.Add("РЅРµС‚ СЃРІСЏР·Рё СЃ РїРѕР»С‘С‚РЅРёРєРѕРј");
        }

        if (_selectedReference is null)
        {
            problems.Add("РЅРµ РІС‹Р±СЂР°РЅ СЌС‚Р°Р»РѕРЅ");
        }

        if (problems.Count == 0)
        {
            ReadyBar.Severity = InfoBarSeverity.Success;
            ReadyBar.Message = "Р‘РѕСЂС‚ РЅР° СЃРІСЏР·Рё, СЌС‚Р°Р»РѕРЅ РІС‹Р±СЂР°РЅ. РњРѕР¶РЅРѕ Р·Р°РїСѓСЃРєР°С‚СЊ РїСЂРёС‘РјРєСѓ.";
        }
        else
        {
            ReadyBar.Severity = InfoBarSeverity.Informational;
            ReadyBar.Message = "РќРµ РіРѕС‚РѕРІРѕ: " + string.Join(", ", problems) + ".";
        }

        StartAcceptanceButton.IsEnabled = problems.Count == 0;

        // РРЅРґРёРєР°С‚РѕСЂ СЂР°Р±РѕС‚Р°РµС‚ РѕС‚ СЃРІСЏР·Рё СЃ Р±РѕСЂС‚РѕРј Рё РЅРµ Р¶РґС‘С‚ РІС‹Р±РѕСЂР° СЌС‚Р°Р»РѕРЅР°:
        // РїСЂРёР±РѕСЂС‹ РЅСѓР¶РЅС‹ РѕРїРµСЂР°С‚РѕСЂСѓ СЂР°РЅСЊС€Рµ, С‡РµРј СЌС‚Р°Р»РѕРЅ.
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
    /// РЎС‚СЂРѕРєР° Р¶СѓСЂРЅР°Р»Р° СЃ СЏРІРЅРѕР№ РІР°Р¶РЅРѕСЃС‚СЊСЋ.
    /// </summary>
    /// <remarks>
    /// Р’Р°Р¶РЅРѕСЃС‚СЊ Р·Р°РґР°С‘С‚СЃСЏ РІС‹Р·С‹РІР°СЋС‰РёРј, Р° РЅРµ РІС‹РІРѕРґРёС‚СЃСЏ РёР· С‚РµРєСЃС‚Р°: РѕС‚РєР°Р· РїСЂРёС‘РјРєРё Рё
    /// СЃРѕРѕР±С‰РµРЅРёРµ Рѕ С…РѕРґРµ РІС‹РіР»СЏРґСЏС‚ РІ Р¶СѓСЂРЅР°Р»Рµ РѕРґРёРЅР°РєРѕРІРѕ, РµСЃР»Рё РѕР±Р° Р·Р°РїРёСЃР°РЅС‹ РєР°Рє
    /// В«СЃРІРµРґРµРЅРёСЏВ», Рё РѕРїРµСЂР°С‚РѕСЂ РїСЂРѕР»РёСЃС‚С‹РІР°РµС‚ РѕС‚РєР°Р· РЅРµ Р·Р°РјРµС‚РёРІ.
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


    private void OnHudTick(object? sender, object e) => RenderHud();

    // --- РЈРєР°Р·Р°С‚РµР»СЊ РїРѕР»РѕР¶РµРЅРёСЏ -----------------------------------------------

    /// <summary>РњР°СЃС€С‚Р°Р± С€РєР°Р»С‹ С‚Р°РЅРіР°Р¶Р°, РїРёРєСЃРµР»РµР№ РЅР° РіСЂР°РґСѓСЃ.</summary>
    /// <remarks>
    /// РљСЂСѓРїРЅС‹Р№ РјР°СЃС€С‚Р°Р± РІС‹Р±СЂР°РЅ РЅР°РјРµСЂРµРЅРЅРѕ: РІ РѕРєРЅРµ РІС‹СЃРѕС‚РѕР№ 170 С‚РѕС‡РµРє РѕРЅ РѕСЃС‚Р°РІР»СЏРµС‚
    /// РІ РїРѕР»Рµ Р·СЂРµРЅРёСЏ В±21В°, С‚Рѕ РµСЃС‚СЊ С‚СЂРё-С‡РµС‚С‹СЂРµ СЃС‚СѓРїРµРЅРё. РњРµР»РєРёР№ РјР°СЃС€С‚Р°Р± РЅР°Р±РёРІР°Р»
    /// РїРѕР»Рµ РґРµСЃСЏС‚РєРѕРј Р»РёРЅРёР№, Рё С€РєР°Р»Р° РїРµСЂРµСЃС‚Р°РІР°Р»Р° С‡РёС‚Р°С‚СЊСЃСЏ.
    /// </remarks>
    private const double PitchScale = 4.0;

    private static Brush Ink => (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];

    private static Brush InkDim => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    private void OnHudSizeChanged(object sender, SizeChangedEventArgs e) => BuildHudGeometry();

    /// <summary>
    /// РџРµСЂРµСЃС‚СЂР°РёРІР°РµС‚ С€РєР°Р»Сѓ РїРѕРґ С‚РµРєСѓС‰РёР№ СЂР°Р·РјРµСЂ. Р’С‹Р·С‹РІР°РµС‚СЃСЏ РЅР° РёР·РјРµРЅРµРЅРёРµ СЂР°Р·РјРµСЂР°,
    /// Р° РЅРµ РЅР° РєР°Р¶РґС‹Р№ РєР°РґСЂ: РїРѕ С‚Р°Р№РјРµСЂСѓ РјРµРЅСЏСЋС‚СЃСЏ С‚РѕР»СЊРєРѕ РґРІР° РїСЂРµРѕР±СЂР°Р·РѕРІР°РЅРёСЏ.
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

    /// <summary>РЁРєР°Р»Р° С‚Р°РЅРіР°Р¶Р°: РіРѕСЂРёР·РѕРЅС‚ Рё СЃС‚СѓРїРµРЅРё С‡РµСЂРµР· 10В°, РїРѕРґРїРёСЃРё СЃР»РµРІР°.</summary>
    private void BuildPitchLadder(double w, double h)
    {
        LadderHost.Children.Clear();

        var cx = w / 2;
        var cy = h / 2;

        // РљСЂРµРЅ РІСЂР°С‰Р°РµС‚ С€РєР°Р»Сѓ РІРѕРєСЂСѓРі С‚РѕС‡РєРё РІРёР·РёСЂРѕРІР°РЅРёСЏ, Р° РЅРµ РІРѕРєСЂСѓРі СѓРіР»Р° С…РѕР»СЃС‚Р°.
        RollRotate.CenterX = cx;
        RollRotate.CenterY = cy;

        const double Gap = 40;
        const double BarLength = 46;

        for (var deg = -40; deg <= 40; deg += 10)
        {
            var y = cy - (deg * PitchScale);

            if (deg == 0)
            {
                // Р“РѕСЂРёР·РѕРЅС‚ вЂ” СЃРїР»РѕС€РЅР°СЏ Р»РёРЅРёСЏ РІРѕ РІСЃСЋ С€РёСЂРёРЅСѓ СЃ СЂР°Р·СЂС‹РІРѕРј РїРѕРґ РјР°СЂРєРµСЂРѕРј.
                AddLine(cx - Math.Max(cx, 0), y, cx - 22, y, Ink, 2);
                AddLine(cx + 22, y, cx + Math.Max(cx, 0) + w, y, Ink, 2);
                continue;
            }

            AddLine(cx - Gap - BarLength, y, cx - Gap, y, InkDim, 1.6, dashed: deg < 0);
            AddLine(cx + Gap, y, cx + Gap + BarLength, y, InkDim, 1.6, dashed: deg < 0);

            // Р—Р°СЃРµС‡РєРё РЅР° РІРЅСѓС‚СЂРµРЅРЅРёС… РєРѕРЅС†Р°С… СЃРјРѕС‚СЂСЏС‚ Рє РіРѕСЂРёР·РѕРЅС‚Сѓ.
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

        // РџРёРєРёСЂРѕРІР°РЅРёРµ вЂ” РїСѓРЅРєС‚РёСЂРѕРј: РѕС‚Р»РёС‡Р°РµС‚СЃСЏ РѕС‚ РЅР°Р±РѕСЂР° РЅРµ С‚РѕР»СЊРєРѕ РїРѕР»РѕР¶РµРЅРёРµРј.
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

    /// <summary>РќРµРїРѕРґРІРёР¶РЅС‹Р№ РјР°СЂРєРµСЂ РїРѕР»РѕР¶РµРЅРёСЏ: РєСЂСѓРїРЅС‹Р№, С‡С‚РѕР±С‹ С‡РёС‚Р°Р»СЃСЏ СЃСЂР°Р·Сѓ.</summary>
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
            HudArmedText.Text = "РќРµС‚ РґР°РЅРЅС‹С…: Р±РѕСЂС‚ РЅРµ РЅР° СЃРІСЏР·Рё";
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

            HudRollText.Text = string.Create(CultureInfo.InvariantCulture, $"{rollDeg:+0.0;-0.0;0.0}В°");
            HudPitchText.Text = string.Create(CultureInfo.InvariantCulture, $"{pitchDeg:+0.0;-0.0;0.0}В°");
            HudYawText.Text = string.Create(CultureInfo.InvariantCulture, $"{yawDeg:000}В°");
        }

        // РЎРµРЅС‚РёРЅРµР»С‹ СѓР¶Рµ РѕС‚СЃРµСЏРЅС‹ РєР°РЅР°Р»РѕРј: РїСѓСЃС‚Рѕ РѕР·РЅР°С‡Р°РµС‚ В«Р±РѕСЂС‚ РЅРµ СЃРѕРѕР±С‰Р°РµС‚В».
        HudVoltageText.Text = state.VoltageVolts is { } v
            ? string.Create(CultureInfo.InvariantCulture, $"{v:0.00} Р’")
            : "РЅРµС‚";

        HudCurrentText.Text = state.CurrentAmperes is { } a
            ? string.Create(CultureInfo.InvariantCulture, $"{a:0.00} Рђ")
            : "РЅРµС‚";

        HudModeText.Text = state.ModeName;

        // Р’Р·РІРµРґС‘РЅРЅС‹Рµ РґРІРёРіР°С‚РµР»Рё вЂ” РїСЂРµРґСѓРїСЂРµР¶РґР°СЋС‰РёРј С†РІРµС‚РѕРј Рё СЃР»РѕРІРѕРј, РЅРµ РѕРґРЅРёРј С†РІРµС‚РѕРј.
        HudArmedText.Text = state.Armed ? "Р’Р—Р’Р•Р”РЃРќ" : "РћР±РµР·РІСЂРµР¶РµРЅ";
        HudArmedText.Foreground = state.Armed
            ? (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            : InkDim;

        RenderGps(state);
        RenderCompasses(state);
    }

    /// <summary>
    /// РЎРѕСЃС‚РѕСЏРЅРёРµ GPS: РІРёРґ СЂРµС€РµРЅРёСЏ, СЃРїСѓС‚РЅРёРєРё, РіРµРѕРјРµС‚СЂРёСЏ, РєРѕРѕСЂРґРёРЅР°С‚С‹.
    /// </summary>
    /// <remarks>
    /// рџ”ґ РўСЂРё СЃРѕСЃС‚РѕСЏРЅРёСЏ СЂР°Р·РІРµРґРµРЅС‹ Рё РЅРµ СЃР»РёРІР°СЋС‚СЃСЏ РІ РѕРґРёРЅ РїСЂРѕС‡РµСЂРє: В«СЃРѕРѕР±С‰РµРЅРёР№ РЅРµ
    /// Р±С‹Р»РѕВ» (СЃСѓРґРёС‚СЊ СЂР°РЅРѕ), В«РїСЂРёС‘РјРЅРёРєР° РЅРµС‚В» (РёР·РґРµР»РёРµ РЅРµРіРѕРґРЅРѕ) Рё В«СЂРµС€РµРЅРёСЏ РЅРµС‚В»
    /// (Р¶РґР°С‚СЊ РёР»Рё РёСЃРєР°С‚СЊ РЅРµР±Рѕ). РћРґРёРЅР°РєРѕРІС‹Р№ РїСЂРѕС‡РµСЂРє РЅР° РІСЃРµ С‚СЂРё Р·Р°СЃС‚Р°РІР»СЏР» Р±С‹
    /// РѕРїРµСЂР°С‚РѕСЂР° РіР°РґР°С‚СЊ, С‡С‚Рѕ РґРµР»Р°С‚СЊ РґР°Р»СЊС€Рµ.
    /// </remarks>
    private void RenderGps(VehicleLiveState state)
    {
        if (state.Gps is not { } gps)
        {
            HudGpsFixText.Text = "СЃРѕРѕР±С‰РµРЅРёР№ РЅРµ Р±С‹Р»Рѕ";
            HudGpsFixText.Foreground = InkDim;
            HudGpsSatsText.Text = "вЂ”";
            HudGpsHdopText.Text = "вЂ”";
            HudGpsAltText.Text = "вЂ”";
            HudGpsPositionText.Text = "РєРѕРѕСЂРґРёРЅР°С‚ РЅРµС‚";
            return;
        }

        HudGpsFixText.Text = gps.FixTypeText;
        HudGpsFixText.Foreground = gps.Is3D
            ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
            : (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];

        HudGpsSatsText.Text = gps.SatellitesVisible.ToString(CultureInfo.InvariantCulture);

        // РќРµРёР·РІРµСЃС‚РЅР°СЏ РіРµРѕРјРµС‚СЂРёСЏ РїРѕРєР°Р·С‹РІР°РµС‚СЃСЏ СЃР»РѕРІРѕРј. РџСЂРёРІРµРґС‘РЅРЅР°СЏ Рє С‡РёСЃР»Сѓ, РѕРЅР°
        // РІС‹РіР»СЏРґРµР»Р° Р±С‹ Р»РёР±Рѕ РёРґРµР°Р»СЊРЅРѕР№, Р»РёР±Рѕ С‡СѓРґРѕРІРёС‰РЅРѕР№ вЂ” Рё С‚Рѕ Рё РґСЂСѓРіРѕРµ Р»РѕР¶СЊ.
        HudGpsHdopText.Text = gps.Hdop is { } hdop
            ? string.Create(CultureInfo.InvariantCulture, $"{hdop:0.00}")
            : "РЅРµС‚";

        HudGpsAltText.Text = gps.Is3D
            ? string.Create(CultureInfo.InvariantCulture, $"{gps.AltitudeMeters:0} Рј")
            : "РЅРµС‚";

        // РљРѕРѕСЂРґРёРЅР°С‚С‹ Р±РµР· СЂРµС€РµРЅРёСЏ вЂ” СЌС‚Рѕ РїРѕСЃР»РµРґРЅРµРµ РёР·РІРµСЃС‚РЅРѕРµ РёР»Рё РЅРѕР»СЊ; РІС‹РґР°РІР°С‚СЊ
        // РёС… Р·Р° РїРѕР»РѕР¶РµРЅРёРµ Р±РѕСЂС‚Р° РЅРµР»СЊР·СЏ.
        HudGpsPositionText.Text = gps.Is3D
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{gps.LatitudeDeg:F7}, {gps.LongitudeDeg:F7}")
            : "РєРѕРѕСЂРґРёРЅР°С‚ РЅРµС‚: СЂРµС€РµРЅРёРµ РЅРµ С‚СЂС‘С…РјРµСЂРЅРѕРµ";
    }

    /// <summary>
    /// РћРїРѕР·РЅР°РЅРёРµ РєРѕРјРїР°СЃРѕРІ, РїСЂРѕС‡РёС‚Р°РЅРЅРѕРµ СЃ Р±РѕСЂС‚Р°. РљР»СЋС‡ вЂ” РЅРѕРјРµСЂ СЃР»РѕС‚Р°.
    /// </summary>
    /// <remarks>
    /// Р§РёС‚Р°РµС‚СЃСЏ РѕРґРёРЅ СЂР°Р· РЅР° СЃРѕРµРґРёРЅРµРЅРёРµ: <c>COMPASS_DEV_ID*</c> РјРµРЅСЏРµС‚СЃСЏ С‚РѕР»СЊРєРѕ
    /// РїСЂРё РїРµСЂРµСЃС‚Р°РЅРѕРІРєРµ СЃР»РѕС‚РѕРІ, Р° С‡С‚РµРЅРёРµ РїР°СЂР°РјРµС‚СЂРѕРІ РЅР° РєР°Р¶РґРѕРј С‚Р°РєС‚Рµ РёРЅРґРёРєР°С‚РѕСЂР°
    /// Р·Р°Р±РёР»Рѕ Р±С‹ РєР°РЅР°Р» С‚РµР»РµРјРµС‚СЂРёРё.
    /// </remarks>
    private readonly Dictionary<int, CompassIdentityRow> _compassIdentity = new();

    private bool _compassIdentityLoading;

    /// <summary>
    /// Р§РёС‚Р°РµС‚ СЃ Р±РѕСЂС‚Р°, РєР°РєРѕР№ РєРѕРјРїР°СЃ РІРЅРµС€РЅРёР№, Р° РєР°РєРѕР№ РІРЅСѓС‚СЂРµРЅРЅРёР№.
    /// </summary>
    /// <remarks>
    /// рџ”ґ РЎСѓРґРёС‚СЊ Рѕ РІРЅРµС€РЅРѕСЃС‚Рё РїРѕ РЅРѕРјРµСЂСѓ СЃР»РѕС‚Р° РЅРµР»СЊР·СЏ. РџРѕСЂСЏРґРѕРє СЃР»РѕС‚РѕРІ Р·Р°РґР°С‘С‚СЃСЏ
    /// С‚Р°Р±Р»РёС†РµР№ РїСЂРёРѕСЂРёС‚РµС‚РѕРІ Рё РјРµРЅСЏРµС‚СЃСЏ РїСЂРё РїРµСЂРµСЃС‚Р°РЅРѕРІРєРµ, РїРѕСЌС‚РѕРјСѓ В«РїРµСЂРІС‹Р№ вЂ”
    /// Р·РЅР°С‡РёС‚ РІРЅРµС€РЅРёР№В» РЅРµРІРµСЂРЅРѕ СЂРѕРІРЅРѕ РЅР° С‚РµС… Р±РѕСЂС‚Р°С…, СЂР°РґРё РєРѕС‚РѕСЂС‹С… РїСЂРѕРІРµСЂРєР° Рё
    /// СЃСѓС‰РµСЃС‚РІСѓРµС‚. Р’С‹РІРѕРґ РґРµР»Р°РµС‚СЃСЏ РїРѕ С‚РёРїСѓ С€РёРЅС‹ Рё С„Р»Р°РіСѓ <c>COMPASS_EXTERNAL*</c>.
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
                    // РџСЂРѕС€РёРІРєР° Р·Р°РєРѕРЅРЅРѕ РЅРµ Р·РЅР°РµС‚ РёРјРµРЅРё РїСЂРё РјРµРЅСЊС€РµРј С‡РёСЃР»Рµ
                    // РєРѕРјРїР°СЃРѕРІ вЂ” СЌС‚Рѕ СЃРѕРѕР±С‰РµРЅРёРµ Рѕ СЃРѕСЃС‚Р°РІРµ, Р° РЅРµ СЃР±РѕР№.
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

                found[slot] = new CompassIdentityRow(
                    slot,
                    CompassIdentity.IsExternal(kind),
                    CompassIdentity.ExternalKindText(kind),
                    CompassIdentity.Describe(id));
            }

            _compassIdentity.Clear();
            foreach (var pair in found)
            {
                _compassIdentity[pair.Key] = pair.Value;
            }

            // РЎС‚СЂРѕРєРё РїРµСЂРµСЃРѕР±РёСЂР°СЋС‚СЃСЏ: Сѓ РЅРёС… РѕРїРѕР·РЅР°РЅРёРµ Р·Р°РґР°С‘С‚СЃСЏ РїСЂРё СЃРѕР·РґР°РЅРёРё.
            Compasses.Clear();
            RenderHud();
        }
        catch (Exception)
        {
            // РРЅРґРёРєР°С‚РѕСЂ Р±РµР· РѕРїРѕР·РЅР°РЅРёСЏ С…СѓР¶Рµ, С‡РµРј СЃ РЅРёРј, РЅРѕ Р»СѓС‡С€Рµ, С‡РµРј СѓРїР°РІС€РёР№
            // СЂР°Р±РѕС‡РёР№ СЌРєСЂР°РЅ: СЃС‚СЂРѕРєРё РїРѕРєР°Р¶СѓС‚ В«РѕРїРѕР·РЅР°РЅРёРµ РЅРµ РїСЂРѕС‡РёС‚Р°РЅРѕВ».
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
            // 🔴 Порядок правится перестановкой (Move), а не переприсваиванием.
            // Список, молча перерисованный в другом порядке, читается как
            // «ничего не изменилось»: оператор не заметит, что первым стал
            // другой датчик, а это и есть главный итог перестановки очереди.
            // Move даёт анимацию переезда строки, замена — нет.
            for (var i = 0; i < samples.Count; i++)
            {
                var wanted = samples[i];
                var current = Compasses[i];

                if (!string.Equals(current.Title, $"Компас {wanted.Instance + 1}", StringComparison.Ordinal))
                {
                    var from = -1;
                    for (var j = i + 1; j < Compasses.Count; j++)
                    {
                        if (string.Equals(
                            Compasses[j].Title,
                            $"Компас {wanted.Instance + 1}",
                            StringComparison.Ordinal))
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

                Compasses[i] = new CompassRow(wanted, IdentityFor(wanted));
            }
        }

        CompassHintText.Text = samples.Count == 0 ? "РџРѕРєР°Р·Р°РЅРёР№ РјР°РіРЅРёС‚РѕРјРµС‚СЂРѕРІ РїРѕРєР° РЅРµС‚." : string.Empty;
    }

    /// <summary>
    /// РћРїРѕР·РЅР°РЅРёРµ РґР»СЏ РїРѕРєР°Р·Р°РЅРёР№ РјР°РіРЅРёС‚РѕРјРµС‚СЂР°.
    /// </summary>
    /// <remarks>
    /// рџ”ґ Р­РєР·РµРјРїР»СЏСЂ С‚РµР»РµРјРµС‚СЂРёРё РЅСѓРјРµСЂСѓРµС‚СЃСЏ СЃ РЅСѓР»СЏ, СЃР»РѕС‚ РєРѕРјРїР°СЃР° вЂ” СЃ РµРґРёРЅРёС†С‹.
    /// РЎРґРІРёРі РЅР° РµРґРёРЅРёС†Сѓ Р·РґРµСЃСЊ РЅРµ РєРѕСЃРјРµС‚РёРєР°: Р±РµР· РЅРµРіРѕ РІРЅРµС€РЅРёР№ РєРѕРјРїР°СЃ РїРѕРґРїРёСЃР°Р»СЃСЏ
    /// Р±С‹ РґР°РЅРЅС‹РјРё СЃРѕСЃРµРґРЅРµРіРѕ, Рё РѕРїРµСЂР°С‚РѕСЂ С‡РёС‚Р°Р» Р±С‹ В«РІРЅРµС€РЅРёР№В» РїРѕРґ РІРЅСѓС‚СЂРµРЅРЅРёРј.
    /// </remarks>
    private CompassIdentityRow? IdentityFor(MagSample sample) =>
        _compassIdentity.TryGetValue(sample.Instance + CompassIdentity.MinSlot, out var identity)
            ? identity
            : null;
}

