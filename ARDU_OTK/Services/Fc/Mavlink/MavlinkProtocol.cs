using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace ARDU_OTK.Services.Fc.Mavlink;

/// <summary>
/// Числовые идентификаторы сообщений MAVLink, которыми пользуется приложение.
/// </summary>
public static class MavMessageId
{
    public const uint Heartbeat = 0;
    public const uint SysStatus = 1;
    public const uint ParamRequestRead = 20;
    public const uint ParamValue = 22;
    public const uint ParamSet = 23;
    public const uint GpsRawInt = 24;
    public const uint RawImu = 27;
    public const uint Attitude = 30;
    public const uint CommandLong = 76;
    public const uint CommandAck = 77;
    public const uint ScaledImu2 = 116;
    public const uint ScaledImu3 = 129;
    public const uint StatusText = 253;
}

/// <summary>
/// Константы кадрирования MAVLink v2. Держатся в одном месте, потому что ими
/// пользуются и разборщик, и упаковщик, а расхождение между ними даёт молчаливо
/// битые кадры.
/// </summary>
internal static class MavlinkFraming
{
    /// <summary>Признак начала кадра версии 2.</summary>
    internal const byte StartOfFrameV2 = 0xFD;

    /// <summary>Признак начала кадра версии 1. Мы такие кадры только пропускаем.</summary>
    internal const byte StartOfFrameV1 = 0xFE;

    /// <summary>STX, len, incompat, compat, seq, sysid, compid, msgid(3).</summary>
    internal const int HeaderLength = 10;

    /// <summary>STX, len, seq, sysid, compid, msgid(1) — заголовок кадра версии 1.</summary>
    internal const int HeaderLengthV1 = 6;

    internal const int ChecksumLength = 2;

    /// <summary>Блок подписи: link id (1), timestamp (6), сигнатура (6).</summary>
    internal const int SignatureLength = 13;

    internal const int MaxPayloadLength = 255;

    /// <summary>Бит <c>MAVLINK_IFLAG_SIGNED</c> в <c>incompat_flags</c>.</summary>
    internal const byte IncompatFlagSigned = 0x01;

    internal const int MaxFrameLength =
        HeaderLength + MaxPayloadLength + ChecksumLength + SignatureLength;
}

/// <summary>
/// Контрольная сумма MAVLink — CRC-16/MCRF4XX (полином 0x1021 в отражённой
/// форме, начальное значение 0xFFFF, без финального XOR).
/// </summary>
/// <remarks>
/// Считается по заголовку начиная с поля <c>len</c> (то есть без байта STX),
/// затем по полезной нагрузке, и в самом конце в неё домешивается
/// <c>CRC_EXTRA</c> — байт, зависящий от имён и типов полей сообщения. Именно
/// <c>CRC_EXTRA</c> ловит рассогласование версий определений: если он неверен,
/// каждое сообщение этого типа будет молча отброшено как битое.
/// </remarks>
public static class MavlinkCrc
{
    /// <summary>Начальное значение аккумулятора.</summary>
    public const ushort InitialValue = 0xFFFF;

    /// <summary>
    /// Один шаг <c>crc_accumulate</c> из эталонной реализации MAVLink.
    /// </summary>
    public static ushort Accumulate(byte value, ushort crc)
    {
        unchecked
        {
            byte tmp = (byte)(value ^ (byte)(crc & 0xFF));
            tmp ^= (byte)(tmp << 4);
            return (ushort)((crc >> 8) ^ (tmp << 8) ^ (tmp << 3) ^ (tmp >> 4));
        }
    }

    /// <summary>
    /// Считает контрольную сумму по указанному участку кадра и домешивает
    /// <paramref name="crcExtra"/>.
    /// </summary>
    /// <param name="data">Заголовок начиная с <c>len</c> плюс полезная нагрузка.</param>
    /// <param name="crcExtra">Байт <c>CRC_EXTRA</c> сообщения.</param>
    public static ushort Compute(ReadOnlySpan<byte> data, byte crcExtra)
    {
        ushort crc = InitialValue;
        foreach (byte value in data)
        {
            crc = Accumulate(value, crc);
        }

        return Accumulate(crcExtra, crc);
    }

