using System;
using System.Collections.Generic;
using System.Globalization;

namespace ARDU_OTK.Services.Fc;

/// <summary>Нужна ли калибровка и почему.</summary>
/// <param name="Known">Есть ли данные для суждения. <c>false</c> — таблица параметров не прочитана.</param>
/// <param name="Required">Калибровка требуется.</param>
/// <param name="Detail">Чем именно вывод обоснован — для оператора и протокола.</param>
public readonly record struct CalibrationVerdict(bool Known, bool Required, string Detail)
{
    public static CalibrationVerdict Unknown(string detail) => new(false, false, detail);

    public static CalibrationVerdict Needed(string detail) => new(true, true, detail);

    public static CalibrationVerdict Done(string detail) => new(true, false, detail);
}

/// <summary>
/// Вывод о том, откалиброваны ли акселерометры и компасы.
/// </summary>
/// <remarks>
/// 🔴 Признаки взяты те же, по которым судит сама прошивка при предполётной
/// проверке: нулевые смещения — это «не калибровали», а масштаб акселерометра
/// вне разумных пределов — «калибровали неудачно». Придумывать собственный
/// критерий нельзя: стенд обязан говорить о борте то же, что скажет борт, иначе
/// оператор получает зелёную панель и отказ взвода одновременно.
///
/// Судим по уже прочитанной таблице параметров, а не отдельным опросом: полная
/// таблица снимается сверкой на каждом подключении, и второй проход по тысяче
/// имён ради двух панелей — это лишние секунды занятого канала.
/// </remarks>
public static class CalibrationStatus
{
    /// <summary>
    /// Пределы масштаба акселерометра. Значение вне их означает неудачную
    /// калибровку, а не другую настройку.
    /// </summary>
    private const double MinScale = 0.8;

    private const double MaxScale = 1.2;

    /// <summary>Префиксы экземпляров: <c>INS_ACC</c>, <c>INS_ACC2</c>, <c>INS_ACC3</c>.</summary>
    private static readonly string[] AccelPrefixes = ["INS_ACC", "INS_ACC2", "INS_ACC3"];

    /// <summary>
    /// Идентификаторы акселерометров. Ненулевой — датчик на плате есть.
    /// </summary>
    /// <remarks>
    /// 🔴 Наличие параметра не означает наличия датчика. Прошивка объявляет
    /// смещения всех трёх экземпляров независимо от того, сколько их на плате,
    /// и у несуществующего они законно нулевые. Судить по нулям без проверки
    /// идентификатора — значит требовать калибровку датчика, которого нет:
    /// именно так исправная плата с двумя акселерометрами получала красную
    /// панель, которую нечем погасить.
    /// </remarks>
    private static readonly string[] AccelIdNames = ["INS_ACC_ID", "INS_ACC2_ID", "INS_ACC3_ID"];

    /// <summary>Признак «экземпляр применяется прошивкой».</summary>
    private static readonly string[] AccelUseNames = ["INS_USE", "INS_USE2", "INS_USE3"];

    /// <summary>Компасные смещения по слотам.</summary>
    private static readonly string[] CompassOffsetPrefixes = ["COMPASS_OFS", "COMPASS_OFS2", "COMPASS_OFS3"];

    private static readonly string[] CompassUseNames = ["COMPASS_USE", "COMPASS_USE2", "COMPASS_USE3"];

    private static readonly string[] CompassDevIdNames = ["COMPASS_DEV_ID", "COMPASS_DEV_ID2", "COMPASS_DEV_ID3"];

