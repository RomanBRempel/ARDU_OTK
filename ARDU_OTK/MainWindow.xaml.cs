using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

        RootFrame.Navigate(typeof(MainPage));
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
        if (args.IsSettingsSelected)
        {
            RootFrame.Navigate(typeof(SettingsPage));
            return;
        }

        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        RootFrame.Navigate(tag == "references" ? typeof(ReferencesPage) : typeof(MainPage));
    }
}