    /// <summary>
    /// Таблица <c>CRC_EXTRA</c> для поддерживаемых сообщений.
    /// </summary>
    /// <remarks>
    /// Значения взяты из генерируемых заголовков MAVLink и сверены по двум
    /// независимым местам одного и того же источника:
    /// <list type="bullet">
    /// <item>сводная таблица <c>MAVLINK_MESSAGE_CRCS</c> (кортежи
    /// <c>{msgid, crc_extra, min_len, len, ...}</c>) —
    /// https://raw.githubusercontent.com/mavlink/c_library_v2/master/common/common.h</item>
    /// <item>определения <c>MAVLINK_MSG_ID_&lt;NAME&gt;_CRC</c> в заголовке
    /// каждого сообщения, например
    /// https://raw.githubusercontent.com/mavlink/c_library_v2/master/common/mavlink_msg_attitude.h
    /// (HEARTBEAT лежит в диалекте <c>minimal</c>:
    /// https://raw.githubusercontent.com/mavlink/c_library_v2/master/minimal/mavlink_msg_heartbeat.h)</item>
    /// </list>
    /// Наизусть эти байты не восстанавливаются и по памяти не правятся: ошибка
    /// в одном значении не ломает сборку и не даёт ошибки в логе — сообщения
    /// этого типа просто перестают приходить.
    /// </remarks>
    /// <returns><c>false</c>, если сообщение вне поддерживаемого набора.</returns>
    public static bool TryGetCrcExtra(uint messageId, out byte crcExtra)
    {
        crcExtra = messageId switch
        {
            MavMessageId.Heartbeat => 50,
            MavMessageId.SysStatus => 124,
            MavMessageId.ParamRequestRead => 214,
            MavMessageId.ParamValue => 220,
            MavMessageId.ParamSet => 168,

            // Совпадение номера сообщения и CRC_EXTRA у GPS_RAW_INT — случайность,
            // а не опечатка: {24, 24, 30, 52, ...} в MAVLINK_MESSAGE_CRCS.
            MavMessageId.GpsRawInt => 24,

            MavMessageId.RawImu => 144,
            MavMessageId.Attitude => 39,
            MavMessageId.CommandLong => 152,
            MavMessageId.CommandAck => 143,
            MavMessageId.ScaledImu2 => 76,
            MavMessageId.ScaledImu3 => 46,
            MavMessageId.StatusText => 83,
            _ => 0,
        };

        return crcExtra != 0;
    }
}

/// <summary>
/// Курсор по полезной нагрузке сообщения.
/// </summary>
/// <remarks>
/// Чтение за концом буфера возвращает нули. Это не защита от ошибок, а прямая
/// реализация правила MAVLink 2 о дополнении усечённой нагрузки нулями:
/// отправитель обрезает хвостовые нулевые байты, поэтому поля-расширения (и
/// даже часть обязательных полей) могут физически отсутствовать в кадре.
/// Благодаря этому декодеры читают поля подряд, без проверок длины на каждом шаге.
/// </remarks>
internal ref struct PayloadReader
{
    private readonly ReadOnlySpan<byte> _payload;
    private int _offset;

    internal PayloadReader(ReadOnlySpan<byte> payload)
    {
        _payload = payload;
        _offset = 0;
    }

    internal byte ReadUInt8()
    {
        byte value = (uint)_offset < (uint)_payload.Length ? _payload[_offset] : (byte)0;
        _offset++;
        return value;
    }

    internal sbyte ReadInt8() => unchecked((sbyte)ReadUInt8());

    internal ushort ReadUInt16()
    {
        Span<byte> raw = stackalloc byte[sizeof(ushort)];
        Fill(raw);
        return BinaryPrimitives.ReadUInt16LittleEndian(raw);
    }

    internal short ReadInt16()
    {
        Span<byte> raw = stackalloc byte[sizeof(short)];
        Fill(raw);
        return BinaryPrimitives.ReadInt16LittleEndian(raw);
    }

    internal uint ReadUInt32()
    {
        Span<byte> raw = stackalloc byte[sizeof(uint)];
        Fill(raw);
        return BinaryPrimitives.ReadUInt32LittleEndian(raw);
    }

    internal int ReadInt32()
    {
        Span<byte> raw = stackalloc byte[sizeof(int)];
        Fill(raw);
        return BinaryPrimitives.ReadInt32LittleEndian(raw);
    }

    internal ulong ReadUInt64()
    {
        Span<byte> raw = stackalloc byte[sizeof(ulong)];
        Fill(raw);
        return BinaryPrimitives.ReadUInt64LittleEndian(raw);
    }

    internal float ReadSingle()
    {
        Span<byte> raw = stackalloc byte[sizeof(float)];
        Fill(raw);
        return BinaryPrimitives.ReadSingleLittleEndian(raw);
    }

    /// <summary>
    /// Читает поле <c>char[count]</c>.
    /// </summary>
    /// <param name="count">Объявленный размер поля.</param>
    /// <param name="isFull">
    /// <c>true</c>, если завершающего нуля в поле нет, то есть строка заняла его
    /// целиком. Для <c>STATUSTEXT</c> это признак того, что сообщение продолжается
    /// в следующем чанке.
    /// </param>
    internal string ReadChars(int count, out bool isFull)
    {
        Span<byte> raw = count <= 64 ? stackalloc byte[count] : new byte[count];
        Fill(raw);

        int terminator = raw.IndexOf((byte)0);
        isFull = terminator < 0;
        ReadOnlySpan<byte> text = isFull ? raw : raw[..terminator];
        return text.IsEmpty ? string.Empty : Encoding.UTF8.GetString(text);
    }

    /// <summary>Читает поле <c>char[count]</c>, когда признак заполненности не нужен.</summary>
    internal string ReadChars(int count) => ReadChars(count, out _);

    /// <summary>
    /// Копирует очередные <c>destination.Length</c> байт, дополняя нулями всё,
    /// что вышло за границу усечённой нагрузки.
    /// </summary>
    private void Fill(Span<byte> destination)
    {
        destination.Clear();

        int available = Math.Clamp(_payload.Length - _offset, 0, destination.Length);
        if (available > 0)
        {
            _payload.Slice(_offset, available).CopyTo(destination);
        }

        _offset += destination.Length;
    }
}

