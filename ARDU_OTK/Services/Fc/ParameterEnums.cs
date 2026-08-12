using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;

namespace ARDU_OTK.Services.Fc;

/// <summary>
/// Расшифровка перечислимых значений параметров — так же, как их показывает
/// Mission Planner.
/// </summary>
/// <remarks>
/// 🔴 Число само по себе оператору ничего не сообщает: «70» в
/// <c>SERVO3_FUNCTION</c> не читается как «газ», и сверять по такому списку
/// сборку глазами нельзя — можно только сличать столбики цифр, а это ровно то,
/// ради чего заведён эталон. Таблицы взяты из метаданных самой прошивки
/// (<c>@Values</c> в исходниках ArduPilot), а не составлены по памяти:
/// придуманное имя опаснее числа, потому что выглядит осмысленно и не вызывает
/// сомнений.
///
/// Значение, которого в таблице нет, остаётся числом: прошивка на борту может
/// быть новее таблицы, и подставлять вместо неизвестного имени ближайшее
/// известное нельзя — это выглядело бы как достоверная расшифровка.
/// </remarks>
public static class ParameterEnums
{
    // SERVOn_FUNCTION — SRV_Channel.cpp, @Values для Plane и общие.
    private static readonly FrozenDictionary<int, string> ServoFunctions = new Dictionary<int, string>
    {
        [-1] = "GPIO",
        [0] = "Disabled",
        [1] = "RCPassThru",
        [2] = "Flap",
        [3] = "FlapAuto",
        [4] = "Aileron",
        [6] = "Mount1Yaw",
        [7] = "Mount1Pitch",
        [8] = "Mount1Roll",
        [9] = "Mount1Retract",
        [10] = "CameraTrigger",
        [12] = "Mount2Yaw",
        [13] = "Mount2Pitch",
        [14] = "Mount2Roll",
        [15] = "Mount2Retract",
        [16] = "DifferentialSpoilerLeft1",
        [17] = "DifferentialSpoilerRight1",
        [19] = "Elevator",
        [21] = "Rudder",
        [22] = "SprayerPump",
        [23] = "SprayerSpinner",
        [24] = "FlaperonLeft",
        [25] = "FlaperonRight",
        [26] = "GroundSteering",
        [27] = "Parachute",
        [28] = "Gripper",
        [29] = "LandingGear",
        [30] = "EngineRunEnable",
        [31] = "HeliRSC",
        [32] = "HeliTailRSC",
        [33] = "Motor1",
        [34] = "Motor2",
        [35] = "Motor3",
        [36] = "Motor4",
        [37] = "Motor5",
        [38] = "Motor6",
        [39] = "Motor7",
        [40] = "Motor8",
        [41] = "TiltMotorsFront",
        [45] = "TiltMotorsRear",
        [46] = "TiltMotorRearLeft",
        [47] = "TiltMotorRearRight",
        [51] = "RCIN1",
        [52] = "RCIN2",
        [53] = "RCIN3",
        [54] = "RCIN4",
        [55] = "RCIN5",
        [56] = "RCIN6",
        [57] = "RCIN7",
        [58] = "RCIN8",
        [59] = "RCIN9",
        [60] = "RCIN10",
        [61] = "RCIN11",
        [62] = "RCIN12",
        [63] = "RCIN13",
        [64] = "RCIN14",
        [65] = "RCIN15",
        [66] = "RCIN16",
        [67] = "Ignition",
        [69] = "Starter",
        [70] = "Throttle",
        [71] = "TrackerYaw",
        [72] = "TrackerPitch",
        [73] = "ThrottleLeft",
        [74] = "ThrottleRight",
        [75] = "TiltMotorFrontLeft",
        [76] = "TiltMotorFrontRight",
        [77] = "ElevonLeft",
        [78] = "ElevonRight",
        [79] = "VTailLeft",
        [80] = "VTailRight",
        [81] = "BoostThrottle",
        [82] = "Motor9",
        [83] = "Motor10",
        [84] = "Motor11",
        [85] = "Motor12",
        [86] = "DifferentialSpoilerLeft2",
        [87] = "DifferentialSpoilerRight2",
        [88] = "Winch",
        [89] = "Main Sail",
        [90] = "CameraISO",
        [91] = "CameraAperture",
        [92] = "CameraFocus",
        [93] = "CameraShutterSpeed",
        [94] = "Script1",
        [95] = "Script2",
        [96] = "Script3",
        [97] = "Script4",
        [98] = "Script5",
        [99] = "Script6",
        [100] = "Script7",
        [101] = "Script8",
        [102] = "Script9",
        [103] = "Script10",
        [104] = "Script11",
        [105] = "Script12",
        [106] = "Script13",
        [107] = "Script14",
        [108] = "Script15",
        [109] = "Script16",
        [110] = "Airbrakes",
        [120] = "NeoPixel1",
        [121] = "NeoPixel2",
        [122] = "NeoPixel3",
        [123] = "NeoPixel4",
        [124] = "RateRoll",
        [125] = "RatePitch",
        [126] = "RateThrust",
        [127] = "RateYaw",
        [128] = "WingSailElevator",
        [129] = "ProfiLED1",
        [130] = "ProfiLED2",
        [131] = "ProfiLED3",
        [132] = "ProfiLEDClock",
        [133] = "Winch Clutch",
        [134] = "SERVOn_MIN",
        [135] = "SERVOn_TRIM",
        [136] = "SERVOn_MAX",
        [137] = "SailMastRotation",
        [138] = "Alarm",
        [139] = "Alarm Inverted",
        [140] = "RCIN1Scaled",
        [141] = "RCIN2Scaled",
        [142] = "RCIN3Scaled",
        [143] = "RCIN4Scaled",
        [144] = "RCIN5Scaled",
        [145] = "RCIN6Scaled",
        [146] = "RCIN7Scaled",
        [147] = "RCIN8Scaled",
        [148] = "RCIN9Scaled",
        [149] = "RCIN10Scaled",
        [150] = "RCIN11Scaled",
        [151] = "RCIN12Scaled",
        [152] = "RCIN13Scaled",
        [153] = "RCIN14Scaled",
        [154] = "RCIN15Scaled",
        [155] = "RCIN16Scaled",
        [160] = "Motor13",
        [161] = "Motor14",
        [162] = "Motor15",
        [163] = "Motor16",
        [164] = "Motor17",
        [165] = "Motor18",
        [166] = "Motor19",
        [167] = "Motor20",
        [168] = "Motor21",
        [169] = "Motor22",
        [170] = "Motor23",
        [171] = "Motor24",
        [172] = "Motor25",
        [173] = "Motor26",
        [174] = "Motor27",
        [175] = "Motor28",
        [176] = "Motor29",
        [177] = "Motor30",
        [178] = "Motor31",
        [179] = "Motor32",
        [180] = "CameraZoom",
    }.ToFrozenDictionary();

