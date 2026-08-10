using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ARDU_OTK.Services.Fc;

/// <summary>Ось магнитометра. Нужна только для сборки имён параметров.</summary>
public enum MagAxis
{
    X = 0,
    Y = 1,
    Z = 2,
}

/// <summary>
/// Вывод о том, внешний компас или внутренний, сделанный по флагу
/// <c>COMPASS_EXTERNAL*</c> и типу шины из <c>DEV_ID</c>.
/// </summary>
/// <remarks>
/// Значения флага в прошивке: <c>0</c> внутренний, <c>1</c> внешний,
/// <c>2</c> внешний принудительно. Ноль и единицу автоопределение
/// переписывает после загрузки, поэтому прочитанное после ребута значение —
/// это уже вывод драйвера, а не пожелание оператора. Двойка — блокировка:
/// <c>set_external()</c> ничего не делает, если в параметре уже лежит 2,
/// и драйвер её не перебьёт.
/// </remarks>
public enum ExternalKind
{
    /// <summary>Внутренний компас.</summary>
    Internal,

    /// <summary>Внешний компас, значение разрешено автоопределением.</summary>
    External,

    /// <summary>Внешний принудительно (<c>COMPASS_EXTERNAL* = 2</c>) — оператор заблокировал автоопределение.</summary>
    ExternalLocked,

    /// <summary>
    /// Судить нельзя: данных нет либо флаг и шина противоречат друг другу
    /// непримиримо. Приравнивать к любому из остальных значений запрещено —
    /// вызывающий обязан остановить процедуру.
    /// </summary>
    Ambiguous,
}

/// <summary>Ожидаемая конфигурация одного слота компаса, вычитанная из эталона.</summary>
/// <param name="Slot">Номер слота 1..3.</param>
/// <param name="DeviceId">Ожидаемый <c>COMPASS_DEV_IDx</c>.</param>
/// <param name="ExternalFlag">Ожидаемый <c>COMPASS_EXTERNAL/EXTERN2/EXTERN3</c>. <c>null</c>, если в эталоне его нет.</param>
public sealed record ExpectedCompassSlot(int Slot, CompassDeviceId DeviceId, int? ExternalFlag)
{
    public bool IsPresent => !DeviceId.IsEmpty;
}

/// <summary>Сравнение одного подполя. Существует ради протокола: нужно записать, что именно разошлось.</summary>
/// <param name="Field">Имя подполя для протокола.</param>
/// <param name="Expected">Ожидаемое значение в читаемом виде.</param>
/// <param name="Actual">Фактическое значение в читаемом виде.</param>
/// <param name="Matches">Совпало ли.</param>
/// <param name="Decisive">
/// Входит ли подполе в вердикт. Номер шины и сырой <c>DEV_ID</c> —
/// не входят: они законно отличаются у физически одинаковых плат.
/// </param>
public sealed record CompassFieldComparison(
    string Field,
    string Expected,
    string Actual,
    bool Matches,
    bool Decisive);

/// <summary>Итог сравнения одного слота с эталоном.</summary>
/// <remarks>
/// 🔴 Вердикт строится по типу шины, адресу и <c>devtype</c>, а НЕ по целому
/// <c>DEV_ID</c>. Подполе <c>bus</c> — это порядковый номер шины на конкретной
/// плате; у двух физически одинаковых бортов он законно отличается, и сравнение
/// сырых чисел забраковало бы исправное изделие.
/// </remarks>
public sealed record CompassSlotComparison(
    int Slot,
    CompassDeviceId Expected,
    CompassDeviceId Actual,
    bool ExpectedPresent,
    bool ActualPresent,
    bool BusTypeMatches,
    bool AddressMatches,
    bool DevTypeMatches,
    bool BusNumberMatches,
    bool RawMatches,
    IReadOnlyList<CompassFieldComparison> Fields)
{
    /// <summary>Слот считается совпавшим, если совпало присутствие и все три решающих подполя.</summary>
    public bool Matches =>
        ExpectedPresent == ActualPresent
        && (!ExpectedPresent || (BusTypeMatches && AddressMatches && DevTypeMatches));

    /// <summary>Имена разошедшихся решающих подполей — готовая строка для протокола.</summary>
    public IReadOnlyList<string> DecisiveDifferences =>
        Fields.Where(f => f.Decisive && !f.Matches).Select(f => f.Field).ToArray();
}