/// <summary>
/// Разобранный кадр MAVLink v2 с проверенной контрольной суммой.
/// </summary>
/// <param name="MessageId">Идентификатор сообщения (24 бита).</param>
/// <param name="SystemId">Система-отправитель.</param>
/// <param name="ComponentId">Компонент-отправитель.</param>
/// <param name="Sequence">Счётчик отправителя — по нему видны потери кадров.</param>
public sealed record MavlinkFrame(
    uint MessageId,
    byte SystemId,
    byte ComponentId,
    byte Sequence,
    ReadOnlyMemory<byte> Payload);

/// <summary>Сообщение <c>HEARTBEAT</c> (id 0).</summary>
public readonly record struct HeartbeatMessage(
    uint CustomMode,
    byte Type,
    byte Autopilot,
    byte BaseMode,
    byte SystemStatus,
    byte MavlinkVersion)
{
    /// <summary>
    /// <c>MAV_AUTOPILOT_INVALID</c>. Так помечают себя наземные станции и
    /// периферия: у них есть HEARTBEAT, но автопилотом они не являются.
    /// </summary>
    public const byte AutopilotInvalid = 8;

    /// <summary>Отправитель — настоящий автопилот, а не GCS или компаньон.</summary>
    public bool IsAutopilot => Autopilot != AutopilotInvalid;

    public static HeartbeatMessage Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        uint customMode = reader.ReadUInt32();
        byte type = reader.ReadUInt8();
        byte autopilot = reader.ReadUInt8();
        byte baseMode = reader.ReadUInt8();
        byte systemStatus = reader.ReadUInt8();
        byte mavlinkVersion = reader.ReadUInt8();
        return new HeartbeatMessage(customMode, type, autopilot, baseMode, systemStatus, mavlinkVersion);
    }
}

/// <summary>Сообщение <c>SYS_STATUS</c> (id 1) — без полей-расширений.</summary>
public readonly record struct SysStatusMessage(
    uint SensorsPresent,
    uint SensorsEnabled,
    uint SensorsHealth,
    ushort Load,
    ushort VoltageBattery,
    short CurrentBattery,
    ushort DropRateComm,
    ushort ErrorsComm,
    sbyte BatteryRemaining)
{
    public static SysStatusMessage Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        uint present = reader.ReadUInt32();
        uint enabled = reader.ReadUInt32();
        uint health = reader.ReadUInt32();
        ushort load = reader.ReadUInt16();
        ushort voltage = reader.ReadUInt16();
        short current = reader.ReadInt16();
        ushort dropRate = reader.ReadUInt16();
        ushort errorsComm = reader.ReadUInt16();

        // errors_count1..4 читаются, но не хранятся: приёмке они не нужны, а
        // пропустить их нельзя — за ними идёт battery_remaining.
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt16();

        sbyte batteryRemaining = reader.ReadInt8();

        // Дальше идут три расширения *_extended (MAVLink 2), описывающие датчики
        // с номерами 32..63. Ни один из битов SysStatusSensor туда не попадает,
        // поэтому они намеренно не разбираются.
        return new SysStatusMessage(
            present, enabled, health, load, voltage, current, dropRate, errorsComm, batteryRemaining);
    }
}

