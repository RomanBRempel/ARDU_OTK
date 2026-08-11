using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ARDU_OTK.Services.Fc;

/// <summary>Найденный COM-порт вместе с описанием устройства.</summary>
/// <param name="PortName">Имя порта, например <c>COM7</c>.</param>
/// <param name="Description">Понятное имя устройства из системы, без хвоста «(COMn)».</param>
/// <param name="Manufacturer">Производитель по данным драйвера, если известен.</param>
/// <param name="VendorId">USB VID, если порт вообще USB.</param>
/// <param name="ProductId">USB PID.</param>
/// <param name="IsBootloader">Устройство представилось как загрузчик (имя оканчивается на <c>-BL</c>).</param>
/// <param name="UsbInterface">
/// Номер USB-интерфейса композитного устройства (<c>MI_xx</c>), если он есть.
/// Платы F7/H7 отдают второй CDC под SLCAN, и MAVLink живёт на нулевом
/// интерфейсе — по этому номеру их и различают.
/// </param>
public sealed record SerialPortDescription(
    string PortName,
    string? Description,
    string? Manufacturer,
    ushort? VendorId,
    ushort? ProductId,
    bool IsBootloader,
    int? UsbInterface = null)
{
    /// <summary>
    /// Похоже на плату с ArduPilot.
    /// </summary>
    /// <remarks>
    /// Совпадения только по VID мало: <c>0x1209</c> — общий идентификатор
    /// pid.codes, его использует не только ArduPilot. Поэтому проверяется либо
    /// вендорский VID, либо строка производителя.
    /// </remarks>
    public bool LooksLikeArduPilot =>
        (VendorId is 0x2DAE or 0x3162 or 0x26AC)
        || (VendorId is 0x1209 && ProductId is 0x5740 or 0x5741)
        || string.Equals(Manufacturer, "ArduPilot", StringComparison.OrdinalIgnoreCase)
        || (Description?.Contains("ArduPilot", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>Подпись для списка портов.</summary>
    public string Caption
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Description))
            {
                return PortName;
            }

            var caption = $"{PortName} — {Description}";
            if (IsBootloader)
            {
                caption += " · загрузчик";
            }

            return caption;
        }
    }

    /// <summary>Вторая строка: VID/PID и производитель.</summary>
    public string Details
    {
        get
        {
            var parts = new List<string>();

            if (VendorId is { } vid && ProductId is { } pid)
            {
                parts.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"VID_{vid:X4}&PID_{pid:X4}"));
            }

            if (!string.IsNullOrWhiteSpace(Manufacturer))
            {
                parts.Add(Manufacturer);
            }

            if (UsbInterface is { } mi)
            {
                parts.Add(mi == 0 ? "интерфейс 0 (MAVLink)" : $"интерфейс {mi}");
            }

            if (IsBootloader)
            {
                parts.Add("режим загрузчика — прошивка не запущена");
            }

            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Перечисление COM-портов с описанием устройств.
/// </summary>
/// <remarks>
/// <see cref="SerialPort.GetPortNames"/> отдаёт только имена: ни модели платы,
/// ни VID/PID, ни производителя, а список читается из ветки реестра, где
/// остаются записи об уже отключённых устройствах. Поэтому описания берутся из
/// WMI (<c>Win32_PnPEntity</c> класса «Порты»), а голый список имён остаётся
/// подстраховкой на случай, если WMI недоступен.
/// </remarks>
public static class SerialPortCatalog
{
    /// <summary>GUID класса устройств «Порты (COM и LPT)».</summary>
    private const string PortsClassGuid = "{4d36e978-e325-11ce-bfc1-08002be10318}";

    private static readonly Regex PortInName = new(@"\((COM\d+)\)\s*$", RegexOptions.Compiled);

    private static readonly Regex UsbInterfaceId = new(
        @"&MI_(?<mi>\d{2})",
        RegexOptions.Compiled);

    private static readonly Regex UsbIds = new(
        @"VID_(?<vid>[0-9A-Fa-f]{4})&PID_(?<pid>[0-9A-Fa-f]{4})",
        RegexOptions.Compiled);

    /// <summary>
    /// Читает список портов. Запрос к WMI блокирующий и заметно небыстрый,
    /// поэтому выполняется в пуле потоков и никогда — в потоке интерфейса.
    /// </summary>
    public static Task<IReadOnlyList<SerialPortDescription>> ListAsync() => Task.Run(List);

    private static IReadOnlyList<SerialPortDescription> List()
    {
        var byPort = new Dictionary<string, SerialPortDescription>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT Name, Manufacturer, PNPDeviceID FROM Win32_PnPEntity WHERE ClassGuid = '{PortsClassGuid}'");

            foreach (var item in searcher.Get().Cast<ManagementBaseObject>())
            {
                using (item)
                {
                    var name = item["Name"] as string;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var match = PortInName.Match(name);
                    if (!match.Success)
                    {
                        continue;
                    }

                    var port = match.Groups[1].Value;
                    var description = name[..match.Index].Trim();

                    ushort? vid = null;
                    ushort? pid = null;
                    int? usbInterface = null;

                    if (item["PNPDeviceID"] is string pnp)
                    {
                        if (UsbIds.Match(pnp) is { Success: true } ids)
                        {
                            vid = ushort.Parse(ids.Groups["vid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            pid = ushort.Parse(ids.Groups["pid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        }

                        if (UsbInterfaceId.Match(pnp) is { Success: true } mi)
                        {
                            usbInterface = int.Parse(mi.Groups["mi"].Value, CultureInfo.InvariantCulture);
                        }
                    }

                    byPort[port] = new SerialPortDescription(
                        port,
                        description,
                        item["Manufacturer"] as string,
                        vid,
                        pid,
                        description.EndsWith("-BL", StringComparison.OrdinalIgnoreCase),
                        usbInterface);
                }
            }
        }
        catch (ManagementException)
        {
            // WMI может быть недоступен или урезан политикой. Это не повод
            // остаться без списка портов — ниже отработает запасной путь.
        }
        catch (UnauthorizedAccessException)
        {
        }

        // Порты, которых WMI не показал, всё равно попадают в список: без
        // описания, но выбрать их оператор сможет.
        foreach (var port in SerialPort.GetPortNames())
        {
            if (!byPort.ContainsKey(port))
            {
                byPort[port] = new SerialPortDescription(port, null, null, null, null, false);
            }
        }

        return byPort.Values
            .OrderByDescending(static p => p.LooksLikeArduPilot)
            .ThenBy(static p => p.PortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