/// <summary>Вердикт по топологии компасов борта относительно эталона.</summary>
/// <param name="Slots">Послотовые сравнения 1..3.</param>
/// <param name="Slot1Kind">Классификация слота 1.</param>
/// <param name="Slot1External">Проверка «в слоте 1 стоит внешний компас».</param>
/// <param name="Outcome">Общий исход. <see cref="CheckOutcome.Inconclusive"/> никогда не равен «прошло».</param>
/// <param name="Detail">Что именно сопоставлено и что разошлось.</param>
public sealed record CompassTopologyVerdict(
    IReadOnlyList<CompassSlotComparison> Slots,
    ExternalKind Slot1Kind,
    CheckOutcome Slot1External,
    CheckOutcome Outcome,
    string Detail);

/// <summary>
/// Опознание компасов: расшифровка <c>DEV_ID</c>, разбор «внешний/внутренний»,
/// сверка топологии с эталоном и точные имена параметров.
/// </summary>
/// <remarks>
/// Класс намеренно не знает ни про MAVLink, ни про файлы, ни про UI: всё, что
/// он делает, — чистые вычисления над уже прочитанными значениями. Это даёт
/// возможность покрыть его тестами без стенда и без борта.
/// </remarks>
public static class CompassIdentity
{
    /// <summary>Текст для пустого слота. Константа, чтобы протокол и тесты сравнивали одно и то же.</summary>
    public const string EmptyDeviceText = "нет устройства";

    /// <summary>Минимальный номер слота.</summary>
    public const int MinSlot = 1;

    /// <summary>Максимальный номер слота. Прошивка регистрирует три приоритетных компаса.</summary>
    public const int MaxSlot = 3;

    // ---------------------------------------------------------------------
    // Имена параметров. Только фиксированные таблицы.
    //
    // Именование в прошивке нерегулярно, и это не опечатка в документации:
    // EXTERNAL превращается в EXTERN2/EXTERN3 (а не EXTERNAL2), у первого
    // экземпляра цифры нет вовсе, а у осевых параметров цифра стоит в
    // середине имени (COMPASS_OFS2_X, а не COMPASS_OFS_X2). Любая генерация
    // этих имён «по шаблону» даёт параметры, которых на борту не существует:
    // чтение вернёт отказ, а запись молча не применится.
    // ---------------------------------------------------------------------

    private static readonly string[] UseNames =
    {
        "COMPASS_USE", "COMPASS_USE2", "COMPASS_USE3",
    };

    private static readonly string[] ExternalNames =
    {
        "COMPASS_EXTERNAL", "COMPASS_EXTERN2", "COMPASS_EXTERN3",
    };

    private static readonly string[] OrientNames =
    {
        "COMPASS_ORIENT", "COMPASS_ORIENT2", "COMPASS_ORIENT3",
    };

    private static readonly string[] DevIdNames =
    {
        "COMPASS_DEV_ID", "COMPASS_DEV_ID2", "COMPASS_DEV_ID3",
    };

    private static readonly string[] ScaleNames =
    {
        "COMPASS_SCALE", "COMPASS_SCALE2", "COMPASS_SCALE3",
    };

    private static readonly string[] PriorityIdNames =
    {
        "COMPASS_PRIO1_ID", "COMPASS_PRIO2_ID", "COMPASS_PRIO3_ID",
    };

    private static readonly string[][] OffsetNames =
    {
        new[] { "COMPASS_OFS_X", "COMPASS_OFS_Y", "COMPASS_OFS_Z" },
        new[] { "COMPASS_OFS2_X", "COMPASS_OFS2_Y", "COMPASS_OFS2_Z" },
        new[] { "COMPASS_OFS3_X", "COMPASS_OFS3_Y", "COMPASS_OFS3_Z" },
    };

