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

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            RootFrame.Navigate(typeof(SettingsPage));
            return;
        }

        RootFrame.Navigate(typeof(MainPage));
    }
}
