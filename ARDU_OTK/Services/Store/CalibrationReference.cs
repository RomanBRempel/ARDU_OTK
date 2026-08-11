using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ARDU_OTK.Services.Fc;

namespace ARDU_OTK.Services.Store;

/// <summary>
/// Эталон изделия: всё, что постоянно для модели борта и потому не должно
/// вводиться заново на каждый прогон.
/// </summary>
/// <remarks>
/// <para>
/// Два уровня понятия разведены намеренно. <b>Эталон</b> — этот объект целиком:
/// имя, параметры, допуски приёмки. <b>Параметры эталона</b>
/// (<see cref="ReferenceParameters"/>) — его разобранное содержимое, снятое с
/// образца или прочитанное из файла. Одно слово на оба уровня превращает любую
/// фразу про «эталон» в двусмысленность.
/// </para>
/// <para>
/// 🔴 Эталон хранит <b>содержимое</b> параметров (<see cref="ParamText"/>), а не
/// путь к файлу. Путь остаётся только как свидетельство происхождения. Причина
/// в том, что файл на сетевом ресурсе можно удалить, переименовать или
/// переписать, и эталон, ссылающийся на путь, ломается именно тогда, когда по
/// нему разбирают рекламацию. Хранимый текст делает эталон самодостаточным: то,
/// чем сдавали плату, воспроизводимо спустя годы.
/// </para>
/// <para>
/// Азимут стапеля в эталон <b>не входит</b>: это свойство рабочего места, а не
/// изделия. Он живёт в <see cref="WorkstationSettings"/>.
/// </para>
/// <para>
/// Ожидаемый состав компасов здесь не хранится и не дублируется: он полностью
/// выводится из <see cref="ParamText"/> методом <see cref="ReadExpectedSlots"/>.
/// Второй экземпляр той же истины рано или поздно разойдётся с первым.
/// </para>
/// </remarks>
/// <param name="Id">Ключ в реестре.</param>
/// <param name="Name">Имя, как его называет предприятие.</param>
/// <param name="Description">Пояснение для оператора; может быть пустым.</param>
/// <param name="SourceName">Откуда взяты параметры: имя файла либо подпись снимка с борта.</param>
/// <param name="ParamFormat">Формат исходника: <c>MissionPlanner</c> либо <c>QGroundControl</c>.</param>
/// <param name="ParamText">Содержимое параметров целиком, с переводами строк <c>\n</c>.</param>
/// <param name="ParamHash">Канонический SHA-256 набора — см. <see cref="ReferenceParamFile.ComputeHash(ReferenceParamSet)"/>.</param>
/// <param name="ParamCount">Сколько параметров разобралось на момент заведения.</param>
/// <param name="HeadingVsJigDeg">Допуск курса против азимута стапеля, градусы.</param>
/// <param name="InterCompassSpreadDeg">Допуск расхождения курсов между компасами, градусы.</param>
/// <param name="TransferMotorComp">Переносить ли <c>COMPASS_MOT*</c> — см. <see cref="ReferenceParamFile.FindMissingTransferable"/>.</param>
/// <param name="ParamRoles">
/// Отступления технолога от умолчаний технологической карты по ролям
/// параметров, сериализованные <see cref="ParameterRoleMap.SerializeOverrides"/>.
/// Пустая строка — отступлений нет, действуют одни умолчания.
/// </param>
/// <param name="CreatedBy">Кто завёл эталон.</param>
/// <param name="CreatedUtc">Когда заведён.</param>
/// <param name="RetiredUtc">Когда выведен из обращения; <c>null</c> — действующий.</param>
/// <param name="RunCount">Сколько прогонов сдано по этому эталону.</param>
public sealed record CalibrationReference(
    long Id,
    string Name,
    string Description,
    string SourceName,
    string ParamFormat,
    string ParamText,
    string ParamHash,
    int ParamCount,
    double HeadingVsJigDeg,
    double InterCompassSpreadDeg,
    bool TransferMotorComp,
    string ParamRoles,
    string CreatedBy,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? RetiredUtc,
    int RunCount)
{
    /// <summary>Эталон выведен из обращения: выбрать его для нового прогона нельзя.</summary>
    public bool IsRetired => RetiredUtc.HasValue;

    /// <summary>По этому эталону уже сдавали платы — менять допуски нельзя.</summary>
    public bool HasRuns => RunCount > 0;

    /// <summary>Разбирает хранимые параметры.</summary>
    /// <remarks>
    /// Текст разбирается заново на каждый вызов — набор мал (десятки строк), а
    /// кэш означал бы третий экземпляр той же истины.
    /// </remarks>
    /// <exception cref="InvalidDataException">Хранимый текст не разбирается — база правилась снаружи.</exception>
    public ReferenceParamSet ParseParameters() =>
        ReferenceParamFile.Parse(SourceName, ReferenceParameters.SplitLines(ParamText));

    /// <summary>Ожидаемый состав компасов, выведенный из параметров.</summary>
    public IReadOnlyList<ExpectedCompassSlot> ReadExpectedSlots() =>
        CompassIdentity.ReadExpectedSlots(ParseParameters());

    /// <summary>
    /// Роли параметров этого эталона: умолчания технологической карты плюс
    /// сохранённые отступления технолога.
    /// </summary>
    /// <remarks>
    /// 🔴 Единственный способ узнать, что этот эталон контролирует. Стадии
    /// процедуры и мастер эталона обязаны спрашивать здесь; собственный список
    /// «что проверять» где-либо ещё — это второй экземпляр той же истины, и он
    /// молча разойдётся с первым.
    /// </remarks>
    /// <param name="discardedOverrides">
    /// Сколько записей отступлений не разобралось. Ненулевое значение означает,
    /// что поле правили снаружи; эталон при этом остаётся годным и работает по
    /// умолчаниям, но сказать об этом оператору обязательно.
    /// </param>
    public ParameterRoleMap ToRoleMap(out int discardedOverrides) =>
        ParameterRoleMap.Create(
            TransferMotorComp,
            ParameterRoleMap.DeserializeOverrides(ParamRoles, out discardedOverrides));

    /// <inheritdoc cref="ToRoleMap(out int)"/>
    public ParameterRoleMap ToRoleMap() => ToRoleMap(out _);

    /// <summary>Допуски процедуры, как их задаёт этот эталон.</summary>
    /// <remarks>
    /// Окна наблюдения (<see cref="CalibrationTolerances.TelemetryWindow"/> и
    /// <see cref="CalibrationTolerances.PrearmWindow"/>) эталоном не управляются:
    /// это свойства канала связи, а не изделия.
    /// </remarks>
    public CalibrationTolerances ToTolerances() => new()
    {
        HeadingVsJigDeg = HeadingVsJigDeg,
        InterCompassSpreadDeg = InterCompassSpreadDeg,
    };

    /// <summary>Короткая подпись: имя и первые разряды хеша параметров.</summary>
    public string ShortCaption => string.Create(
        CultureInfo.InvariantCulture,
        $"{Name} · {ReferenceParameters.ShortHash(ParamHash)}");
}