    private static readonly string[][] DiagonalNames =
    {
        new[] { "COMPASS_DIA_X", "COMPASS_DIA_Y", "COMPASS_DIA_Z" },
        new[] { "COMPASS_DIA2_X", "COMPASS_DIA2_Y", "COMPASS_DIA2_Z" },
        new[] { "COMPASS_DIA3_X", "COMPASS_DIA3_Y", "COMPASS_DIA3_Z" },
    };

    private static readonly string[][] OffDiagonalNames =
    {
        new[] { "COMPASS_ODI_X", "COMPASS_ODI_Y", "COMPASS_ODI_Z" },
        new[] { "COMPASS_ODI2_X", "COMPASS_ODI2_Y", "COMPASS_ODI2_Z" },
        new[] { "COMPASS_ODI3_X", "COMPASS_ODI3_Y", "COMPASS_ODI3_Z" },
    };

    private static readonly string[][] MotorCompNames =
    {
        new[] { "COMPASS_MOT_X", "COMPASS_MOT_Y", "COMPASS_MOT_Z" },
        new[] { "COMPASS_MOT2_X", "COMPASS_MOT2_Y", "COMPASS_MOT2_Z" },
        new[] { "COMPASS_MOT3_X", "COMPASS_MOT3_Y", "COMPASS_MOT3_Z" },
    };

    /// <summary>
    /// Имена, которые запрещено писать на борт при переносе калибровки.
    /// </summary>
    /// <remarks>
    /// <c>COMPASS_DEV_ID*</c> помечен <c>@ReadOnly</c> и сверяется при загрузке
    /// с реально найденными датчиками: подложенное чужое значение «пересаживает»
    /// датчики по слотам. <c>COMPASS_PRIO*_ID</c> хранит идентификаторы датчиков
    /// именно той платы, с которой снят эталон, — скопированные, они называют
    /// несуществующее железо и дают <c>PreArm: Compass N not found</c>.
    /// Приоритеты выставляются из собственных <c>COMPASS_DEV_IDx</c> целевой платы.
    /// </remarks>
    public static IReadOnlyList<string> NeverWriteNames { get; } = new[]
    {
        "COMPASS_DEV_ID", "COMPASS_DEV_ID2", "COMPASS_DEV_ID3",
        "COMPASS_PRIO1_ID", "COMPASS_PRIO2_ID", "COMPASS_PRIO3_ID",
    };

    private static readonly HashSet<string> NeverWriteSet =
        new(NeverWriteNames, StringComparer.Ordinal);

    /// <summary>Точное имя <c>COMPASS_USE*</c> для слота.</summary>
    public static string UseName(int slot) => UseNames[Index(slot)];

    /// <summary>Точное имя <c>COMPASS_EXTERNAL/EXTERN2/EXTERN3</c> для слота. Обратите внимание: не <c>EXTERNAL2</c>.</summary>
    public static string ExternalName(int slot) => ExternalNames[Index(slot)];

    /// <summary>Точное имя <c>COMPASS_ORIENT*</c> для слота.</summary>
    public static string OrientName(int slot) => OrientNames[Index(slot)];

    /// <summary>Точное имя <c>COMPASS_DEV_ID*</c> для слота. Только для чтения.</summary>
    public static string DevIdName(int slot) => DevIdNames[Index(slot)];

    /// <summary>Точное имя <c>COMPASS_SCALE*</c> для слота.</summary>
    public static string ScaleName(int slot) => ScaleNames[Index(slot)];

    /// <summary>Точное имя <c>COMPASS_PRIO*_ID</c> для слота. Здесь цифра есть и у первого.</summary>
    public static string PriorityIdName(int slot) => PriorityIdNames[Index(slot)];

    /// <summary>Точное имя <c>COMPASS_OFS*</c> по оси.</summary>
    public static string OffsetName(int slot, MagAxis axis) => OffsetNames[Index(slot)][AxisIndex(axis)];