/// <summary>Сообщение <c>PARAM_VALUE</c> (id 22).</summary>
public readonly record struct ParamValueMessage(
    float ParamValue,
    ushort ParamCount,
    ushort ParamIndex,
    string ParamId,
    byte ParamType)
{
    /// <summary>
    /// Значение <c>param_index</c> в широковещательном эхе после <c>PARAM_SET</c>.
    /// Такой ответ не является подтверждением записи.
    /// </summary>
    public const ushort BroadcastIndex = 0xFFFF;

    public static ParamValueMessage Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        float value = reader.ReadSingle();
        ushort count = reader.ReadUInt16();
        ushort index = reader.ReadUInt16();
        string id = reader.ReadChars(MavlinkEncoder.ParamIdLength);
        byte type = reader.ReadUInt8();
        return new ParamValueMessage(value, count, index, id, type);
    }
}

/// <summary>Сообщение <c>GPS_RAW_INT</c> (id 24) — без полей-расширений.</summary>
public readonly record struct GpsRawIntMessage(
    ulong TimeUsec,
    int Lat,
    int Lon,
    int Alt,
    ushort Eph,
    ushort Epv,
    ushort Vel,
    ushort Cog,
    byte FixType,
    byte SatellitesVisible)
{
    public static GpsRawIntMessage Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        ulong timeUsec = reader.ReadUInt64();
        int lat = reader.ReadInt32();
        int lon = reader.ReadInt32();
        int alt = reader.ReadInt32();
        ushort eph = reader.ReadUInt16();
        ushort epv = reader.ReadUInt16();
        ushort vel = reader.ReadUInt16();
        ushort cog = reader.ReadUInt16();
        byte fixType = reader.ReadUInt8();
        byte satellites = reader.ReadUInt8();

        // Расширения alt_ellipsoid, h_acc, v_acc, vel_acc, hdg_acc, yaw приёмке
        // не нужны и не разбираются.
        return new GpsRawIntMessage(timeUsec, lat, lon, alt, eph, epv, vel, cog, fixType, satellites);
    }
}

/// <summary>
/// Показания одного инерциального блока: <c>RAW_IMU</c> (id 27),
/// <c>SCALED_IMU2</c> (id 116) и <c>SCALED_IMU3</c> (id 129).
/// </summary>
/// <remarks>
/// Три сообщения свёрнуты в один тип: раскладка полей после метки времени у них
/// совпадает, а различаются только её ширина (<c>uint64 time_usec</c> против
/// <c>uint32 time_boot_ms</c>) и наличие поля <c>id</c> у <c>RAW_IMU</c>.
/// </remarks>
public readonly record struct ImuMessage(
    short Xacc,
    short Yacc,
    short Zacc,
    short Xgyro,
    short Ygyro,
    short Zgyro,
    short Xmag,
    short Ymag,
    short Zmag,
    short Temperature)
{
    /// <summary>Разбирает <c>RAW_IMU</c>: метка времени 64-битная, есть расширение <c>id</c>.</summary>
    public static ImuMessage DecodeRawImu(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        _ = reader.ReadUInt64();
        return DecodeAxes(ref reader, hasInstanceField: true);
    }

    /// <summary>Разбирает <c>SCALED_IMU2</c> и <c>SCALED_IMU3</c>: метка времени 32-битная.</summary>
    public static ImuMessage DecodeScaledImu(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        _ = reader.ReadUInt32();
        return DecodeAxes(ref reader, hasInstanceField: false);
    }

    private static ImuMessage DecodeAxes(ref PayloadReader reader, bool hasInstanceField)
    {
        short xacc = reader.ReadInt16();
        short yacc = reader.ReadInt16();
        short zacc = reader.ReadInt16();
        short xgyro = reader.ReadInt16();
        short ygyro = reader.ReadInt16();
        short zgyro = reader.ReadInt16();
        short xmag = reader.ReadInt16();
        short ymag = reader.ReadInt16();
        short zmag = reader.ReadInt16();

        if (hasInstanceField)
        {
            // RAW_IMU.id — расширение MAVLink 2, стоящее перед temperature.
            // Номер экземпляра компаса приёмка выводит из типа сообщения, а не
            // отсюда, но байт всё равно нужно пропустить.
            _ = reader.ReadUInt8();
        }

        short temperature = reader.ReadInt16();
        return new ImuMessage(xacc, yacc, zacc, xgyro, ygyro, zgyro, xmag, ymag, zmag, temperature);
    }
}

