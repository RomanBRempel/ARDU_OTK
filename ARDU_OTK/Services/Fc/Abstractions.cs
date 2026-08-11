using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ARDU_OTK.Services.Fc.Mavlink;

namespace ARDU_OTK.Services.Fc;

/// <summary>
/// Типы параметров, которыми пользуется ArduPilot. Прошивка присылает именно
/// эти четыре и никогда не сводит всё к REAL32.
/// </summary>
/// <remarks>
/// Целые кладутся в поле <c>float param_value</c> обычным приведением, а не
/// побитово: раскодировать нужно через <c>(int)MathF.Round(v)</c>.
/// </remarks>
public enum MavParamType : byte
{
    Int8 = 2,
    Int16 = 4,
    Int32 = 6,
    Real32 = 9,
}

/// <summary>Результат выполнения команды (<c>MAV_RESULT</c>).</summary>
public enum MavResult : byte
{
    Accepted = 0,
    TemporarilyRejected = 1,
    Denied = 2,
    Unsupported = 3,
    Failed = 4,
    InProgress = 5,
    Cancelled = 6,
}

/// <summary>Уровни <c>MAV_SEVERITY</c>. Меньше значение — тяжелее.</summary>
public enum MavSeverity : byte
{
    Emergency = 0,
    Alert = 1,
    Critical = 2,
    Error = 3,
    Warning = 4,
    Notice = 5,
    Info = 6,
    Debug = 7,
}

/// <summary>Идентификаторы команд, которые использует процедура.</summary>
public static class MavCommand
{
    public const ushort PreflightCalibration = 241;
    public const ushort PreflightRebootShutdown = 246;
    public const ushort RunPrearmChecks = 401;
    public const ushort FixedMagCalYaw = 42006;
}

/// <summary>Биты <c>MAV_SYS_STATUS_SENSOR</c>, нужные приёмке.</summary>
public static class SysStatusSensor
{
    public const uint Gyro3D = 0x01;
    public const uint Accel3D = 0x02;
    public const uint Mag3D = 0x04;
    public const uint PrearmCheck = 0x10000000;
}

/// <summary>Значение параметра, как его отдала прошивка.</summary>
public readonly record struct ParamValue(string Name, float Value, MavParamType Type)
{
    /// <summary>Целочисленное представление. Осмысленно только для целых типов.</summary>
    public int AsInt() => (int)System.MathF.Round(Value);

    public bool IsInteger => Type is MavParamType.Int8 or MavParamType.Int16 or MavParamType.Int32;
}

/// <summary>Сообщение <c>STATUSTEXT</c>, уже собранное из чанков.</summary>
public sealed record StatusTextEvent(MavSeverity Severity, string Text, System.DateTimeOffset ReceivedUtc);

/// <summary>Ориентация борта из <c>ATTITUDE</c>. Углы в радианах, рыскание — от истинного севера.</summary>
public readonly record struct AttitudeSample(float RollRad, float PitchRad, float YawRad);

/// <summary>
/// Магнитное поле одного компаса, миллигауссы, скорректированное прошивкой.
/// Экземпляр 0 приходит из <c>RAW_IMU</c>, 1 — из <c>SCALED_IMU2</c>, 2 — из <c>SCALED_IMU3</c>.
/// </summary>
public readonly record struct MagSample(int Instance, short X, short Y, short Z)
{
    /// <summary>Модуль вектора поля в миллигауссах.</summary>
    public double Magnitude => System.Math.Sqrt((double)X * X + (double)Y * Y + (double)Z * Z);

    /// <summary>Все три оси нулевые — прошивка отдаёт такое для несуществующего компаса.</summary>
    public bool IsEmpty => X == 0 && Y == 0 && Z == 0;
}

/// <summary>Состояние GPS из <c>GPS_RAW_INT</c>.</summary>
public readonly record struct GpsFix(byte FixType, int LatE7, int LonE7, byte SatellitesVisible)
{
    public bool Is3D => FixType >= 3;

    public double LatitudeDeg => LatE7 / 1e7;

    public double LongitudeDeg => LonE7 / 1e7;
}

