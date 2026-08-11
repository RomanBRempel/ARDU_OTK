using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ARDU_OTK.Services;
using ARDU_OTK.Services.Fc;
using ARDU_OTK.Services.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ARDU_OTK;

/// <summary>Параметр навигации на экран приёмки.</summary>
/// <param name="Services">Корень композиции.</param>
/// <param name="Reference">Эталон, по которому сдаётся изделие.</param>
/// <param name="PortName">Порт целевого борта.</param>
public sealed record AcceptanceArgs(AppServices Services, CalibrationReference Reference, string PortName);

/// <summary>
/// Приёмка изделия: автоматическая часть, решение оператора, команды оператора.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 Порядок разорван намеренно. Автоматическая часть доводит борт до
/// состояния «конфигурация перенесена, борт перезагружен» и останавливается на
/// показе оставшихся расхождений. Дальше решает человек: исправить записью или
/// принять как есть. Автомат, проходящий эту развилку сам, либо останавливает
/// годное изделие, либо пропускает негодное.
/// </para>
/// <para>
/// Азимут оснастки на этом экране отсутствует как понятие. Курс вводится в
/// момент калибровки компаса и означает истинный курс борта <b>сейчас</b>:
/// борт можно повернуть между двумя прогонами, и настройка стенда об этом не
/// узнает.
/// </para>
/// </remarks>
public sealed partial class AcceptancePage : Page
{
    private AppServices? _services;
    private CalibrationReference? _reference;
    private string _portName = string.Empty;

    private AcceptanceSession? _session;
    private CancellationTokenSource? _cts;
    private bool _busy;

    // Шаги автоматической части. Список фиксирован и показывается до запуска:
    // оператор должен видеть, что программа собирается сделать с бортом.
    private readonly AcceptanceStepRow _stepCompare = new("Вычитывание прошивки и сверка с эталоном");
    private readonly AcceptanceStepRow _stepPrimary = new("Внешний компас первым в очереди, перезагрузка");
    private readonly AcceptanceStepRow _stepWrite = new("Перенос расхождений из эталона");
    private readonly AcceptanceStepRow _stepReboot = new("Перезагрузка борта");
    private readonly AcceptanceStepRow _stepRecheck = new("Повторная сверка после перезагрузки");

    private readonly bool _ready;

    public AcceptancePage()
    {
        InitializeComponent();

        Steps.Add(_stepCompare);
        Steps.Add(_stepPrimary);
        Steps.Add(_stepWrite);
        Steps.Add(_stepReboot);
        Steps.Add(_stepRecheck);

        _ready = true;
    }

    /// <summary>Шаги автоматической части.</summary>
    public ObservableCollection<AcceptanceStepRow> Steps { get; } = new();

    /// <summary>Расхождения последней сверки.</summary>
    public ObservableCollection<ParameterDifferenceRow> Differences { get; } = new();