/// <summary>Сообщение <c>ATTITUDE</c> (id 30). Углы в радианах.</summary>
public readonly record struct AttitudeMessage(
    uint TimeBootMs,
    float Roll,
    float Pitch,
    float Yaw,
    float RollSpeed,
    float PitchSpeed,
    float YawSpeed)
{
    public static AttitudeMessage Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        uint timeBootMs = reader.ReadUInt32();
        float roll = reader.ReadSingle();
        float pitch = reader.ReadSingle();
        float yaw = reader.ReadSingle();
        float rollSpeed = reader.ReadSingle();
        float pitchSpeed = reader.ReadSingle();
        float yawSpeed = reader.ReadSingle();
        return new AttitudeMessage(timeBootMs, roll, pitch, yaw, rollSpeed, pitchSpeed, yawSpeed);
    }
}

/// <summary>Сообщение <c>COMMAND_ACK</c> (id 77).</summary>
/// <remarks>
/// Обязательными являются только <c>command</c> и <c>result</c> (min_len 3);
/// остальное — расширения MAVLink 2, которых у старых прошивок в кадре может
/// физически не быть.
/// </remarks>
public readonly record struct CommandAckMessage(
    ushort Command,
    byte Result,
    byte Progress,
    int ResultParam2,
    byte TargetSystem,
    byte TargetComponent)
{
    public static CommandAckMessage Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        ushort command = reader.ReadUInt16();
        byte result = reader.ReadUInt8();
        byte progress = reader.ReadUInt8();
        int resultParam2 = reader.ReadInt32();
        byte targetSystem = reader.ReadUInt8();
        byte targetComponent = reader.ReadUInt8();
        return new CommandAckMessage(command, result, progress, resultParam2, targetSystem, targetComponent);
    }
}

/// <summary>Сообщение <c>STATUSTEXT</c> (id 253), один чанк.</summary>
/// <param name="Text">Содержимое поля <c>char[50]</c> без завершающего нуля.</param>
/// <param name="Id">
/// Идентификатор многочанкового сообщения. Ноль означает, что сообщение целое.
/// </param>
/// <param name="ChunkSeq">Номер чанка, считая с нуля.</param>
/// <param name="IsTextFieldFull">
/// Поле <c>text</c> занято целиком, завершающего нуля нет — значит, сообщение
/// продолжается в следующем чанке.
/// </param>
public readonly record struct StatusTextMessage(
    byte Severity,
    string Text,
    ushort Id,
    byte ChunkSeq,
    bool IsTextFieldFull)
{
    /// <summary>Размер поля <c>text</c>.</summary>
    public const int TextLength = 50;

    public static StatusTextMessage Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new PayloadReader(payload);
        byte severity = reader.ReadUInt8();
        string text = reader.ReadChars(TextLength, out bool isFull);

        // id и chunk_seq — расширения MAVLink 2. У прошивки без поддержки чанков
        // их в кадре нет, и дополнение нулями даёт id = 0, то есть «целое сообщение».
        ushort id = reader.ReadUInt16();
        byte chunkSeq = reader.ReadUInt8();
        return new StatusTextMessage(severity, text, id, chunkSeq, isFull);
    }
}

/// <summary>
/// Потоковый разборщик MAVLink v2: принимает байты из последовательного порта
/// произвольными кусками и отдаёт целые проверенные кадры.
/// </summary>
/// <remarks>
/// Разборщик рассчитан на то, что поток начинается с середины кадра (порт
/// открыт, когда борт уже что-то передавал) и что в нём попадается мусор.
/// Ресинхронизация идёт сдвигом на один байт от неудачного кандидата, а не
/// пропуском объявленной длины: доверять длине из непроверенного заголовка
/// нельзя, иначе следующий настоящий кадр будет проглочен вместе с мусором.
/// </remarks>
public sealed class MavlinkParser
{
    private byte[] _buffer = new byte[8 * 1024];
    private int _start;
    private int _end;

    /// <summary>
    /// Признак того, что предыдущий кадр разобран успешно и текущая позиция —
    /// настоящая граница кадров. Только в этом состоянии разрешено доверять
    /// полю длины у сообщения, контрольную сумму которого проверить нечем.
    /// </summary>
    private bool _synchronised;

    /// <summary>
    /// Сколько кадров отброшено: битая контрольная сумма, подпись, неизвестные
    /// обязательные флаги, версия 1 или сообщение вне поддерживаемого набора.
    /// Величина диагностическая — по её росту видно качество линии.
    /// </summary>
    public long DroppedFrames { get; private set; }

