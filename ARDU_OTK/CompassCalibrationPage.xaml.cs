using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using ARDU_OTK.Services.Fc;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ARDU_OTK;

/// <summary>
/// Экран серийной калибровки компаса: настройка прогона, ход выполнения,
/// приёмочные проверки, журнал и история по бортам.
/// </summary>
/// <remarks>
/// Страница ничего не создаёт сама — ни канал связи, ни реестр. Хост
/// подставляет <see cref="RunJob"/> и <see cref="LoadHistory"/>, поэтому экран
/// компилируется и открывается независимо от того, собран ли уже прикладной слой.
/// Пока делегаты не заданы, запуск заблокирован и это видно оператору.
/// </remarks>
public sealed partial class CompassCalibrationPage : Page
{
    private readonly DispatcherQueue _dispatcher;
    private CancellationTokenSource? _cts;
    private bool _running;

    public CompassCalibrationPage()
    {
        InitializeComponent();

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        BuildStageRows();

        Loaded += OnLoaded;
    }

    /// <summary>Запуск процедуры. Подставляется хостом.</summary>
    public Func<CalibrationRequest, ICalibrationProgress, CancellationToken, Task<CalibrationRunResult>>? RunJob
    {
        get;
        set;
    }

    /// <summary>Чтение истории прогонов. Подставляется хостом.</summary>
    public Func<Task<IReadOnlyList<RunSummary>>>? LoadHistory { get; set; }

    /// <summary>
    /// Повторная запись эталонных значений на борт. Подставляется хостом.
    /// </summary>
    /// <remarks>
    /// Отдельная точка входа, а не часть <see cref="RunJob"/>: перезапись — это
    /// не продолжение прогона, а действие после него, и в реестре она обязана
    /// выглядеть именно так.
    /// </remarks>
    public Func<long, string, IReadOnlyList<ParamMismatch>, IProgress<string>?, CancellationToken,
        Task<IReadOnlyList<ParamWriteRecord>>>? RewriteFromReference
    { get; set; }

    /// <summary>Параметры, не прошедшие проверку в последнем прогоне.</summary>
    public ObservableCollection<ParamMismatchRow> Mismatches { get; } = new();

    public ObservableCollection<CalibrationStageRow> Stages { get; } = new();

    public ObservableCollection<CalibrationCheckRow> Checks { get; } = new();

    public ObservableCollection<CalibrationLogRow> LogEntries { get; } = new();

    public ObservableCollection<CalibrationHistoryRow> History { get; } = new();