/// <summary>Биты здоровья датчиков из <c>SYS_STATUS</c>.</summary>
public readonly record struct SensorHealth(uint Present, uint Enabled, uint Health)
{
    /// <summary>Датчик исправен только когда бит стоит во всех трёх масках.</summary>
    public bool IsOk(uint bit) => (Present & bit) != 0 && (Enabled & bit) != 0 && (Health & bit) != 0;

    /// <summary>Бит присутствует и включён — то есть о нём вообще можно судить.</summary>
    public bool IsReported(uint bit) => (Present & bit) != 0 && (Enabled & bit) != 0;
}

/// <summary>
/// Последнее известное состояние борта для индикатора: то, что показывают
/// оператору непрерывно, а не измеряют по окну.
/// </summary>
/// <param name="Attitude">Ориентация, радианы. <c>null</c>, если ATTITUDE ещё не приходил.</param>
/// <param name="VoltageVolts">Напряжение батареи. <c>null</c>, если борт его не сообщает.</param>
/// <param name="CurrentAmperes">Ток. <c>null</c>, если не измеряется.</param>
/// <param name="BatteryPercent">Остаток заряда в процентах, если известен.</param>
/// <param name="ModeName">Полётный режим, уже расшифрованный.</param>
/// <param name="Armed">Двигатели взведены (<c>MAV_MODE_FLAG_SAFETY_ARMED</c>).</param>
/// <param name="VehicleType">Значение <c>HEARTBEAT.type</c>.</param>
/// <param name="Mags">Последние показания магнитометров по экземплярам.</param>
/// <param name="UpdatedUtc">Когда состояние обновлялось последний раз.</param>
public sealed record VehicleLiveState(
    AttitudeSample? Attitude,
    double? VoltageVolts,
    double? CurrentAmperes,
    int? BatteryPercent,
    string ModeName,
    bool Armed,
    byte VehicleType,
    IReadOnlyList<MagSample> Mags,
    System.DateTimeOffset UpdatedUtc);

/// <summary>Срез телеметрии, усреднённый по окну наблюдения.</summary>
public sealed record TelemetrySnapshot(
    AttitudeSample Attitude,
    IReadOnlyList<MagSample> Mags,
    SensorHealth? Health,
    int AttitudeSampleCount);

/// <summary>
/// Канал связи с бортом. Прячет за собой кадрирование MAVLink, последовательный
/// порт и переподключение после ребута.
/// </summary>
/// <remarks>
/// Все методы бросают <see cref="VehicleLinkException"/> на ошибках протокола и
/// таймаутах. Молчаливых отказов нет: если операция не подтверждена бортом,
/// вызывающий обязан узнать об этом исключением, а не по значению по умолчанию.
/// </remarks>
public interface IVehicleLink : System.IAsyncDisposable
{
    bool IsConnected { get; }

    byte TargetSystem { get; }

    byte TargetComponent { get; }

    /// <summary>Строка версии прошивки из баннера, если он был получен.</summary>
    string? FirmwareBanner { get; }

    /// <summary>
    /// Последнее известное состояние борта. <c>null</c>, пока не пришло ни
    /// одного сообщения телеметрии.
    /// </summary>
    VehicleLiveState? LiveState { get; }

    /// <summary>Собранные <c>STATUSTEXT</c>. Подписка живёт всё время соединения.</summary>
    event System.EventHandler<StatusTextEvent>? StatusTextReceived;

    /// <summary>Открывает порт и ждёт первый <c>HEARTBEAT</c>.</summary>
    Task ConnectAsync(string portName, System.TimeSpan timeout, CancellationToken ct);

    /// <summary>Читает параметр по имени. Индекс не используется — он нестабилен.</summary>
    Task<ParamValue> ReadParamAsync(string name, CancellationToken ct);

    /// <summary>
    /// Пишет параметр. Подтверждение эхом не считается: проверку чтением делает
    /// вызывающий слой.
    /// </summary>
    Task WriteParamAsync(string name, float value, MavParamType type, CancellationToken ct);

    /// <summary>Отправляет <c>COMMAND_LONG</c> и ждёт <c>COMMAND_ACK</c>.</summary>
    Task<MavResult> SendCommandAsync(
        ushort command,
        float p1, float p2, float p3, float p4, float p5, float p6, float p7,
        System.TimeSpan ackTimeout,
        CancellationToken ct);

    /// <summary>Ждёт трёхмерный фикс GPS — он нужен модели магнитного поля.</summary>
    Task<GpsFix> WaitForGpsFixAsync(System.TimeSpan timeout, CancellationToken ct);

    /// <summary>Собирает срез телеметрии за окно наблюдения.</summary>
    Task<TelemetrySnapshot> CaptureTelemetryAsync(System.TimeSpan window, CancellationToken ct);

