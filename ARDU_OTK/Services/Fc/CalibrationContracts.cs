using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ARDU_OTK.Services.Fc;

/// <summary>Тип шины из <c>DEV_ID</c> (<c>AP_HAL::Device::BusType</c>).</summary>
public enum MavBusType : byte
{
    Unknown = 0,
    I2C = 1,
    Spi = 2,

    /// <summary>UAVCAN и DroneCAN — одно и то же значение.</summary>
    DroneCan = 3,
    Sitl = 4,
    Msp = 5,

    /// <summary>Обычный последовательный порт и ExternalAHRS. Отдельного типа для ExternalAHRS нет.</summary>
    Serial = 6,
    WSpi = 7,
}

/// <summary>
/// Разобранный <c>COMPASS_DEV_IDx</c>. Поля упакованы так, чтобы значение
/// проходило через float-транспорт параметров без потерь.
/// </summary>
public readonly record struct CompassDeviceId(uint Raw)
{
    public MavBusType BusType => (MavBusType)(Raw & 0x07);

    public int Bus => (int)((Raw >> 3) & 0x1F);

    public int Address => (int)((Raw >> 8) & 0xFF);

    public int DevType => (int)((Raw >> 16) & 0xFF);

    public bool IsEmpty => Raw == 0;
}

/// <summary>Один слот компаса, как он виден в параметрах борта.</summary>
/// <param name="Slot">Номер слота 1..3. Слот 1 — приоритетный.</param>
/// <param name="DeviceId">Значение <c>COMPASS_DEV_IDx</c>.</param>
/// <param name="ExternalFlag">Значение <c>COMPASS_EXTERNAL/EXTERN2/EXTERN3</c>: 0 внутренний, 1 внешний, 2 внешний принудительно.</param>
/// <param name="UseFlag">Значение <c>COMPASS_USE/USE2/USE3</c>.</param>
/// <param name="Orientation">Значение <c>COMPASS_ORIENT*</c>.</param>
/// <param name="Offsets">Текущие <c>COMPASS_OFS*</c> по трём осям.</param>
public sealed record CompassSlot(
    int Slot,
    CompassDeviceId DeviceId,
    int ExternalFlag,
    int UseFlag,
    int Orientation,
    (double X, double Y, double Z) Offsets)
{
    public bool IsPresent => !DeviceId.IsEmpty;

    /// <summary>Модуль вектора смещений — с ним сравнивается <c>COMPASS_OFFS_MAX</c>.</summary>
    public double OffsetMagnitude => Math.Sqrt(
        Offsets.X * Offsets.X + Offsets.Y * Offsets.Y + Offsets.Z * Offsets.Z);
}

/// <summary>Разобранный эталонный файл параметров.</summary>
/// <param name="FilePath">Откуда прочитан.</param>
/// <param name="Format">Формат исходника, для протокола.</param>
/// <param name="Values">Все прочитанные пары имя-значение.</param>
public sealed record ReferenceParamSet(
    string FilePath,
    string Format,
    IReadOnlyDictionary<string, double> Values)
{
    public bool TryGet(string name, out double value) => Values.TryGetValue(name, out value);
}

/// <summary>Исход одной проверки.</summary>
public enum CheckOutcome
{
    Pass,
    Fail,

    /// <summary>Данных не хватило, чтобы судить. Никогда не приравнивается к <see cref="Pass"/>.</summary>
    Inconclusive,
}

/// <summary>Результат одной приёмочной или технологической проверки.</summary>
/// <param name="Id">Стабильный идентификатор для реестра.</param>
/// <param name="Title">Название для оператора.</param>
/// <param name="Outcome">Исход.</param>
/// <param name="Detail">Что именно измерено и с чем сопоставлено.</param>
/// <param name="Measured">Числовое значение, если применимо.</param>
/// <param name="Limit">Допуск, если применим.</param>
public sealed record CheckResult(
    string Id,
    string Title,
    CheckOutcome Outcome,
    string Detail,
    double? Measured = null,
    double? Limit = null);

