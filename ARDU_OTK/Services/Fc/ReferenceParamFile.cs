using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ARDU_OTK.Services.Fc;

/// <summary>
/// Чтение и сверка эталонного файла параметров.
/// </summary>
/// <remarks>
/// Класс работает только с текстом и числами: ни диалогов, ни MAVLink, ни UI.
/// Дисковый ввод сведён к одному методу <see cref="Load"/>, остальное
/// принимает готовые строки — так весь разбор покрывается тестами без файлов.
/// </remarks>
public static class ReferenceParamFile
{
    /// <summary>Формат Mission Planner (<c>.param</c>/<c>.parm</c>). Значение попадает в <see cref="ReferenceParamSet.Format"/>.</summary>
    public const string MissionPlannerFormat = "MissionPlanner";

    /// <summary>Формат QGroundControl (<c>.params</c>). Значение попадает в <see cref="ReferenceParamSet.Format"/>.</summary>
    public const string QGroundControlFormat = "QGroundControl";

    /// <summary>Префикс параметров компаса. Эталон этой процедуры — только компасный.</summary>
    public const string CompassPrefix = "COMPASS_";

    /// <summary>
    /// Относительный допуск для <c>REAL32</c>. Провод — IEEE-754 binary32,
    /// это примерно 7,2 значащих десятичных знака.
    /// </summary>
    public const float DefaultRelativeEpsilon = 1e-6f;

    /// <summary>Абсолютный порог: без него сравнение около нуля никогда не сходится.</summary>
    public const float DefaultAbsoluteFloor = 1e-9f;

    /// <summary>Максимальная длина имени параметра: поле <c>param_id</c> в MAVLink — <c>char[16]</c>.</summary>
    private const int MaxParamNameLength = 16;

    private static readonly char[] MissionPlannerSeparators = { ' ', ',', '\t' };

