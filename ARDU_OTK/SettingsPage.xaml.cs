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
        LoadFontScale();
        LoadTheme();
        await LoadWorkstationAsync().ConfigureAwait(true);
    }

    // --- Масштаб шрифтов --------------------------------------------------

    /// <summary>Ступени масштаба. Множитель и подпись идут парой.</summary>
    private static readonly (double Factor, string Label)[] FontScaleSteps =
    {
        (1.00, "100 % — как свёрстано"),
        (1.15, "115 %"),
        (1.30, "130 %"),
        (1.50, "150 %"),
        (1.75, "175 %"),
        (2.00, "200 % — максимум"),
    };

    /// <summary>Заполнение списка не должно выглядеть как выбор оператора.</summary>
    private bool _fontScaleLoading;

    private void LoadFontScale()
    {
        _fontScaleLoading = true;
        try
        {
            FontScaleBox.Items.Clear();
            int selected = 0;
            for (int i = 0; i < FontScaleSteps.Length; i++)
            {
                FontScaleBox.Items.Add(FontScaleSteps[i].Label);

                // Ближайшая ступень, а не точное совпадение: в файле мог
                // остаться масштаб от прежнего набора ступеней, и пустой
                // список выбора сказал бы оператору, что масштаб не задан.
                if (Math.Abs(FontScaleSteps[i].Factor - UiScale.Current)
                    < Math.Abs(FontScaleSteps[selected].Factor - UiScale.Current))
                {
                    selected = i;
                }
            }

            FontScaleBox.SelectedIndex = selected;

            // Проценты сами по себе ни о чём не говорят: показываем, во что
            // они превращаются на самом мелком тексте — в строке таблицы.
            FontScaleText.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"строка таблицы — {Math.Round(12 * UiScale.Current)} px");
        }
        finally
        {
            _fontScaleLoading = false;
        }
    }

    private void OnFontScaleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fontScaleLoading || FontScaleBox.SelectedIndex < 0)
        {
            return;
        }

        double factor = FontScaleSteps[FontScaleBox.SelectedIndex].Factor;
        if (Math.Abs(factor - UiScale.Current) < 0.001)
        {
            return;
        }

        UiScale.Apply(factor);
        UiScale.Save(factor);

        // Страница пересобирается, чтобы новый размер подставился в разметку.
        // Текущий экземпляр после этого выброшен, поэтому ничего дописывать
        // после вызова нельзя.
        if (App.MainWindow is MainWindow window)
        {
            window.RebuildCurrentPage();
        }
    }

    // --- Тема оформления --------------------------------------------------

    /// <summary>Выбор темы. Значение и подпись идут парой.</summary>
    private static readonly (AppTheme Theme, string Label)[] ThemeChoices =
    {
        (AppTheme.System, "Как в Windows"),
        (AppTheme.Light, "Светлая"),
        (AppTheme.Dark, "Тёмная"),
    };

    /// <summary>Заполнение списка не должно выглядеть как выбор оператора.</summary>
    private bool _themeLoading;

    private void LoadTheme()
    {
        _themeLoading = true;
        try
        {
            ThemeBox.Items.Clear();
            int selected = 0;
            for (int i = 0; i < ThemeChoices.Length; i++)
            {
                ThemeBox.Items.Add(ThemeChoices[i].Label);
                if (ThemeChoices[i].Theme == UiTheme.Current)
                {
                    selected = i;
                }
            }

            ThemeBox.SelectedIndex = selected;
        }
        finally
        {
            _themeLoading = false;
        }

        RenderThemeRestart();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_themeLoading || ThemeBox.SelectedIndex < 0)
        {
            return;
        }

        // Выбор того же самого — не изменение. Список успевает поднять событие
        // и без участия оператора (например, когда страница только строится), и
        // без этой проверки такое событие переписало бы сохранённую настройку.
        AppTheme theme = ThemeChoices[ThemeBox.SelectedIndex].Theme;
        if (theme == UiTheme.Current)
        {
            return;
        }

        UiTheme.Save(theme);
        RenderThemeRestart();
    }

    /// <summary>
    /// Показывает расхождение между выбранной темой и той, с которой работает
    /// приложение сейчас.
    /// </summary>
    /// <remarks>
    /// Сообщение нужно именно потому, что выбор в списке не меняет экран
    /// немедленно: молча сохранённая настройка выглядит как несработавшая, и
    /// оператор жмёт её второй и третий раз, решив, что не попал.
    /// </remarks>
    private void RenderThemeRestart()
    {
        ThemeRestartBar.IsOpen = UiTheme.RestartRequired;
        if (!UiTheme.RestartRequired)
        {
            return;
        }

        ThemeRestartBar.Severity = InfoBarSeverity.Informational;
        ThemeRestartBar.Message =
            "Новая тема сохранена и появится при следующем запуске приложения.";
    }

    private void OnThemeRestartClick(object sender, RoutedEventArgs e)
    {
        // Тот же запрет, что и у обновления: перезапуск в середине замера
        // теряет результат, а деталь уже стоит в приспособлении.
        if (_updates.IsBusy())
        {
            ThemeRestartBar.Severity = InfoBarSeverity.Warning;
            ThemeRestartBar.Message =
                "Идёт работа со стендом — тема применится после её завершения, "
              + "при следующем запуске приложения.";
            return;
        }

        try
        {
            Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
        }
        catch (Exception ex)
        {
            ThemeRestartBar.Severity = InfoBarSeverity.Warning;
            ThemeRestartBar.Message =
                "Перезапустить не удалось: " + ex.Message
              + ". Закройте и откройте приложение — тема уже сохранена.";
        }
    }

    // --- Рабочее место ----------------------------------------------------

    private async Task LoadWorkstationAsync()
    {
        _loading = true;
        try
        {
            _settings = await _services.LoadSettingsAsync().ConfigureAwait(true);

            // 🔴 NaN, а не 0: пустое поле означает «не задано», а нулевые
            // широта и долгота — это точка в Гвинейском заливе, и калибровка
            // по магнитной модели в ней даст компас, «успешно» откалиброванный
            // на чужое поле.
            StandLatBox.Value = _settings.StandLatitudeDeg ?? double.NaN;
            StandLonBox.Value = _settings.StandLongitudeDeg ?? double.NaN;

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

        if (!_settings.HasStandPosition)
        {
            await DetectStandPositionAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Определяет координаты рабочего места службой Windows.
    /// </summary>
    /// <remarks>
    /// 🔴 Спрашивать оператора первым делом — значит требовать от него данные,
    /// которые машина знает сама. Ручной ввод остаётся, но как запасной путь:
    /// он нужен, когда определение местоположения выключено политикой цеха.
    /// Определённые координаты сразу сохраняются — иначе при следующем запуске
    /// приложение спросит снова о том, что уже выяснило.
    /// </remarks>
    private async Task DetectStandPositionAsync()
    {
        WorkstationBar.Severity = InfoBarSeverity.Informational;
        WorkstationBar.Title = "Определяю координаты";
        WorkstationBar.Message = "Спрашиваю службу определения местоположения Windows…";
        WorkstationBar.IsOpen = true;

        var fix = await WorkstationLocator.TryDetectAsync().ConfigureAwait(true);

        if (!fix.Found)
        {
            ShowWorkstationProblem(
                "Координаты рабочего места определить не удалось: " + fix.Detail
              + ". Введите их вручную — без них калибровка компаса ответит отказом.",
                InfoBarSeverity.Warning);
            return;
        }

        _loading = true;
        try
        {
            StandLatBox.Value = fix.LatitudeDeg;
            StandLonBox.Value = fix.LongitudeDeg;
        }
        finally
        {
            _loading = false;
        }

        await SaveWorkstationAsync().ConfigureAwait(true);
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
            StandLatitudeDeg = double.IsNaN(StandLatBox.Value) ? null : StandLatBox.Value,
            StandLongitudeDeg = double.IsNaN(StandLonBox.Value) ? null : StandLonBox.Value,
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