    /// <summary>Журнал приёмки.</summary>
    public ObservableCollection<CalibrationLogRow> LogEntries { get; } = new();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not AcceptanceArgs args)
        {
            VerdictBar.Severity = InfoBarSeverity.Error;
            VerdictBar.Title = "Экран открыт без эталона";
            VerdictBar.Message = "Это сбой сборки, а не действие оператора. Вернитесь на рабочий экран.";
            VerdictBar.IsOpen = true;
            StartButton.IsEnabled = false;
            return;
        }

        _services = args.Services;
        _reference = args.Reference;
        _portName = args.PortName;

        SubjectText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Эталон «{_reference.Name}» · {ReferenceCaption.Describe(_reference)} · порт {_portName}");

        // Курс подставляем текущий, измеренный бортом: чаще всего он и верен, а
        // подставленный ноль оператор бы не заметил и откалибровал бы на север.
        if (_services.LiveState?.Attitude is { } attitude)
        {
            HeadingBox.Value = Math.Round(((attitude.YawRad * 180.0 / Math.PI) + 360.0) % 360.0, 1);
        }
    }

    /// <remarks>
    /// Приёмка закрывается вместе с уходом с экрана: порт открыт монопольно, и
    /// брошенная сессия не дала бы ни подключиться заново, ни запустить
    /// повторную приёмку.
    /// </remarks>
    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        _cts?.Cancel();
        await CloseSessionAsync().ConfigureAwait(true);
    }

    // --- Автоматическая часть ----------------------------------------------

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (_services is null || _reference is null || _busy)
        {
            return;
        }

        SetBusy(true);
        Differences.Clear();
        VerdictBar.IsOpen = false;
        DiffBar.IsOpen = false;

        foreach (var step in Steps)
        {
            step.Set(AcceptanceStepState.Pending);
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var progress = new Progress<string>(text => ProgressText.Text = text);

        try
        {
            await CloseSessionAsync().ConfigureAwait(true);

            Log("Открываю канал связи с бортом…");
            _session = await _services.BeginAcceptanceAsync(_portName, ct).ConfigureAwait(true);
            Log($"Борт на связи. {_session.FirmwareBanner ?? "баннер прошивки не получен"}");

            var expected = _reference.ParseParameters().Values;
            var roles = _reference.ToRoleMap();

            // --- 1. Сверка ---
            _stepCompare.Set(AcceptanceStepState.Running);
            var plan = await _session.CompareAsync(expected, roles, progress, ct).ConfigureAwait(true);
            _stepCompare.Set(
                AcceptanceStepState.Pass,
                $"сопоставлено {plan.Compared}, совпало {plan.Matched}, расхождений {plan.Differences.Count}");
            Log($"Сверка: сопоставлено {plan.Compared}, совпало {plan.Matched}, "
              + $"расхождений {plan.Differences.Count}, пропущено неконтролируемых {plan.SkippedUncontrolled}.");

            // --- 2. Внешний компас первым ---
            _stepPrimary.Set(AcceptanceStepState.Running);
            var primary = await _session.MakeExternalCompassPrimaryAsync(progress, ct).ConfigureAwait(true);
            _stepPrimary.Set(primary.Ok ? AcceptanceStepState.Pass : AcceptanceStepState.Fail, primary.Message);
            Log(primary.Message);

            if (!primary.Ok)
            {
                // Дальше идти нельзя: калибровка легла бы на слот, который
                // принадлежит не тому датчику.
                Verdict(false, "Приёмка остановлена", primary.Message);
                return;
            }

            // --- 3. Перенос ---
            _stepWrite.Set(AcceptanceStepState.Running);
            var writable = plan.Writable;
            if (writable.Count == 0)
            {
                _stepWrite.Set(AcceptanceStepState.Pass, "переносить нечего: расхождений по записываемым именам нет");
                Log("Переносить нечего: расхождений по записываемым именам нет.");
            }
            else
            {
                var records = await _session.ApplyAsync(writable, progress, ct).ConfigureAwait(true);
                var accepted = records.Count(static r => r.Outcome == WriteOutcome.Verified);
                var refused = records.Count - accepted;

                _stepWrite.Set(
                    refused == 0 ? AcceptanceStepState.Pass : AcceptanceStepState.Fail,
                    $"записано {accepted}, отклонено {refused}");
                Log($"Перенос: записано и подтверждено {accepted}, отклонено бортом {refused}.");
            }

            // --- 4. Перезагрузка ---
            _stepReboot.Set(AcceptanceStepState.Running);
            var reboot = await _session.RebootAsync(progress, ct).ConfigureAwait(true);
            _stepReboot.Set(reboot.Ok ? AcceptanceStepState.Pass : AcceptanceStepState.Fail, reboot.Message);
            Log(reboot.Message);

            if (!reboot.Ok)
            {
                Verdict(false, "Приёмка остановлена", reboot.Message);
                return;
            }

            // --- 5. Повторная сверка ---
            _stepRecheck.Set(AcceptanceStepState.Running);
            var after = await _session.CompareAsync(expected, roles, progress, ct).ConfigureAwait(true);
            _stepRecheck.Set(
                AcceptanceStepState.Pass,
                $"осталось расхождений {after.Differences.Count}");

            ShowDifferences(after);

            if (after.IsClean)
            {
                Verdict(true, "Конфигурация совпала",
                    "Борт совпал с эталоном по всем контролируемым параметрам. "
                  + "Можно переходить к калибровкам.");
                Log("Повторная сверка: расхождений нет.");
            }
            else
            {
                Verdict(false, "Остались расхождения",
                    $"Не сошлось параметров: {after.Differences.Count}. Исправьте записью или примите как есть — "
                  + "решение записывается в протокол. Пока расхождения не разобраны, калибровки заблокированы.");
                Log($"Повторная сверка: осталось расхождений {after.Differences.Count}.");
            }
        }
        catch (OperationCanceledException)
        {
            Log("Приёмка отменена оператором.");
            Verdict(false, "Отменено", "Приёмка остановлена. Состояние борта могло остаться изменённым.");
        }
        catch (Exception ex)
        {
            MarkRunningStepFailed(ex.Message);
            Log($"Сбой: {ex.GetType().Name}: {ex.Message}");
            Verdict(false, "Приёмка прервана ошибкой", ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
            UpdateCommands();
        }
    }

    // --- Разбор расхождений -------------------------------------------------

    private void ShowDifferences(ParameterTransferPlan plan)
    {
        Differences.Clear();
        foreach (var difference in plan.Differences)
        {
            Differences.Add(new ParameterDifferenceRow(difference));
        }

        DiffSummaryText.Text = plan.IsClean
            ? $"Сопоставлено {plan.Compared}, совпало всё. Неконтролируемых пропущено {plan.SkippedUncontrolled}."
            : $"Сопоставлено {plan.Compared}, совпало {plan.Matched}, расходится {plan.Differences.Count}. "
              + $"Неконтролируемых пропущено {plan.SkippedUncontrolled}.";

        var writable = Differences.Count(static d => d.CanWrite);
        if (!plan.IsClean)
        {
            DiffBar.Severity = InfoBarSeverity.Warning;
            DiffBar.Message = writable == plan.Differences.Count
                ? "Все расхождения устранимы записью."
                : $"Записью устранимо {writable} из {plan.Differences.Count}. Остальные записать нельзя: "
                  + "имени нет на борту, оно называет конкретный экземпляр железа либо эталон о нём молчит.";
            DiffBar.IsOpen = true;
        }

        _accepted = false;
        UpdateCommands();
    }

    private bool _accepted;

    private async void OnWriteOneClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name })
        {
            return;
        }

        var row = Differences.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal));
        if (row is not null)
        {
            await WriteAsync(new[] { row }).ConfigureAwait(true);
        }
    }

    private async void OnWriteAllClick(object sender, RoutedEventArgs e) =>
        await WriteAsync(Differences.Where(static r => r.CanWrite).ToArray()).ConfigureAwait(true);

    private async Task WriteAsync(IReadOnlyList<ParameterDifferenceRow> rows)
    {
        if (_session is null || rows.Count == 0 || _busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var progress = new Progress<string>(text => ProgressText.Text = text);
            var records = await _session
                .ApplyAsync(rows.Select(static r => r.Source).ToArray(), progress, CancellationToken.None)
                .ConfigureAwait(true);

            foreach (var record in records)
            {
                var row = rows.FirstOrDefault(r => string.Equals(r.Name, record.Name, StringComparison.Ordinal));
                row?.ApplyOutcome(record);
            }

            var accepted = records.Count(static r => r.Outcome == WriteOutcome.Verified);
            var refused = records.Count - accepted;

            Log($"Запись по требованию: принято {accepted}, отклонено {refused}.");

            DiffBar.Severity = refused == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            DiffBar.Message = refused == 0
                ? $"Записано и подтверждено обратным чтением: {accepted}. "
                  + "Часть значений вступит в силу только после перезагрузки борта."
                : $"Принято бортом {accepted}, отклонено {refused}. Отклонённые разбирать по журналу — "
                  + "повторная запись тем же способом их не возьмёт.";
            DiffBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            Log($"Запись не выполнена: {ex.Message}");
            ShowError("Запись не выполнена", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateCommands();
        }
    }

    /// <summary>
    /// Оператор принимает оставшиеся расхождения.
    /// </summary>
    /// <remarks>
    /// 🔴 Решение записывается в журнал поимённо. «Принято как есть» без списка
    /// имён через полгода неотличимо от «сошлось само», и по протоколу нельзя
    /// будет понять, что именно на плате отличается от эталона.
    /// </remarks>
    private void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        var outstanding = Differences.Where(static d => d.OutcomeVisibility == Visibility.Collapsed).ToArray();

        _accepted = true;
        Log(outstanding.Length == 0
            ? "Оператор принял состояние борта: неразобранных расхождений нет."
            : "Оператор принял расхождения как есть: "
              + string.Join(", ", outstanding.Select(static d => d.Name)));

        DiffBar.Severity = InfoBarSeverity.Informational;
        DiffBar.Message = outstanding.Length == 0
            ? "Расхождения разобраны. Калибровки разблокированы."
            : $"Принято как есть: {outstanding.Length}. Эти имена останутся отличными от эталона, "
              + "и они перечислены в журнале. Калибровки разблокированы.";
        DiffBar.IsOpen = true;

        UpdateCommands();
    }

    // --- Команды оператора --------------------------------------------------

    private async void OnLevelClick(object sender, RoutedEventArgs e)
    {
        if (_session is null || _busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var progress = new Progress<string>(text => ProgressText.Text = text);
            var result = await _session.LevelCalibrationAsync(progress, CancellationToken.None)
                .ConfigureAwait(true);

            Log(result.Message);
            Verdict(result.Ok, result.Ok ? "Уровень откалиброван" : "Калибровка уровня не выполнена", result.Message);
        }
        catch (Exception ex)
        {
            Log($"Калибровка уровня не выполнена: {ex.Message}");
            ShowError("Калибровка уровня не выполнена", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateCommands();
        }
    }

    private async void OnCompassClick(object sender, RoutedEventArgs e)
    {
        if (_session is null || _busy)
        {
            return;
        }

        var heading = HeadingBox.Value;
        if (double.IsNaN(heading))
        {
            ShowError(
                "Курс не задан",
                new InvalidOperationException(
                    "Введите истинный курс борта. Калибровка по неверному курсу оставит компас «успешно» "
                  + "откалиброванным на чужое направление, и это не будет видно ни по одной проверке."));
            return;
        }

        SetBusy(true);
        try
        {
            var progress = new Progress<string>(text => ProgressText.Text = text);
            var result = await _session.CompassCalibrationAsync(heading, progress, CancellationToken.None)
                .ConfigureAwait(true);

            Log(result.Message);
            Verdict(result.Ok, result.Ok ? "Компасы откалиброваны" : "Калибровка компаса требует разбора", result.Message);
        }
        catch (Exception ex)
        {
            Log($"Калибровка компаса не выполнена: {ex.Message}");
            ShowError("Калибровка компаса не выполнена", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateCommands();
        }
    }

    private async void OnFinishClick(object sender, RoutedEventArgs e)
    {
        if (_session is null || _busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var progress = new Progress<string>(text => ProgressText.Text = text);
            var readiness = await _session.CheckArmReadinessAsync(progress, CancellationToken.None)
                .ConfigureAwait(true);

            Log(readiness.Detail);
            foreach (var blocker in readiness.Blockers)
            {
                Log("Мешает взведению: " + blocker);
            }

            Verdict(
                readiness.Ready,
                readiness.Ready ? "ГОДЕН: борт готов быть взведённым" : "Борт взвестись не даст",
                readiness.Detail);
        }
        catch (Exception ex)
        {
            Log($"Проверка готовности не выполнена: {ex.Message}");
            ShowError("Проверка готовности не выполнена", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateCommands();
        }
    }

    // --- Служебное ----------------------------------------------------------

    /// <summary>
    /// Калибровки разблокированы только после разбора расхождений.
    /// </summary>
    /// <remarks>
    /// 🔴 Калибровать компас на борту, конфигурация которого расходится с
    /// эталоном и не разобрана, бессмысленно: результат ляжет на неизвестную
    /// настройку, и приёмка подтвердит не то изделие, которое сдаётся.
    /// </remarks>
    private void UpdateCommands()
    {
        if (!_ready)
        {
            return;
        }

        var live = _session is not null && !_busy;
        var resolved = _accepted || Differences.All(static d => d.OutcomeVisibility == Visibility.Visible);

        StartButton.IsEnabled = !_busy && _services is not null && _reference is not null;
        WriteAllButton.IsEnabled = live && Differences.Any(static d => d.CanWrite);
        AcceptButton.IsEnabled = live && Differences.Count > 0 && !_accepted;

        LevelButton.IsEnabled = live && resolved;
        CompassButton.IsEnabled = live && resolved;
        FinishButton.IsEnabled = live && resolved;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;

        BusyRing.IsActive = busy;
        ProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ProgressText.Text = string.Empty;

        CloseButton.IsEnabled = !busy;
        UpdateCommands();
    }

    private void MarkRunningStepFailed(string detail)
    {
        foreach (var step in Steps)
        {
            if (step.RunningVisibility == Visibility.Visible)
            {
                step.Set(AcceptanceStepState.Fail, detail);
            }
        }
    }

    private void Verdict(bool ok, string title, string message)
    {
        VerdictBar.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        VerdictBar.Title = title;
        VerdictBar.Message = message;
        VerdictBar.IsOpen = true;
    }

    private void ShowError(string title, Exception ex)
    {
        VerdictBar.Severity = InfoBarSeverity.Error;
        VerdictBar.Title = title;
        VerdictBar.Message = ex.Message;
        VerdictBar.IsOpen = true;
    }

    private void Log(string text) =>
        LogEntries.Insert(0, new CalibrationLogRow(MavSeverity.Info, text, DateTimeOffset.Now));

    private void OnClearLogClick(object sender, RoutedEventArgs e) => LogEntries.Clear();

    private async Task CloseSessionAsync()
    {
        if (_services is not null)
        {
            await _services.EndAcceptanceAsync().ConfigureAwait(true);
        }

        _session = null;
    }

    private async void OnCloseClick(object sender, RoutedEventArgs e)
    {
        await CloseSessionAsync().ConfigureAwait(true);

        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }
}