    // RCn_OPTION — RC_Channel.cpp, @Values всех аппаратов (номера не пересекаются).
    private static readonly FrozenDictionary<int, string> RcOptions = new Dictionary<int, string>
    {
        [0] = "Do Nothing",
        [2] = "FLIP Mode",
        [3] = "Simple Mode",
        [4] = "RTL",
        [5] = "Save Trim",
        [7] = "Save WP",
        [9] = "Camera Trigger",
        [10] = "RangeFinder Enable",
        [11] = "Fence Enable",
        [13] = "Super Simple Mode",
        [14] = "Acro Trainer",
        [15] = "Sprayer Enable",
        [16] = "AUTO Mode",
        [17] = "AUTOTUNE Mode",
        [18] = "LAND Mode",
        [19] = "Gripper",
        [21] = "Parachute Enable",
        [22] = "Parachute Release",
        [23] = "Parachute 3pos",
        [24] = "Auto Mission Reset",
        [25] = "AttCon Feed Forward",
        [26] = "AttCon Accel Limits",
        [27] = "Retract Mount1",
        [28] = "Relay1 On/Off",
        [29] = "Landing Gear",
        [30] = "Lost Copter Sound",
        [31] = "Motor Emergency Stop",
        [32] = "Motor Interlock",
        [33] = "BRAKE Mode",
        [34] = "Relay2 On/Off",
        [35] = "Relay3 On/Off",
        [36] = "Relay4 On/Off",
        [37] = "THROW Mode",
        [38] = "ADSB Avoidance Enable",
        [39] = "PrecLoiter Enable",
        [40] = "Proximity Avoidance Enable",
        [41] = "ArmDisarm (4.1 and lower)",
        [42] = "SMARTRTL Mode",
        [43] = "InvertedFlight Enable",
        [44] = "Winch Enable",
        [45] = "Winch Control",
        [46] = "RC Override Enable",
        [47] = "User Function 1",
        [48] = "User Function 2",
        [49] = "User Function 3",
        [50] = "LearnCruise Speed",
        [51] = "MANUAL Mode",
        [52] = "ACRO Mode",
        [53] = "STEERING Mode",
        [54] = "HOLD Mode",
        [55] = "GUIDED Mode",
        [56] = "LOITER Mode",
        [57] = "FOLLOW Mode",
        [58] = "Clear Waypoints",
        [59] = "Simple Mode",
        [60] = "ZigZag Mode",
        [61] = "ZigZag SaveWP",
        [62] = "Compass Learn",
        [63] = "Sailboat Tack",
        [64] = "Reverse Throttle",
        [65] = "GPS Disable",
        [66] = "Relay5 On/Off",
        [67] = "Relay6 On/Off",
        [68] = "STABILIZE Mode",
        [69] = "POSHOLD Mode",
        [70] = "ALTHOLD Mode",
        [71] = "FLOWHOLD Mode",
        [72] = "CIRCLE Mode",
        [73] = "DRIFT Mode",
        [74] = "Sailboat motoring 3pos",
        [75] = "SurfaceTrackingUpDown",
        [76] = "STANDBY Mode",
        [77] = "TAKEOFF Mode",
        [78] = "RunCam Control",
        [79] = "RunCam OSD Control",
        [80] = "VisOdom Align",
        [81] = "Disarm",
        [82] = "QAssist 3pos",
        [83] = "ZigZag Auto",
        [84] = "AirMode",
        [85] = "Generator",
        [86] = "Non Auto Terrain Follow Disable",
        [87] = "Crow Select",
        [88] = "Soaring Enable",
        [89] = "Landing Flare",
        [90] = "EKF Source Set",
        [91] = "Airspeed Ratio Calibration",
        [92] = "FBWA Mode",
        [94] = "VTX Power",
        [95] = "FBWA taildragger takeoff mode",
        [96] = "Trigger re-reading of mode switch",
        [97] = "Windvane home heading direction offset",
        [98] = "TRAINING Mode",
        [99] = "AUTO RTL",
        [100] = "KillIMU1",
        [101] = "KillIMU2",
        [102] = "Camera Mode Toggle",
        [103] = "EKF lane switch attempt",
        [104] = "EKF yaw reset",
        [105] = "GPS Disable Yaw",
        [106] = "Disable Airspeed Use",
        [107] = "Enable FW Autotune",
        [108] = "QRTL Mode",
        [109] = "use Custom Controller",
        [110] = "KillIMU3",
        [111] = "Loweheiser starter",
        [112] = "SwitchExternalAHRS",
        [113] = "Retract Mount2",
        [150] = "CRUISE Mode",
        [151] = "TURTLE Mode",
        [152] = "SIMPLE heading reset",
        [153] = "ArmDisarm (4.2 and higher)",
        [154] = "ArmDisarm with AirMode  (4.2 and higher)",
        [155] = "Set steering trim to current servo and RC",
        [156] = "Torqeedo Clear Err",
        [157] = "Force FS Action to FBWA",
        [158] = "Optflow Calibration",
        [159] = "Force IS_Flying",
        [160] = "Weathervane Enable",
        [161] = "Turbine Start(heli)",
        [162] = "FFT Tune",
        [163] = "Mount Yaw Lock",
        [164] = "Pause Stream Logging",
        [165] = "Arm/Emergency Motor Stop",
        [166] = "Camera Record Video",
        [167] = "Camera Zoom",
        [168] = "Camera Manual Focus",
        [169] = "Camera Auto Focus",
        [170] = "QSTABILIZE Mode",
        [171] = "Calibrate Compasses",
        [172] = "Battery MPPT Enable",
        [173] = "Plane AUTO Mode Landing Abort",
        [174] = "Camera Image Tracking",
        [175] = "Camera Lens",
        [176] = "Quadplane Fwd Throttle Override enable",
        [177] = "Mount LRF enable",
        [178] = "FlightMode Pause/Resume",
        [179] = "ICEngine start / stop",
        [180] = "Test autotuned gains after tune is complete",
        [181] = "QuickTune",
        [182] = "AHRS AutoTrim",
        [183] = "AUTOLAND mode",
        [184] = "System ID Chirp",
        [185] = "Mount Roll/Pitch Lock",
        [186] = "Mount POI Lock",
        [187] = "EKF Reset",
        [201] = "Roll",
        [202] = "Pitch",
        [207] = "MainSail",
        [208] = "Flap",
        [209] = "VTOL Forward Throttle",
        [210] = "Airbrakes",
        [211] = "Walking Height",
        [212] = "Mount1 Roll",
        [213] = "Mount1 Pitch",
        [214] = "Mount1 Yaw",
        [215] = "Mount2 Roll",
        [216] = "Mount2 Pitch",
        [217] = "Mount2 Yaw",
        [218] = "Loweheiser throttle",
        [219] = "Transmitter Tuning",
        [300] = "Scripting1",
        [301] = "Scripting2",
        [302] = "Scripting3",
        [303] = "Scripting4",
        [304] = "Scripting5",
        [305] = "Scripting6",
        [306] = "Scripting7",
        [307] = "Scripting8",
        [308] = "Scripting9",
        [309] = "Scripting10",
        [310] = "Scripting11",
        [311] = "Scripting12",
        [312] = "Scripting13",
        [313] = "Scripting14",
        [314] = "Scripting15",
        [315] = "Scripting16",
        [316] = "Stop-Restart Scripting",
    }.ToFrozenDictionary();