/// <summary>
/// Заготовка эталона: то, что оператор задал в мастере, до записи в реестр.
/// </summary>
/// <remarks>
/// Тип существует затем, чтобы негодный эталон нельзя было составить: параметры
/// заполняются только из <see cref="ReferenceParameters.Load"/> либо
/// <see cref="ReferenceParameters.FromSnapshot"/>, а те сначала разбирают
/// исходник и падают на непригодном.
/// </remarks>
public sealed record NewCalibrationReference(
    string Name,
    string Description,
    ReferenceParameters Parameters,
    double HeadingVsJigDeg,
    double InterCompassSpreadDeg,
    bool TransferMotorComp,
    string CreatedBy)
{
    /// <summary>
    /// Отступления технолога от умолчаний по ролям параметров. Пустая карта —
    /// эталон работает целиком по технологической карте.
    /// </summary>
    public ParameterRoleMap Roles { get; init; } = ParameterRoleMap.Default;

    /// <summary>
    /// Скрипты изделия. Пустой список — изделие без скриптов, и это законное
    /// состояние, а не незаполненное поле.
    /// </summary>
    public IReadOnlyList<ReferenceScript> Scripts { get; init; } = Array.Empty<ReferenceScript>();
}

/// <summary>
/// Параметры эталона: разобранное и проверенное содержимое, готовое лечь в
/// <see cref="CalibrationReference"/>.
/// </summary>
/// <param name="SourceName">Откуда взято — имя файла либо подпись снимка с борта.</param>
/// <param name="SourcePath">Полный путь, откуда прочитано. Хранению не подлежит.</param>
/// <param name="Format">Формат исходника.</param>
/// <param name="Text">Содержимое с нормализованными переводами строк.</param>
/// <param name="Hash">Канонический хеш набора.</param>
/// <param name="Set">Разобранный набор параметров.</param>
/// <param name="ExpectedSlots">Ожидаемый состав компасов, слоты 1..3.</param>
/// <param name="PopulatedSlots">
/// Слоты, для которых набор обязан нести переносимый блок, — см.
/// <see cref="Load"/>. Слот, объявленный пустым, сюда не входит.
/// </param>
/// <param name="MissingCore">
/// Переносимые параметры калибровки, которых в наборе нет (без <c>COMPASS_MOT*</c>).
/// Непустой список — не отказ сам по себе, но оператор обязан его увидеть.
/// </param>
/// <param name="MissingMotorComp">Отсутствующие <c>COMPASS_MOT*</c>.</param>
public sealed record ReferenceParameters(
    string SourceName,
    string SourcePath,
    string Format,
    string Text,
    string Hash,
    ReferenceParamSet Set,
    IReadOnlyList<ExpectedCompassSlot> ExpectedSlots,
    IReadOnlyList<int> PopulatedSlots,
    IReadOnlyList<string> MissingCore,
    IReadOnlyList<string> MissingMotorComp)
{
    /// <summary>Сколько параметров всего разобралось.</summary>
    public int ParamCount => Set.Values.Count;

    /// <summary>Сколько из них относится к компасу.</summary>
    public int CompassParamCount => Set.Values.Keys.Count(
        static k => k.StartsWith(ReferenceParamFile.CompassPrefix, StringComparison.Ordinal));

    /// <summary>Слоты, о которых набор вообще что-то говорит.</summary>
    public IReadOnlyList<int> DescribedSlots => ReferenceParamFile.DetectSlots(Set);

    /// <summary>
    /// Читает файл параметров и проверяет его пригодность для переноса калибровки.
    /// </summary>
    /// <remarks>
    /// 🔴 Одна из двух точек, где параметры попадают в приложение (вторая —
    /// <see cref="FromSnapshot"/>). Проверка идёт здесь, при заведении эталона, а
    /// не внутри уже запущенной процедуры: негодный набор обязан остановить
    /// оператора до того, как плата встала на стапель, а не на четвёртой стадии
    /// из девяти.
    /// </remarks>
    /// <exception cref="FileNotFoundException">Файла нет.</exception>
    /// <exception cref="InvalidDataException">
    /// Файл не разбирается, разобрался в ноль параметров либо не содержит ни
    /// одного <c>COMPASS_*</c> — переносить из него нечего.
    /// </exception>
    public static ReferenceParameters Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Файл параметров не найден: {path}", path);
        }

        // Читаем строками, а не целиком: ReadAllLines снимает BOM и сам
        // разбирается с CRLF/LF/CR. Дальше по приложению текст ходит уже
        // нормализованным — с одним видом перевода строки.
        var lines = File.ReadAllLines(path);
        var text = string.Join("\n", lines);

        // Разбор идёт по тем же строкам, которые лягут в базу: если сохранённый
        // текст не разберётся обратно, узнать об этом надо сейчас, а не при
        // первом прогоне через полгода.
        var set = ReferenceParamFile.Parse(path, SplitLines(text));

        return Create(Path.GetFileName(path), path, text, set);
    }

    /// <summary>
    /// Восстанавливает разбор из уже заведённого эталона.
    /// </summary>
    /// <remarks>
    /// Идёт по тому же пути, что и <see cref="Load"/>, поэтому экран правки
    /// показывает ровно то же, что показывал экран заведения. Файл на диске при
    /// этом не нужен: эталон самодостаточен.
    /// </remarks>
    /// <exception cref="InvalidDataException">Хранимый текст не разбирается — база правилась снаружи.</exception>
    public static ReferenceParameters FromStored(CalibrationReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var set = ReferenceParamFile.Parse(reference.SourceName, SplitLines(reference.ParamText));
        return Create(reference.SourceName, sourcePath: string.Empty, reference.ParamText, set);
    }

    /// <summary>
    /// Собирает набор из снимка, снятого с образцового изделия.
    /// </summary>
    /// <param name="snapshot">Прочитанные с борта параметры.</param>
    /// <param name="caption">
    /// Чем подписать происхождение: порт и время снятия. Ложится в
    /// <see cref="CalibrationReference.SourceName"/> вместо имени файла.
    /// </param>
    /// <remarks>
    /// <para>
    /// Снимок сериализуется в формат Mission Planner и разбирается тем же
    /// разборщиком, что и файл. Лишний оборот здесь намеренный: он гарантирует,
    /// что текст в базе и разобранный набор — одно и то же, и что хеш считается
    /// по той же канонической форме. Иначе эталон, снятый с борта, и эталон,
    /// заведённый из выгрузки той же платы, дали бы разные хеши и выглядели бы
    /// разными.
    /// </para>
    /// <para>
    /// Происхождение записывается комментарием внутрь самого текста: выгрузив
    /// параметры через полгода, надо иметь возможность узнать, с чего они сняты,
    /// не заглядывая в базу.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidDataException">В снимке нет ни одного <c>COMPASS_*</c>.</exception>
    public static ReferenceParameters FromSnapshot(CompassSnapshot snapshot, string caption)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(caption);

        var builder = new List<string>(snapshot.Values.Count + 4)
        {
            "# Параметры сняты с образцового изделия",
            "# " + caption,
        };

        if (!string.IsNullOrWhiteSpace(snapshot.FirmwareBanner))
        {
            builder.Add("# прошивка образца: " + snapshot.FirmwareBanner);
        }

        builder.Add(string.Empty);

        // Порядок имён фиксирован: иначе два снимка одного борта дали бы разный
        // текст при одинаковом наборе значений, и сравнение выгрузок глазами
        // стало бы бессмысленным. На хеш порядок не влияет — он канонический.
        foreach (var pair in snapshot.Values.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            builder.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{pair.Key},{ReferenceParamFile.FormatCanonical(pair.Value)}"));
        }

        var text = string.Join("\n", builder);
        var set = ReferenceParamFile.Parse(caption, SplitLines(text));

        return Create(caption, sourcePath: string.Empty, text, set);
    }

    private static ReferenceParameters Create(
        string sourceName,
        string sourcePath,
        string text,
        ReferenceParamSet set)
    {
        var where = string.IsNullOrEmpty(sourcePath) ? sourceName : sourcePath;

        var compassCount = set.Values.Keys.Count(
            static k => k.StartsWith(ReferenceParamFile.CompassPrefix, StringComparison.Ordinal));
        if (compassCount == 0)
        {
            throw new InvalidDataException(
                $"В наборе «{where}» нет ни одного параметра {ReferenceParamFile.CompassPrefix}* — "
                + "перенос калибровки компаса по нему невозможен.");
        }

        var expectedSlots = CompassIdentity.ReadExpectedSlots(set);

        // 🔴 «Слот, о котором набор что-то говорит» и «слот, для которого набор
        // обязан нести калибровку» — разные множества, и путать их нельзя.
        // Строка COMPASS_DEV_ID3 = 0 означает «третьего компаса нет»: требовать
        // для него COMPASS_OFS3_* значит выдать десять ложных пропусков. Список
        // пропусков, где большинство строк — шум, оператор пролистывает не читая,
        // и настоящая недостача COMPASS_DIA_* проходит незамеченной.
        //
        // Слот с ненулевым DEV_ID блок обязан иметь. Слот, у которого DEV_ID в
        // наборе вовсе отсутствует, но смещения есть, тоже проверяется: такой
        // набор неполон, и оператор должен об этом узнать.
        var populatedSlots = ReferenceParamFile.DetectSlots(set)
            .Where(slot => expectedSlots[slot - CompassIdentity.MinSlot].IsPresent
                        || !set.Values.ContainsKey(CompassIdentity.DevIdName(slot)))
            .ToArray();

        var missingCore = ReferenceParamFile.FindMissingTransferable(
            set, populatedSlots, includeMotorCompensation: false);

        // COMPASS_MOT* считаем отдельно: их отсутствие — не дефект набора, а
        // сообщение о том, что CompassMot на образце не выполнялся.
        var missingAll = ReferenceParamFile.FindMissingTransferable(
            set, populatedSlots, includeMotorCompensation: true);
        var missingMot = missingAll.Except(missingCore, StringComparer.Ordinal).ToArray();

        return new ReferenceParameters(
            SourceName: sourceName,
            SourcePath: sourcePath,
            Format: set.Format,
            Text: text,
            Hash: ReferenceParamFile.ComputeHash(set),
            Set: set,
            ExpectedSlots: expectedSlots,
            PopulatedSlots: populatedSlots,
            MissingCore: missingCore,
            MissingMotorComp: missingMot);
    }

    /// <summary>
    /// Разбивает хранимый текст на строки.
    /// </summary>
    /// <remarks>
    /// 🔴 Хвостовой <c>\r</c> обязан быть срезан. Разборщик Mission Planner
    /// делит строку по пробелу, запятой и табуляции, и возврат каретки
    /// прилипает к числовому полю: <c>«12.5\r»</c> не разбирается как число, и
    /// весь набор отвергается как битый.
    /// </remarks>
    public static IReadOnlyList<string> SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd('\r');
        }

        return lines;
    }

    /// <summary>Первые разряды хеша — столько, сколько человек реально сверяет глазами.</summary>
    public static string ShortHash(string? hash) =>
        string.IsNullOrWhiteSpace(hash) ? "—"
        : hash.Length <= 12 ? hash
        : hash[..8] + "…" + hash[^4..];
}

