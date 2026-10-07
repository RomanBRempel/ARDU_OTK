using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ARDU_OTK.Services;
using ARDU_OTK.Services.Network;
using ARDU_OTK.Shared.Network;
using ARDU_OTK.Shared.Security;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ARDU_OTK;

/// <summary>Строка пользователя в списке администратора.</summary>
public sealed class NetworkUserRow
{
    public NetworkUserRow(NetworkUser user)
    {
        User = user;
        DisplayName = user.DisplayName;
        Details = $"{user.Login} · {OtkRoles.Caption(user.Role)} · "
            + (user.IsActive ? "действует" : "отключён")
            + (user.PinHash is null ? string.Empty : " · есть PIN");
    }

    public NetworkUser User { get; }

    public string DisplayName { get; }

    public string Details { get; }
}

/// <summary>
/// Администрирование сети ОТК: пользователи, выпуск пакета, ключ подписи.
/// </summary>
/// <remarks>
/// Страница открывается только администратору, но проверка роли — в
/// <see cref="NetworkService"/>, а не в навигации: скрытый пункт меню —
/// это удобство, а не защита.
/// </remarks>
public sealed partial class NetworkAdminPage : Page
{
    private readonly NetworkService _network = AppServices.Instance.Network;

    private NetworkUser? _editing;

    public NetworkAdminPage()
    {
        InitializeComponent();

        RoleBox.ItemsSource = OtkRoles.All.Select(OtkRoles.Caption).ToList();
        RoleBox.SelectedIndex = OtkRoles.All.Count - 1;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ReloadAsync().ConfigureAwait(true);

    private async Task ReloadAsync()
    {
        try
        {
            var state = await _network.GetStateAsync().ConfigureAwait(true);
            IssueStateText.Text = state.Serial > 0
                ? $"Последний выпуск — №{state.Serial} от {state.IssuedUtc?.ToLocalTime():dd.MM.yyyy HH:mm} ({state.IssuedBy})."
                : "Пакет ещё не выпускался.";
            NetworkCodeBox.Text = FormatCode(state.NetworkCode);

            var users = await _network.ListUsersAsync().ConfigureAwait(true);
            UsersList.ItemsSource = users.Select(static u => new NetworkUserRow(u)).ToList();
        }
        catch (Exception ex)
        {
            Show("Сеть не прочитана: " + ex.Message, InfoBarSeverity.Error);
        }

        IssueButton.IsEnabled = NetworkService.HasSigningKey && NetworkService.HasGitHubToken;
        IssueFileButton.IsEnabled = NetworkService.HasSigningKey;
        TokenStateText.Text = NetworkService.HasGitHubToken
            ? "Токен сохранён на этом компьютере. Новый токен заменит прежний."
            : "Токен не задан — выложить пакет в GitHub нельзя, только сохранить в файл.";
        KeyStateText.Text = NetworkService.HasSigningKey
            ? $"Ключ на этом компьютере: {NetworkSigningKeyStore.KeyPath}."
            : "На этом компьютере ключа нет — выпускать пакет сети отсюда нельзя.";
    }

    private void OnUserSelected(object sender, SelectionChangedEventArgs e)
    {
        if (UsersList.SelectedItem is not NetworkUserRow row)
        {
            return;
        }

        _editing = row.User;
        FormTitle.Text = row.User.DisplayName;
        UserLoginBox.Text = row.User.Login;
        UserNameBox.Text = row.User.DisplayName;
        RoleBox.SelectedIndex = Math.Max(0, OtkRoles.All.ToList().IndexOf(row.User.Role));
        ActiveSwitch.IsOn = row.User.IsActive;
        ClearForm();
    }

    private void OnNewUserClick(object sender, RoutedEventArgs e)
    {
        _editing = null;
        UsersList.SelectedItem = null;
        FormTitle.Text = "Новый пользователь";
        UserLoginBox.Text = string.Empty;
        UserNameBox.Text = string.Empty;
        RoleBox.SelectedIndex = OtkRoles.All.Count - 1;
        ActiveSwitch.IsOn = true;
        ClearForm();
    }

    private async void OnSaveUserClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var user = (_editing ?? new NetworkUser(Guid.NewGuid(), string.Empty, string.Empty, OtkRoles.Operator, true, null, null)) with
            {
                Login = UserLoginBox.Text,
                DisplayName = UserNameBox.Text,
                Role = OtkRoles.All[Math.Max(0, RoleBox.SelectedIndex)],
                IsActive = ActiveSwitch.IsOn,
            };

            await _network.SaveUserAsync(user, UserPasswordBox.Password, UserPinBox.Password, ClearPinBox.IsChecked == true)
                .ConfigureAwait(true);

            Show($"Пользователь «{user.DisplayName}» сохранён. До стендов изменение дойдёт со следующим пакетом сети.",
                InfoBarSeverity.Success);
            ClearForm();
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Show("Пользователь не сохранён: " + ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void OnIssueClick(object sender, RoutedEventArgs e) => await IssueAsync(saveTo: null).ConfigureAwait(true);

    private async void OnIssueFileClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.SuggestedFileName = "network";
        picker.FileTypeChoices.Add("Пакет сети ОТК", new List<string> { ".otknet" });

        if (await picker.PickSaveFileAsync() is { } file)
        {
            await IssueAsync(file.Path).ConfigureAwait(true);
        }
    }

    private async void OnSaveTokenClick(object sender, RoutedEventArgs e)
    {
        try
        {
            NetworkService.SaveGitHubToken(TokenBox.Password);
            TokenBox.Password = string.Empty;
            Show("Токен GitHub сохранён.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Show("Токен не сохранён: " + ex.Message, InfoBarSeverity.Error);
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    private async Task IssueAsync(string? saveTo)
    {
        IssueButton.IsEnabled = false;
        IssueFileButton.IsEnabled = false;
        try
        {
            var result = await _network.IssueAsync(saveTo).ConfigureAwait(true);
            Show(result.Message, result.Failed ? InfoBarSeverity.Error : InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Show("Пакет не выпущен: " + ex.Message, InfoBarSeverity.Error);
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    private async void OnBackupKeyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SecretPolicy.ValidatePassword(BackupPasswordBox.Password) is { } error)
            {
                Show(error, InfoBarSeverity.Error);
                return;
            }

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            picker.SuggestedFileName = "ARDU_OTK-network-key";
            picker.FileTypeChoices.Add("Ключ сети ОТК (зашифрован)", new List<string> { ".otkkey" });

            if (await picker.PickSaveFileAsync() is not { } file)
            {
                return;
            }

            await NetworkService.ExportKeyBackupAsync(file.Path, BackupPasswordBox.Password).ConfigureAwait(true);
            BackupPasswordBox.Password = string.Empty;
            Show($"Резервная копия ключа сохранена: {file.Path}. Пароль от неё храните отдельно.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Show("Копия ключа не сохранена: " + ex.Message, InfoBarSeverity.Error);
        }
    }

    /// <summary>Код группами по 4 — так его диктуют и вводят.</summary>
    private static string FormatCode(string code) =>
        string.Join('-', Enumerable.Range(0, code.Length / 4).Select(i => code.Substring(i * 4, 4)));

    private void ClearForm()
    {
        UserPasswordBox.Password = string.Empty;
        UserPinBox.Password = string.Empty;
        ClearPinBox.IsChecked = false;
    }

    private void Show(string message, InfoBarSeverity severity)
    {
        PageBar.Message = message;
        PageBar.Severity = severity;
        PageBar.IsOpen = true;
    }
}
