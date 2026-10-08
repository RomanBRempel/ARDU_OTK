using System;
using System.Collections.Generic;
using System.IO;
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
/// При каждом открытии подключённый стенд сам проверяет, нет ли нового
/// пакета. Отсутствие связи вход не блокирует: стенд работает по последнему
/// принятому пакету — обмен редкий, и цех не должен стоять из-за интернета.
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
        _ = CheckUpdatesAsync();

        try
        {
            await _services.InitializeAsync().ConfigureAwait(true);
            _state = await Network.GetStateAsync().ConfigureAwait(true);

            if (_state.IsJoined && _state.NetworkCode.Length > 0)
            {
                var result = await Network.RefreshAsync().ConfigureAwait(true);
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

        await RenderAsync().ConfigureAwait(true);
    }

    private async Task RenderAsync()
    {
        BusyRing.IsActive = false;
        BusyRing.Visibility = Visibility.Collapsed;

        var joined = _state?.IsJoined == true;
        ConnectPanel.Visibility = joined ? Visibility.Collapsed : Visibility.Visible;
        LoginPanel.Visibility = joined ? Visibility.Visible : Visibility.Collapsed;

        if (!joined)
        {
            CreatePanel.Visibility = NetworkService.HasSigningKey ? Visibility.Visible : Visibility.Collapsed;
            try
            {
                var local = await _services.LoadReferencesAsync().ConfigureAwait(true);
                LocalReferencesText.Text = local.Count == 0
                    ? "На этой станции эталонов нет."
                    : $"На этой станции эталонов: {local.Count} ({string.Join(", ", local.Select(static r => r.Name))}). "
                      + "Если какой-то из них должен войти в сеть — сохраните его в файл и передайте администратору до подключения. "
                      + "Подключение их не удалит: эталоны, которых нет в пакете сети, уходят в архив, а когда администратор "
                      + "выпустит их в сеть, вернутся в работу с прежней историей прогонов.";
            }
            catch (Exception ex)
            {
                LocalReferencesText.Text = "Эталоны станции не прочитаны: " + ex.Message;
            }

            return;
        }

        NetworkInfoText.Text = _state!.Serial > 0
            ? $"Пакет сети №{_state.Serial} от {_state.IssuedUtc?.ToLocalTime():dd.MM.yyyy HH:mm}, выпустил {_state.IssuedBy}."
            : "Сеть заведена, пакет ещё не выпущен.";

        // На стенде обычно работает один и тот же человек: его логин уже
        // известен, остаётся ввести пароль или PIN.
        if (LoginBox.Text.Length == 0)
        {
            try
            {
                LoginBox.Text = await Network.GetLastLoginAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                App.LogFatal("Чтение последнего логина", ex);
            }
        }

        if (LoginBox.Text.Length > 0)
        {
            SecretBox.Focus(FocusState.Programmatic);
        }
        else
        {
            LoginBox.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>
    /// Проверяет и скачивает обновление в фоне. Ошибка сети — не повод
    /// мешать входу: стенд работает и без обновлений.
    /// </summary>
    private async Task CheckUpdatesAsync()
    {
        var updates = _services.Updates;
        try
        {
            if (updates.State != UpdateState.ReadyToApply)
            {
                await updates.CheckAndDownloadAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            App.LogFatal("Проверка обновлений на экране входа", ex);
            return;
        }

        if (updates.State == UpdateState.ReadyToApply)
        {
            UpdateBar.Message = $"Версия {updates.PendingVersion} скачана (сейчас {updates.CurrentVersion}). "
                + "Установите её до входа: в ней могут быть исправления подключения к сети.";
            UpdateBar.IsOpen = true;
        }
    }

    private void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (!_services.Updates.ApplyAndRestart())
        {
            UpdateBar.Severity = InfoBarSeverity.Warning;
            UpdateBar.Message = "Установить сейчас нельзя: стенд занят. Повторите после завершения работы.";
        }
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            var result = await Network.ConnectAsync(CodeBox.Text).ConfigureAwait(true);
            Show(result.Message, result.Failed ? InfoBarSeverity.Error : InfoBarSeverity.Success);
        }).ConfigureAwait(true);
    }

    private async void OnImportFileClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();

        // Приложение unpackaged: пикеру обязательно нужно окно-владелец.
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add(".otknet");

        if (await picker.PickSingleFileAsync() is not { } file)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await Network.ImportFileAsync(file.Path, CodeBox.Text).ConfigureAwait(true);
            Show(result.Message, result.Failed ? InfoBarSeverity.Error : InfoBarSeverity.Success);
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Сохраняет все действующие эталоны станции файлами выгрузки — тем же
    /// форматом, что принимает «Эталоны → Принять из файла» у администратора.
    /// </summary>
    private async void OnExportLocalClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add("*");

        if (await picker.PickSingleFolderAsync() is not { } folder)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var references = await _services.LoadReferencesAsync().ConfigureAwait(true);
            var written = new List<string>();
            foreach (var reference in references)
            {
                var scripts = await Task.Run(() => _services.Store.GetReferenceScriptsAsync(reference.Id)).ConfigureAwait(true);
                var package = ReferencePackage.FromReference(reference, scripts, "станция " + Environment.MachineName);
                var path = Path.Combine(folder.Path, SafeFileName(reference.Name) + ReferencePackage.FileExtension);
                await File.WriteAllTextAsync(path, package.ToJson()).ConfigureAwait(true);
                written.Add(Path.GetFileName(path));
            }

            Show(written.Count == 0
                    ? "На станции нет действующих эталонов — сохранять нечего."
                    : $"Сохранено в {folder.Path}: {string.Join(", ", written)}. Передайте файлы администратору сети.",
                written.Count == 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
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
            var result = await Network.CreateNetworkAsync(AdminLoginBox.Text, AdminNameBox.Text, AdminPasswordBox.Password)
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
            var result = await Network.RefreshAsync().ConfigureAwait(true);
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

        await RenderAsync().ConfigureAwait(true);
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return safe.Length == 0 ? "эталон" : safe;
    }

    private void Show(string message, InfoBarSeverity severity)
    {
        GateBar.Message = message;
        GateBar.Severity = severity;
        GateBar.IsOpen = true;
    }
}