    // FLTMODEn для самолёта — ArduPlane/Parameters.cpp.
    private static readonly FrozenDictionary<int, string> PlaneModes = new Dictionary<int, string>
    {
        [0] = "Manual",
        [1] = "CIRCLE",
        [2] = "STABILIZE",
        [3] = "TRAINING",
        [4] = "ACRO",
        [5] = "FBWA",
        [6] = "FBWB",
        [7] = "CRUISE",
        [8] = "AUTOTUNE",
        [10] = "Auto",
        [11] = "RTL",
        [12] = "Loiter",
        [13] = "TAKEOFF",
        [14] = "AVOID_ADSB",
        [15] = "Guided",
        [17] = "QSTABILIZE",
        [18] = "QHOVER",
        [19] = "QLOITER",
        [20] = "QLAND",
        [21] = "QRTL",
        [22] = "QAUTOTUNE",
        [23] = "QACRO",
        [24] = "THERMAL",
        [25] = "Loiter to QLand",
        [26] = "AUTOLAND",
    }.ToFrozenDictionary();

    // FLTMODEn для коптера — ArduCopter/Parameters.cpp.
    private static readonly FrozenDictionary<int, string> CopterModes = new Dictionary<int, string>
    {
        [0] = "Stabilize",
        [1] = "Acro",
        [2] = "AltHold",
        [3] = "Auto",
        [4] = "Guided",
        [5] = "Loiter",
        [6] = "RTL",
        [7] = "Circle",
        [9] = "Land",
        [11] = "Drift",
        [13] = "Sport",
        [14] = "Flip",
        [15] = "AutoTune",
        [16] = "PosHold",
        [17] = "Brake",
        [18] = "Throw",
        [19] = "Avoid_ADSB",
        [20] = "Guided_NoGPS",
        [21] = "Smart_RTL",
        [22] = "FlowHold",
        [23] = "Follow",
        [24] = "ZigZag",
        [25] = "SystemID",
        [26] = "Heli_Autorotate",
        [27] = "Auto RTL",
        [28] = "Turtle",
    }.ToFrozenDictionary();
    /// <summary>Класс аппарата из <c>HEARTBEAT.type</c>: у режимов свои номера на каждом.</summary>
    public enum VehicleClass
    {
        Unknown,
        Plane,
        Copter,
    }

