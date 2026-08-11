using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ARDU_OTK.Services.Fc;

/// <summary>
/// Снимок компасных параметров с эталонного борта.
/// </summary>
/// <param name="Values">Прочитанные пары имя-значение.</param>
/// <param name="Unreadable">Имена, которые борт не отдал. Пустой список — прочитано всё.</param>
/// <param name="OccupiedSlots">Слоты с ненулевым <c>COMPASS_DEV_IDx</c>.</param>
/// <param name="FirmwareBanner">Баннер прошивки образца, если он был получен.</param>
public sealed record CompassSnapshot(
    IReadOnlyDictionary<string, double> Values,
    IReadOnlyList<string> Unreadable,
    IReadOnlyList<int> OccupiedSlots,
    string? FirmwareBanner);

/// <summary>
/// Снятие эталона прямо с образцового изделия.
/// </summary>
/// <remarks>
/// <para>
/// Существует потому, что эталон рождается не в файле, а на образце: сначала
/// плату калибруют вручную, и только потом кто-то выгружает из неё
/// <c>.param</c>. Промежуточная выгрузка — лишний шаг, на котором теряется связь
/// между эталоном и бортом, с которого он снят: файл можно переименовать,
/// перепутать или отредактировать в блокноте.
/// </para>
/// <para>
/// 🔴 <c>COMPASS_PRIO*_ID</c> в снимок не входит намеренно. Эти значения
/// описывают датчики именно той платы, с которой снимали, и в эталоне они
/// назвали бы чужое железо. Процедура их и не переносит — они в
/// <see cref="CompassIdentity.NeverWriteNames"/>.
/// </para>
/// </remarks>
public static class CompassParameterSnapshot
{
    /// <summary>
    /// Читает с борта всё, что составляет эталон компасов.
    /// </summary>
    /// <param name="link">Открытый канал связи с образцом.</param>
    /// <param name="progress">Куда сообщать ход чтения; может быть <c>null</c>.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <exception cref="ArgumentNullException">Канал не задан.</exception>
    /// <exception cref="VehicleLinkException">
    /// Не прочитан ни один <c>COMPASS_DEV_IDx</c> — связь есть, но борт на
    /// параметры не отвечает, и снимок был бы пустым.
    /// </exception>
    /// <remarks>
    /// Отдельное имя, которое борт не отдал, снимок не рушит: прошивка законно
    /// не знает части параметров (например, <c>COMPASS_MOT3_*</c> при двух
    /// компасах), и таймаут на них — норма. Но каждое такое имя попадает в
    /// <see cref="CompassSnapshot.Unreadable"/>: молча подставить ноль значило бы
    /// записать в эталон число, которого на образце нет.
    /// </remarks>
    public static async Task<CompassSnapshot> ReadAsync(
        IVehicleLink link,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(link);

        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        var unreadable = new List<string>();
        var occupied = new List<int>();
        var devIdRead = 0;

        // Сначала состав: по нулевому DEV_ID видно, что слот пуст, и остальные
        // его параметры читать незачем — это полсотни лишних обменов.
        for (var slot = CompassIdentity.MinSlot; slot <= CompassIdentity.MaxSlot; slot++)
        {
            ct.ThrowIfCancellationRequested();

            var name = CompassIdentity.DevIdName(slot);
            progress?.Report($"Чтение {name}…");

            if (!await TryReadAsync(link, name, values, unreadable, ct).ConfigureAwait(false))
            {
                continue;
            }

            devIdRead++;
            if (!CompassIdentity.ToDeviceId(values[name]).IsEmpty)
            {
                occupied.Add(slot);
            }
        }

        if (devIdRead == 0)
        {
            throw new VehicleLinkException(
                "Борт не отдал ни одного COMPASS_DEV_IDx. Связь есть, но состав компасов неизвестен — "
                + "снимать эталон не с чего.");
        }

        foreach (var slot in occupied)
        {
            foreach (var name in SnapshotNames(slot))
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Чтение {name}…");
                await TryReadAsync(link, name, values, unreadable, ct).ConfigureAwait(false);
            }
        }

        progress?.Report(string.Format(
            CultureInfo.InvariantCulture,
            "Снято параметров: {0}, занятых слотов: {1}.",
            values.Count,
            occupied.Count));

        return new CompassSnapshot(values, unreadable, occupied, SafeBanner(link));
    }

    /// <summary>
    /// Что снимается с занятого слота: описание датчика плюс весь переносимый
    /// калибровочный блок.
    /// </summary>
    /// <remarks>
    /// <c>EXTERNAL</c>, <c>USE</c> и <c>ORIENT</c> процедура не переносит, но
    /// сверяет с эталоном — значит, в эталоне они быть обязаны. Особенно
    /// <c>ORIENT</c>: калибровка по фиксированному курсу не исправляет
    /// ориентацию, и неверное значение даёт «успешно» откалиброванный негодный
    /// компас.
    /// </remarks>
    private static IEnumerable<string> SnapshotNames(int slot)
    {
        yield return CompassIdentity.ExternalName(slot);
        yield return CompassIdentity.UseName(slot);
        yield return CompassIdentity.OrientName(slot);

        foreach (var name in CompassIdentity.TransferableCalibrationNames(slot))
        {
            yield return name;
        }
    }

    private static async Task<bool> TryReadAsync(
        IVehicleLink link,
        string name,
        Dictionary<string, double> values,
        List<string> unreadable,
        CancellationToken ct)
    {
        try
        {
            var value = await link.ReadParamAsync(name, ct).ConfigureAwait(false);
            values[name] = value.Value;
            return true;
        }
        catch (VehicleLinkException)
        {
            // Прошивка вправе не знать имени — это не сбой связи, а сообщение о
            // том, что такого параметра на образце нет.
            unreadable.Add(name);
            return false;
        }
    }

    private static string? SafeBanner(IVehicleLink link)
    {
        try
        {
            return link.FirmwareBanner;
        }
        catch (VehicleLinkException)
        {
            return null;
        }
    }
}
