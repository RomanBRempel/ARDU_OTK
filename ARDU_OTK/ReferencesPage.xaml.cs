using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ARDU_OTK.Services;
using ARDU_OTK.Services.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ARDU_OTK;

/// <summary>Строка списка эталонов.</summary>
/// <remarks>
/// Отдельный тип, а не сам <see cref="CalibrationReference"/>: списку нужны
/// готовые к показу строки и решения о доступности действий, а тянуть эти
/// решения в запись реестра значило бы поселить в ней знание об интерфейсе.
/// </remarks>
public sealed class ReferenceRow
{
    public ReferenceRow(CalibrationReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        Id = reference.Id;
        Name = reference.Name;

        OriginText = string.Create(
            CultureInfo.InvariantCulture,
            $"{reference.SourceName} · параметров {reference.ParamCount} · "
          + $"{ReferenceParameters.ShortHash(reference.ParamHash)}");

        if (reference.HasFirmware)
        {
            OriginText += " · " + reference.Firmware;
        }

        var runs = reference.RunCount == 0
            ? "прогонов нет"
            : string.Create(CultureInfo.InvariantCulture, $"прогонов {reference.RunCount}");

        UsageText = string.Create(
            CultureInfo.InvariantCulture,
            $"{runs} · завёл {reference.CreatedBy} · {reference.CreatedUtc.ToLocalTime():dd.MM.yyyy HH:mm}");

        // 🔴 Правка разрешена, пока по эталону не сдавали. После первого прогона
        // менять допуски и раскладку нельзя: прогоны в реестре выполнялись по
        // прежним значениям, и правка сделала бы реестр ложным. Кнопка не
        // прячется, а гаснет — исчезнувшая кнопка выглядит как недоработка, а
        // погашенная объясняет правило подсказкой.
        CanEdit = !reference.HasRuns && !reference.IsRetired;

        IsRetired = reference.IsRetired;
        RetireActionText = reference.IsRetired ? "Вернуть" : "В архив";

        StateText = reference.IsRetired
            ? "в архиве"
            : reference.HasRuns ? "заморожен прогонами" : "правится";

        StateBadgeVisibility = Visibility.Visible;
    }

    public long Id { get; }

    public string Name { get; }

    public string OriginText { get; }

    public string UsageText { get; }

    public bool CanEdit { get; }

    public bool IsRetired { get; }

    public string RetireActionText { get; }

    public string StateText { get; }

    public Visibility StateBadgeVisibility { get; }
}

/// <summary>
/// Управление эталонами: список, правка, клонирование и архив.
/// </summary>
/// <remarks>
/// 🔴 До этой страницы эталоны жили только внутри рабочего экрана: завести и
/// поправить их можно было лишь через плашку выбора, а выведенные из обращения
/// не показывались нигде. Это делало архив невидимым — при том, что по
/// архивным эталонам сданы платы, и разбор рекламации начинается именно с них.
/// </remarks>
public sealed partial class ReferencesPage : Page
{
    private readonly AppServices _services = AppServices.Instance;

    private static readonly (string Label, bool IncludeRetired, bool OnlyRetired)[] Scopes =
    {
        ("В работе", false, false),
        ("Архив", true, true),
        ("Все", true, false),
    };

    /// <summary>Пока список заполняется, обработчик выбора не перечитывает реестр.</summary>
    private bool _loading;

    public ReferencesPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public ObservableCollection<ReferenceRow> Rows { get; } = new();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        try
        {
            ScopeBox.Items.Clear();
            foreach (var scope in Scopes)
            {
                ScopeBox.Items.Add(scope.Label);
            }

            ScopeBox.SelectedIndex = 0;
        }
        finally
        {
            _loading = false;
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    private async void OnScopeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    private async Task ReloadAsync()
    {
        var index = Math.Max(0, ScopeBox.SelectedIndex);
        var scope = Scopes[index];

        try
        {
            var references = await _services.Store
                .ListReferencesAsync(scope.IncludeRetired)
                .ConfigureAwait(true);

            IEnumerable<CalibrationReference> shown = references;
            if (scope.OnlyRetired)
            {
                shown = references.Where(static r => r.IsRetired);
            }

            Rows.Clear();
            foreach (var reference in shown)
            {
                Rows.Add(new ReferenceRow(reference));
            }

            CountText.Text = Rows.Count == 0
                ? "ничего не найдено"
                : string.Create(CultureInfo.InvariantCulture, $"эталонов: {Rows.Count}");

            PageBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            Show("Список эталонов не прочитан: " + ex.Message, InfoBarSeverity.Error);
        }
    }

    private void OnNewClick(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(ReferenceEditorPage), new ReferenceEditorArgs(_services, null));

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (await FindAsync(sender).ConfigureAwait(true) is { } reference)
        {
            Frame.Navigate(typeof(ReferenceEditorPage), new ReferenceEditorArgs(_services, reference));
        }
    }

    private async void OnCloneClick(object sender, RoutedEventArgs e)
    {
        if (await FindAsync(sender).ConfigureAwait(true) is { } reference)
        {
            Frame.Navigate(
                typeof(ReferenceEditorPage),
                new ReferenceEditorArgs(_services, reference, AsClone: true));
        }
    }

    /// <summary>
    /// Выводит эталон из обращения или возвращает его обратно.
    /// </summary>
    /// <remarks>
    /// Архив — не удаление. Записи об эталоне и прогонах по нему остаются в
    /// реестре: удалённый эталон превратил бы сданные по нему платы в записи
    /// без основания.
    /// </remarks>
    private async void OnRetireClick(object sender, RoutedEventArgs e)
    {
        if (await FindAsync(sender).ConfigureAwait(true) is not { } reference)
        {
            return;
        }

        try
        {
            await _services.Store
                .SetReferenceRetiredAsync(reference.Id, !reference.IsRetired)
                .ConfigureAwait(true);

            Show(
                reference.IsRetired
                    ? $"Эталон «{reference.Name}» возвращён в обращение."
                    : $"Эталон «{reference.Name}» выведен из обращения: выбрать его для нового прогона нельзя, "
                    + "прогоны по нему в реестре сохранены.",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Show("Состояние эталона не изменено: " + ex.Message, InfoBarSeverity.Error);
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>Достаёт эталон по идентификатору из <c>Tag</c> нажатой кнопки.</summary>
    /// <remarks>
    /// Перечитывается из реестра, а не берётся из строки списка: список мог
    /// устареть, пока страница была открыта, и действовать по устаревшей копии
    /// значило бы править не то состояние, которое оператор видит.
    /// </remarks>
    private async Task<CalibrationReference?> FindAsync(object sender)
    {
        if (sender is not FrameworkElement { Tag: long id })
        {
            return null;
        }

        try
        {
            var reference = await _services.Store.GetReferenceAsync(id).ConfigureAwait(true);
            if (reference is null)
            {
                Show("Эталон не найден: он удалён из реестра другой копией приложения.", InfoBarSeverity.Warning);
                await ReloadAsync().ConfigureAwait(true);
            }

            return reference;
        }
        catch (Exception ex)
        {
            Show("Эталон не прочитан: " + ex.Message, InfoBarSeverity.Error);
            return null;
        }
    }

    private void Show(string message, InfoBarSeverity severity)
    {
        PageBar.Severity = severity;
        PageBar.Message = message;
        PageBar.IsOpen = true;
    }
}
