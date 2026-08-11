using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ARDU_OTK.Services.Fc;
using Microsoft.UI.Xaml;

namespace ARDU_OTK;

/// <summary>Состояние стадии в списке слева.</summary>
public enum StageRowState
{
    Pending,
    Running,
    Pass,
    Fail,
    Inconclusive,
}

/// <summary>
/// Строка списка стадий. Обновляется по ходу прогона, поэтому уведомляет об
/// изменениях: привязки в шаблоне работают в режиме OneWay.
/// </summary>
public sealed class CalibrationStageRow : INotifyPropertyChanged
{
    private StageRowState _state = StageRowState.Pending;
    private string _stateText = "—";

    public CalibrationStageRow(CalibrationStage stage, string title)
    {
        Stage = stage;
        Title = title;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CalibrationStage Stage { get; }

    public string Title { get; }

    public string StateText
    {
        get => _stateText;
        private set
        {
            if (_stateText == value)
            {
                return;
            }

            _stateText = value;
            Raise();
        }
    }

    public Visibility PendingVisibility => Visible(StageRowState.Pending);

    public Visibility RunningVisibility => Visible(StageRowState.Running);

    public Visibility PassVisibility => Visible(StageRowState.Pass);

    public Visibility FailVisibility => Visible(StageRowState.Fail);

    public Visibility InconclusiveVisibility => Visible(StageRowState.Inconclusive);

    /// <summary>Переводит строку в новое состояние и обновляет все привязки разом.</summary>
    public void SetState(StageRowState state, string? stateText = null)
    {
        _state = state;
        StateText = stateText ?? state switch
        {
            StageRowState.Pending => "—",
            StageRowState.Running => "выполняется",
            StageRowState.Pass => "готово",
            StageRowState.Fail => "отказ",
            _ => "нет данных",
        };

        Raise(nameof(PendingVisibility));
        Raise(nameof(RunningVisibility));
        Raise(nameof(PassVisibility));
        Raise(nameof(FailVisibility));
        Raise(nameof(InconclusiveVisibility));
    }

    private Visibility Visible(StageRowState state) =>
        _state == state ? Visibility.Visible : Visibility.Collapsed;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Строка списка проверок. Создаётся готовой и больше не меняется.</summary>
public sealed class CalibrationCheckRow
{
    public CalibrationCheckRow(CheckResult result)
    {
        HeaderText = result.Title;
        Detail = result.Detail;

        MeasuredText = result switch
        {
            { Measured: { } m, Limit: { } l } =>
                string.Format(CultureInfo.InvariantCulture, "{0:0.###} / доп. {1:0.###}", m, l),
            { Measured: { } m } => string.Format(CultureInfo.InvariantCulture, "{0:0.###}", m),
            _ => string.Empty,
        };

        MeasuredVisibility = MeasuredText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        PassVisibility = Visible(result.Outcome, CheckOutcome.Pass);
        FailVisibility = Visible(result.Outcome, CheckOutcome.Fail);
        InconclusiveVisibility = Visible(result.Outcome, CheckOutcome.Inconclusive);
    }

    public string HeaderText { get; }

    public string Detail { get; }

    public string MeasuredText { get; }

    public Visibility MeasuredVisibility { get; }

    public Visibility PassVisibility { get; }

    public Visibility FailVisibility { get; }

    /// <summary>
    /// Отдельная индикация «нет данных». Приравнивать её к успеху нельзя:
    /// отсутствие доказательства — не доказательство исправности.
    /// </summary>
    public Visibility InconclusiveVisibility { get; }

    private static Visibility Visible(CheckOutcome actual, CheckOutcome expected) =>
        actual == expected ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// Строка параметра, не прошедшего проверку, с действием «перезаписать из
/// эталона».
/// </summary>
/// <remarks>
/// Строка изменяема: после нажатия кнопки исход записи дописывается сюда же.
/// Заводить ради этого второй список «что перезаписано» значило бы заставить
/// оператора сличать глазами два перечня имён.
/// </remarks>
public sealed class ParamMismatchRow : INotifyPropertyChanged
{
    private string _outcomeText = string.Empty;
    private Visibility _outcomeVisibility = Visibility.Collapsed;
    private bool _canRewrite = true;

    public ParamMismatchRow(ParamMismatch mismatch)
    {
        ArgumentNullException.ThrowIfNull(mismatch);

        Source = mismatch;
        Name = mismatch.Name;
        Detail = mismatch.Detail;
        StageText = mismatch.Stage.ToString();

        ExpectedText = string.Format(CultureInfo.InvariantCulture, "эталон {0:G9}", mismatch.Expected);
        ActualText = string.Format(CultureInfo.InvariantCulture, "борт {0:G9}", mismatch.Actual);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ParamMismatch Source { get; }

    public string Name { get; }

    public string Detail { get; }

    public string StageText { get; }

    public string ExpectedText { get; }

    public string ActualText { get; }

    /// <summary>Исход повторной записи. Пусто, пока её не пытались выполнить.</summary>
    public string OutcomeText
    {
        get => _outcomeText;
        private set => Set(ref _outcomeText, value);
    }

    public Visibility OutcomeVisibility
    {
        get => _outcomeVisibility;
        private set => Set(ref _outcomeVisibility, value);
    }

    /// <summary>Кнопка доступна: перезапись ещё не удалась и сейчас не идёт.</summary>
    public bool CanRewrite
    {
        get => _canRewrite;
        set => Set(ref _canRewrite, value);
    }

    /// <summary>Записывает исход попытки и гасит кнопку, если борт значение принял.</summary>
    public void ApplyOutcome(ParamWriteRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        OutcomeText = record.Outcome switch
        {
            WriteOutcome.Verified => string.Format(
                CultureInfo.InvariantCulture,
                "Перезаписано и подтверждено обратным чтением: {0:G9}. Вердикт прогона не меняется — "
              + "сдавать изделие следует повторным прогоном целиком.",
                record.ReadBack ?? record.Requested),

            WriteOutcome.Failed =>
                "Перезапись не выполнена: борт не ответил. Проверьте кабель и питание.",

            _ => string.Format(
                CultureInfo.InvariantCulture,
                "Борт снова не принял значение: записывали {0:G9}, обратное чтение дало {1:G9}. "
              + "Дело не в сбое связи — параметр либо только для чтения, либо перекрывается прошивкой.",
                record.Requested,
                record.ReadBack ?? double.NaN),
        };

        OutcomeVisibility = Visibility.Visible;

        // Повторять успешную запись незачем, а безуспешную — бессмысленно тем
        // же способом: оба раза кнопка должна перестать звать на новое нажатие.
        CanRewrite = false;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>Строка журнала операций.</summary>
public sealed class CalibrationLogRow
{
    public CalibrationLogRow(MavSeverity severity, string text, DateTimeOffset at)
    {
        Text = text;
        TimeText = at.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        // MAV_SEVERITY: чем меньше число, тем тяжелее.
        CriticalVisibility = severity <= MavSeverity.Error ? Visibility.Visible : Visibility.Collapsed;
        WarningVisibility = severity == MavSeverity.Warning ? Visibility.Visible : Visibility.Collapsed;
        InfoVisibility = severity > MavSeverity.Warning ? Visibility.Visible : Visibility.Collapsed;
    }

    public string TimeText { get; }

    public string Text { get; }

    public Visibility CriticalVisibility { get; }

    public Visibility WarningVisibility { get; }

    public Visibility InfoVisibility { get; }
}

/// <summary>Строка истории прогонов.</summary>
public sealed class CalibrationHistoryRow
{
    public CalibrationHistoryRow(RunSummary summary)
    {
        UnitId = summary.UnitId;
        SubtitleText = string.Format(
            CultureInfo.InvariantCulture,
            "{0:dd.MM.yyyy HH:mm} · {1}",
            summary.StartedUtc.ToLocalTime(),
            summary.Operator);

        VerdictText = summary.Passed switch
        {
            true => "годен",
            false => summary.FailedStage is { Length: > 0 } stage ? $"отказ: {stage}" : "отказ",
            null => summary.FailedStage is { Length: > 0 } s ? s : "прерван",
        };

        PassVisibility = summary.Passed == true ? Visibility.Visible : Visibility.Collapsed;
        FailVisibility = summary.Passed == false ? Visibility.Visible : Visibility.Collapsed;

        // Прерванный прогон обязан отличаться и от годного, и от отказа.
        UnknownVisibility = summary.Passed is null ? Visibility.Visible : Visibility.Collapsed;
    }

    public string UnitId { get; }

    public string SubtitleText { get; }

    public string VerdictText { get; }

    public Visibility PassVisibility { get; }

    public Visibility FailVisibility { get; }

    public Visibility UnknownVisibility { get; }
}