    /// <summary>
    /// Перезагружает борт и дожидается его возвращения. Порт переоткрывается
    /// заново: номер COM после ребута может смениться.
    /// </summary>
    Task RebootAndReconnectAsync(System.TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Запускает предполётные проверки и собирает <c>PreArm:</c> сообщения за
    /// окно ожидания.
    /// </summary>
    Task<PrearmReport> RunPrearmChecksAsync(System.TimeSpan window, CancellationToken ct);
}

/// <summary>
/// Файловый обмен с бортом по MAVFTP.
/// </summary>
/// <remarks>
/// <para>
/// Отдельный интерфейс, а не члены <see cref="IVehicleLink"/>: файлы нужны
/// только скриптовой части, и процедура калибровки не должна зависеть от того,
/// умеет ли канал их читать. Прошивка без MAVFTP — законный случай, и он обязан
/// отличаться от сбоя связи.
/// </para>
/// <para>
/// 🔴 Обмен последовательный по одному куску за раз, без пакетного чтения
/// (<c>BurstReadFile</c>). Скрипты Lua — единицы килобайт, выигрыш пакетного
/// режима на таком объёме измеряется долями секунды, а цена — отдельный конечный
/// автомат с досылкой потерянных кусков и разбором признака завершения.
/// </para>
/// </remarks>
public interface IVehicleFileTransfer
{
    /// <summary>Перечисляет содержимое каталога на карте борта.</summary>
    /// <param name="path">Путь без ведущего слэша, например <c>APM/scripts</c>.</param>
    /// <exception cref="VehicleLinkException">Каталога нет, либо прошивка не поддерживает MAVFTP.</exception>
    Task<IReadOnlyList<MavFtpEntry>> ListDirectoryAsync(string path, CancellationToken ct);

    /// <summary>Читает файл целиком.</summary>
    /// <exception cref="VehicleLinkException">Файла нет либо чтение прервано отказом.</exception>
    Task<byte[]> ReadFileAsync(string path, System.IProgress<long>? progress, CancellationToken ct);

    /// <summary>
    /// Создаёт файл заново и записывает содержимое.
    /// </summary>
    /// <remarks>
    /// Существующий файл перезаписывается: частичная запись поверх старого
    /// содержимого оставила бы на борту склейку двух скриптов, которая
    /// синтаксически верна и делает не то.
    /// </remarks>
    /// <exception cref="VehicleLinkException">Запись отклонена бортом.</exception>
    Task WriteFileAsync(string path, ReadOnlyMemory<byte> content, System.IProgress<long>? progress, CancellationToken ct);

    /// <summary>
    /// Удаляет файл с карты борта.
    /// </summary>
    /// <remarks>
    /// Нужно для скрипта, который есть на целевом борту и которого нет в
    /// эталоне: оставленный лишний скрипт исполняется наравне с остальными, и
    /// изделие ведёт себя не так, как эталонное, при полностью совпавших
    /// параметрах.
    /// </remarks>
    /// <exception cref="VehicleLinkException">Удаление отклонено бортом.</exception>
    Task RemoveFileAsync(string path, CancellationToken ct);
}

/// <summary>Итог предполётных проверок.</summary>
/// <param name="CommandResult">Результат команды 401. <c>null</c>, если прошивка её не знает.</param>
/// <param name="Messages">Собранные строки <c>PreArm:</c> без префикса.</param>
/// <param name="Health">Последний <c>SYS_STATUS</c> за окно, если он был.</param>
public sealed record PrearmReport(
    MavResult? CommandResult,
    IReadOnlyList<StatusTextEvent> Messages,
    SensorHealth? Health)
{
    /// <summary>Строки, относящиеся к компасу.</summary>
    public IEnumerable<StatusTextEvent> CompassMessages =>
        System.Linq.Enumerable.Where(Messages, m => m.Text.Contains("ompass", System.StringComparison.Ordinal)
                                                 || m.Text.Contains("mag field", System.StringComparison.OrdinalIgnoreCase));
}

/// <summary>Ошибка канала связи: таймаут, отказ порта, нарушение протокола.</summary>
public sealed class VehicleLinkException : System.Exception
{
    public VehicleLinkException(string message) : base(message)
    {
    }

    public VehicleLinkException(string message, System.Exception inner) : base(message, inner)
    {
    }
}
