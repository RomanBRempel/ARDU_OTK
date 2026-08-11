using System;
using ARDU_OTK.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ARDU_OTK;

/// <summary>
/// Настройки рабочего места: обновления и сведения о хранилище.
/// </summary>
/// <remarks>
/// Проверка обновлений живёт здесь, а не на рабочем экране: на заглавной
/// странице оператор занят бортом и эталоном, и кнопка обновления там только
/// отвлекает.
/// </remarks>
public sealed partial class SettingsPage : Page
{
    private readonly UpdateService _updates = AppServices.Instance.Updates;

    public SettingsPage()
    {
        InitializeComponent();

        _updates.StateChanged += OnUpdateStateChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text = _updates.CurrentVersion is { } version
            ? $"Версия {version}"
            : "Версия — (сборка запущена не из установленной копии)";

        try
        {
            StorePathText.Text = AppServices.Instance.Store.DatabaseFilePath;
        }
        catch (Exception ex)
        {
            StoreBar.Message = "Не удалось определить путь к хранилищу: " + ex.Message;
            StoreBar.IsOpen = true;
        }

        RenderState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        _updates.StateChanged -= OnUpdateStateChanged;

    private void OnUpdateStateChanged(object? sender, EventArgs e)
    {
        // Velopack работает в фоновых потоках — возвращаемся в поток UI.
        DispatcherQueue.TryEnqueue(RenderState);
    }

    private async void OnUpdateActionClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_updates.State == UpdateState.ReadyToApply)
            {
                if (!_updates.ApplyAndRestart())
                {
                    UpdateBar.Message = "Идёт работа со стендом — обновление установится после её завершения.";
                }

                return;
            }

            await _updates.CheckAndDownloadAsync();
        }
        catch (Exception ex)
        {
            UpdateBar.Severity = InfoBarSeverity.Warning;
            UpdateBar.Message = "Проверить обновления не удалось: " + ex.Message;
        }
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
