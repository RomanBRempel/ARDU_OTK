using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ARDU_OTK.Services;
using ARDU_OTK.Services.Fc;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ARDU_OTK;

/// <summary>Строка журнала приёмок.</summary>
public sealed class RunRow
{
    /// <summary>Маркер прерванного прогона в поле стадии — так его пишет реестр.</summary>
    private const string AbortedMarker = "aborted";

    private const string InProgressMarker = "in-progress";

    public RunRow(RunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        UnitId = summary.UnitId.Length == 0 ? "без номера" : summary.UnitId;
        StartedText = summary.StartedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        OperatorText = summary.Operator.Length == 0 ? "оператор не указан" : summary.Operator;

        ReferenceText = summary.ReferenceFile.Length == 0
            ? "эталон не записан"
            : summary.ReferenceFile;

        var stage = summary.FailedStage ?? string.Empty;

        // 🔴 Четыре исхода, а не два. Прерванный прогон — это не провал
        // изделия, а незаконченная работа, и сваливать их в одно означало бы
        // считать браком плату, которую никто не проверял до конца. Идущий
        // прогон — тоже отдельное состояние: он ещё ничего не утверждает.
        if (stage.StartsWith(InProgressMarker, StringComparison.Ordinal))
        {
            VerdictText = "идёт";
            VerdictBrush = Brush("SystemFillColorAttentionBrush");
        }
        else if (stage.StartsWith(AbortedMarker, StringComparison.Ordinal))
        {
            VerdictText = "прерван";
            VerdictBrush = Brush("SystemFillColorCautionBrush");
        }
        else if (summary.Passed == true)
        {
            VerdictText = "сдан";
            VerdictBrush = Brush("SystemFillColorSuccessBrush");
        }
        else
        {
            VerdictText = "не сдан";
            VerdictBrush = Brush("SystemFillColorCriticalBrush");
        }

        var parts = new List<string>();

        if (stage.Length > 0 && !stage.StartsWith(InProgressMarker, StringComparison.Ordinal))
        {
            parts.Add(stage);
        }

        if (summary.FailedCheckCount > 0)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"непройденных проверок: {summary.FailedCheckCount}"));
        }

        DetailText = string.Join(" · ", parts);
        IsPassed = summary.Passed == true;
        IsFailed = summary.Passed == false && !stage.StartsWith(AbortedMarker, StringComparison.Ordinal)
            && !stage.StartsWith(InProgressMarker, StringComparison.Ordinal);
        IsAborted = stage.StartsWith(AbortedMarker, StringComparison.Ordinal);
        UnitKey = summary.UnitId;
    }

    public string UnitId { get; }

    public string StartedText { get; }

    public string OperatorText { get; }

    public string ReferenceText { get; }

    public string DetailText { get; }

    public string VerdictText { get; }

    public Brush VerdictBrush { get; }

    public bool IsPassed { get; }

    public bool IsFailed { get; }

    public bool IsAborted { get; }

    /// <summary>Номер борта как он лежит в реестре — для подсчёта уникальных.</summary>
    public string UnitKey { get; }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}

/// <summary>
/// Журнал приёмок: история прогонов из реестра.
/// </summary>
/// <remarks>
/// 🔴 Реестр вёлся, но посмотреть его из приложения было нельзя: прогоны
/// писались в базу и оставались там невидимыми. История нужна ровно тогда,
/// когда стенд не работает, — при разборе рекламации и при вопросе «чем сдавали
/// вот эту плату», и лезть за ответом в SQLite через стороннюю программу
/// означает, что ответа нет.
/// </remarks>
public sealed partial class RunsPage : Page
{
    private readonly AppServices _services = AppServices.Instance;

    /// <summary>
    /// Сколько прогонов показывать.
    /// </summary>
    /// <remarks>
    /// Предел назван числом, а не «последние»: список, молча обрезанный,
    /// читается как полная история и врёт о числе сдач.
    /// </remarks>
    private const int Limit = 500;

    public RunsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ReloadAsync().ConfigureAwait(true);
    }

    public ObservableCollection<RunRow> Rows { get; } = new();

    private async void OnRefreshClick(object sender, RoutedEventArgs e) =>
        await ReloadAsync().ConfigureAwait(true);

    private async void OnFilterChanged(object sender, TextChangedEventArgs e) =>
        await ReloadAsync().ConfigureAwait(true);

    private async Task ReloadAsync()
    {
        var filter = UnitFilterBox.Text.Trim();

        try
        {
            var runs = await _services.Store
                .ListRunsAsync(filter.Length == 0 ? null : filter, Limit)
                .ConfigureAwait(true);

            Rows.Clear();
            foreach (var run in runs)
            {
                Rows.Add(new RunRow(run));
            }

            PassedCountText.Text = Rows.Count(static r => r.IsPassed).ToString(CultureInfo.InvariantCulture);
            FailedCountText.Text = Rows.Count(static r => r.IsFailed).ToString(CultureInfo.InvariantCulture);
            AbortedCountText.Text = Rows.Count(static r => r.IsAborted).ToString(CultureInfo.InvariantCulture);

            UnitsCountText.Text = Rows
                .Select(static r => r.UnitKey)
                .Where(static u => u.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
                .ToString(CultureInfo.InvariantCulture);

            CountText.Text = Rows.Count switch
            {
                0 => "прогонов не найдено",
                Limit => string.Create(
                    CultureInfo.InvariantCulture,
                    $"показаны последние {Limit} — в реестре их может быть больше"),
                _ => string.Create(CultureInfo.InvariantCulture, $"прогонов: {Rows.Count}"),
            };

            PageBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            PageBar.Severity = InfoBarSeverity.Error;
            PageBar.Message = "Журнал не прочитан: " + ex.Message;
            PageBar.IsOpen = true;
        }
    }
}