    /// <summary>Точное имя <c>COMPASS_DIA*</c> по оси.</summary>
    public static string DiagonalName(int slot, MagAxis axis) => DiagonalNames[Index(slot)][AxisIndex(axis)];

    /// <summary>Точное имя <c>COMPASS_ODI*</c> по оси.</summary>
    public static string OffDiagonalName(int slot, MagAxis axis) => OffDiagonalNames[Index(slot)][AxisIndex(axis)];

    /// <summary>Точное имя <c>COMPASS_MOT*</c> по оси.</summary>
    public static string MotorCompName(int slot, MagAxis axis) => MotorCompNames[Index(slot)][AxisIndex(axis)];

    /// <summary>Запрещено ли имя к записи. Сравнение порядковое: имена параметров регистрозависимы.</summary>
    public static bool IsNeverWrite(string name) => NeverWriteSet.Contains(name);

    /// <summary>
    /// Переносимая калибровка слота: смещения, диагонали, внедиагонали,
    /// компенсация тока двигателей и масштаб.
    /// </summary>
    /// <remarks>
    /// Это ровно тот блок, который прошивка сама сохраняет после успешной
    /// калибровки и переносит между слотами при смене приоритетов
    /// (<c>mag_state::copy_from</c>). Оговорка по <c>COMPASS_MOT*</c>: это
    /// результат CompassMot, он зависит от силовой проводки конкретного
    /// изделия, и переносить его допустимо только между полностью
    /// одинаковыми сборками — см. <see cref="CoreCalibrationNames"/>.
    /// </remarks>
    public static IReadOnlyList<string> TransferableCalibrationNames(int slot)
    {
        int i = Index(slot);
        return new[]
        {
            OffsetNames[i][0], OffsetNames[i][1], OffsetNames[i][2],
            DiagonalNames[i][0], DiagonalNames[i][1], DiagonalNames[i][2],
            OffDiagonalNames[i][0], OffDiagonalNames[i][1], OffDiagonalNames[i][2],
            MotorCompNames[i][0], MotorCompNames[i][1], MotorCompNames[i][2],
            ScaleNames[i],
        };
    }

    /// <summary>
    /// То же, но без <c>COMPASS_MOT*</c>: калибровка железа компаса как таковая,
    /// без привязки к силовой части изделия.
    /// </summary>
    public static IReadOnlyList<string> CoreCalibrationNames(int slot)
    {
        int i = Index(slot);
        return new[]
        {
            OffsetNames[i][0], OffsetNames[i][1], OffsetNames[i][2],
            DiagonalNames[i][0], DiagonalNames[i][1], DiagonalNames[i][2],
            OffDiagonalNames[i][0], OffDiagonalNames[i][1], OffDiagonalNames[i][2],
            ScaleNames[i],
        };
    }

    // ---------------------------------------------------------------------
    // Расшифровка DEV_ID
    // ---------------------------------------------------------------------

    /// <summary>Читаемое имя типа шины. Поле трёхбитное, поэтому непокрытых значений нет.</summary>
    public static string BusTypeName(MavBusType busType) => busType switch
    {
        MavBusType.Unknown => "UNKNOWN",
        MavBusType.I2C => "I2C",
        MavBusType.Spi => "SPI",
        MavBusType.DroneCan => "DroneCAN",
        MavBusType.Sitl => "SITL",
        MavBusType.Msp => "MSP",
        MavBusType.Serial => "Serial",
        MavBusType.WSpi => "WSPI",
        _ => "UNKNOWN",
    };