    /// <summary>Добавляет очередную порцию байт из порта.</summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        EnsureRoom(data.Length);
        data.CopyTo(_buffer.AsSpan(_end));
        _end += data.Length;
    }

    /// <summary>
    /// Достаёт очередной целый кадр.
    /// </summary>
    /// <returns>
    /// <c>false</c>, если накопленных байт не хватает. Вызывать в цикле до
    /// <c>false</c> после каждого <see cref="Append"/>.
    /// </returns>
    public bool TryReadFrame([NotNullWhen(true)] out MavlinkFrame? frame)
    {
        frame = null;

        while (true)
        {
            if (!SeekStartOfFrame())
            {
                return false;
            }

            if (_buffer[_start] == MavlinkFraming.StartOfFrameV1)
            {
                if (!TryAdvancePastV1Frame())
                {
                    return false;
                }

                continue;
            }

            if (_end - _start < MavlinkFraming.HeaderLength)
            {
                return false;
            }

            int payloadLength = _buffer[_start + 1];
            byte incompatFlags = _buffer[_start + 2];
            int signatureLength = (incompatFlags & MavlinkFraming.IncompatFlagSigned) != 0
                ? MavlinkFraming.SignatureLength
                : 0;
            int totalLength =
                MavlinkFraming.HeaderLength + payloadLength + MavlinkFraming.ChecksumLength + signatureLength;

            if (_end - _start < totalLength)
            {
                return false;
            }

            uint messageId = (uint)(_buffer[_start + 7]
                | (_buffer[_start + 8] << 8)
                | (_buffer[_start + 9] << 16));

            if (!MavlinkCrc.TryGetCrcExtra(messageId, out byte crcExtra))
            {
                // Сообщение вне нашего набора: CRC_EXTRA неизвестен, проверить
                // кадр нечем. Пропускать по объявленной длине можно только
                // стоя на достоверной границе кадров.
                if (_synchronised)
                {
                    _start += totalLength;
                    DroppedFrames++;
                    continue;
                }

                Desynchronise();
                continue;
            }

            int checksumOffset = _start + MavlinkFraming.HeaderLength + payloadLength;
            ushort expected = MavlinkCrc.Compute(
                _buffer.AsSpan(_start + 1, MavlinkFraming.HeaderLength - 1 + payloadLength),
                crcExtra);
            ushort actual = (ushort)(_buffer[checksumOffset] | (_buffer[checksumOffset + 1] << 8));

            if (expected != actual)
            {
                Desynchronise();
                continue;
            }

            // Контрольная сумма сошлась — кадрирование достоверно, значит границу
            // следующего кадра уже можно вычислять по длине.
            _synchronised = true;

            byte sequence = _buffer[_start + 4];
            byte systemId = _buffer[_start + 5];
            byte componentId = _buffer[_start + 6];
            byte[] payload = _buffer.AsSpan(_start + MavlinkFraming.HeaderLength, payloadLength).ToArray();
            _start += totalLength;

            if (incompatFlags != 0)
            {
                // Бит 0x01 — подписанный кадр; проверять подпись мы не умеем и
                // принимать её на веру не будем. Любой другой ненулевой бит
                // incompat_flags по стандарту тоже обязывает отбросить кадр.
                // Кадр при этом полностью вычитан из буфера, а не «потерян» в
                // потоке, поэтому разбор следующего пойдёт с верной границы.
                DroppedFrames++;
                continue;
            }

            frame = new MavlinkFrame(messageId, systemId, componentId, sequence, payload);
            return true;
        }
    }

    /// <summary>
    /// Сдвигает начало окна к ближайшему байту STX.
    /// </summary>
    /// <returns><c>false</c>, если кандидатов в буфере не осталось.</returns>
    private bool SeekStartOfFrame()
    {
        int index = _start;
        while (index < _end
            && _buffer[index] != MavlinkFraming.StartOfFrameV2
            && _buffer[index] != MavlinkFraming.StartOfFrameV1)
        {
            index++;
        }

        if (index != _start)
        {
            // Между кадрами оказался мусор — граница кадров больше не достоверна.
            _synchronised = false;
            _start = index;
        }

        return _start < _end;
    }

    /// <summary>
    /// Обрабатывает кандидата в кадр MAVLink v1.
    /// </summary>
    /// <remarks>
    /// Такие кадры не разбираются: приложение говорит только на второй версии.
    /// Но и просто сдвинуться на байт нельзя — внутри полезной нагрузки v1
    /// вполне может встретиться 0xFD и породить ложный кадр v2. Поэтому кадр
    /// сначала проверяется по контрольной сумме (алгоритм и CRC_EXTRA у обеих
    /// версий одни и те же) и, если он настоящий, пропускается целиком.
    /// </remarks>
    /// <returns><c>false</c>, если байт в буфере ещё не хватает.</returns>
    private bool TryAdvancePastV1Frame()
    {
        if (_end - _start < 2)
        {
            return false;
        }

        int payloadLength = _buffer[_start + 1];
        int totalLength = MavlinkFraming.HeaderLengthV1 + payloadLength + MavlinkFraming.ChecksumLength;
        if (_end - _start < totalLength)
        {
            return false;
        }

        uint messageId = _buffer[_start + 5];
        if (MavlinkCrc.TryGetCrcExtra(messageId, out byte crcExtra))
        {
            int checksumOffset = _start + MavlinkFraming.HeaderLengthV1 + payloadLength;
            ushort expected = MavlinkCrc.Compute(
                _buffer.AsSpan(_start + 1, MavlinkFraming.HeaderLengthV1 - 1 + payloadLength),
                crcExtra);
            ushort actual = (ushort)(_buffer[checksumOffset] | (_buffer[checksumOffset + 1] << 8));

            if (expected == actual)
            {
                _start += totalLength;
                _synchronised = true;
                DroppedFrames++;
                return true;
            }
        }

        Desynchronise();
        return true;
    }

    /// <summary>Сдвигает окно на байт после неудачной попытки разбора.</summary>
    private void Desynchronise()
    {
        _synchronised = false;
        _start++;
    }

    private void EnsureRoom(int required)
    {
        if (_start == _end)
        {
            _start = 0;
            _end = 0;
        }

        if (_buffer.Length - _end >= required)
        {
            return;
        }

        int used = _end - _start;
        if (_start > 0)
        {
            _buffer.AsSpan(_start, used).CopyTo(_buffer);
            _start = 0;
            _end = used;
        }

        if (_buffer.Length - _end >= required)
        {
            return;
        }

        // Незавершённый кадр удерживает не больше MaxFrameLength байт, поэтому
        // рост буфера ограничен размером одной порции чтения из порта.
        int capacity = _buffer.Length;
        while (capacity - _end < required)
        {
            capacity *= 2;
        }

        Array.Resize(ref _buffer, capacity);
    }
}

