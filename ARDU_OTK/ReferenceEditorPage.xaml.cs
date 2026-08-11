using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ARDU_OTK.Services;
using ARDU_OTK.Services.Fc;
using ARDU_OTK.Services.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace ARDU_OTK;

/// <summary>
/// Параметр навигации на мастер эталона.
/// </summary>
/// <param name="Services">Корень композиции.</param>
/// <param name="Reference">Правимый эталон; <c>null</c> — заведение нового.</param>
public sealed record ReferenceEditorArgs(AppServices Services, CalibrationReference? Reference);

/// <summary>
/// Заведение и правка эталона изделия.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 Это единственное место, где эталон проверяется перед тем, как по нему
/// начнут сдавать платы. Проверка стоит здесь, а не внутри процедуры, по
/// простой причине: негодный эталон обязан остановить оператора до того, как
/// плата встала на стапель, а не на четвёртой стадии из девяти, когда часть
/// параметров уже записана в борт.
/// </para>
/// <para>
/// Страница ничего не создаёт сама: реестр приходит параметром навигации. Пока
/// он не задан, сохранение заблокировано и это видно оператору.
/// </para>
/// </remarks>
public sealed partial class ReferenceEditorPage : Page
{
    /// <summary>Допуски по умолчанию — из технологической карты.</summary>
    private static readonly CalibrationTolerances DefaultTolerances = new();

    private AppServices? _services;
    private CalibrationReference? _editing;
    private ReferenceParameters? _parameters;
    private bool _saving;
    private bool _snapshotting;

    // Роли параметров живут в поле, а не выводятся из UI каждый раз: отступления
    // технолога — состояние формы наравне с именем и допусками, и восстановить
    // их из разметки невозможно.
    private ParameterRoleMap _roles = ParameterRoleMap.Default;

    /// <summary>Раскладка заморожена: по эталону уже сдавали платы.</summary>
    private bool _rolesFrozen;

    /// <summary>Сколько сохранённых отступлений не разобралось — признак правки базы снаружи.</summary>
    private int _discardedOverrides;

    /// <summary>
    /// Страница собрана и готова отвечать на события своих элементов.
    /// </summary>
    /// <remarks>
    /// 🔴 Обязателен, и вот почему. <c>ValueChanged</c> и <c>Toggled</c>
    /// подключаются в разметке раньше, чем разметка присваивает начальное
    /// значение, поэтому обработчик срабатывает <b>посреди разбора XAML</b> —
    /// когда поля <c>x:Name</c> заполнены только для элементов, объявленных
    /// выше по документу. Всё, что ниже (плашка готовности, кнопка сохранения,
    /// раздел контроля, список скриптов), в этот момент равно <c>null</c>, и
    /// обращение к нему рушит разбор целиком: наружу выходит
    /// <c>XamlParseException: Failed to assign to property NumberBox.Value</c>,
    /// по которому настоящую причину не видно.
    ///
    /// Проверять каждый элемент на <c>null</c> по отдельности — лечение
    /// симптома: список элементов растёт, и следующий добавленный снова
    /// уронит страницу. Правило одно: до конца сборки страница на события
    /// своих элементов не отвечает, а согласованное состояние выставляется
    /// один раз явным вызовом в конце конструктора.
    /// </remarks>
    private readonly bool _ready;

    public ReferenceEditorPage()
    {
        InitializeComponent();

        HeadingToleranceBox.Value = DefaultTolerances.HeadingVsJigDeg;
        SpreadToleranceBox.Value = DefaultTolerances.InterCompassSpreadDeg;

        _ready = true;

        RenderScripts();
        RebuildRoleSections();
        UpdateGate();
    }

    /// <summary>Ожидаемый состав компасов, выведенный из выбранного эталона.</summary>
    public ObservableCollection<ExpectedCompassSlotRow> Slots { get; } = new();

    /// <summary>
    /// Все параметры эталона одним списком, с режимом контроля в каждой строке.
    /// </summary>
    /// <remarks>
    /// Один список, а не три раздела, именно из-за ползунка в строке:
    /// переключённая строка обязана была бы перепрыгнуть в соседний раздел и
    /// исчезнуть из-под пальца. Раскладка видна счётчиками над списком и
    /// фильтром по режиму.
    /// </remarks>
    public ObservableCollection<ParameterRoleRow> RoleRows { get; } = new();

