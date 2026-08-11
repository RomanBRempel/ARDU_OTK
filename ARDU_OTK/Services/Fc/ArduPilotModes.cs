using System.Collections.Generic;

namespace ARDU_OTK.Services.Fc;

/// <summary>
/// Расшифровка полётного режима из <c>HEARTBEAT</c>.
/// </summary>
/// <remarks>
/// У ArduPilot <c>custom_mode</c> — обычное число, а не битовое поле, но набор
/// значений свой для каждого типа аппарата, поэтому таблица выбирается по
/// <c>HEARTBEAT.type</c>. Расшифровывать так можно только когда
/// <c>autopilot == 3</c> (<c>MAV_AUTOPILOT_ARDUPILOTMEGA</c>): PX4 упаковывает в
/// то же поле основной и вспомогательный режим.
///
/// В нумерации есть пропуски (у Copter это 8, 10 и 12), поэтому таблицы —
/// словари, а не массивы с доступом по индексу.
/// </remarks>
public static class ArduPilotModes
{
    private static readonly Dictionary<uint, string> Copter = new()
    {
        [0] = "STABILIZE", [1] = "ACRO", [2] = "ALT_HOLD", [3] = "AUTO", [4] = "GUIDED",
        [5] = "LOITER", [6] = "RTL", [7] = "CIRCLE", [9] = "LAND", [11] = "DRIFT",
        [13] = "SPORT", [14] = "FLIP", [15] = "AUTOTUNE", [16] = "POSHOLD", [17] = "BRAKE",
        [18] = "THROW", [19] = "AVOID_ADSB", [20] = "GUIDED_NOGPS", [21] = "SMART_RTL",
        [22] = "FLOWHOLD", [23] = "FOLLOW", [24] = "ZIGZAG", [25] = "SYSTEMID",
        [26] = "AUTOROTATE", [27] = "AUTO_RTL", [28] = "TURTLE",
    };

    private static readonly Dictionary<uint, string> Plane = new()
    {
        [0] = "MANUAL", [1] = "CIRCLE", [2] = "STABILIZE", [3] = "TRAINING", [4] = "ACRO",
        [5] = "FBWA", [6] = "FBWB", [7] = "CRUISE", [8] = "AUTOTUNE", [10] = "AUTO",
        [11] = "RTL", [12] = "LOITER", [13] = "TAKEOFF", [14] = "AVOID_ADSB", [15] = "GUIDED",
        [16] = "INITIALIZING", [17] = "QSTABILIZE", [18] = "QHOVER", [19] = "QLOITER",
        [20] = "QLAND", [21] = "QRTL", [22] = "QAUTOTUNE", [23] = "QACRO", [24] = "THERMAL",
        [25] = "LOITER_ALT_QLAND", [26] = "AUTOLAND",
    };

    private static readonly Dictionary<uint, string> Rover = new()
    {
        [0] = "MANUAL", [1] = "ACRO", [3] = "STEERING", [4] = "HOLD", [5] = "LOITER",
        [6] = "FOLLOW", [7] = "SIMPLE", [8] = "DOCK", [9] = "CIRCLE", [10] = "AUTO",
        [11] = "RTL", [12] = "SMART_RTL", [15] = "GUIDED", [16] = "INITIALIZING",
    };

    /// <summary>Название режима либо номер, если тип аппарата не опознан.</summary>
    public static string Describe(byte vehicleType, uint customMode)
    {
        var table = TableFor(vehicleType);
        if (table is null)
        {
            // Тип неизвестен — показываем номер, а не режим Copter по умолчанию:
            // ошибиться в названии режима хуже, чем признать неопределённость.
            return $"режим {customMode}";
        }

        return table.TryGetValue(customMode, out var name) ? name : $"режим {customMode}";
    }

    /// <summary>Понятное название типа аппарата.</summary>
    public static string DescribeVehicle(byte vehicleType) => vehicleType switch
    {
        1 => "самолёт",
        2 => "квадрокоптер",
        3 => "соосный",
        4 => "вертолёт",
        5 => "антенный трекер",
        10 or 11 => "наземный аппарат",
        13 => "гексакоптер",
        14 => "октокоптер",
        15 => "трикоптер",
        29 => "додекакоптер",
        35 => "декакоптер",
        43 => "мультиротор",
        19 or 20 or 21 or 22 or 23 or 24 => "конвертоплан",
        _ => $"тип {vehicleType}",
    };

    private static Dictionary<uint, string>? TableFor(byte vehicleType) => vehicleType switch
    {
        2 or 3 or 4 or 13 or 14 or 15 or 29 or 35 or 43 => Copter,
        1 or 19 or 20 or 21 or 22 or 23 or 24 => Plane,
        10 or 11 => Rover,
        _ => null,
    };
}
