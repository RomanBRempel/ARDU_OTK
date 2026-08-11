using System;
using System.Globalization;
using System.Threading.Tasks;
using ARDU_OTK.Services;
using ARDU_OTK.Services.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ARDU_OTK;

/// <summary>
/// Настройки: рабочее место, обновления и сведения о хранилище.
/// </summary>
/// <remarks>
/// Проверка обновлений живёт здесь, а не на рабочем экране: на заглавной
/// странице оператор занят бортом и эталоном, и кнопка обновления там только
/// отвлекает. Азимут стапеля и оператор — наоборот, настраиваются один раз на
/// стенд и потому тоже не место на рабочем экране.
/// </remarks>
public sealed partial class SettingsPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly UpdateService _updates = AppServices.Instance.Updates;

    /// <summary>Пока идёт первичное заполнение полей, обработчики не сохраняют.</summary>
    private bool _loading;

    private WorkstationSettings _settings = new();

    public SettingsPage()
    {
        InitializeComponent();

        _updates.StateChanged += OnUpdateStateChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text = _updates.CurrentVersion is { } version
            ? $"Версия {version}"
            : "Версия — (сборка запущена не из установленной копии)";

        try
        {
            StorePathText.Text = _services.Store.DatabaseFilePath;
        }
        catch (Exception ex)
        {
            StoreBar.Message = "Не удалось определить путь к хранилищу: " + ex.Message;
            StoreBar.IsOpen = true;
        }

        RenderState();
        await LoadWorkstationAsync().ConfigureAwait(true);
    }

    // --- Рабочее место ----------------------------------------------------

    private async Task LoadWorkstationAsync()
    {
        _loading = true;
        try
        {
            _settings = await _services.LoadSettingsAsync().ConfigureAwait(true);

            OperatorBox.Text = _settings.DefaultOperator;
        }
        catch (Exception ex)
        {
            ShowWorkstationProblem("Настройки не прочитаны: " + ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _loading = false;
        }

        RenderWorkstation();
    }

    private void OnWorkstationChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        _ = SaveWorkstationAsync();

    private void OnOperatorChanged(object sender, TextChangedEventArgs e) =>
        _ = SaveWorkstationAsync();

    /// <summary>
    /// Сохраняет настройки стенда.
    /// </summary>
    /// <remarks>
    /// Кнопки «Сохранить» здесь нет намеренно: настройка из двух полей, которую
    /// забыли подтвердить, — это ненастроенный стенд, о котором оператор думает,
    /// что он настроен. Запись идёт по каждому изменению.
    /// </remarks>
    private async Task SaveWorkstationAsync()
    {
        if (_loading)
        {
            return;
        }

        _settings = _settings with
        {
            DefaultOperator = OperatorBox.Text.Trim(),
        };

        try
        {
            await _services.SaveSettingsAsync(_settings).ConfigureAwait(true);
            WorkstationSavedText.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"Сохранено в {DateTimeOffset.Now:HH:mm:ss}.");
        }
        catch (Exception ex)
        {
            ShowWorkstationProblem("Настройки не сохранены: " + ex.Message, InfoBarSeverity.Error);
            return;
        }

        RenderWorkstation();
    }

    private void RenderWorkstation()
    {
        if (_settings.IsComplete)
        {
            WorkstationBar.IsOpen = false;
            return;
        }

        ShowWorkstationProblem(
            "Не задано: " + string.Join(", ", _settings.Problems)
          + ". Без этого прогон запустить нельзя.",
            InfoBarSeverity.Warning);
    }

    private void ShowWorkstationProblem(string message, InfoBarSeverity severity)
    {
        WorkstationBar.Severity = severity;
        WorkstationBar.Message = message;
        WorkstationBar.IsOpen = true;
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
