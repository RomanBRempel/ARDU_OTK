using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ARDU_OTK.Services.Fc;
using Microsoft.UI.Xaml;

namespace ARDU_OTK;

/// <summary>Состояние шага приёмки.</summary>
public enum AcceptanceStepState
{
    Pending,
    Running,
    Pass,
    Fail,
}

/// <summary>
/// Шаг автоматической части приёмки.
/// </summary>
/// <remarks>
/// Шаги показываются списком заранее, до запуска: оператор должен видеть, что
/// именно программа собирается сделать с бортом, а не узнавать об этом по ходу.
/// </remarks>
public sealed class AcceptanceStepRow : INotifyPropertyChanged
{
    private AcceptanceStepState _state = AcceptanceStepState.Pending;
    private string _detail = string.Empty;

    public AcceptanceStepRow(string title)
    {
        Title = title;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public string Detail
    {
        get => _detail;
        private set
        {
            _detail = value;
            Raise(nameof(Detail));
            Raise(nameof(DetailVisibility));
        }
    }

    public Visibility DetailVisibility =>
        _detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PendingVisibility => Visible(AcceptanceStepState.Pending);

    public Visibility RunningVisibility => Visible(AcceptanceStepState.Running);

    public Visibility PassVisibility => Visible(AcceptanceStepState.Pass);

    public Visibility FailVisibility => Visible(AcceptanceStepState.Fail);

    public void Set(AcceptanceStepState state, string detail = "")
    {
        _state = state;
        Detail = detail;

        Raise(nameof(PendingVisibility));
        Raise(nameof(RunningVisibility));
        Raise(nameof(PassVisibility));
        Raise(nameof(FailVisibility));
    }

    private Visibility Visible(AcceptanceStepState state) =>
        _state == state ? Visibility.Visible : Visibility.Collapsed;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Строка расхождения с эталоном.
/// </summary>
/// <remarks>
/// Изменяема: исход записи дописывается сюда же. Второй список «что записано»
/// заставил бы оператора сличать глазами два перечня имён.
/// </remarks>
public sealed class ParameterDifferenceRow : INotifyPropertyChanged
{
    private string _outcomeText = string.Empty;
    private Visibility _outcomeVisibility = Visibility.Collapsed;
    private bool _canWrite;

    /// <summary>
    /// Совпавший параметр, который эталон помечен показывать.
    /// </summary>
    /// <remarks>
    /// 🔴 Совпавшие и разошедшиеся имена живут в одном списке намеренно. Это
    /// две стороны одного вопроса «сходится ли борт с эталоном», и разнесённые
    /// по разным спискам они заставляют сверять их памятью: оператор смотрит на
    /// расхождения, потом ищет глазами, а что там с наблюдаемыми, и обратно.
    /// </remarks>
    public ParameterDifferenceRow(string name, double expected, double? actual)
    {
        Name = name;
        Detail = string.Empty;
        _canWrite = false;

        ExpectedText = "эталон " + ReferenceParamFile.FormatCanonical(expected);
        ActualText = actual is { } value
            ? string.Create(CultureInfo.InvariantCulture, $"борт {value:G9}")
            : "на борту нет";

        // Помеченные показывать и так все на виду: значок «показываемый» рядом
        // с каждой строкой был бы шумом.
        VisibleBadgeVisibility = Visibility.Collapsed;
        WriteButtonVisibility = Visibility.Collapsed;

        MatchVisibility = Visibility.Visible;
        DiffVisibility = Visibility.Collapsed;
    }

    public ParameterDifferenceRow(ParameterDifference difference)
    {
        ArgumentNullException.ThrowIfNull(difference);

        Source = difference;
        Name = difference.Name;
        Detail = difference.Detail;
        _canWrite = difference.Writable;

        MatchVisibility = Visibility.Collapsed;
        DiffVisibility = Visibility.Visible;

        ExpectedText = difference.Expected is { } expected
            ? "эталон " + ReferenceParamFile.FormatCanonical(expected)
            : "эталон молчит";

        ActualText = difference.Actual is { } actual
            ? string.Create(CultureInfo.InvariantCulture, $"борт {actual:G9}")
            : "на борту нет";

        VisibleBadgeVisibility = difference.Visible ? Visibility.Visible : Visibility.Collapsed;

        // Кнопка есть только там, где записью что-то решается. Мёртвая кнопка
        // рядом с «записать нельзя» лишь предлагает нажать и получить отказ.
        WriteButtonVisibility = difference.Writable ? Visibility.Visible : Visibility.Collapsed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ParameterDifference? Source { get; }

    /// <summary>Строка о совпавшем параметре — значок согласия.</summary>
    public Visibility MatchVisibility { get; }

    /// <summary>Строка о расхождении — значок несогласия.</summary>
    public Visibility DiffVisibility { get; }

    public string Name { get; }

    public string Detail { get; }

    public string ExpectedText { get; }

    public string ActualText { get; }

    public Visibility VisibleBadgeVisibility { get; }

    public Visibility WriteButtonVisibility { get; }

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

    public bool CanWrite
    {
        get => _canWrite;
        set => Set(ref _canWrite, value);
    }

    /// <summary>Записывает исход попытки и гасит кнопку.</summary>
    public void ApplyOutcome(ParamWriteRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        OutcomeText = record.Outcome switch
        {
            WriteOutcome.Verified => string.Create(
                CultureInfo.InvariantCulture,
                $"Записано и подтверждено обратным чтением: {record.ReadBack ?? record.Requested:G9}."),

            WriteOutcome.Failed =>
                "Записать не удалось: борт не ответил. Проверьте кабель и питание.",

            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"Борт не принял значение: записывали {record.Requested:G9}, обратное чтение дало {record.ReadBack ?? double.NaN:G9}.")
              + " Дело не в связи — параметр либо только для чтения, либо перекрывается прошивкой.",
        };

        OutcomeVisibility = Visibility.Visible;
        CanWrite = false;
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
