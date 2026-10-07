using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ARDU_OTK.Services;
using ARDU_OTK.Shared.Security;

namespace ARDU_OTK;

/// <summary>
/// Окно приложения. Содержит навигацию и кадр со страницами: рабочий экран
/// стенда и раздел настроек.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Frame гасит исключение навигации и поднимает NavigationFailed; без
        // подписки процесс просто умирает с 0xC000027B без объяснений.
        RootFrame.NavigationFailed += (_, e) => App.LogFatal("NavigationFailed", e.Exception);

        // 🔴 Первым открывается вход, а не рабочий экран: на стенде работают
        // только пользователи сети по эталонам сети, и новая установка обязана
        // сначала принять пакет администратора.
        AppServices.Instance.Network.SessionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(ApplySession);
        RootFrame.Navigate(typeof(NetworkGatePage));
    }

    /// <summary>
    /// Переключает окно по сессии: без пользователя — экран входа и скрытое
    /// меню, с пользователем — рабочий экран и пункты по роли.
    /// </summary>
    private void ApplySession()
    {
        var user = AppServices.Instance.Network.Session;
        RootNav.IsPaneVisible = user is not null;
        NetworkItem.Visibility = AppServices.Instance.Network.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

        if (user is null)
        {
            RootFrame.Navigate(typeof(NetworkGatePage));
            RootFrame.BackStack.Clear();
            return;
        }

        SessionItem.Content = $"Выйти · {user.DisplayName} ({OtkRoles.Caption(user.Role)})";
        if (RootFrame.CurrentSourcePageType == typeof(NetworkGatePage))
        {
            RootNav.SelectedItem = StandItem;
            RootFrame.Navigate(typeof(MainPage));
            RootFrame.BackStack.Clear();
        }
    }

    private void OnLogoutTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        // Выйти посреди прогона нельзя: прогон подписан вошедшим, и смена
        // пользователя на ходу сделала бы подпись ложной.
        if (AppServices.Instance.IsBusy)
        {
            return;
        }

        AppServices.Instance.Network.Logout();
    }

    /// <summary>
    /// Пересобирает текущую страницу после смены масштаба шрифтов.
    /// </summary>
    /// <remarks>
    /// Ресурс размера подставляется в элемент при загрузке разметки и потом
    /// не пересчитывается, поэтому одной записи в словарь ресурсов мало:
    /// уже построенная страница осталась бы с прежними шрифтами. Страницы в
    /// кадре не кэшируются, так что повторная навигация создаёт их заново.
    /// </remarks>
    public void RebuildCurrentPage()
    {
        Type? page = RootFrame.CurrentSourcePageType;
        if (page is null)
        {
            return;
        }

        RootFrame.Navigate(page);
        RootFrame.BackStack.Clear();
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // 🔴 Без вошедшего пользователя — только вход, каким бы путём ни пришёл
        // выбор пункта. Скрытого меню мало: NavigationView поднимает выбор и
        // сам, при загрузке окна (пункт «Стенд» выбран в разметке), и этот
        // выбор уводил на рабочий экран в обход входа.
        if (AppServices.Instance.Network.Session is null)
        {
            if (RootFrame.CurrentSourcePageType != typeof(NetworkGatePage))
            {
                RootFrame.Navigate(typeof(NetworkGatePage));
            }

            return;
        }

        if (args.IsSettingsSelected)
        {
            RootFrame.Navigate(typeof(SettingsPage));
            return;
        }

        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        RootFrame.Navigate(tag switch
        {
            "references" => typeof(ReferencesPage),
            "osd" => typeof(OsdPage),
            "runs" => typeof(RunsPage),
            "network" => typeof(NetworkAdminPage),
            _ => typeof(MainPage),
        });
    }
}