    /// <summary>
    /// Читает эталонный файл, сам определив формат.
    /// </summary>
    /// <exception cref="ArgumentException">Путь пуст.</exception>
    /// <exception cref="FileNotFoundException">Файла нет.</exception>
    /// <exception cref="InvalidDataException">Файл разобрался в ноль параметров либо содержит битую строку.</exception>
    public static ReferenceParamSet Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Эталонный файл параметров не найден: {path}", path);
        }

        // ReadAllLines сам распознаёт BOM; файлы обоих ГНС пишутся в UTF-8/ASCII.
        string[] lines = File.ReadAllLines(path);
        return Parse(path, lines);
    }

    /// <summary>
    /// Разбирает уже прочитанные строки. Отдельный метод нужен, чтобы разбор
    /// проверялся тестами без файловой системы.
    /// </summary>
    /// <exception cref="InvalidDataException">Битая строка либо ноль параметров на выходе.</exception>
    public static ReferenceParamSet Parse(string path, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        string format = DetectFormat(lines);
        var values = new Dictionary<string, double>(StringComparer.Ordinal);

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (IsSkippable(line))
            {
                continue;
            }

            if (!TryParseLine(line, format, out string name, out string token))
            {
                // Строка не похожа на параметр вообще (мусор, разделитель,
                // хвост экспорта). Это не повод падать: пустой результат всё
                // равно будет отвергнут ниже.
                continue;
            }

            if (!TryParseValue(token, out double value))
            {
                throw new InvalidDataException(
                    $"Файл {path} (формат {format}), строка {i + 1}: значение параметра {name} " +
                    $"не разбирается как число в инвариантной культуре: «{token}».");
            }

            // Дубликат имени: побеждает последнее вхождение — так же ведут себя
            // оба ГНС при импорте.
            values[name] = value;
        }

        if (values.Count == 0)
        {
            throw new InvalidDataException(
                $"Файл {path} (формат {format}) не содержит ни одного параметра. " +
                "Либо файл пуст, либо его формат определён неверно.");
        }

        return new ReferenceParamSet(path, format, values);
    }

    /// <summary>
    /// Определяет формат файла до разбора.
    /// </summary>
    /// <remarks>
    /// 🔴 Проверка обязательна. Свободный разделитель Mission Planner (пробел,
    /// запятая или табуляция) применённый к файлу QGC даёт из строки
    /// <c>1\t1\tCOMPASS_OFS_X\t12\t9</c> пару <c>имя="1", значение=1</c> —
    /// и так для каждой строки. Ошибка молчаливая: на выходе получается
    /// «набор параметров» из одного мусорного имени, а весь эталон исчезает.
    /// Признак QGC: первая некомментарная строка из пяти полей, разделённых
    /// табуляцией, у которой третье поле — допустимое имя параметра.
    /// </remarks>
    public static string DetectFormat(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        foreach (string line in lines)
        {
            if (IsSkippable(line))
            {
                continue;
            }

            string[] tabFields = line.Split('\t');
            if (tabFields.Length == 5 && IsValidParamName(tabFields[2].Trim()))
            {
                return QGroundControlFormat;
            }

            // Первая содержательная строка решает: смешанных файлов не бывает.
            return MissionPlannerFormat;
        }

        return MissionPlannerFormat;
    }

    /// <summary>
    /// Только параметры компаса. Эталон этой процедуры компасный, и остальные
    /// имена в него попадать не должны — но если попали, дальше их нести незачем.
    /// </summary>
    public static IReadOnlyDictionary<string, double> CompassOnly(ReferenceParamSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pair in set.Values)
        {
            if (pair.Key.StartsWith(CompassPrefix, StringComparison.Ordinal))
            {
                result[pair.Key] = pair.Value;
            }
        }

        return result;
    }

    /// <summary>
    /// Слоты, о которых эталон вообще что-то говорит: есть <c>COMPASS_DEV_IDx</c>
    /// либо хотя бы одно смещение.
    /// </summary>
    public static IReadOnlyList<int> DetectSlots(ReferenceParamSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        var slots = new List<int>(CompassIdentity.MaxSlot);
        for (int slot = CompassIdentity.MinSlot; slot <= CompassIdentity.MaxSlot; slot++)
        {
            bool hasDevId = set.Values.ContainsKey(CompassIdentity.DevIdName(slot));
            bool hasOffsets =
                set.Values.ContainsKey(CompassIdentity.OffsetName(slot, MagAxis.X)) ||
                set.Values.ContainsKey(CompassIdentity.OffsetName(slot, MagAxis.Y)) ||
                set.Values.ContainsKey(CompassIdentity.OffsetName(slot, MagAxis.Z));

            if (hasDevId || hasOffsets)
            {
                slots.Add(slot);
            }
        }

        return slots;
    }

    /// <summary>
    /// Сообщает, каких переносимых параметров в эталоне нет.
    /// </summary>
    /// <param name="set">Разобранный эталон.</param>
    /// <param name="slots">Слоты, которые проверяем.</param>
    /// <param name="includeMotorCompensation">
    /// Включать ли <c>COMPASS_MOT*</c>. По умолчанию нет: это результат
    /// CompassMot, он зависит от силовой проводки изделия и в эталон попадает
    /// не всегда — его отсутствие само по себе не дефект файла.
    /// </param>
    /// <remarks>
    /// Эталон без <c>COMPASS_DIA*</c> — это другой эталон, а не «почти такой
    /// же»: перенести мягкое железо из него нельзя, и оператор обязан об этом
    /// узнать до начала процедуры, а не по результату приёмки.
    /// </remarks>
    public static IReadOnlyList<string> FindMissingTransferable(
        ReferenceParamSet set,
        IReadOnlyList<int> slots,
        bool includeMotorCompensation = false)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(slots);

        var missing = new List<string>();
        foreach (int slot in slots)
        {
            IReadOnlyList<string> expected = includeMotorCompensation
                ? CompassIdentity.TransferableCalibrationNames(slot)
                : CompassIdentity.CoreCalibrationNames(slot);

            foreach (string name in expected)
            {
                if (!set.Values.ContainsKey(name))
                {
                    missing.Add(name);
                }
            }
        }

        return missing;
    }

    /// <summary>
    /// SHA-256 по канонической форме набора: строки <c>ИМЯ=ЗНАЧЕНИЕ</c>,
    /// отсортированные по имени, значения — в инвариантном виде.
    /// </summary>
    /// <returns>Хеш строчными шестнадцатеричными цифрами.</returns>
    /// <remarks>
    /// Канонизация нужна, чтобы один и тот же логический набор давал один и тот
    /// же хеш независимо от порядка строк, разделителя и формата файла: иначе
    /// пересохранение эталона из другого ГНС выглядело бы как подмена эталона.
    /// Значения приводятся к <c>float</c> — к той же решётке binary32, в
    /// которой борт эти числа и хранит.
    /// </remarks>
    public static string ComputeHash(ReferenceParamSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return ComputeHash(set.Values);
    }

    /// <inheritdoc cref="ComputeHash(ReferenceParamSet)"/>
    public static string ComputeHash(IReadOnlyDictionary<string, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var builder = new StringBuilder();
        foreach (var pair in values.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(FormatCanonical(pair.Value));
            builder.Append('\n');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Каноническое представление значения: кратчайшая инвариантная запись
    /// <c>float</c> с нормализованным нулём.
    /// </summary>
    public static string FormatCanonical(double value) =>
        ToBoardFloat(value).ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// Разбирает числовой токен ровно так, как требует сверка: как
    /// <c>float</c> и в инвариантной культуре.
    /// </summary>
    /// <remarks>
    /// Инвариантная культура обязательна: на машине с десятичной запятой
    /// разбор «по умолчанию» превратил бы <c>1.0</c> в <c>10</c> или отказал.
    /// <c>float</c>, а не <c>double</c>, обязателен: значение с борта живёт в
    /// binary32, и разбор в double даёт число, которого в этой решётке нет,
    /// после чего сравнение расходится на последних разрядах.
    /// </remarks>
    public static bool TryParseFloat(string token, out float value)
    {
        if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            value = NormalizeZero(value);
            return true;
        }

        value = 0f;
        return false;
    }

    /// <summary>
    /// Приводит значение из эталона к тому <c>float</c>, который увидит борт.
    /// Преобразование точное: набор читается через <see cref="TryParseFloat"/>,
    /// а расширение <c>float</c> до <c>double</c> и обратно потерь не даёт.
    /// </summary>
    public static float ToBoardFloat(double value) => NormalizeZero((float)value);

    /// <summary>
    /// Сравнивает значение из эталона со значением, которое отдал борт.
    /// </summary>
    /// <remarks>
    /// Тип берётся из <c>PARAM_VALUE.param_type</c>, а не угадывается по виду
    /// числа: прошивка присылает <c>INT8/INT16/INT32/REAL32</c> и для целых
    /// кладёт значение в поле <c>float</c> обычным приведением.
    /// Форматированные строки не сравниваются ни при каких условиях.
    /// </remarks>
    public static bool ValuesEqual(double referenceValue, ParamValue boardValue) =>
        ValuesEqual(ToBoardFloat(referenceValue), boardValue.Value, boardValue.Type);

    /// <inheritdoc cref="ValuesEqual(double, ParamValue)"/>
    public static bool ValuesEqual(float reference, float actual, MavParamType type) =>
        ValuesEqual(reference, actual, type, DefaultRelativeEpsilon, DefaultAbsoluteFloor);

    /// <summary>
    /// Сравнение с явными допусками.
    /// </summary>
    /// <remarks>
    /// Целые сравниваются точно, без эпсилон: <c>(int)MathF.Round(v)</c> с
    /// обеих сторон. Для них любой допуск — это разрешение перепутать соседние
    /// значения перечисления. <c>REAL32</c> сравнивается по относительному
    /// допуску с абсолютным порогом.
    /// <c>NaN</c> не равен ничему, включая себя: параметр, пришедший
    /// как <c>NaN</c>, — это отказ, а не совпадение.
    /// </remarks>
    public static bool ValuesEqual(
        float reference,
        float actual,
        MavParamType type,
        float relativeEpsilon,
        float absoluteFloor)
    {
        reference = NormalizeZero(reference);
        actual = NormalizeZero(actual);

        if (type is MavParamType.Int8 or MavParamType.Int16 or MavParamType.Int32)
        {
            if (float.IsNaN(reference) || float.IsNaN(actual)
                || float.IsInfinity(reference) || float.IsInfinity(actual))
            {
                return false;
            }

            return (int)MathF.Round(reference) == (int)MathF.Round(actual);
        }

        if (reference == actual)
        {
            return true;
        }

        if (float.IsNaN(reference) || float.IsNaN(actual)
            || float.IsInfinity(reference) || float.IsInfinity(actual))
        {
            return false;
        }

        float tolerance = MathF.Max(
            relativeEpsilon * MathF.Max(MathF.Abs(reference), MathF.Abs(actual)),
            absoluteFloor);

        return MathF.Abs(reference - actual) <= tolerance;
    }

    /// <summary>Допустимо ли имя как имя параметра MAVLink. Используется детектором формата.</summary>
    public static bool IsValidParamName(string candidate)
    {
        if (string.IsNullOrEmpty(candidate) || candidate.Length > MaxParamNameLength)
        {
            return false;
        }

        if (!char.IsAsciiLetter(candidate[0]))
        {
            return false;
        }

        foreach (char c in candidate)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSkippable(string line)
    {
        string trimmed = line.Trim();
        return trimmed.Length == 0 || trimmed[0] == '#';
    }

    private static bool TryParseLine(string line, string format, out string name, out string token)
    {
        name = string.Empty;
        token = string.Empty;

        if (format == QGroundControlFormat)
        {
            // Ровно табуляция и ровно пять колонок:
            // Vehicle-Id, Component-Id, Name, Value, Type.
            string[] fields = line.Split('\t');
            if (fields.Length < 5)
            {
                return false;
            }

            name = fields[2].Trim();
            token = fields[3].Trim();
        }
        else
        {
            // Mission Planner пишет через запятую, но его же читалка
            // допускает пробел и табуляцию; берутся только поля [0] и [1].
            string[] fields = line.Split(MissionPlannerSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
            {
                return false;
            }

            name = fields[0].Trim();
            token = fields[1].Trim();
        }

        return IsValidParamName(name) && token.Length > 0;
    }

    private static bool TryParseValue(string token, out double value)
    {
        // Через float — чтобы значение сразу легло в решётку binary32.
        if (TryParseFloat(token, out float parsed))
        {
            value = parsed;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>Нормализация минус-нуля: <c>-0f</c> и <c>0f</c> — одно значение, но у них разные биты.</summary>
    private static float NormalizeZero(float value) => value == 0f ? 0f : value;
}
