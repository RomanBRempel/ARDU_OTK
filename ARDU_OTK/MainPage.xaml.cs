using ARDU_OTK.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ARDU_OTK;

/// <summary>
/// The main content page displayed inside the application window.
/// Add your UI logic, event handlers, and data binding here.
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly UpdateService _updates = new();

    public MainPage()
    {
        InitializeComponent();

        _updates.StateChanged += OnUpdateStateChanged;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text = _updates.CurrentVersion is { } version
            ? $"Версия {version}"
            : "Версия — (сборка запущена не из установленной копии)";

        RenderState();

        // Проверка при старте: единственный момент, когда обновление можно
        // применить, никого не прервав.
        if (await _updates.CheckAndDownloadAsync().ConfigureAwait(true))
        {
            _updates.ApplyAndRestart();
        }
    }

    private void OnUpdateStateChanged(object? sender, EventArgs e)
    {
        // Velopack работает в фоновых потоках — возвращаемся в поток UI.
        DispatcherQueue.TryEnqueue(RenderState);
    }

    private async void OnUpdateActionClick(object sender, RoutedEventArgs e)
    {
        if (_updates.State == UpdateState.ReadyToApply)
        {
            if (!_updates.ApplyAndRestart())
            {
                UpdateBar.Message = "Идёт замер — обновление установится после его завершения.";
            }

            return;
        }

        await _updates.CheckAndDownloadAsync();
    }

    private void RenderState()
    {
        var busy = _updates.State is UpdateState.Checking or UpdateState.Downloading;
        UpdateProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateActionButton.IsEnabled = !busy && _updates.State != UpdateState.NotInstalled;
        UpdateActionButton.Content = _updates.State == UpdateState.ReadyToApply
            ? "Перезапустить и обновить"
            : "Проверить обновления";

        (UpdateBar.Severity, UpdateBar.Message) = _updates.State switch
        {
            UpdateState.NotInstalled => (InfoBarSeverity.Informational,
                "Запуск из каталога сборки. Обновления работают только в установленной копии."),
            UpdateState.Checking => (InfoBarSeverity.Informational, "Проверка обновлений…"),
            UpdateState.Downloading => (InfoBarSeverity.Informational, "Загрузка обновления…"),
            UpdateState.UpToDate => (InfoBarSeverity.Success, "Установлена последняя версия."),
            UpdateState.ReadyToApply => (InfoBarSeverity.Success,
                $"Доступна версия {_updates.PendingVersion}. Обновление загружено."),
            UpdateState.Failed => (InfoBarSeverity.Warning,
                $"Проверить обновления не удалось: {_updates.LastError}"),
            _ => (InfoBarSeverity.Informational, "Проверка обновлений не выполнялась."),
        };
    }
}