    /// <summary>MAV_TYPE_VTOL_* и прочие самолётные типы, где действует таблица Plane.</summary>
    private static readonly byte[] PlaneTypes = [1, 19, 20, 21, 22, 23, 24, 25, 28];

    /// <summary>MAV_TYPE_QUADROTOR, HEXAROTOR, OCTOROTOR, TRICOPTER, HELICOPTER, COAXIAL, DODECAROTOR.</summary>
    private static readonly byte[] CopterTypes = [2, 3, 4, 13, 14, 15, 29];

    /// <summary>Определяет класс аппарата по <c>HEARTBEAT.type</c>.</summary>
    public static VehicleClass Classify(byte mavType)
    {
        if (System.Array.IndexOf(PlaneTypes, mavType) >= 0)
        {
            return VehicleClass.Plane;
        }

        return System.Array.IndexOf(CopterTypes, mavType) >= 0
            ? VehicleClass.Copter
            : VehicleClass.Unknown;
    }

    /// <summary>
    /// Возвращает имя значения параметра или <c>null</c>, если у этого имени
    /// параметра расшифровки нет.
    /// </summary>
    /// <param name="name">Имя параметра, например <c>SERVO3_FUNCTION</c>.</param>
    /// <param name="value">Значение параметра.</param>
    /// <param name="vehicle">
    /// Класс аппарата. Нужен полётным режимам: у самолёта 0 — это Manual, у
    /// коптера — Stabilize, и подставить чужую таблицу хуже, чем не подставить
    /// никакой. При <see cref="VehicleClass.Unknown"/> режимы не расшифровываются.
    /// </param>
    public static string? Describe(string name, double value, VehicleClass vehicle)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        // Дробное значение перечислением быть не может: это либо не тот
        // параметр, либо повреждённое чтение.
        if (value != System.Math.Floor(value) || System.Math.Abs(value) > int.MaxValue)
        {
            return null;
        }