    /// <summary>Скрипты изделия, входящие в эталон.</summary>
    public ObservableCollection<ReferenceScriptRow> Scripts { get; } = new();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ReferenceEditorArgs args)
        {
            _services = args.Services;
            _editing = args.Reference;
        }
        else if (e.Parameter is AppServices services)
        {
            _services = services;
        }

        WiringBar.IsOpen = _services is null;

        if (_editing is not null)
        {
            EnterEditMode(_editing);
        }
        else
        {
            // Оператора по умолчанию подставляем из настроек стенда: заводит
            // эталон обычно тот же, кто стоит за стендом.
            _ = PrefillAuthorAsync();
        }

        UpdateGate();
    }

    /// <summary>Подтягивает скрипты правимого эталона из реестра.</summary>
    /// <remarks>
    /// Отдельным чтением, а не в составе строки эталона: содержимое скриптов —
    /// килобайты текста, и в списке эталонов, где видно только имя и хеш, они
    /// читались бы при каждом открытии экрана.
    /// </remarks>
    private async Task LoadStoredScriptsAsync(CalibrationReference reference)
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            var scripts = await _services.Store
                .GetReferenceScriptsAsync(reference.Id)
                .ConfigureAwait(true);

            _scripts.Clear();
            _scripts.AddRange(scripts);
            RenderScripts();

            if (scripts.Count > 0)
            {
                ScriptsBar.Severity = InfoBarSeverity.Informational;
                ScriptsBar.Message =
                    "Скрипты эталона неизменяемы: подменённый скрипт сделал бы ложными все прогоны, уже "
                  + "сданные по этому эталону. Для другого набора скриптов заведите новый эталон.";
                ScriptsBar.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            ShowError("Скрипты эталона не прочитаны", ex);
        }
    }

    private async Task PrefillAuthorAsync()
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            var settings = await _services.Store.GetWorkstationSettingsAsync().ConfigureAwait(true);
            if (AuthorBox.Text.Length == 0)
            {
                AuthorBox.Text = settings.DefaultOperator;
                UpdateGate();
            }
        }
        catch (Exception ex)
        {
            // Не смогли прочитать настройки — не повод не давать завести эталон.
            ShowError("Настройки рабочего места не прочитаны", ex);
        }
    }

    private void EnterEditMode(CalibrationReference reference)
    {
        PageTitle.Text = "Правка эталона";

        NameBox.Text = reference.Name;
        DescriptionBox.Text = reference.Description;
        HeadingToleranceBox.Value = reference.HeadingVsJigDeg;
        SpreadToleranceBox.Value = reference.InterCompassSpreadDeg;
        MotorCompToggle.IsOn = reference.TransferMotorComp;

        // Автор фиксируется при заведении: подпись под уже сданными прогонами
        // переписывать нельзя.
        AuthorPanel.Visibility = Visibility.Collapsed;

        // 🔴 Параметры эталона неизменяемы. Подменённый набор сделал бы ложными
        // все прогоны, уже сданные по этому эталону: реестр утверждал бы, что
        // плату сдавали по набору, которого в тот момент не существовало.
        // Другие параметры — это другой эталон.
        BrowseButton.IsEnabled = false;
        SnapshotButton.IsEnabled = false;
        ReferencePathBox.PlaceholderText = "параметры эталона неизменяемы";

        // Роли восстанавливаются ДО разбора параметров: раскладка строится
        // прямо в ApplyParameters, и с картой по умолчанию она показала бы не
        // то, по чему этот эталон работает.
        _roles = reference.ToRoleMap(out var discardedOverrides);
        _rolesFrozen = reference.HasRuns;
        _discardedOverrides = discardedOverrides;

        try
        {
            ApplyParameters(ReferenceParameters.FromStored(reference), reference.SourceName);
        }
        catch (Exception ex)
        {
            ShowError("Параметры эталона не разбираются", ex);
        }

        // Скрипты, как и параметры, неизменяемы: показываем их только для
        // чтения и подгружаем отдельно — содержимое в список эталонов не тянут.
        AddScriptButton.IsEnabled = false;
        ReadScriptsButton.IsEnabled = false;
        _ = LoadStoredScriptsAsync(reference);

        if (reference.HasRuns)
        {
            HeadingToleranceBox.IsEnabled = false;
            SpreadToleranceBox.IsEnabled = false;
            MotorCompToggle.IsEnabled = false;

            FrozenBar.Message =
                $"По эталону сдано прогонов: {reference.RunCount.ToString(CultureInfo.InvariantCulture)}. "
              + "Допуски, правило переноса COMPASS_MOT* и раскладка контроля параметров заморожены: прогоны "
              + "в реестре выполнялись по прежним значениям, и правка сделала бы реестр ложным. Менять можно "
              + "имя и описание. Для другой раскладки заведите новый эталон.";
            FrozenBar.IsOpen = true;
        }
    }

    // --- Эталон -----------------------------------------------------------

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
            if (file is null)
            {
                return;
            }

            LoadParameters(file.Path);
        }
        catch (Exception ex)
        {
            ShowError("Не удалось открыть файл эталона", ex);
        }
    }

    /// <summary>
    /// Снимает эталон с подключённого образцового изделия.
    /// </summary>
    /// <remarks>
    /// Эталон рождается на образце, а не в файле: сначала плату калибруют
    /// вручную, и только потом кто-то выгружает из неё <c>.param</c>. Снимок
    /// убирает этот промежуточный шаг вместе с возможностью перепутать файлы.
    /// </remarks>
    private async void OnSnapshotClick(object sender, RoutedEventArgs e)
    {
        if (_services is null || _snapshotting)
        {
            return;
        }

        if (!_services.IsLinkConnected)
        {
            SnapshotBar.Severity = InfoBarSeverity.Warning;
            SnapshotBar.Message =
                "Связи с бортом нет. Подключите образцовое изделие на главном экране и повторите — "
              + "снимок читается по уже открытому каналу.";
            SnapshotBar.IsOpen = true;
            return;
        }

        SetSnapshotting(true);
        SnapshotBar.IsOpen = false;

        try
        {
            var port = _services.ConnectedPort ?? "неизвестный порт";
            var progress = new Progress<string>(text => SnapshotProgressText.Text = text);

            // 🔴 Снимается вся таблица прошивки, а не компасный блок. Эталон
            // изделия — это конфигурация целиком: снимок по заранее известному
            // списку имён описал бы только то, о чём приложение уже знает, и
            // молча потерял бы настройки приёмника, выходов, режимов и
            // регуляторов — то, чем одна сборка и отличается от другой.
            var snapshot = await _services.ReadAllParametersAsync(progress).ConfigureAwait(true);

            var caption = string.Format(
                CultureInfo.InvariantCulture,
                "снято с борта {0}, {1:dd.MM.yyyy HH:mm}",
                port,
                DateTimeOffset.Now);

            ApplyParameters(
                ReferenceParameters.FromFullSet(snapshot, caption, _services.ConnectedFirmware),
                caption);
            ErrorBar.IsOpen = false;

            // Недостача показывается всегда: молчание о ней означало бы, что
            // оператор считает снимок полным, а он неполон.
            if (!snapshot.IsComplete)
            {
                SnapshotBar.Severity = InfoBarSeverity.Warning;
                SnapshotBar.Message = string.Format(
                    CultureInfo.InvariantCulture,
                    "Снято {0} параметров, борт объявил {1}. Не отдано {2} — этих имён в эталоне не будет, "
                  + "и сверять их будет нечем. Повторите снятие или разберитесь с линией связи до заведения эталона.",
                    snapshot.Values.Count,
                    snapshot.Declared,
                    snapshot.Missing.Count);
            }
            else
            {
                SnapshotBar.Severity = InfoBarSeverity.Success;
                SnapshotBar.Message = string.Format(
                    CultureInfo.InvariantCulture,
                    "Снято с борта {0}: вся таблица прошивки, {1} параметров. Прочитано всё.",
                    port,
                    snapshot.Values.Count);
            }

            SnapshotBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            // Неудачный снимок не оставляет за собой прежний разбор: иначе
            // оператор увидит сводку от файла и решит, что снялось.
            ClearParameters();
            ShowError("Снимок с борта не выполнен", ex);
        }
        finally
        {
            SetSnapshotting(false);
        }
    }

    private void SetSnapshotting(bool snapshotting)
    {
        _snapshotting = snapshotting;

        SnapshotRing.IsActive = snapshotting;
        SnapshotProgressPanel.Visibility = snapshotting ? Visibility.Visible : Visibility.Collapsed;
        SnapshotProgressText.Text = string.Empty;

        SnapshotButton.IsEnabled = !snapshotting && _editing is null;
        BrowseButton.IsEnabled = !snapshotting && _editing is null;
        CancelButton.IsEnabled = !snapshotting;

        AddScriptButton.IsEnabled = ScriptsEditable;
        ReadScriptsButton.IsEnabled = ScriptsEditable;

        UpdateGate();
    }

    private void LoadParameters(string path)
    {
        try
        {
            ApplyParameters(ReferenceParameters.Load(path), path);
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            // Негодный эталон не оставляет за собой прежний разбор: иначе
            // оператор увидит сводку от предыдущего файла и решит, что новый
            // прочитался.
            ClearParameters();
            ShowError("Эталон непригоден", ex);
        }
    }

    // --- Контроль параметров ----------------------------------------------

    private void OnRoleFilterChanged(object sender, TextChangedEventArgs e) => RebuildRoleSections();

    /// <summary>
    /// Пересобирает три секции контроля по текущему набору параметров и текущей
    /// карте ролей.
    /// </summary>
    /// <remarks>
    /// Полная пересборка, а не точечная правка списков: раскладка зависит
    /// разом от набора, от карты ролей, от переключателя переноса
    /// <c>COMPASS_MOT*</c> и от фильтра, и инкрементальное обновление по каждому
    /// из четырёх поводов рано или поздно разошлось бы с тем, что уйдёт в базу.
    /// Набор — десятки, в худшем случае тысячи имён; это доли миллисекунды.
    /// </remarks>
    /// <summary>
    /// Пересобирает список параметров и счётчики по текущему набору и текущей
    /// карте ролей.
    /// </summary>
    /// <remarks>
    /// Полная пересборка, а не точечная правка: раскладка зависит разом от
    /// набора, карты ролей, переключателя переноса <c>COMPASS_MOT*</c> и двух
    /// фильтров, и инкрементальное обновление по каждому из поводов рано или
    /// поздно разошлось бы с тем, что уйдёт в базу. Полный дамп прошивки —
    /// это тысяча с небольшим имён; пересборка укладывается в единицы
    /// миллисекунд, а список виртуализован.
    /// </remarks>
    private void RebuildRoleSections()
    {
        if (!_ready)
        {
            return;
        }

        RoleRows.Clear();

        if (_parameters is null)
        {
            RolesEmptyText.Visibility = Visibility.Visible;
            RoleFilterPanel.Visibility = Visibility.Collapsed;
            RolesBar.IsOpen = false;
            RoleCountVisibleText.Text = "0";
            RoleCountHiddenText.Text = "0";
            RoleCountOffText.Text = "0";
            return;
        }

        RolesEmptyText.Visibility = Visibility.Collapsed;
        RoleFilterPanel.Visibility = Visibility.Visible;

        var nameFilter = RoleFilterBox.Text.Trim();
        var modeFilter = RoleModeFilterBox.SelectedIndex switch
        {
            1 => (ParameterControl?)ParameterControl.ControlledVisible,
            2 => ParameterControl.ControlledHidden,
            3 => ParameterControl.Uncontrolled,
            _ => null,
        };

        var visible = 0;
        var hidden = 0;
        var off = 0;
        var hazardous = 0;

        foreach (var pair in _parameters.Set.Values.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            var role = _roles.Classify(pair.Key);

            switch (role.Control)
            {
                case ParameterControl.ControlledVisible: visible++; break;
                case ParameterControl.ControlledHidden: hidden++; break;
                default: off++; break;
            }

            if (role.IsHazardousOverride)
            {
                hazardous++;
            }

            if (modeFilter is { } mode && role.Control != mode)
            {
                continue;
            }

            if (nameFilter.Length > 0
                && !role.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            RoleRows.Add(new ParameterRoleRow(role, pair.Value));
        }

        RoleCountVisibleText.Text = visible.ToString(CultureInfo.InvariantCulture);
        RoleCountHiddenText.Text = hidden.ToString(CultureInfo.InvariantCulture);
        RoleCountOffText.Text = off.ToString(CultureInfo.InvariantCulture);

        UpdateRolesBar(hazardous, visible + hidden + off);
    }

    /// <summary>Плашка над списком: охват, отступления, опасные отступления, заморозка.</summary>
    private void UpdateRolesBar(int hazardous, int total)
    {
        var notes = new List<string>(4)
        {
            $"Под контролем весь набор эталона: {total.ToString(CultureInfo.InvariantCulture)} имён, а не только компасные.",
        };

        if (_discardedOverrides > 0)
        {
            notes.Add(
                $"Не разобрано записей отступлений: {_discardedOverrides.ToString(CultureInfo.InvariantCulture)}. "
              + "Поле правилось вне приложения; эти имена работают по умолчаниям технологической карты.");
        }

        if (hazardous > 0)
        {
            notes.Add(
                $"Возвращено под контроль имён, которые совпасть не могут: {hazardous.ToString(CultureInfo.InvariantCulture)}. "
              + "По такому эталону остановится каждый прогон, включая прогон исправного изделия.");
        }
        else if (_roles.HasOverrides)
        {
            notes.Add(
                $"Отступлений от технологической карты: {_roles.Overrides.Count.ToString(CultureInfo.InvariantCulture)}. "
              + "Каждое записано с подписью и временем и попадёт в протокол.");
        }

        if (_rolesFrozen)
        {
            notes.Add("Раскладка заморожена: по эталону уже сдавали платы, и менять её нельзя.");
        }

        RolesBar.Severity = hazardous > 0 || _discardedOverrides > 0
            ? InfoBarSeverity.Warning
            : InfoBarSeverity.Informational;
        RolesBar.Message = string.Join(" ", notes);
        RolesBar.IsOpen = true;
    }

    private void OnRoleModeFilterChanged(object sender, SelectionChangedEventArgs e) => RebuildRoleSections();

    /// <summary>
    /// Переключение режима контроля ползунком в строке.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Строка остаётся на месте: список один, и переключение меняет только
    /// счётчики над ним. Пересобирать список здесь запрещено — пересборка
    /// вырвала бы ползунок из-под пальца на полудвижении.
    /// </para>
    /// <para>
    /// 🔴 Возврат под контроль имени, которое совпасть не может, требует
    /// подтверждения. Это единственное переключение, которое ломает не выборку
    /// на экране, а каждый будущий прогон, и молчаливым движением мыши оно
    /// проходить не должно.
    /// </para>
    /// </remarks>
    private async void OnRoleSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready || _parameters is null || sender is not Slider { Tag: string name } slider)
        {
            return;
        }

        var role = _roles.Classify(name);
        var chosen = ParameterRoleRow.FromSliderIndex(e.NewValue);

        if (chosen == role.Control)
        {
            return;
        }

        if (_rolesFrozen)
        {
            slider.Value = ParameterRoleRow.ToSliderIndex(role.Control);
            ShowError(
                "Раскладка заморожена",
                new InvalidOperationException(
                    "По этому эталону уже сдавали платы. Раскладка контроля определяет, что именно сверялось "
                  + "с бортом; правка задним числом объявила бы прошедшими проверки, которых в тех прогонах "
                  + "не выполнялось. Для другой раскладки заведите новый эталон."));
            return;
        }

        if (role.CannotMatch && chosen != ParameterControl.Uncontrolled
            && !await ConfirmHazardousAsync(name, role))
        {
            slider.Value = ParameterRoleRow.ToSliderIndex(role.Control);
            return;
        }

        _roles = chosen == role.DefaultControl
            ? _roles.Without(name)
            : _roles.With(new ParameterRoleOverride(
                name,
                chosen,
                Justification: string.Empty,
                CurrentAuthor(),
                DateTimeOffset.UtcNow));

        RefreshRoleCounters();
        UpdateGate();
    }

    /// <summary>Пересчитывает счётчики, не трогая список.</summary>
    private void RefreshRoleCounters()
    {
        if (_parameters is null)
        {
            return;
        }

        var visible = 0;
        var hidden = 0;
        var off = 0;
        var hazardous = 0;

        foreach (var key in _parameters.Set.Values.Keys)
        {
            var role = _roles.Classify(key);
            switch (role.Control)
            {
                case ParameterControl.ControlledVisible: visible++; break;
                case ParameterControl.ControlledHidden: hidden++; break;
                default: off++; break;
            }

            if (role.IsHazardousOverride)
            {
                hazardous++;
            }
        }

        RoleCountVisibleText.Text = visible.ToString(CultureInfo.InvariantCulture);
        RoleCountHiddenText.Text = hidden.ToString(CultureInfo.InvariantCulture);
        RoleCountOffText.Text = off.ToString(CultureInfo.InvariantCulture);

        UpdateRolesBar(hazardous, visible + hidden + off);
    }

    /// <summary>Спрашивает подтверждение на возврат под контроль имени, которое совпасть не может.</summary>
    private async Task<bool> ConfirmHazardousAsync(string name, ParameterRole role)
    {
        var body = role.Reason
            + Environment.NewLine + Environment.NewLine
            + "Под контролем это имя остановит каждый прогон, а не только прогон неисправного изделия: "
            + "значение определяется конкретным экземпляром железа либо ведётся самой прошивкой, "
            + "и совпасть с эталоном оно не может.";

        var dialog = new ContentDialog
        {
            Title = name,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Всё равно контролировать",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Кто подписывает отступление. В правке автор эталона зафиксирован при заведении.</summary>
    private string CurrentAuthor() =>
        _editing is not null ? _editing.CreatedBy : AuthorBox.Text.Trim();

    // --- Скрипты изделия ----------------------------------------------------

    /// <summary>
    /// Скрипты правимого эталона неизменяемы.
    /// </summary>
    /// <remarks>
    /// 🔴 То же правило, что и для параметров, и по той же причине: подменённый
    /// скрипт сделал бы ложными все прогоны, уже сданные по этому эталону.
    /// Реестр утверждал бы, что плату сдавали по набору скриптов, которого в
    /// тот момент не существовало. Другие скрипты — это другой эталон.
    /// </remarks>
    private bool ScriptsEditable => _editing is null && !_saving && !_snapshotting && !_readingScripts;

    private bool _readingScripts;

    private readonly List<ReferenceScript> _scripts = new();

    private async void OnAddScriptClick(object sender, RoutedEventArgs e)
    {
        if (!ScriptsEditable)
        {
            return;
        }

        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

            picker.FileTypeFilter.Add(ReferenceScript.ScriptExtension);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            AddScript(ReferenceScript.Load(file.Path));

            ScriptsBar.Severity = InfoBarSeverity.Success;
            ScriptsBar.Message =
                $"Добавлен {System.IO.Path.GetFileName(file.Path)}. На борту он будет лежать по пути "
              + $"{ReferenceScript.ScriptsDirectory}/{System.IO.Path.GetFileName(file.Path)}.";
            ScriptsBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            ShowError("Скрипт не добавлен", ex);
        }
    }

    /// <summary>
    /// Снимает скрипты с подключённого образцового изделия.
    /// </summary>
    /// <remarks>
    /// Как и параметры, скрипты рождаются на образце: их туда положили и
    /// проверили в полёте. Промежуточная выгрузка на диск — лишний шаг, на
    /// котором теряется связь между эталоном и бортом, с которого он снят.
    /// </remarks>
    private async void OnReadScriptsClick(object sender, RoutedEventArgs e)
    {
        if (_services is null || !ScriptsEditable)
        {
            return;
        }

        if (!_services.IsLinkConnected)
        {
            ScriptsBar.Severity = InfoBarSeverity.Warning;
            ScriptsBar.Message =
                "Связи с бортом нет. Подключите образцовое изделие на главном экране и повторите — "
              + "скрипты читаются по уже открытому каналу.";
            ScriptsBar.IsOpen = true;
            return;
        }

        SetReadingScripts(true);
        ScriptsBar.IsOpen = false;

        try
        {
            var unreadable = new List<string>();
            var progress = new Progress<string>(text => ScriptsProgressText.Text = text);

            var scripts = await _services.ReadScriptsAsync(unreadable, progress).ConfigureAwait(true);

            foreach (var script in scripts)
            {
                AddScript(script);
            }

            // Скрипты не исполняются при выключенном SCR_ENABLE. Эталон со
            // скриптами и выключенным скриптингом описывает изделие, у которого
            // они лежат на карте мёртвым грузом, — сказать об этом надо здесь.
            var scripting = await ReadScriptingStateAsync().ConfigureAwait(true);

            var notes = new List<string>(3)
            {
                scripts.Count == 0
                    ? $"В каталоге {ReferenceScript.ScriptsDirectory} исполняемых скриптов не найдено."
                    : $"Снято скриптов: {scripts.Count.ToString(CultureInfo.InvariantCulture)}.",
            };

            if (scripting is { } value && value == 0)
            {
                notes.Add("На образце SCR_ENABLE = 0: скриптинг выключен, и эти файлы прошивкой не исполняются.");
            }

            if (unreadable.Count > 0)
            {
                notes.Add("Не прочитано: " + string.Join("; ", unreadable)
                    + ". Эти файлы в эталон не вошли, и сверять их будет нечем.");
            }

            ScriptsBar.Severity = unreadable.Count > 0 || scripting == 0
                ? InfoBarSeverity.Warning
                : InfoBarSeverity.Success;
            ScriptsBar.Message = string.Join(" ", notes);
            ScriptsBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            ShowError("Скрипты с борта не сняты", ex);
        }
        finally
        {
            SetReadingScripts(false);
        }
    }

    /// <summary>Значение <c>SCR_ENABLE</c> образца; <c>null</c>, если прошивка его не отдала.</summary>
    private async Task<double?> ReadScriptingStateAsync()
    {
        if (_services is null)
        {
            return null;
        }

        try
        {
            var snapshot = await _services.ReadParameterAsync("SCR_ENABLE").ConfigureAwait(true);
            return snapshot;
        }
        catch (Exception)
        {
            // Прошивка вправе не знать имени — это не отказ снимка.
            return null;
        }
    }

    private void OnRemoveScriptClick(object sender, RoutedEventArgs e)
    {
        if (!ScriptsEditable || sender is not Button { Tag: string path })
        {
            return;
        }

        _scripts.RemoveAll(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
        RenderScripts();
        UpdateGate();
    }

    /// <summary>
    /// Добавляет скрипт, заменяя одноимённый.
    /// </summary>
    /// <remarks>
    /// Путь — ключ: два скрипта по одному пути на борту существовать не могут,
    /// и хранить их в эталоне значило бы записать один поверх другого в порядке,
    /// который нигде не определён.
    /// </remarks>
    private void AddScript(ReferenceScript script)
    {
        _scripts.RemoveAll(s => string.Equals(s.Path, script.Path, StringComparison.OrdinalIgnoreCase));
        _scripts.Add(script);
        RenderScripts();
        UpdateGate();
    }

    private void RenderScripts()
    {
        if (!_ready)
        {
            return;
        }

        Scripts.Clear();
        foreach (var script in _scripts.OrderBy(static s => s.Path, StringComparer.Ordinal))
        {
            Scripts.Add(new ReferenceScriptRow(script, ScriptsEditable));
        }

        ScriptsSummaryText.Text = _scripts.Count == 0
            ? "Скриптов нет — изделие без скриптинга."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Скриптов {_scripts.Count}, всего {_scripts.Sum(static s => s.ByteCount)} байт.");
    }

    private void SetReadingScripts(bool reading)
    {
        _readingScripts = reading;

        ScriptsRing.IsActive = reading;
        ScriptsProgressPanel.Visibility = reading ? Visibility.Visible : Visibility.Collapsed;
        ScriptsProgressText.Text = string.Empty;

        AddScriptButton.IsEnabled = ScriptsEditable;
        ReadScriptsButton.IsEnabled = ScriptsEditable;
        CancelButton.IsEnabled = !reading;

        RenderScripts();
        UpdateGate();
    }

    private void ClearParameters()
    {
        _parameters = null;
        Slots.Clear();
        RoleRows.Clear();

        ReferencePathBox.Text = string.Empty;
        ReferenceSummaryPanel.Visibility = Visibility.Collapsed;
        SlotsList.Visibility = Visibility.Collapsed;
        SlotsEmptyText.Visibility = Visibility.Visible;
        MissingCoreBar.IsOpen = false;
        MissingMotBar.IsOpen = false;
        TopologyBar.IsOpen = false;
        SnapshotBar.IsOpen = false;

        RebuildRoleSections();
        UpdateGate();
    }

    private void ApplyParameters(ReferenceParameters parameters, string displaySource)
    {
        _parameters = parameters;

        ReferencePathBox.Text = displaySource;

        ReferenceSummaryText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "Формат {0}. Параметров {1}, из них COMPASS_* — {2}. Блок калибровки требуется от слотов: {3}.",
            parameters.Format,
            parameters.ParamCount,
            parameters.CompassParamCount,
            parameters.PopulatedSlots.Count == 0
                ? "нет"
                : string.Join(", ", parameters.PopulatedSlots));

        ReferenceHashText.Text = "SHA-256 набора: " + parameters.Hash;
        ReferenceSummaryPanel.Visibility = Visibility.Visible;

        Slots.Clear();
        foreach (var slot in parameters.ExpectedSlots)
        {
            Slots.Add(new ExpectedCompassSlotRow(slot));
        }

        SlotsList.Visibility = Visibility.Visible;
        SlotsEmptyText.Visibility = Visibility.Collapsed;

        if (parameters.MissingCore.Count > 0)
        {
            MissingCoreBar.Message =
                "Не найдены: " + string.Join(", ", parameters.MissingCore)
              + ". Эти параметры не будут перенесены в борт — на месте останутся его собственные значения. "
              + "Эталон без COMPASS_DIA*/COMPASS_ODI* — это другой эталон, а не почти такой же.";
            MissingCoreBar.IsOpen = true;
        }
        else
        {
            MissingCoreBar.IsOpen = false;
        }

        if (parameters.MissingMotorComp.Count > 0)
        {
            MissingMotBar.Message =
                "Компенсация тока двигателей в эталоне отсутствует — на образце не выполнялся CompassMot. "
              + "Это не дефект: если переключатель ниже включён, переносить будет нечего.";
            MissingMotBar.IsOpen = true;
        }
        else
        {
            MissingMotBar.IsOpen = false;
        }

        RebuildRoleSections();
        UpdateGate();
    }

    // --- Готовность к сохранению ------------------------------------------

    private void OnFormFieldChanged(object sender, TextChangedEventArgs e) => UpdateGate();

    /// <summary>
    /// Переключатель переноса <c>COMPASS_MOT*</c> меняет и раскладку контроля:
    /// выключенный перенос выводит эти имена из-под контроля.
    /// </summary>
    /// <remarks>
    /// Согласование здесь, а не при сохранении: иначе оператор видел бы
    /// COMPASS_MOT* в контролируемой секции ровно до того момента, когда менять
    /// раскладку уже поздно.
    /// </remarks>
    private void OnFormToggled(object sender, RoutedEventArgs e)
    {
        _roles = _roles.WithMotorCompTransfer(MotorCompToggle.IsOn);
        RebuildRoleSections();
        UpdateGate();
    }

    private void OnToleranceChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => UpdateGate();

    /// <summary>
    /// Проверяет форму и прямо называет, чего не хватает.
    /// </summary>
    /// <remarks>
    /// Мёртвая кнопка без объяснения — худший исход: оператор перебирает поля
    /// наугад и в итоге вносит в эталон неверные данные, лишь бы кнопка ожила.
    /// </remarks>
    private void UpdateGate()
    {
        if (!_ready)
        {
            return;
        }

        var problems = new List<string>();

        if (_services is null)
        {
            problems.Add("реестр не подключён");
        }

        if (NameBox.Text.Trim().Length == 0)
        {
            problems.Add("не задано имя эталона");
        }

        if (_editing is null && AuthorBox.Text.Trim().Length == 0)
        {
            problems.Add("не указано, кто заводит эталон");
        }

        if (_parameters is null)
        {
            problems.Add("не задан эталон — выберите файл или снимите с борта");
        }

        if (_snapshotting)
        {
            problems.Add("идёт снятие эталона с борта");
        }

        if (_readingScripts)
        {
            problems.Add("идёт чтение скриптов с борта");
        }

        if (!TryReadTolerance(HeadingToleranceBox, out _))
        {
            problems.Add("допуск курса вне диапазона 0,1…90°");
        }

        if (!TryReadTolerance(SpreadToleranceBox, out _))
        {
            problems.Add("допуск расхождения вне диапазона 0,1…90°");
        }

        problems.AddRange(TopologyProblems());

        var ready = problems.Count == 0 && !_saving && !_snapshotting && !_readingScripts;

        GateBar.IsOpen = problems.Count > 0 && !_saving;
        GateBar.Message = problems.Count == 0 ? string.Empty : "Не готово: " + string.Join(", ", problems) + ".";
        SaveButton.IsEnabled = ready;
    }

    /// <summary>
    /// Претензии к составу компасов эталона.
    /// </summary>
    /// <remarks>
    /// 🔴 Технологическая карта требует внешний компас в первом слоте. Эталон,
    /// который описывает там внутренний, гарантированно завалит стадию сверки
    /// топологии на каждом прогоне — принимать такой эталон значит заранее
    /// согласиться на брак. А вот отсутствие в эталоне COMPASS_EXTERNAL*
    /// отказом не является: прошивка определяет внешность сама, и сверять её с
    /// эталоном процедура в этом случае просто не станет.
    /// </remarks>
    private IEnumerable<string> TopologyProblems()
    {
        if (_parameters is null)
        {
            yield break;
        }

        var slot1 = Slots.FirstOrDefault(r => r.Slot == CompassIdentity.MinSlot);
        if (slot1 is null || !slot1.IsPresent)
        {
            TopologyBar.Message =
                "Эталон не описывает компас в слоте 1 (COMPASS_DEV_ID нулевой или отсутствует). "
              + "Приоритетного компаса нет — сверять на борту будет нечего.";
            TopologyBar.Severity = InfoBarSeverity.Error;
            TopologyBar.IsOpen = true;
            yield return "в эталоне нет компаса в слоте 1";
            yield break;
        }

        switch (slot1.IsExternal)
        {
            case false:
                TopologyBar.Message =
                    $"Слот 1 эталона: {slot1.DeviceText} — внутренний компас. "
                  + "Технологическая карта требует в первом слоте внешний. По такому эталону каждый прогон "
                  + "остановится на сверке состава компасов.";
                TopologyBar.Severity = InfoBarSeverity.Error;
                TopologyBar.IsOpen = true;
                yield return "в слоте 1 эталона внутренний компас";
                break;

            case null:
                TopologyBar.Message =
                    "Внешность компаса в слоте 1 по эталону не определяется: в файле нет COMPASS_EXTERNAL "
                  + "либо его значение противоречит типу шины. Прошивка определит внешность сама, и процедура "
                  + "проверит её уже на борту — заводить эталон можно.";
                TopologyBar.Severity = InfoBarSeverity.Warning;
                TopologyBar.IsOpen = true;
                break;

            default:
                TopologyBar.IsOpen = false;
                break;
        }
    }

    /// <summary>Пустой <c>NumberBox</c> отдаёт <c>NaN</c> — это «не задано», а не ноль.</summary>
    private static bool TryReadTolerance(NumberBox box, out double value)
    {
        value = box.Value;
        return !double.IsNaN(value) && !double.IsInfinity(value) && value is > 0 and <= 90;
    }

    // --- Сохранение -------------------------------------------------------

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_services is null || _parameters is null || _saving)
        {
            return;
        }

        if (!TryReadTolerance(HeadingToleranceBox, out var heading)
            || !TryReadTolerance(SpreadToleranceBox, out var spread))
        {
            return;
        }

        SetSaving(true);
        try
        {
            if (_editing is null)
            {
                await _services.Store.CreateReferenceAsync(new NewCalibrationReference(
                    Name: NameBox.Text,
                    Description: DescriptionBox.Text,
                    Parameters: _parameters,
                    HeadingVsJigDeg: heading,
                    InterCompassSpreadDeg: spread,
                    TransferMotorComp: MotorCompToggle.IsOn,
                    CreatedBy: AuthorBox.Text)
                {
                    Roles = _roles,
                    Scripts = _scripts.OrderBy(static s => s.Path, StringComparer.Ordinal).ToArray(),
                }).ConfigureAwait(true);
            }
            else
            {
                await _services.Store.UpdateReferenceAsync(
                    _editing.Id,
                    NameBox.Text,
                    DescriptionBox.Text,
                    heading,
                    spread,
                    MotorCompToggle.IsOn,
                    _roles).ConfigureAwait(true);
            }

            if (Frame.CanGoBack)
            {
                Frame.GoBack();
                return;
            }
        }
        catch (Exception ex)
        {
            ShowError("Эталон не сохранён", ex);
        }
        finally
        {
            SetSaving(false);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void SetSaving(bool saving)
    {
        _saving = saving;

        NameBox.IsEnabled = !saving;
        DescriptionBox.IsEnabled = !saving;
        AuthorBox.IsEnabled = !saving;
        BrowseButton.IsEnabled = !saving && _editing is null;
        SnapshotButton.IsEnabled = !saving && _editing is null;
        CancelButton.IsEnabled = !saving;

        AddScriptButton.IsEnabled = ScriptsEditable;
        ReadScriptsButton.IsEnabled = ScriptsEditable;

        if (_editing?.HasRuns != true)
        {
            HeadingToleranceBox.IsEnabled = !saving;
            SpreadToleranceBox.IsEnabled = !saving;
            MotorCompToggle.IsEnabled = !saving;
        }

        UpdateGate();
    }

    private void ShowError(string title, Exception ex)
    {
        ErrorBar.Severity = InfoBarSeverity.Error;
        ErrorBar.Title = title;
        ErrorBar.Message = ex.Message;
        ErrorBar.IsOpen = true;
    }
}
