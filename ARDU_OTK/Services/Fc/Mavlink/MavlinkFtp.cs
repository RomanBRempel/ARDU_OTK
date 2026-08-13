using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ARDU_OTK.Services.Fc.Mavlink;

/// <summary>Код операции MAVFTP.</summary>
/// <remarks>
/// Значения заданы протоколом и совпадают у ArduPilot, PX4 и наземных станций.
/// Приложению нужны только чтение и запись; операции над каталогами оставлены в
/// перечислении ради разбора ответов, а не ради вызова.
/// </remarks>
public enum MavFtpOpcode : byte
{
    None = 0,
    TerminateSession = 1,
    ResetSessions = 2,
    ListDirectory = 3,
    OpenFileRO = 4,
    ReadFile = 5,
    CreateFile = 6,
    WriteFile = 7,
    RemoveFile = 8,
    CreateDirectory = 9,
    RemoveDirectory = 10,
    OpenFileWO = 11,
    TruncateFile = 12,
    Rename = 13,
    CalcFileCrc32 = 14,
    BurstReadFile = 15,

    Ack = 128,
    Nak = 129,
}

/// <summary>Код отказа MAVFTP — первый байт данных в ответе <see cref="MavFtpOpcode.Nak"/>.</summary>
public enum MavFtpError : byte
{
    None = 0,
    Fail = 1,

    /// <summary>Отказ файловой системы; во втором байте данных лежит <c>errno</c>.</summary>
    FailErrno = 2,
    InvalidDataSize = 3,
    InvalidSession = 4,
    NoSessionsAvailable = 5,

    /// <summary>Конец файла или каталога. Не ошибка: штатное завершение чтения.</summary>
    EndOfFile = 6,
    UnknownCommand = 7,
    FileExists = 8,
    FileProtected = 9,
    FileNotFound = 10,
}