/// <summary>Исход записи одного параметра, для протокола и аудита.</summary>
public enum WriteOutcome
{
    /// <summary>Записано и подтверждено обратным чтением.</summary>
    Verified,

    /// <summary>Значение уже совпадало, запись не потребовалась.</summary>
    AlreadyEqual,

    /// <summary>Прошивка не приняла изменение в пределах своей полосы схлопывания. Не ошибка.</summary>
    Coalesced,

    /// <summary>Обратное чтение показало другое значение.</summary>
    Mismatch,

    /// <summary>Записать или прочитать не удалось.</summary>
    Failed,
}

/// <summary>
/// Параметр, не прошедший проверку: борт держит не то, что требует эталон.
/// </summary>
/// <remarks>
/// 🔴 Расхождения собираются все, а не до первого. Останов на первом давал
/// оператору одно имя из десяти и заставлял искать остальные девять
/// перезапусками прогона — по одному имени за прогон, каждый со снятием платы
/// со стапеля. Полный список позволяет решить, что это: единичный сбой записи
/// или негодный эталон.
/// </remarks>
/// <param name="Name">Имя параметра.</param>
/// <param name="Stage">Стадия, на которой расхождение обнаружено.</param>
/// <param name="Expected">Что требует эталон.</param>
/// <param name="Actual">Что борт отдал при обратном чтении.</param>
/// <param name="Type">Тип параметра на борту — нужен для повторной записи.</param>
/// <param name="Detail">Пояснение для оператора и для протокола.</param>
public sealed record ParamMismatch(
    string Name,
    CalibrationStage Stage,
    double Expected,
    double Actual,
    MavParamType Type,
    string Detail);

/// <summary>Запись аудита по одному параметру.</summary>
public sealed record ParamWriteRecord(
    string Name,
    double? Before,
    double Requested,
    double? ReadBack,
    WriteOutcome Outcome,
    DateTimeOffset AtUtc);

/// <summary>Задание на серийную калибровку.</summary>
/// <param name="PortName">COM-порт целевого борта.</param>
/// <param name="ReferenceFilePath">Эталонный .param.</param>
/// <param name="JigAzimuthDeg">Азимут стапеля, градусы, ИСТИННЫЙ.</param>
/// <param name="UnitId">Серийный номер борта, введённый оператором.</param>
/// <param name="Operator">Кто выполняет.</param>
/// <param name="Roles">
/// Роли параметров эталона: что сверяется и переносится, а что исключено.
/// <c>null</c> — умолчания технологической карты
/// (<see cref="ParameterRoleMap.Default"/>).
/// </param>
public sealed record CalibrationRequest(
    string PortName,
    string ReferenceFilePath,
    double JigAzimuthDeg,
    string UnitId,
    string Operator,
    ParameterRoleMap? Roles = null)
{
    /// <summary>
    /// Действующая карта ролей. Единственное место, где подставляется
    /// умолчание: <c>null</c> не должен разъезжаться по стадиям процедуры и
    /// проверяться в каждой заново.
    /// </summary>
    public ParameterRoleMap EffectiveRoles => Roles ?? ParameterRoleMap.Default;
}

/// <summary>Допуски процедуры. Значения по умолчанию — из технологической карты.</summary>
public sealed record CalibrationTolerances
{
    /// <summary>Допуск обратного чтения параметра, относительный.</summary>
    public double ParamVerifyTolerance { get; init; } = 1e-4;

    /// <summary>Допуск на курс борта против азимута стапеля, градусы.</summary>
    public double HeadingVsJigDeg { get; init; } = 3.0;

    /// <summary>Допустимое расхождение курсов между компасами, градусы.</summary>
    public double InterCompassSpreadDeg { get; init; } = 5.0;