        int code = (int)value;

        if (Matches(name, "SERVO", "_FUNCTION"))
        {
            return Lookup(ServoFunctions, code);
        }

        if (Matches(name, "RC", "_OPTION"))
        {
            return Lookup(RcOptions, code);
        }

        if (IsFlightMode(name))
        {
            return vehicle switch
            {
                VehicleClass.Plane => Lookup(PlaneModes, code),
                VehicleClass.Copter => Lookup(CopterModes, code),
                _ => null,
            };
        }

        if (name.EndsWith("_REVERSED", System.StringComparison.Ordinal))
        {
            return code switch
            {
                0 => "Normal",
                1 => "Reversed",
                _ => null,
            };
        }

        return null;
    }

    /// <summary>
    /// Готовая подпись: имя значения, а если его нет — само число.
    /// </summary>
    /// <remarks>
    /// 🔴 Имя показывается <b>вместо</b> числа, а не рядом с ним. Пара «имя ·
    /// число» вдвое длиннее и в столбце значений тонет: список читают по
    /// вертикали, скользя взглядом, и лишний разряд в каждой строке сбивает
    /// это скольжение. Число никуда не девается — оно в подсказке строки и в
    /// протоколе.
    /// </remarks>
    public static string Format(string name, double value, VehicleClass vehicle, string numeric) =>
        Describe(name, value, vehicle) ?? numeric;

    private static string? Lookup(FrozenDictionary<int, string> table, int code) =>
        table.TryGetValue(code, out string? label) ? label : null;

    /// <summary>
    /// Имя вида «префикс + номер + суффикс»: <c>SERVO13_FUNCTION</c>, но не
    /// <c>SERVO_RNG_ENABLE</c> и не <c>RCMAP_ROLL</c>.
    /// </summary>
    private static bool Matches(string name, string prefix, string suffix)
    {
        if (!name.StartsWith(prefix, System.StringComparison.Ordinal)
            || !name.EndsWith(suffix, System.StringComparison.Ordinal))
        {
            return false;
        }

        int digits = name.Length - prefix.Length - suffix.Length;
        if (digits <= 0)
        {
            return false;
        }

        for (int i = prefix.Length; i < prefix.Length + digits; i++)
        {
            if (!char.IsAsciiDigit(name[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Полётный режим по положению переключателя: <c>FLTMODE1</c>…<c>FLTMODE6</c>
    /// у самолёта и коптера, <c>MODE1</c>…<c>MODE6</c> у Rover и Sub.
    /// </summary>
    /// <remarks>
    /// <c>FLTMODE_CH</c> и <c>MODE_CH</c> сюда не попадают: там номер канала, а
    /// не режим, и подпись «Manual» на номере канала была бы прямой ложью.
    /// </remarks>
    private static bool IsFlightMode(string name) =>
        (name.StartsWith("FLTMODE", System.StringComparison.Ordinal)
            && name.Length == 8
            && char.IsAsciiDigit(name[7]))
        || (name.StartsWith("MODE", System.StringComparison.Ordinal)
            && name.Length == 5
            && char.IsAsciiDigit(name[4]));
}
