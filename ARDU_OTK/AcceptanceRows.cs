using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ARDU_OTK.Services.Fc;
using ARDU_OTK.Services.Store;
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
/// Строка сверки скриптов.
/// </summary>
/// <remarks>
/// Скрипты стоят своей колонкой рядом с параметрами, а не в общем списке:
/// это другая сущность — файл на карте, а не число, — и действие над ней
/// другое: залить или удалить, а не записать значение.
/// </remarks>
public sealed class ScriptDifferenceRow : INotifyPropertyChanged
{
    private string _outcomeText = string.Empty;
    private Visibility _outcomeVisibility = Visibility.Collapsed;
    private bool _canApply = true;

    /// <summary>
    /// Совпавший скрипт: путь, имя и размер.
    /// </summary>
    /// <remarks>
    /// 🔴 Совпавшие показываются наравне с разошедшимися и с теми же
    /// подробностями. Слово «совпали» без пути, имени и размера ничего не
    /// доказывает: по нему нельзя понять, что именно сравнивали и был ли на
    /// борту вообще тот файл, о котором идёт речь.
    /// </remarks>
    public ScriptDifferenceRow(ReferenceScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        Path = script.Path;
        FileName = script.FileName;
        SizeText = string.Create(CultureInfo.InvariantCulture, $"{script.ByteCount} байт");
        HashText = ReferenceParameters.ShortHash(script.Hash);
        Detail = string.Empty;

        KindText = "совпал";
        ActionText = string.Empty;
        ActionVisibility = Visibility.Collapsed;
        _canApply = false;

        MatchVisibility = Visibility.Visible;
        DiffVisibility = Visibility.Collapsed;
    }

    public ScriptDifferenceRow(ScriptDifference difference)
    {
        ArgumentNullException.ThrowIfNull(difference);

        Source = difference;
        Path = difference.Path;
        FileName = difference.Path[(difference.Path.LastIndexOf('/') + 1)..];
        Detail = difference.Detail;

        SizeText = difference.Expected is { } expected
            ? string.Create(CultureInfo.InvariantCulture, $"{expected.ByteCount} байт")
            : string.Empty;

        HashText = difference.Expected is { } withHash
            ? ReferenceParameters.ShortHash(withHash.Hash)
            : ReferenceParameters.ShortHash(difference.ActualHash);

        KindText = difference.Outcome switch
        {
            ScriptComparison.MissingOnBoard => "нет на борту",
            ScriptComparison.ContentDiffers => "содержимое другое",
            ScriptComparison.ExtraOnBoard => "лишний на борту",
            ScriptComparison.Unreadable => "не прочитан",
            _ => "совпал",
        };

        ActionText = difference.Outcome == ScriptComparison.ExtraOnBoard ? "Удалить" : "Залить";

        // Нечитаемый файл действием не лечится: сначала нужно понять, почему
        // борт его не отдал.
        _canApply = difference.Outcome != ScriptComparison.Unreadable;
        ActionVisibility = _canApply ? Visibility.Visible : Visibility.Collapsed;

        MatchVisibility = difference.Outcome == ScriptComparison.Match
            ? Visibility.Visible
            : Visibility.Collapsed;
        DiffVisibility = difference.Outcome == ScriptComparison.Match
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Расхождение; <c>null</c> — строка о совпавшем скрипте.</summary>
    public ScriptDifference? Source { get; }

    public string Path { get; }

    public string FileName { get; }

    public string KindText { get; }

    /// <summary>Размер по эталону. Пусто, если эталон об этом файле молчит.</summary>
    public string SizeText { get; } = string.Empty;

    /// <summary>Первые разряды SHA-256 — по ним файл и опознаётся.</summary>
    public string HashText { get; } = string.Empty;

    public string Detail { get; }

    public string ActionText { get; }

    public Visibility ActionVisibility { get; }

    public Visibility MatchVisibility { get; }

    public Visibility DiffVisibility { get; }

    public string OutcomeText
    {
        get => _outcomeText;
        private set
        {
            _outcomeText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OutcomeText)));
        }
    }

    public Visibility OutcomeVisibility
    {
        get => _outcomeVisibility;
        private set
        {
            _outcomeVisibility = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OutcomeVisibility)));
        }
    }

    public bool CanApply
    {
        get => _canApply;
        set
        {
            _canApply = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanApply)));
        }
    }

    /// <summary>Записывает исход действия и гасит кнопку.</summary>
    /// <remarks>
    /// 🔴 Про перезагрузку сказано прямо. Записанный скрипт подхватывается
    /// прошивкой только при старте, и молчание об этом читалось бы как
    /// «применено», чего не произошло.
    /// </remarks>
    public void ApplyOutcome(string? failure)
    {
        OutcomeText = failure is null
            ? "Выполнено и подтверждено обратным чтением. Исполняться начнёт после перезагрузки борта."
            : "Не выполнено: " + failure;

        OutcomeVisibility = Visibility.Visible;
        CanApply = false;
    }
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
    public ParameterDifferenceRow(
        string name,
        double expected,
        double? actual,
        ParameterEnums.VehicleClass vehicle = ParameterEnums.VehicleClass.Unknown)
    {
        Name = name;
        Detail = string.Empty;
        _canWrite = false;

        // 🔴 Без слов «эталон» и «борт» в каждой строке: это заголовки
        // колонок, а не часть значения. Повторённые полсотни раз, они
        // вытесняют собой сами числа, ради которых оператор в список и смотрит.
        ExpectedText = ParameterEnums.Format(
            name, expected, vehicle, ReferenceParamFile.FormatCanonical(expected));

        ActualText = actual is { } value
            ? ParameterEnums.Format(
                name, value, vehicle, string.Create(CultureInfo.InvariantCulture, $"{value:G9}"))
            : "нет";

        // Помеченные показывать и так все на виду: значок «показываемый» рядом
        // с каждой строкой был бы шумом.
        VisibleBadgeVisibility = Visibility.Collapsed;
        WriteButtonVisibility = Visibility.Collapsed;

        MatchVisibility = Visibility.Visible;
        DiffVisibility = Visibility.Collapsed;
    }

    public ParameterDifferenceRow(
        ParameterDifference difference,
        ParameterEnums.VehicleClass vehicle = ParameterEnums.VehicleClass.Unknown)
    {
        ArgumentNullException.ThrowIfNull(difference);

        Source = difference;
        Name = difference.Name;
        Detail = difference.Detail;
        _canWrite = difference.Writable;

        MatchVisibility = Visibility.Collapsed;
        DiffVisibility = Visibility.Visible;

        ExpectedText = difference.Expected is { } expected
            ? ParameterEnums.Format(
                difference.Name, expected, vehicle, ReferenceParamFile.FormatCanonical(expected))
            : "—";

        ActualText = difference.Actual is { } actual
            ? ParameterEnums.Format(
                difference.Name,
                actual,
                vehicle,
                string.Create(CultureInfo.InvariantCulture, $"{actual:G9}"))
            : "нет";

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