    /// <summary>
    /// Требуется ли калибровка акселерометров.
    /// </summary>
    /// <remarks>
    /// Проверяются все экземпляры, у которых есть идентификатор: борт не
    /// взлетит из-за любого некалиброванного, а не только первого.
    /// </remarks>
    public static CalibrationVerdict Imu(IReadOnlyDictionary<string, double>? values)
    {
        if (values is null || values.Count == 0)
        {
            return CalibrationVerdict.Unknown("таблица параметров не прочитана");
        }

        var problems = new List<string>();
        var checkedInstances = 0;

        for (int i = 0; i < AccelPrefixes.Length; i++)
        {
            string prefix = AccelPrefixes[i];

            // Датчика нет — судить не о чем. Нулевые смещения отсутствующего
            // экземпляра не являются отказом.
            if (!values.TryGetValue(AccelIdNames[i], out double id) || id == 0)
            {
                continue;
            }

            // Выключенный экземпляр прошивка не читает, и его калибровка ни на
            // что не влияет.
            if (values.TryGetValue(AccelUseNames[i], out double use) && use == 0)
            {
                continue;
            }

            if (!TryVector(values, prefix + "OFFS", out double ox, out double oy, out double oz))
            {
                problems.Add($"акселерометр {i + 1}: смещений нет в таблице");
                continue;
            }

            checkedInstances++;

            if (ox == 0 && oy == 0 && oz == 0)
            {
                problems.Add($"акселерометр {i + 1}: смещения нулевые");
                continue;
            }

            if (!TryVector(values, prefix + "SCAL", out double sx, out double sy, out double sz))
            {
                problems.Add($"акселерометр {i + 1}: масштаб не задан");
                continue;
            }

            if (OutOfRange(sx) || OutOfRange(sy) || OutOfRange(sz))
            {
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"акселерометр {i + 1}: масштаб вне пределов ({sx:0.###}, {sy:0.###}, {sz:0.###})"));
            }
        }

        if (checkedInstances == 0 && problems.Count == 0)
        {
            return CalibrationVerdict.Unknown("применяемых акселерометров не найдено");
        }

        return problems.Count == 0
            ? CalibrationVerdict.Done($"проверено экземпляров: {checkedInstances}, все откалиброваны")
            : CalibrationVerdict.Needed(string.Join("; ", problems));
    }

    /// <summary>
    /// Требуется ли калибровка компасов.
    /// </summary>
    /// <remarks>
    /// Проверяются только применяемые компасы: выключенный <c>COMPASS_USE</c>
    /// означает, что прошивка этот датчик не читает, и его смещения ни на что
    /// не влияют.
    /// </remarks>
    public static CalibrationVerdict Mag(IReadOnlyDictionary<string, double>? values)
    {
        if (values is null || values.Count == 0)
        {
            return CalibrationVerdict.Unknown("таблица параметров не прочитана");
        }

        var problems = new List<string>();
        var checkedSlots = 0;

        for (int i = 0; i < CompassOffsetPrefixes.Length; i++)
        {
            if (!values.TryGetValue(CompassDevIdNames[i], out double devId) || devId == 0)
            {
                continue;
            }

            if (values.TryGetValue(CompassUseNames[i], out double use) && use == 0)
            {
                continue;
            }

            if (!TryVector(values, CompassOffsetPrefixes[i], out double x, out double y, out double z))
            {
                problems.Add($"компас {i + 1}: смещений нет в таблице");
                continue;
            }

            checkedSlots++;

            if (x == 0 && y == 0 && z == 0)
            {
                problems.Add($"компас {i + 1}: смещения нулевые");
            }
        }

        if (checkedSlots == 0 && problems.Count == 0)
        {
            return CalibrationVerdict.Unknown("применяемых компасов не найдено");
        }

        return problems.Count == 0
            ? CalibrationVerdict.Done($"проверено компасов: {checkedSlots}, все откалиброваны")
            : CalibrationVerdict.Needed(string.Join("; ", problems));
    }

    private static bool OutOfRange(double scale) => scale < MinScale || scale > MaxScale;

    private static bool TryVector(
        IReadOnlyDictionary<string, double> values,
        string prefix,
        out double x,
        out double y,
        out double z)
    {
        x = y = z = 0;

        return values.TryGetValue(prefix + "_X", out x)
            && values.TryGetValue(prefix + "_Y", out y)
            && values.TryGetValue(prefix + "_Z", out z);
    }
}