/// <summary>
/// Упаковщик исходящих сообщений MAVLink v2.
/// </summary>
/// <remarks>
/// Экземпляр хранит счётчик последовательности, поэтому потокобезопасным не
/// является: писать в порт должен один поток (или под общей блокировкой).
/// </remarks>
public sealed class MavlinkEncoder
{
    /// <summary>Размер поля <c>param_id</c> в <c>PARAM_*</c>.</summary>
    public const int ParamIdLength = 16;

    private const int ParamRequestReadLength = 20;
    private const int ParamSetLength = 23;
    private const int CommandLongLength = 33;

    private byte _sequence;

    /// <summary>
    /// Создаёт упаковщик от имени наземной станции.
    /// </summary>
    /// <param name="systemId">
    /// Номер системы отправителя. 255 — общепринятый номер наземной станции;
    /// он не должен совпадать с номером борта.
    /// </param>
    /// <param name="componentId">
    /// Компонент отправителя. 190 — <c>MAV_COMP_ID_MISSIONPLANNER</c>: ArduPilot
    /// адресует ответы <c>PARAM_VALUE</c> и <c>COMMAND_ACK</c> именно тому
    /// компоненту, который прислал запрос.
    /// </param>
    public MavlinkEncoder(byte systemId = 255, byte componentId = 190)
    {
        SystemId = systemId;
        ComponentId = componentId;
    }

    public byte SystemId { get; }

    public byte ComponentId { get; }

    /// <summary>Собирает <c>PARAM_REQUEST_READ</c> (id 20).</summary>
    /// <param name="paramIndex">
    /// -1 означает «искать по имени». Индексы параметров нестабильны между
    /// прошивками, поэтому используется только этот режим.
    /// </param>
    public byte[] EncodeParamRequestRead(
        byte targetSystem,
        byte targetComponent,
        string paramId,
        short paramIndex)
    {
        ArgumentNullException.ThrowIfNull(paramId);

        Span<byte> payload = stackalloc byte[ParamRequestReadLength];
        payload.Clear();
        BinaryPrimitives.WriteInt16LittleEndian(payload, paramIndex);
        payload[2] = targetSystem;
        payload[3] = targetComponent;
        WriteParamId(payload.Slice(4, ParamIdLength), paramId);
        return Frame(MavMessageId.ParamRequestRead, payload);
    }

