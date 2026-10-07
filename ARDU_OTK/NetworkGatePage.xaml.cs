using System;
using System.Linq;
using System.Threading.Tasks;
using ARDU_OTK.Services;
using ARDU_OTK.Services.Network;
using ARDU_OTK.Services.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ARDU_OTK;

/// <summary>
/// Вход в стенд: подключение к сети ОТК при первом запуске, затем вход
/// пользователя.
/// </summary>
/// <remarks>
/// При каждом открытии подключённый стенд сам заглядывает в папку обмена и
/// принимает новый пакет, если он есть. Недоступная папка вход не блокирует:
/// стенд работает по последнему принятому пакету — обмен редкий, а цех не
/// должен стоять из-за того, что Google Диск сегодня не синхронизировался.
/// </remarks>
public sealed partial class NetworkGatePage : Page
{
    private readonly AppServices _services = AppServices.Instance;

    private NetworkState? _state;

    public NetworkGatePage()
    {
        InitializeComponent();
    }

    private NetworkService Network => _services.Network;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _services.InitializeAsync().ConfigureAwait(true);
            _state = await Network.GetStateAsync().ConfigureAwait(true);

            if (_state.IsJoined && _state.ExchangeDir.Length > 0)
            {
                var result = await Network.RefreshAsync(_state.ExchangeDir).ConfigureAwait(true);
                if (result.Applied || result.Failed)
                {
                    Show(result.Message + (result.Failed ? " Вход — по последнему принятому пакету." : string.Empty),
                        result.Failed ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
                }

                _state = await Network.GetStateAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Show("Реестр стенда недоступен: " + ex.Message, InfoBarSeverity.Error);
        }

        Render();
    }

    private void Render()
    {
        BusyRing.IsActive = false;
        BusyRing.Visibility = Visibility.Collapsed;

        var joined = _state?.IsJoined == true;
        ConnectPanel.Visibility = joined ? Visibility.Collapsed : Visibility.Visible;
        LoginPanel.Visibility = joined ? Visibility.Visible : Visibility.Collapsed;

        if (!joined)
        {
            CreatePanel.Visibility = NetworkService.HasSigningKey ? Visibility.Visible : Visibility.Collapsed;
            if (ExchangeDirBox.Text.Length == 0)
            {
                ExchangeDirBox.Text = _state?.ExchangeDir is { Length: > 0 } saved
                    ? saved
                    : NetworkService.FindExchangeDirs().FirstOrDefault() ?? string.Empty;
            }

            return;
        }

        NetworkInfoText.Text = _state!.Serial > 0
            ? $"Пакет сети №{_state.Serial} от {_state.IssuedUtc?.ToLocalTime():dd.MM.yyyy HH:mm}, выпустил {_state.IssuedBy}. Папка обмена: {_state.ExchangeDir}."
            : "Сеть заведена, пакет ещё не выпущен.";
        LoginBox.Focus(FocusState.Programmatic);
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();

        // Приложение unpackaged: пикеру обязательно нужно окно-владелец.
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add("*");

        if (await picker.PickSingleFolderAsync() is { } folder)
        {
            ExchangeDirBox.Text = folder.Path;
        }
    }

    private async void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            var result = await Network.RefreshAsync(ExchangeDirBox.Text.Trim()).ConfigureAwait(true);
            Show(result.Message, result.Failed ? InfoBarSeverity.Error : InfoBarSeverity.Success);
        }).ConfigureAwait(true);
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (AdminPasswordBox.Password != AdminPasswordRepeatBox.Password)
        {
            Show("Пароли не совпадают.", InfoBarSeverity.Error);
            return;
        }

        await RunAsync(async () =>
        {
            var result = await Network.CreateNetworkAsync(
                ExchangeDirBox.Text.Trim(), AdminLoginBox.Text, AdminNameBox.Text, AdminPasswordBox.Password)
                .ConfigureAwait(true);
            Show(result.Message, result.Failed ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }).ConfigureAwait(true);
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e) => await LoginAsync().ConfigureAwait(true);

    private async void OnSecretKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await LoginAsync().ConfigureAwait(true);
        }
    }

    private async Task LoginAsync()
    {
        LoginButton.IsEnabled = false;
        try
        {
            // Удачный вход меняет сессию, и окно само уводит на рабочий экран.
            if (await Network.LoginAsync(LoginBox.Text, SecretBox.Password).ConfigureAwait(true) is { } error)
            {
                Show(error, InfoBarSeverity.Error);
                SecretBox.Password = string.Empty;
            }
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            var result = await Network.RefreshAsync(_state?.ExchangeDir ?? string.Empty).ConfigureAwait(true);
            Show(result.Message, result.Failed ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }).ConfigureAwait(true);
    }

    /// <summary>Выполняет действие, показывая ошибку и перечитывая состояние.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        IsEnabled = false;
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Show(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsEnabled = true;
        }

        try
        {
            _state = await Network.GetStateAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Show("Реестр стенда недоступен: " + ex.Message, InfoBarSeverity.Error);
        }

        Render();
    }

    private void Show(string message, InfoBarSeverity severity)
    {
        GateBar.Message = message;
        GateBar.Severity = severity;
        GateBar.IsOpen = true;
    }
}