/// <summary>
/// Разобранная нагрузка MAVFTP: заголовок фиксированной раскладки плюс данные.
/// </summary>
/// <remarks>
/// 🔴 Поле <c>payload</c> сообщения объявлено как <c>uint8_t[251]</c>, и
/// смещения внутри него заданы протоколом жёстко. Собирать нагрузку короче
/// нельзя — приёмник читает поля по абсолютным смещениям, и укороченный буфер
/// даёт не отказ, а чтение мусора из соседних полей.
/// </remarks>
/// <param name="Sequence">Номер запроса. Ответ приходит с номером на единицу больше.</param>
/// <param name="Session">Номер открытой сессии; для операций без сессии — 0.</param>
/// <param name="Opcode">Операция либо <see cref="MavFtpOpcode.Ack"/>/<see cref="MavFtpOpcode.Nak"/> в ответе.</param>
/// <param name="RequestOpcode">В ответе — операция, на которую он отвечает.</param>
/// <param name="BurstComplete">Признак конца пакетного чтения. Приложение пакетное чтение не использует.</param>
/// <param name="Offset">Смещение в файле либо порядковый номер записи каталога.</param>
/// <param name="Data">Данные операции: имя файла, прочитанный кусок, код отказа.</param>
public readonly record struct MavFtpPayload(
    ushort Sequence,
    byte Session,
    MavFtpOpcode Opcode,
    MavFtpOpcode RequestOpcode,
    byte BurstComplete,
    uint Offset,
    ReadOnlyMemory<byte> Data)
{
    /// <summary>Полная длина поля <c>payload</c> сообщения 110.</summary>
    public const int Length = 251;

    /// <summary>Длина заголовка: до поля данных.</summary>
    public const int HeaderLength = 12;

    /// <summary>Сколько байт данных помещается в один обмен.</summary>
    public const int MaxDataLength = Length - HeaderLength;

    /// <summary>Код отказа. Осмыслен только при <see cref="MavFtpOpcode.Nak"/>.</summary>
    public MavFtpError Error =>
        Data.Length > 0 ? (MavFtpError)Data.Span[0] : MavFtpError.None;

    /// <summary><c>errno</c> файловой системы при <see cref="MavFtpError.FailErrno"/>.</summary>
    public byte Errno => Data.Length > 1 ? Data.Span[1] : (byte)0;

    /// <summary>
    /// Отказ объясняется состоянием файловой сессии на борту, а не самим файлом.
    /// </summary>
    /// <remarks>
    /// 🔴 Файловая сессия у борта одна, и открытый в ней файл закрывается только
    /// явным завершением сессии. Незакрытая сессия — оборванный кабель, снятое
    /// питание станции, потерянный ответ на открытие — отвечает на любое
    /// следующее открытие общим кодом <see cref="MavFtpError.Fail"/>, по которому
    /// причину не отличить от отказа самого файла. Признак собран здесь, а не на
    /// месте вызова, чтобы чтение и запись лечили это состояние одинаково.
    /// </remarks>
    public bool IndicatesStaleSession => Opcode == MavFtpOpcode.Nak
        && Error is MavFtpError.Fail
                 or MavFtpError.None
                 or MavFtpError.InvalidSession
                 or MavFtpError.NoSessionsAvailable;

    /// <summary>Читаемое описание отказа — уходит оператору без перевода на месте вызова.</summary>
    public string DescribeError() => Opcode != MavFtpOpcode.Nak
        ? string.Empty
        : Error switch
        {
            MavFtpError.EndOfFile => "конец файла или каталога",
            MavFtpError.FileNotFound => "файла или каталога нет на борту",
            MavFtpError.FileExists => "файл уже существует",
            MavFtpError.FileProtected => "файл защищён от изменения",
            MavFtpError.InvalidSession => "сессия недействительна",
            MavFtpError.NoSessionsAvailable => "у борта нет свободных файловых сессий",
            MavFtpError.InvalidDataSize => "неверный размер данных в запросе",
            MavFtpError.UnknownCommand => "прошивка не знает этой операции MAVFTP",
            MavFtpError.FailErrno => string.Create(
                CultureInfo.InvariantCulture,
                $"отказ файловой системы, errno {Errno}"),
            // Код в тексте — не украшение: Fail приходит и на занятую сессию, и
            // на отказ самого файла, и без номера разбор упирается в общую фразу.
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"отказ файловой операции, код {(byte)Error}"),
        };

    /// <summary>Собирает нагрузку запроса в буфер ровно <see cref="Length"/> байт.</summary>
    /// <exception cref="ArgumentException">Данные не помещаются в один обмен.</exception>
    public static byte[] EncodeRequest(
        ushort sequence,
        byte session,
        MavFtpOpcode opcode,
        uint offset,
        ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxDataLength)
        {
            throw new ArgumentException(
                $"В один обмен MAVFTP помещается {MaxDataLength} байт данных, передано {data.Length}.",
                nameof(data));
        }

        var payload = new byte[Length];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0), sequence);
        payload[2] = session;
        payload[3] = (byte)opcode;
        payload[4] = (byte)data.Length;
        payload[5] = 0;
        payload[6] = 0;
        payload[7] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), offset);
        data.CopyTo(payload.AsSpan(HeaderLength));
        return payload;
    }

    /// <summary>
    /// Разбирает нагрузку из сообщения 110.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Кадр MAVLink v2 обрезает хвостовые нулевые байты, и обрезка эта
    /// лоскутная: подтверждение без данных ужимается до девяти байт, то есть
    /// короче самого заголовка MAVFTP. Поэтому нагрузка сначала разворачивается
    /// в полный буфер, добитый нулями, и только потом читается по смещениям.
    /// Требовать 12 байт заголовка нельзя — все короткие подтверждения
    /// (<c>TerminateSession</c>, <c>ResetSessions</c>) молча отбрасывались бы
    /// как неразобранные, и обмен вставал бы на таймауте там, где борт ответил.
    /// </para>
    /// <para>
    /// 🔴 Длина данных берётся из объявленного поля <c>size</c>, а не из
    /// фактически принятого числа байт. Обрезанные нули — это настоящие нули
    /// файла, а не потеря: файл, кончающийся байтом <c>0x00</c>, при подсчёте
    /// «по принятому» стал бы на этот байт короче, и обратное чтение вечно
    /// расходилось бы с записанным.
    /// </para>
    /// </remarks>
    /// <param name="messagePayload">Нагрузка сообщения целиком, начиная с <c>target_network</c>.</param>
    public static bool TryDecode(ReadOnlySpan<byte> messagePayload, out MavFtpPayload result)
    {
        result = default;

        // Первые три байта — target_network, target_system, target_component.
        // Меньше чем на номер запроса рассчитывать не на что.
        const int AddressLength = 3;
        if (messagePayload.Length < AddressLength + 2)
        {
            return false;
        }

        Span<byte> payload = stackalloc byte[Length];
        payload.Clear();

        int received = Math.Min(messagePayload.Length - AddressLength, Length);
        messagePayload.Slice(AddressLength, received).CopyTo(payload);

        int size = Math.Min((int)payload[4], MaxDataLength);

        result = new MavFtpPayload(
            Sequence: BinaryPrimitives.ReadUInt16LittleEndian(payload),
            Session: payload[2],
            Opcode: (MavFtpOpcode)payload[3],
            RequestOpcode: (MavFtpOpcode)payload[5],
            BurstComplete: payload[6],
            Offset: BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]),
            Data: payload.Slice(HeaderLength, size).ToArray());

        return true;
    }
}