/// <summary>
/// Настройки рабочего места: то, что постоянно для стенда и не относится ни к
/// изделию, ни к конкретной плате.
/// </summary>
/// <remarks>
/// 🔴 <see cref="JigAzimuthDeg"/> допускает <c>null</c>, и это существенно:
/// 0° — законный азимут (север). Если бы «не настроено» кодировалось нулём,
/// ненастроенный стенд был бы неотличим от стапеля, направленного на север, и
/// первая же плата уехала бы с калибровкой на чужой курс.
/// </remarks>
public sealed record WorkstationSettings
{
    /// <summary>ИСТИННЫЙ азимут стапеля, градусы 0…360. <c>null</c> — не настроен.</summary>
    public double? JigAzimuthDeg { get; init; }

    /// <summary>Оператор по умолчанию. Пустая строка — не задан.</summary>
    public string DefaultOperator { get; init; } = string.Empty;

    /// <summary>Эталон, выбранный в прошлый раз: стенд должен открываться там, где его закрыли.</summary>
    public long? LastReferenceId { get; init; }

    /// <summary>Подключаться к борту автоматически при открытии рабочего экрана.</summary>
    public bool AutoConnect { get; init; }

    /// <summary>Порт, выбранный в прошлый раз. Пустая строка — не выбирался.</summary>
    public string LastPortName { get; init; } = string.Empty;

    /// <summary>Настроено ли рабочее место настолько, чтобы запускать прогон.</summary>
    public bool IsComplete =>
        JigAzimuthDeg is >= 0 and <= 360 && !string.IsNullOrWhiteSpace(DefaultOperator);

    /// <summary>Чего именно не хватает — готовая строка для панели готовности.</summary>
    public IReadOnlyList<string> Problems
    {
        get
        {
            var problems = new List<string>(2);
            if (JigAzimuthDeg is null)
            {
                problems.Add("не задан истинный азимут стапеля");
            }
            else if (JigAzimuthDeg is < 0 or > 360 || double.IsNaN(JigAzimuthDeg.Value))
            {
                problems.Add("азимут стапеля вне диапазона 0…360");
            }

            if (string.IsNullOrWhiteSpace(DefaultOperator))
            {
                problems.Add("не указан оператор");
            }

            return problems;
        }
    }
}