    /// <summary>
    /// Имя датчика по <c>devtype</c> из <c>AP_Compass_Backend.h</c>.
    /// <c>null</c>, если код не известен — гадать нельзя.
    /// </summary>
    /// <remarks>
    /// Источник — заголовок прошивки. Скрипт <c>Tools/scripts/decode_devid.py</c>
    /// из того же репозитория расходится с заголовком по кодам
    /// <c>0x09</c>, <c>0x11</c>, <c>0x12</c> и <c>0x13</c>; здесь взяты значения
    /// заголовка, потому что именно он компилируется в прошивку.
    /// <c>0x19 LIS2MDL</c> выведен из обращения (тот же датчик, что
    /// <c>0x18 IIS2MDC</c>) и намеренно не переиспользуется.
    /// </remarks>
    public static string? TryGetSensorName(int devType) => devType switch
    {
        0x01 => "HMC5883_OLD",
        0x02 => "LSM303D",
        0x04 => "AK8963",
        0x05 => "BMM150",
        0x06 => "LSM9DS1",
        0x07 => "HMC5883",
        0x08 => "LIS3MDL",
        0x09 => "AK09916",
        0x0A => "IST8310",
        0x0B => "ICM20948",
        0x0C => "MMC3416",
        0x0D => "QMC5883L",
        0x0E => "MAG3110",
        0x0F => "SITL",
        0x10 => "IST8308",
        0x11 => "RM3100",
        0x12 => "RM3100_2",
        0x13 => "MMC5983",
        0x14 => "AK09918",
        0x15 => "AK09915",
        0x16 => "QMC5883P",
        0x17 => "BMM350",
        0x18 => "IIS2MDC",
        _ => null,
    };

    /// <summary>
    /// Имя датчика либо честное <c>devtype 0xNN</c>, если код не из таблицы.
    /// Подставлять сюда «похожий» датчик запрещено.
    /// </summary>
    public static string DescribeSensor(int devType) =>
        TryGetSensorName(devType) ?? string.Create(
            CultureInfo.InvariantCulture,
            $"devtype 0x{devType:X2}");