/// <summary>Запись каталога борта, вычитанная из ответа <see cref="MavFtpOpcode.ListDirectory"/>.</summary>
/// <param name="Name">Имя без пути.</param>
/// <param name="IsDirectory">Каталог, а не файл.</param>
/// <param name="Size">Размер файла в байтах; для каталога — 0.</param>
public sealed record MavFtpEntry(string Name, bool IsDirectory, long Size);

/// <summary>Разбор записей каталога.</summary>
/// <remarks>
/// Формат: записи разделены нулевым байтом, первый символ задаёт вид —
/// <c>F</c> файл, <c>D</c> каталог, <c>S</c> пропуск. У файла после имени идёт
/// табуляция и размер. Пропуск (<c>S</c>) — это занятая позиция, которую сервер
/// не отдаёт; она обязана считаться за запись при подсчёте смещения, иначе
/// перечисление зациклится на одном и том же месте.
/// </remarks>
public static class MavFtpDirectory
{
    /// <summary>Разбирает один ответ и сообщает, сколько позиций он занял.</summary>
    public static int Parse(ReadOnlySpan<byte> data, ICollection<MavFtpEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var consumed = 0;
        var start = 0;

        for (var i = 0; i <= data.Length; i++)
        {
            if (i != data.Length && data[i] != 0)
            {
                continue;
            }

            if (i > start)
            {
                consumed++;
                AddEntry(data[start..i], entries);
            }

            start = i + 1;
        }

        return consumed;
    }

    private static void AddEntry(ReadOnlySpan<byte> raw, ICollection<MavFtpEntry> entries)
    {
        var text = Encoding.UTF8.GetString(raw);
        if (text.Length == 0)
        {
            return;
        }

        var kind = text[0];
        var body = text[1..];

        switch (kind)
        {
            case 'D':
                entries.Add(new MavFtpEntry(body, IsDirectory: true, Size: 0));
                break;

            case 'F':
                var tab = body.IndexOf('\t');
                if (tab < 0)
                {
                    // Размер не пришёл. Это не повод потерять файл: имя важнее
                    // размера, а размер всё равно перепроверяется при чтении.
                    entries.Add(new MavFtpEntry(body, IsDirectory: false, Size: 0));
                    break;
                }

                var size = long.TryParse(
                    body[(tab + 1)..],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                    ? parsed
                    : 0;

                entries.Add(new MavFtpEntry(body[..tab], IsDirectory: false, Size: size));
                break;

            // 'S' — пропуск: позиция занята, но содержимого нет. Записи не даёт,
            // но в счётчик позиций уже вошёл у вызывающего.
            default:
                break;
        }
    }
}