    /// <summary>
    /// Хост передаёт корень композиции параметром навигации — так страница
    /// остаётся независимой от того, как собрано приложение.
    /// </summary>
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is Services.AppServices services)
        {
            RunJob = services.RunCompassCalibrationAsync;
            LoadHistory = services.LoadHistoryAsync;
            RewriteFromReference = services.RewriteFromReferenceAsync;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshPorts();
        UpdateGate();

        WiringBar.IsOpen = RunJob is null;
        WiringBar.Message = "Прикладной слой не подключён: запуск процедуры недоступен.";

        _ = ReloadHistoryAsync();
    }

    private void BuildStageRows()
    {
        Stages.Clear();
        foreach (var (stage, title) in StageTitles())
        {
            Stages.Add(new CalibrationStageRow(stage, title));
        }
    }

    private static IEnumerable<(CalibrationStage Stage, string Title)> StageTitles()
    {
        yield return (CalibrationStage.Connect, "Подключение к борту");
        yield return (CalibrationStage.GpsFix, "Ожидание фиксации GPS");
        yield return (CalibrationStage.VerifyTopology, "Сверка состава компасов");
        yield return (CalibrationStage.WriteReference, "Запись эталонных значений");
        yield return (CalibrationStage.VerifyWrites, "Проверка записи обратным чтением");
        yield return (CalibrationStage.Reboot, "Перезагрузка борта");
        yield return (CalibrationStage.ReReadControlSample, "Контрольное перечитывание");
        yield return (CalibrationStage.FixedYaw, "Калибровка по фиксированному курсу");
        yield return (CalibrationStage.Acceptance, "Приёмочные проверки");
        yield return (CalibrationStage.Protocol, "Протокол");
    }

    // --- Форма и готовность к запуску -------------------------------------

    private void OnFormFieldChanged(object sender, TextChangedEventArgs e) => UpdateGate();

    private void OnPortSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateGate();

    private void OnRefreshPortsClick(object sender, RoutedEventArgs e) => RefreshPorts();

    private void RefreshPorts()
    {
        // GetPortNames отдаёт только имена: ни VID/PID, ни производителя, и в
        // списке попадаются уже отключённые порты. После перезагрузки борта имя
        // может смениться — поэтому выбор порта не кэшируется между прогонами.
        var previous = PortCombo.SelectedItem as string;
        PortCombo.Items.Clear();

        foreach (var name in SerialPort.GetPortNames())
        {
            PortCombo.Items.Add(name);
        }

        if (previous is not null && PortCombo.Items.Contains(previous))
        {
            PortCombo.SelectedItem = previous;
        }
        else if (PortCombo.Items.Count > 0)
        {
            PortCombo.SelectedIndex = 0;
        }

        UpdateGate();
    }

    /// <summary>Проверяет заполненность формы и объясняет, чего именно не хватает.</summary>
    private bool UpdateGate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ReferenceFileBox.Text))
        {
            problems.Add("не выбран эталонный файл");
        }

        var azimuthOk = TryReadAzimuth(out _);
        if (!azimuthOk)
        {
            problems.Add("азимут стапеля вне диапазона 0…360");
        }

        if (string.IsNullOrWhiteSpace(UnitIdBox.Text))
        {
            problems.Add("не введён серийный номер борта");
        }

        if (string.IsNullOrWhiteSpace(OperatorBox.Text))
        {
            problems.Add("не указан оператор");
        }

        if (PortCombo.SelectedItem is not string)
        {
            problems.Add("не выбран COM-порт");
        }

        if (RunJob is null)
        {
            problems.Add("прикладной слой не подключён");
        }

        var ready = problems.Count == 0;

        GateBar.IsOpen = !ready && !_running;
        GateBar.Message = ready ? string.Empty : "Не готово: " + string.Join(", ", problems) + ".";
        StartButton.IsEnabled = ready && !_running;

        return ready;
    }

    private bool TryReadAzimuth(out double azimuth)
    {
        azimuth = 0;

        var text = AzimuthBox.Text?.Trim().Replace(',', '.');
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out azimuth))
        {
            return false;
        }

        return azimuth is >= 0 and <= 360;
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();

            // Приложение unpackaged: пикеру обязательно нужно окно-владелец,
            // иначе вызов падает в рантайме.
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

            picker.FileTypeFilter.Add(".param");
            picker.FileTypeFilter.Add(".parm");
            picker.FileTypeFilter.Add(".params");

            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                ReferenceFileBox.Text = file.Path;
                UpdateGate();
            }
        }
        catch (Exception ex)
        {
            ShowError("Не удалось открыть файл эталона", ex);
        }
    }

    // --- Прогон -----------------------------------------------------------

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!UpdateGate() || RunJob is null)
            {
                return;
            }

            if (!TryReadAzimuth(out var azimuth))
            {
                return;
            }

            var confirmed = await ConfirmAsync(azimuth);
            if (!confirmed)
            {
                return;
            }

            var request = new CalibrationRequest(
                (string)PortCombo.SelectedItem,
                ReferenceFileBox.Text.Trim(),
                azimuth,
                UnitIdBox.Text.Trim(),
                OperatorBox.Text.Trim());

            await RunAsync(request).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowError("Прогон не удалось выполнить", ex);
            SetRunning(false);
        }
    }

    private async Task<bool> ConfirmAsync(double azimuth)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Запустить калибровку?",
            PrimaryButtonText = "Запустить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close,
            Content =
                $"Борт {UnitIdBox.Text.Trim()}, порт {PortCombo.SelectedItem}, азимут {azimuth:0.#}° (истинный).\n\n" +
                "В плату будут записаны параметры компасов из эталона, борт будет перезагружен, " +
                "затем выполнена калибровка по фиксированному курсу.\n\n" +
                "Калибровка сохраняется в плату немедленно и не может быть отменена. " +
                "Значения COMPASS_DIA и COMPASS_ODI при этом сбрасываются прошивкой.",
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task RunAsync(CalibrationRequest request)
    {
        SetRunning(true);
        Checks.Clear();
        ClearMismatches();
        BuildStageRows();
        VerdictBar.IsOpen = false;
        ErrorBar.IsOpen = false;
        RunSubjectText.Text = $"Борт {request.UnitId} · {request.PortName}";

        _cts = new CancellationTokenSource();
        var progress = new PageProgress(this);

        try
        {
            var result = await RunJob!(request, progress, _cts.Token).ConfigureAwait(true);
            ShowResult(result);
        }
        catch (OperationCanceledException)
        {
            StageText.Text = "Прогон отменён оператором";
            VerdictBar.Severity = InfoBarSeverity.Informational;
            VerdictBar.Title = "Отменено";
            VerdictBar.Message = "Прогон остановлен. Состояние платы могло остаться изменённым.";
            VerdictBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            ShowError("Прогон прерван ошибкой", ex);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetRunning(false);
            await ReloadHistoryAsync().ConfigureAwait(true);
        }
    }

    private void ShowResult(CalibrationRunResult result)
    {
        foreach (var check in result.Checks)
        {
            Checks.Add(new CalibrationCheckRow(check));
        }

        var failed = 0;
        var inconclusive = 0;
        foreach (var check in result.Checks)
        {
            if (check.Outcome == CheckOutcome.Fail)
            {
                failed++;
            }
            else if (check.Outcome == CheckOutcome.Inconclusive)
            {
                inconclusive++;
            }
        }

        ChecksSummaryText.Text = result.Checks.Count == 0
            ? "Проверки не выполнялись"
            : $"Всего {result.Checks.Count}, отказов {failed}, без данных {inconclusive}";

        VerdictBar.IsOpen = true;
        if (result.Passed)
        {
            VerdictBar.Severity = InfoBarSeverity.Success;
            VerdictBar.Title = "ГОДЕН";
            VerdictBar.Message = "Все приёмочные проверки пройдены.";
        }
        else
        {
            VerdictBar.Severity = InfoBarSeverity.Error;
            VerdictBar.Title = "ОТКАЗ";
            VerdictBar.Message = result.FailureReason is { Length: > 0 } reason
                ? $"Остановлено на стадии «{result.FailedStage}»: {reason}"
                : "Процедура не завершена.";
        }

        if (result.FailedStage is { } stage)
        {
            foreach (var row in Stages)
            {
                if (row.Stage == stage)
                {
                    row.SetState(StageRowState.Fail);
                }
            }
        }

        ShowMismatches(result);
    }

    // --- Не прошедшие проверку параметры -----------------------------------

    /// <summary>Прогон, к которому относятся расхождения, — попытки перезаписи ложатся в него.</summary>
    private long _mismatchRunId;

    private string _mismatchPort = string.Empty;

    private bool _rewriting;

    private void ClearMismatches()
    {
        Mismatches.Clear();
        _mismatchRunId = 0;
        _mismatchPort = string.Empty;

        MismatchCard.Visibility = Visibility.Collapsed;
        RewriteBar.IsOpen = false;
        RewriteProgressPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowMismatches(CalibrationRunResult result)
    {
        Mismatches.Clear();
        _mismatchRunId = result.RunId;
        _mismatchPort = result.PortName;

        foreach (var mismatch in result.Mismatches)
        {
            Mismatches.Add(new ParamMismatchRow(mismatch));
        }

        MismatchCard.Visibility = Mismatches.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RewriteAllButton.IsEnabled = Mismatches.Count > 0 && CanRewrite();
    }

    /// <summary>
    /// Возможна ли перезапись вообще: нужен хост, порт и незанятый стенд.
    /// </summary>
    private bool CanRewrite() =>
        RewriteFromReference is not null && _mismatchPort.Length > 0 && !_running && !_rewriting;

    private async void OnRewriteOneClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name })
        {
            return;
        }

        var row = Mismatches.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal));
        if (row is not null)
        {
            await RewriteAsync(new[] { row }).ConfigureAwait(true);
        }
    }

    private async void OnRewriteAllClick(object sender, RoutedEventArgs e) =>
        await RewriteAsync(Mismatches.Where(static r => r.CanRewrite).ToArray()).ConfigureAwait(true);

    /// <summary>
    /// Возвращается к борту и пишет эталонные значения выбранных параметров.
    /// </summary>
    /// <remarks>
    /// 🔴 Вердикт прогона не пересматривается ни при каком исходе. Плата,
    /// провалившая проверку, остаётся проваленной: сдавать её следует повторным
    /// прогоном целиком. Иначе в реестре появилось бы изделие, «прошедшее»
    /// проверку, которую оно не проходило.
    /// </remarks>
    private async Task RewriteAsync(IReadOnlyList<ParamMismatchRow> rows)
    {
        if (rows.Count == 0 || !CanRewrite())
        {
            return;
        }

        SetRewriting(true);
        RewriteBar.IsOpen = false;

        try
        {
            var progress = new Progress<string>(text => RewriteProgressText.Text = text);

            var records = await RewriteFromReference!(
                _mismatchRunId,
                _mismatchPort,
                rows.Select(static r => r.Source).ToArray(),
                progress,
                CancellationToken.None).ConfigureAwait(true);

            foreach (var record in records)
            {
                var row = rows.FirstOrDefault(r => string.Equals(r.Name, record.Name, StringComparison.Ordinal));
                row?.ApplyOutcome(record);
            }

            var accepted = records.Count(static r => r.Outcome == WriteOutcome.Verified);
            var refused = records.Count - accepted;

            RewriteBar.Severity = refused == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            RewriteBar.Message = refused == 0
                ? $"Перезаписано и подтверждено обратным чтением: {accepted}. Вердикт прогона не изменился — "
                  + "изделие сдаётся повторным прогоном целиком."
                : $"Принято бортом: {accepted}, отклонено: {refused}. Отклонённые имена разбирать по журналу — "
                  + "повторная запись тем же способом их не возьмёт.";
            RewriteBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            ShowError("Повторная запись не выполнена", ex);
        }
        finally
        {
            SetRewriting(false);
        }
    }

    private void SetRewriting(bool rewriting)
    {
        _rewriting = rewriting;

        RewriteRing.IsActive = rewriting;
        RewriteProgressPanel.Visibility = rewriting ? Visibility.Visible : Visibility.Collapsed;
        RewriteProgressText.Text = string.Empty;

        RewriteAllButton.IsEnabled = !rewriting && Mismatches.Any(static r => r.CanRewrite) && CanRewrite();

        // Кнопки строк гасятся на время записи все разом: порт открыт
        // монопольно, и второе нажатие получило бы отказ доступа, а не
        // повторную попытку.
        foreach (var row in Mismatches)
        {
            if (row.OutcomeVisibility == Visibility.Collapsed)
            {
                row.CanRewrite = !rewriting;
            }
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void OnNewRunClick(object sender, RoutedEventArgs e)
    {
        Checks.Clear();
        LogEntries.Clear();
        ClearMismatches();
        BuildStageRows();
        VerdictBar.IsOpen = false;
        ErrorBar.IsOpen = false;
        ChecksSummaryText.Text = string.Empty;
        StageText.Text = "Готов к запуску";
        StageDetailText.Text = string.Empty;
        UnitIdBox.Text = string.Empty;
        UpdateGate();
    }

    private void OnClearLogClick(object sender, RoutedEventArgs e) => LogEntries.Clear();

    private void SetRunning(bool running)
    {
        _running = running;
        RunRing.IsActive = running;
        RunProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        RunStatusPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        // StackPanel не Control, свойства IsEnabled у него нет — гасим сами поля.
        ReferenceFileBox.IsEnabled = !running;
        BrowseButton.IsEnabled = !running;
        AzimuthBox.IsEnabled = !running;
        UnitIdBox.IsEnabled = !running;
        OperatorBox.IsEnabled = !running;
        PortCombo.IsEnabled = !running;
        RefreshPortsButton.IsEnabled = !running;
        CancelButton.IsEnabled = running;
        NewRunButton.IsEnabled = !running;
        UpdateGate();
    }

    private void ShowError(string title, Exception ex)
    {
        ErrorBar.Severity = InfoBarSeverity.Error;
        ErrorBar.Title = title;
        ErrorBar.Message = ex.Message;
        ErrorBar.IsOpen = true;
    }

    // --- История ----------------------------------------------------------

    private async void OnRefreshHistoryClick(object sender, RoutedEventArgs e) =>
        await ReloadHistoryAsync().ConfigureAwait(true);

    private async Task ReloadHistoryAsync()
    {
        if (LoadHistory is null)
        {
            HistoryHintText.Text = "Реестр не подключён.";
            return;
        }

        try
        {
            var runs = await LoadHistory().ConfigureAwait(true);
            History.Clear();
            foreach (var run in runs)
            {
                History.Add(new CalibrationHistoryRow(run));
            }

            HistoryHintText.Text = History.Count == 0
                ? "Прогонов ещё не было."
                : $"Показано записей: {History.Count}";
        }
        catch (Exception ex)
        {
            HistoryHintText.Text = "Не удалось прочитать реестр: " + ex.Message;
        }
    }

    // --- Прогресс ---------------------------------------------------------

    /// <summary>
    /// Мост от процедуры к UI. Процедура работает не в потоке интерфейса,
    /// поэтому каждый вызов переносится в очередь диспетчера.
    /// </summary>
    private sealed class PageProgress : ICalibrationProgress
    {
        private readonly CompassCalibrationPage _page;

        public PageProgress(CompassCalibrationPage page) => _page = page;

        public void StageStarted(CalibrationStage stage, string message) => Post(() =>
        {
            _page.StageText.Text = message;
            _page.StageDetailText.Text = string.Empty;

            foreach (var row in _page.Stages)
            {
                if (row.Stage == stage)
                {
                    row.SetState(StageRowState.Running);
                }
            }
        });

        public void StageFinished(CalibrationStage stage, CheckOutcome outcome, string message) => Post(() =>
        {
            _page.StageDetailText.Text = message;

            var state = outcome switch
            {
                CheckOutcome.Pass => StageRowState.Pass,
                CheckOutcome.Fail => StageRowState.Fail,
                _ => StageRowState.Inconclusive,
            };

            foreach (var row in _page.Stages)
            {
                if (row.Stage == stage)
                {
                    row.SetState(state);
                }
            }
        });

        public void Log(MavSeverity severity, string message) => Post(() =>
        {
            _page.LogEntries.Add(new CalibrationLogRow(severity, message, DateTimeOffset.Now));

            // Журнал цехового прогона не должен расти без границ.
            while (_page.LogEntries.Count > 500)
            {
                _page.LogEntries.RemoveAt(0);
            }
        });

        private void Post(Action action) => _page._dispatcher.TryEnqueue(() => action());
    }
}
