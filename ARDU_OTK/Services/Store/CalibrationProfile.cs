using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ARDU_OTK.Services.Fc;

namespace ARDU_OTK.Services.Store;

/// <summary>
/// Профиль изделия: всё, что постоянно для модели борта и потому не должно
/// вводиться заново на каждый прогон.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 Профиль хранит <b>содержимое</b> эталона (<see cref="ReferenceText"/>), а не
/// путь к нему. Путь остаётся только как свидетельство происхождения. Причина
/// в том, что файл на сетевом ресурсе можно удалить, переименовать или
/// переписать, и профиль, ссылающийся на путь, ломается именно тогда, когда по
/// нему разбирают рекламацию. Хранимый текст делает профиль самодостаточным:
/// эталон, которым сдавали плату, воспроизводим спустя годы.
/// </para>
/// <para>
/// Азимут стапеля в профиль <b>не входит</b>: это свойство рабочего места, а не
/// изделия. Он живёт в <see cref="WorkstationSettings"/>.
/// </para>
/// <para>
/// Ожидаемый состав компасов здесь не хранится и не дублируется: он полностью
/// выводится из <see cref="ReferenceText"/> методом <see cref="ReadExpectedSlots"/>.
/// Второй экземпляр той же истины рано или поздно разойдётся с первым.
/// </para>
/// </remarks>
/// <param name="Id">Ключ в реестре.</param>
/// <param name="Name">Имя, как его называет предприятие.</param>
/// <param name="Description">Пояснение для оператора; может быть пустым.</param>
/// <param name="ReferenceFileName">Имя исходного файла эталона — только для протокола.</param>
/// <param name="ReferenceFormat">Формат исходника: <c>MissionPlanner</c> либо <c>QGroundControl</c>.</param>
/// <param name="ReferenceText">Содержимое эталона целиком, с переводами строк <c>\n</c>.</param>
/// <param name="ReferenceHash">Канонический SHA-256 набора — см. <see cref="ReferenceParamFile.ComputeHash(ReferenceParamSet)"/>.</param>
/// <param name="ParamCount">Сколько параметров разобралось на момент заведения.</param>
/// <param name="HeadingVsJigDeg">Допуск курса против азимута стапеля, градусы.</param>
/// <param name="InterCompassSpreadDeg">Допуск расхождения курсов между компасами, градусы.</param>
/// <param name="TransferMotorComp">Переносить ли <c>COMPASS_MOT*</c> — см. <see cref="ReferenceParamFile.FindMissingTransferable"/>.</param>
/// <param name="CreatedBy">Кто завёл профиль.</param>
/// <param name="CreatedUtc">Когда заведён.</param>
/// <param name="RetiredUtc">Когда выведен из обращения; <c>null</c> — действующий.</param>
/// <param name="RunCount">Сколько прогонов сдано по этому профилю.</param>
public sealed record CalibrationProfile(
    long Id,
    string Name,
    string Description,
    string ReferenceFileName,
    string ReferenceFormat,
    string ReferenceText,
    string ReferenceHash,
    int ParamCount,
    double HeadingVsJigDeg,
    double InterCompassSpreadDeg,
    bool TransferMotorComp,
    string CreatedBy,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? RetiredUtc,
    int RunCount)
{
    /// <summary>Профиль выведен из обращения: выбрать его для нового прогона нельзя.</summary>
    public bool IsRetired => RetiredUtc.HasValue;

    /// <summary>По этому профилю уже сдавали платы — менять допуски нельзя.</summary>
    public bool HasRuns => RunCount > 0;

    /// <summary>Разбирает хранимый эталон.</summary>
    /// <remarks>
    /// Текст разбирается заново на каждый вызов — набор мал (десятки строк), а
    /// кэш означал бы третий экземпляр той же истины.
    /// </remarks>
    /// <exception cref="InvalidDataException">Хранимый текст не разбирается — база правилась снаружи.</exception>
    public ReferenceParamSet ParseReference() =>
        ReferenceParamFile.Parse(ReferenceFileName, ProfileReference.SplitLines(ReferenceText));

    /// <summary>Ожидаемый состав компасов, выведенный из эталона.</summary>
    public IReadOnlyList<ExpectedCompassSlot> ReadExpectedSlots() =>
        CompassIdentity.ReadExpectedSlots(ParseReference());

    /// <summary>Допуски процедуры, как их задаёт этот профиль.</summary>
    /// <remarks>
    /// Окна наблюдения (<see cref="CalibrationTolerances.TelemetryWindow"/> и
    /// <see cref="CalibrationTolerances.PrearmWindow"/>) профилем не управляются:
    /// это свойства канала связи, а не изделия.
    /// </remarks>
    public CalibrationTolerances ToTolerances() => new()
    {
        HeadingVsJigDeg = HeadingVsJigDeg,
        InterCompassSpreadDeg = InterCompassSpreadDeg,
    };

    /// <summary>Короткая подпись для списка: имя и первые разряды хеша эталона.</summary>
    public string ShortCaption => string.Create(
        CultureInfo.InvariantCulture,
        $"{Name} · эталон {ProfileReference.ShortHash(ReferenceHash)}");
}