    /// <summary>
    /// Читаемое описание <c>DEV_ID</c> вида <c>I2C:bus 1:addr 0x0E:IST8310</c>.
    /// </summary>
    public static string Describe(CompassDeviceId id)
    {
        if (id.IsEmpty)
        {
            return EmptyDeviceText;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{BusTypeName(id.BusType)}:bus {id.Bus}:addr 0x{id.Address:X2}:{DescribeSensor(id.DevType)}");
    }

    /// <summary>Читаемое описание слота: устройство плюс вывод о внешности.</summary>
    public static string Describe(CompassSlot slot) => string.Create(
        CultureInfo.InvariantCulture,
        $"слот {slot.Slot}: {Describe(slot.DeviceId)} [{ExternalKindText(Classify(slot))}]");

    /// <summary>Русский текст для <see cref="ExternalKind"/> — для протокола и UI.</summary>
    public static string ExternalKindText(ExternalKind kind) => kind switch
    {
        ExternalKind.Internal => "внутренний",
        ExternalKind.External => "внешний",
        ExternalKind.ExternalLocked => "внешний, принудительно",
        _ => "неоднозначно",
    };

    // ---------------------------------------------------------------------
    // Внешний / внутренний
    // ---------------------------------------------------------------------

    /// <summary>
    /// Определяет, внешний компас в слоте или внутренний.
    /// </summary>
    /// <remarks>
    /// Порядок разбора:
    /// <list type="number">
    /// <item>пустой слот или флаг вне диапазона 0..2 — <see cref="ExternalKind.Ambiguous"/>,
    /// потому что данных для вывода нет;</item>
    /// <item>шина <c>UNKNOWN</c> при непустом <c>DEV_ID</c> — тоже
    /// <see cref="ExternalKind.Ambiguous"/>: значение параметра испорчено, и
    /// подпирать его флагом нельзя;</item>
    /// <item>DroneCAN (3), MSP (5) и Serial (6) — всегда внешние независимо от
    /// флага: соответствующие бэкенды принудительно вызывают
    /// <c>set_external(true)</c>. Отдельного типа шины для ExternalAHRS нет,
    /// такие компасы регистрируются как Serial и попадают сюда же;</item>
    /// <item>в остальных случаях решает флаг: 0 внутренний, 1 внешний,
    /// 2 внешний с блокировкой автоопределения.</item>
    /// </list>
    /// SITL (4) прошивка тоже считает внешним, но на серийном изделии эта шина
    /// не встречается, поэтому специального правила для неё здесь нет —
    /// решает флаг.
    /// Судить по номеру слота нельзя ни при каких условиях.
    /// </remarks>
    public static ExternalKind Classify(CompassSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return Classify(slot.DeviceId, slot.ExternalFlag);
    }

    /// <summary>Тот же разбор от «сырых» значений — удобно для тестов и для эталона.</summary>
    public static ExternalKind Classify(CompassDeviceId deviceId, int externalFlag)
    {
        if (deviceId.IsEmpty)
        {
            return ExternalKind.Ambiguous;
        }

        if (externalFlag is < 0 or > 2)
        {
            return ExternalKind.Ambiguous;
        }

        if (deviceId.BusType == MavBusType.Unknown)
        {
            return ExternalKind.Ambiguous;
        }

        bool busForcesExternal = deviceId.BusType
            is MavBusType.DroneCan
            or MavBusType.Msp
            or MavBusType.Serial;

        if (busForcesExternal)
        {
            // Флаг 2 сохраняем отдельным значением: он говорит, что
            // автоопределение выключено оператором, и это факт про настройку,
            // а не про шину.
            return externalFlag == 2 ? ExternalKind.ExternalLocked : ExternalKind.External;
        }

        return externalFlag switch
        {
            0 => ExternalKind.Internal,
            1 => ExternalKind.External,
            _ => ExternalKind.ExternalLocked,
        };
    }

    /// <summary>Считается ли компас внешним. <c>null</c> при <see cref="ExternalKind.Ambiguous"/>.</summary>
    public static bool? IsExternal(ExternalKind kind) => kind switch
    {
        ExternalKind.Internal => false,
        ExternalKind.External => true,
        ExternalKind.ExternalLocked => true,
        _ => null,
    };

    // ---------------------------------------------------------------------
    // Сверка топологии с эталоном
    // ---------------------------------------------------------------------

    /// <summary>
    /// Один ли это датчик: тип шины, адрес и <c>devtype</c>.
    /// </summary>
    /// <remarks>
    /// 🔴 Номер шины (<c>bus</c>) сознательно не участвует. Он нумерует шины
    /// внутри платы и законно отличается у физически одинаковых бортов —
    /// сравнение целых <c>DEV_ID</c> забраковало бы исправное изделие.
    /// </remarks>
    public static bool IsSameSensor(CompassDeviceId a, CompassDeviceId b) =>
        a.BusType == b.BusType && a.Address == b.Address && a.DevType == b.DevType;

    /// <summary>
    /// Вычитывает из эталона ожидаемую конфигурацию слотов 1..3.
    /// </summary>
    /// <remarks>
    /// Отсутствующий <c>COMPASS_DEV_IDx</c> даёт пустой слот, а не ошибку:
    /// эталон может описывать два компаса из трёх. Значения приводятся к
    /// <c>uint</c> округлением — в файле они лежат как целые, записанные в
    /// десятичном виде.
    /// </remarks>
    public static IReadOnlyList<ExpectedCompassSlot> ReadExpectedSlots(ReferenceParamSet reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var result = new ExpectedCompassSlot[MaxSlot];
        for (int slot = MinSlot; slot <= MaxSlot; slot++)
        {
            CompassDeviceId id = default;
            if (reference.TryGet(DevIdName(slot), out double rawDevId))
            {
                id = ToDeviceId(rawDevId);
            }

            int? external = reference.TryGet(ExternalName(slot), out double rawExternal)
                ? (int)Math.Round(rawExternal, MidpointRounding.AwayFromZero)
                : null;

            result[slot - MinSlot] = new ExpectedCompassSlot(slot, id, external);
        }

        return result;
    }

    /// <summary>
    /// Преобразует значение <c>DEV_ID</c> из файла или из параметра в структуру.
    /// Отрицательное или выходящее за <c>uint</c> значение считается пустым слотом.
    /// </summary>
    public static CompassDeviceId ToDeviceId(double rawValue)
    {
        double rounded = Math.Round(rawValue, MidpointRounding.AwayFromZero);
        if (rounded <= 0 || rounded > uint.MaxValue)
        {
            return default;
        }

        return new CompassDeviceId((uint)rounded);
    }

    /// <summary>
    /// Сверяет топологию компасов борта с эталонным файлом.
    /// </summary>
    public static CompassTopologyVerdict CompareToReference(
        IReadOnlyList<CompassSlot> boardSlots,
        ReferenceParamSet reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return Compare(boardSlots, ReadExpectedSlots(reference));
    }

    /// <summary>
    /// Сверяет топологию компасов борта с ожидаемой конфигурацией.
    /// </summary>
    /// <remarks>
    /// Решающие подполя — тип шины, адрес и <c>devtype</c>. Номер шины и сырой
    /// <c>DEV_ID</c> тоже попадают в результат, но помечены как несущественные:
    /// протокол должен показать оператору, что именно разошлось, и при этом не
    /// забраковать плату из-за перенумерации шин.
    /// Исход <see cref="CheckOutcome.Inconclusive"/> выдаётся, когда сравнивать
    /// нечего: ни у эталона, ни у борта нет ни одного компаса. Он никогда не
    /// приравнивается к <see cref="CheckOutcome.Pass"/>.
    /// </remarks>
    public static CompassTopologyVerdict Compare(
        IReadOnlyList<CompassSlot> boardSlots,
        IReadOnlyList<ExpectedCompassSlot> expectedSlots)
    {
        ArgumentNullException.ThrowIfNull(boardSlots);
        ArgumentNullException.ThrowIfNull(expectedSlots);

        var comparisons = new List<CompassSlotComparison>(MaxSlot);
        var problems = new List<string>();
        bool anythingToCompare = false;

        for (int slot = MinSlot; slot <= MaxSlot; slot++)
        {
            CompassSlot? board = boardSlots.FirstOrDefault(s => s is not null && s.Slot == slot);
            ExpectedCompassSlot? expected = expectedSlots.FirstOrDefault(s => s is not null && s.Slot == slot);

            CompassDeviceId actualId = board?.DeviceId ?? default;
            CompassDeviceId expectedId = expected?.DeviceId ?? default;

            bool actualPresent = !actualId.IsEmpty;
            bool expectedPresent = !expectedId.IsEmpty;
            anythingToCompare |= actualPresent || expectedPresent;

            var comparison = BuildSlotComparison(slot, expectedId, actualId, expectedPresent, actualPresent);
            comparisons.Add(comparison);

            if (comparison.Matches)
            {
                continue;
            }

            if (expectedPresent != actualPresent)
            {
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"слот {slot}: эталон — {Describe(expectedId)}, борт — {Describe(actualId)}"));
            }
            else
            {
                string differences = string.Join(", ", comparison.DecisiveDifferences);
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"слот {slot}: разошлось {differences} (эталон {Describe(expectedId)}, борт {Describe(actualId)})"));
            }
        }

        CompassSlot? firstSlot = boardSlots.FirstOrDefault(s => s is not null && s.Slot == MinSlot);
        ExternalKind slot1Kind = firstSlot is null ? ExternalKind.Ambiguous : Classify(firstSlot);
        CheckOutcome slot1External = IsExternal(slot1Kind) switch
        {
            true => CheckOutcome.Pass,
            false => CheckOutcome.Fail,
            _ => CheckOutcome.Inconclusive,
        };

        CheckOutcome outcome;
        if (!anythingToCompare)
        {
            outcome = CheckOutcome.Inconclusive;
            problems.Add("ни в эталоне, ни на борту нет ни одного компаса — сравнивать нечего");
        }
        else if (problems.Count > 0)
        {
            outcome = CheckOutcome.Fail;
        }
        else if (slot1External == CheckOutcome.Fail)
        {
            outcome = CheckOutcome.Fail;
            problems.Add("в слоте 1 внутренний компас, ожидался внешний");
        }
        else if (slot1External == CheckOutcome.Inconclusive)
        {
            // Топология совпала, но про слот 1 судить нельзя — это не «прошло».
            outcome = CheckOutcome.Inconclusive;
            problems.Add("внешность компаса в слоте 1 определить не удалось");
        }
        else
        {
            outcome = CheckOutcome.Pass;
        }

        string detail = problems.Count > 0
            ? string.Join("; ", problems)
            : "топология совпала с эталоном по типу шины, адресу и devtype; в слоте 1 внешний компас";

        return new CompassTopologyVerdict(comparisons, slot1Kind, slot1External, outcome, detail);
    }

    private static CompassSlotComparison BuildSlotComparison(
        int slot,
        CompassDeviceId expected,
        CompassDeviceId actual,
        bool expectedPresent,
        bool actualPresent)
    {
        bool bothPresent = expectedPresent && actualPresent;

        bool busTypeMatches = bothPresent && expected.BusType == actual.BusType;
        bool addressMatches = bothPresent && expected.Address == actual.Address;
        bool devTypeMatches = bothPresent && expected.DevType == actual.DevType;
        bool busNumberMatches = bothPresent && expected.Bus == actual.Bus;
        bool rawMatches = expected.Raw == actual.Raw;

        var fields = new List<CompassFieldComparison>(6)
        {
            new(
                "Присутствие",
                expectedPresent ? "есть" : "нет",
                actualPresent ? "есть" : "нет",
                expectedPresent == actualPresent,
                Decisive: true),
            new(
                "Тип шины",
                expectedPresent ? BusTypeName(expected.BusType) : EmptyDeviceText,
                actualPresent ? BusTypeName(actual.BusType) : EmptyDeviceText,
                busTypeMatches,
                Decisive: true),
            new(
                "Адрес",
                expectedPresent ? FormatAddress(expected.Address) : EmptyDeviceText,
                actualPresent ? FormatAddress(actual.Address) : EmptyDeviceText,
                addressMatches,
                Decisive: true),
            new(
                "Датчик (devtype)",
                expectedPresent ? DescribeSensor(expected.DevType) : EmptyDeviceText,
                actualPresent ? DescribeSensor(actual.DevType) : EmptyDeviceText,
                devTypeMatches,
                Decisive: true),

            // Ниже — справочные поля. Их расхождение не является браком:
            // номер шины зависит от разводки конкретной платы, а сырой DEV_ID
            // содержит этот номер внутри себя.
            new(
                "Номер шины",
                expectedPresent ? expected.Bus.ToString(CultureInfo.InvariantCulture) : EmptyDeviceText,
                actualPresent ? actual.Bus.ToString(CultureInfo.InvariantCulture) : EmptyDeviceText,
                busNumberMatches,
                Decisive: false),
            new(
                "Сырой DEV_ID",
                expected.Raw.ToString(CultureInfo.InvariantCulture),
                actual.Raw.ToString(CultureInfo.InvariantCulture),
                rawMatches,
                Decisive: false),
        };

        return new CompassSlotComparison(
            slot,
            expected,
            actual,
            expectedPresent,
            actualPresent,
            busTypeMatches,
            addressMatches,
            devTypeMatches,
            busNumberMatches,
            rawMatches,
            fields);
    }

    private static string FormatAddress(int address) => string.Create(
        CultureInfo.InvariantCulture,
        $"0x{address:X2}");

    private static int Index(int slot)
    {
        if (slot is < MinSlot or > MaxSlot)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slot),
                slot,
                $"Номер слота компаса должен быть в диапазоне {MinSlot}..{MaxSlot}.");
        }

        return slot - MinSlot;
    }

    private static int AxisIndex(MagAxis axis)
    {
        if (axis is < MagAxis.X or > MagAxis.Z)
        {
            throw new ArgumentOutOfRangeException(nameof(axis), axis, "Ось магнитометра должна быть X, Y или Z.");
        }

        return (int)axis;
    }
}