    /// <summary>Окно накопления телеметрии перед измерением.</summary>
    public TimeSpan TelemetryWindow { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Окно сбора сообщений предполётных проверок.</summary>
    public TimeSpan PrearmWindow { get; init; } = TimeSpan.FromSeconds(3);
}

/// <summary>Стадии процедуры. Порядок соответствует технологической карте.</summary>
public enum CalibrationStage
{
    Connect,
    GpsFix,
    VerifyTopology,
    WriteReference,
    VerifyWrites,
    Reboot,
    ReReadControlSample,
    FixedYaw,
    Acceptance,
    Protocol,
}

/// <summary>Канал прогресса для UI. Реализация обязана быть потокобезопасной.</summary>
public interface ICalibrationProgress
{
    void StageStarted(CalibrationStage stage, string message);

    void StageFinished(CalibrationStage stage, CheckOutcome outcome, string message);

    void Log(MavSeverity severity, string message);
}

/// <summary>Итог прогона.</summary>
/// <param name="Passed">Приёмка пройдена целиком.</param>
/// <param name="FailedStage">Стадия, на которой остановились. <c>null</c>, если дошли до конца.</param>
/// <param name="FailureReason">Причина остановки для оператора.</param>
public sealed record CalibrationRunResult(
    bool Passed,
    CalibrationStage? FailedStage,
    string? FailureReason,
    IReadOnlyList<CheckResult> Checks,
    IReadOnlyList<ParamWriteRecord> Writes,
    IReadOnlyList<CompassSlot> TopologyBefore,
    IReadOnlyList<CompassSlot> TopologyAfter,
    IReadOnlyList<StatusTextEvent> PrearmMessages,
    string? FirmwareBanner,
    DateTimeOffset StartedUtc,
    DateTimeOffset EndedUtc)
{
    /// <summary>
    /// Параметры, не прошедшие проверку, — все, а не только тот, на котором
    /// прогон остановился.
    /// </summary>
    public IReadOnlyList<ParamMismatch> Mismatches { get; init; } = Array.Empty<ParamMismatch>();

    /// <summary>
    /// Ключ прогона в реестре. <c>0</c> — прогон не был открыт (отказ до
    /// первой записи).
    /// </summary>
    /// <remarks>
    /// Нужен повторной записи параметра: попытка исправления — такое же
    /// свидетельство, как и сам прогон, и она обязана лечь в тот же прогон, а
    /// не потеряться.
    /// </remarks>
    public long RunId { get; init; }

    /// <summary>Порт, на котором выполнялся прогон, — по нему возвращаются к борту для повторной записи.</summary>
    public string PortName { get; init; } = string.Empty;
}

/// <summary>Строка реестра для списка истории.</summary>
public sealed record RunSummary(
    long RunId,
    string UnitId,
    DateTimeOffset StartedUtc,
    string Operator,
    string ReferenceFile,
    string ReferenceHash,
    bool? Passed,
    string? FailedStage,
    int FailedCheckCount);

/// <summary>
/// Реестр прогонов. Пишется по ходу дела, а не одной транзакцией в конце:
/// убитый процесс должен оставлять восстановимый частичный прогон.
/// </summary>
public interface ICalibrationStore
{
    /// <summary>Создаёт или мигрирует схему. Вызывается один раз при старте.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>Открывает прогон и возвращает его идентификатор.</summary>
    Task<long> BeginRunAsync(
        CalibrationRequest request,
        string referenceHash,
        string appVersion,
        CancellationToken ct = default);

    Task RecordWriteAsync(long runId, ParamWriteRecord record, CancellationToken ct = default);

    Task RecordCheckAsync(long runId, CheckResult result, CancellationToken ct = default);

    Task RecordMessageAsync(long runId, StatusTextEvent message, CancellationToken ct = default);

    /// <summary>Закрывает прогон итогом. Прогон без этого вызова остаётся прерванным.</summary>
    Task CompleteRunAsync(long runId, CalibrationRunResult result, CancellationToken ct = default);

    /// <summary>Помечает прогоны, брошенные предыдущим запуском процесса.</summary>
    Task<int> SweepAbandonedRunsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<RunSummary>> ListRunsAsync(
        string? unitIdFilter = null,
        int limit = 200,
        CancellationToken ct = default);

    /// <summary>Есть ли открытый прогон — используется блокировкой обновления.</summary>
    bool HasOpenRun { get; }
}