    /// <summary>Собирает <c>PARAM_SET</c> (id 23).</summary>
    public byte[] EncodeParamSet(
        byte targetSystem,
        byte targetComponent,
        string paramId,
        float value,
        byte paramType)
    {
        ArgumentNullException.ThrowIfNull(paramId);

        Span<byte> payload = stackalloc byte[ParamSetLength];
        payload.Clear();
        BinaryPrimitives.WriteSingleLittleEndian(payload, value);
        payload[4] = targetSystem;
        payload[5] = targetComponent;
        WriteParamId(payload.Slice(6, ParamIdLength), paramId);
        payload[22] = paramType;
        return Frame(MavMessageId.ParamSet, payload);
    }

    /// <summary>Собирает <c>COMMAND_LONG</c> (id 76).</summary>
    public byte[] EncodeCommandLong(
        byte targetSystem,
        byte targetComponent,
        ushort command,
        byte confirmation,
        float param1,
        float param2,
        float param3,
        float param4,
        float param5,
        float param6,
        float param7)
    {
        Span<byte> payload = stackalloc byte[CommandLongLength];
        payload.Clear();
        BinaryPrimitives.WriteSingleLittleEndian(payload[0..], param1);
        BinaryPrimitives.WriteSingleLittleEndian(payload[4..], param2);
        BinaryPrimitives.WriteSingleLittleEndian(payload[8..], param3);
        BinaryPrimitives.WriteSingleLittleEndian(payload[12..], param4);
        BinaryPrimitives.WriteSingleLittleEndian(payload[16..], param5);
        BinaryPrimitives.WriteSingleLittleEndian(payload[20..], param6);
        BinaryPrimitives.WriteSingleLittleEndian(payload[24..], param7);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[28..], command);
        payload[30] = targetSystem;
        payload[31] = targetComponent;
        payload[32] = confirmation;
        return Frame(MavMessageId.CommandLong, payload);
    }

    /// <summary>
    /// Кладёт имя параметра в поле <c>char[16]</c>.
    /// </summary>
    /// <remarks>
    /// Имя длиной ровно 16 символов записывается без завершающего нуля — это
    /// штатный случай, а не переполнение. Имена параметров ArduPilot всегда
    /// ASCII, поэтому символ занимает ровно байт.
    /// </remarks>
    private static void WriteParamId(Span<byte> destination, string paramId)
    {
        destination.Clear();
        int length = Math.Min(paramId.Length, destination.Length);
        if (length > 0)
        {
            Encoding.ASCII.GetBytes(paramId.AsSpan(0, length), destination);
        }
    }

    /// <summary>
    /// Заворачивает полезную нагрузку в кадр MAVLink v2.
    /// </summary>
    /// <remarks>
    /// Хвостовые нулевые байты обрезаются — это обязательная часть формата, а не
    /// оптимизация: приёмник обязан дополнять короткую нагрузку нулями сам.
    /// Последний байт не обрезается никогда, поэтому длина кадра не опускается
    /// ниже единицы (так же поступает эталонная реализация
    /// <c>_mav_trim_payload</c>).
    /// </remarks>
    private byte[] Frame(uint messageId, ReadOnlySpan<byte> payload)
    {
        if (!MavlinkCrc.TryGetCrcExtra(messageId, out byte crcExtra))
        {
            throw new ArgumentOutOfRangeException(
                nameof(messageId),
                messageId,
                "Для сообщения нет CRC_EXTRA: собрать кадр невозможно.");
        }

        int length = payload.Length;
        while (length > 1 && payload[length - 1] == 0)
        {
            length--;
        }

        byte[] frame = new byte[MavlinkFraming.HeaderLength + length + MavlinkFraming.ChecksumLength];
        frame[0] = MavlinkFraming.StartOfFrameV2;
        frame[1] = (byte)length;
        frame[2] = 0;
        frame[3] = 0;
        frame[4] = _sequence;
        frame[5] = SystemId;
        frame[6] = ComponentId;
        frame[7] = (byte)messageId;
        frame[8] = (byte)(messageId >> 8);
        frame[9] = (byte)(messageId >> 16);
        unchecked
        {
            _sequence++;
        }

        payload[..length].CopyTo(frame.AsSpan(MavlinkFraming.HeaderLength));

        ushort crc = MavlinkCrc.Compute(
            frame.AsSpan(1, MavlinkFraming.HeaderLength - 1 + length),
            crcExtra);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(MavlinkFraming.HeaderLength + length), crc);
        return frame;
    }
}