/// <summary>
/// Заготовка профиля: то, что оператор задал в мастере, до записи в реестр.
/// </summary>
/// <remarks>
/// Тип существует затем, чтобы невалидный профиль нельзя было составить: поля
/// эталона заполняются только из <see cref="ProfileReference.Load"/>, который
/// сначала разбирает файл и падает на негодном.
/// </remarks>
public sealed record NewCalibrationProfile(
    string Name,
    string Description,
    ProfileReference Reference,
    double HeadingVsJigDeg,
    double InterCompassSpreadDeg,
    bool TransferMotorComp,
    string CreatedBy);

/// <summary>
/// Разобранный и проверенный эталон, готовый лечь в профиль.
/// </summary>
/// <param name="FileName">Имя исходного файла — для протокола.</param>
/// <param name="SourcePath">Полный путь, откуда прочитан. Хранению не подлежит.</param>
/// <param name="Format">Формат исходника.</param>
/// <param name="Text">Содержимое с нормализованными переводами строк.</param>
/// <param name="Hash">Канонический хеш набора.</param>
/// <param name="Set">Разобранный набор параметров.</param>
/// <param name="ExpectedSlots">Ожидаемый состав компасов, слоты 1..3.</param>
/// <param name="PopulatedSlots">
/// Слоты, для которых эталон обязан нести переносимый блок, — см.
/// <see cref="Load"/>. Слот, объявленный пустым, сюда не входит.
/// </param>
/// <param name="MissingCore">
/// Переносимые параметры калибровки, которых в эталоне нет (без <c>COMPASS_MOT*</c>).
/// Непустой список — не отказ сам по себе, но оператор обязан его увидеть.
/// </param>
/// <param name="MissingMotorComp">Отсутствующие <c>COMPASS_MOT*</c>.</param>
public sealed record ProfileReference(
    string FileName,
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

    /// <summary>Слоты, о которых эталон вообще что-то говорит.</summary>
    public IReadOnlyList<int> DescribedSlots => ReferenceParamFile.DetectSlots(Set);

    /// <summary>
    /// Читает эталонный файл и проверяет его пригодность для переноса калибровки.
    /// </summary>
    /// <remarks>
    /// 🔴 Единственная точка, где эталон попадает в приложение. Проверка идёт
    /// здесь, при заведении профиля, а не внутри уже запущенной процедуры:
    /// негодный эталон обязан остановить оператора до того, как плата встала на
    /// стапель, а не на четвёртой стадии из девяти.
    /// </remarks>
    /// <exception cref="FileNotFoundException">Файла нет.</exception>
    /// <exception cref="InvalidDataException">
    /// Файл не разбирается, разобрался в ноль параметров либо не содержит ни
    /// одного <c>COMPASS_*</c> — переносить из него нечего.
    /// </exception>
    public static ProfileReference Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Эталонный файл параметров не найден: {path}", path);
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

        var compassCount = set.Values.Keys.Count(
            static k => k.StartsWith(ReferenceParamFile.CompassPrefix, StringComparison.Ordinal));
        if (compassCount == 0)
        {
            throw new InvalidDataException(
                $"В эталоне «{path}» нет ни одного параметра {ReferenceParamFile.CompassPrefix}* — "
                + "перенос калибровки компаса по нему невозможен.");
        }

        var expectedSlots = CompassIdentity.ReadExpectedSlots(set);

        // 🔴 «Слот, о котором эталон что-то говорит» и «слот, для которого эталон
        // обязан нести калибровку» — разные множества, и путать их нельзя.
        // Строка COMPASS_DEV_ID3 = 0 означает «третьего компаса нет»: требовать
        // для него COMPASS_OFS3_* значит выдать десять ложных пропусков. Список
        // пропусков, где большинство строк — шум, оператор пролистывает не читая,
        // и настоящая недостача COMPASS_DIA_* проходит незамеченной.
        //
        // Слот с ненулевым DEV_ID блок обязан иметь. Слот, у которого DEV_ID в
        // эталоне вовсе отсутствует, но смещения есть, тоже проверяется: такой
        // эталон неполон, и оператор должен об этом узнать.
        var populatedSlots = ReferenceParamFile.DetectSlots(set)
            .Where(slot => expectedSlots[slot - CompassIdentity.MinSlot].IsPresent
                        || !set.Values.ContainsKey(CompassIdentity.DevIdName(slot)))
            .ToArray();

        var missingCore = ReferenceParamFile.FindMissingTransferable(
            set, populatedSlots, includeMotorCompensation: false);

        // COMPASS_MOT* считаем отдельно: их отсутствие — не дефект эталона, а
        // сообщение о том, что CompassMot на образце не выполнялся.
        var missingAll = ReferenceParamFile.FindMissingTransferable(
            set, populatedSlots, includeMotorCompensation: true);
        var missingMot = missingAll.Except(missingCore, StringComparer.Ordinal).ToArray();

        return new ProfileReference(
            FileName: Path.GetFileName(path),
            SourcePath: path,
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
    /// Разбивает хранимый текст эталона на строки.
    /// </summary>
    /// <remarks>
    /// 🔴 Хвостовой <c>\r</c> обязан быть срезан. Разборщик Mission Planner
    /// делит строку по пробелу, запятой и табуляции, и возврат каретки
    /// прилипает к числовому полю: <c>«12.5\r»</c> не разбирается как число, и
    /// весь эталон отвергается как битый.
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

    /// <summary>Профиль, выбранный в прошлый раз: стенд должен открываться там, где его закрыли.</summary>
    public long? LastProfileId { get; init; }

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
