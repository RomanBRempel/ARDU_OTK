using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;

namespace ARDU_OTK.Services;

/// <summary>Исход определения координат рабочего места.</summary>
/// <param name="Found">Координаты получены.</param>
/// <param name="LatitudeDeg">Широта, градусы.</param>
/// <param name="LongitudeDeg">Долгота, градусы.</param>
/// <param name="Detail">Чем всё кончилось — словами, для журнала и для оператора.</param>
public readonly record struct WorkstationFix(
    bool Found,
    double LatitudeDeg,
    double LongitudeDeg,
    string Detail);

/// <summary>
/// Определение координат рабочего места службой определения местоположения
/// Windows.
/// </summary>
/// <remarks>
/// 🔴 Координаты нужны калибровке компаса: команда фиксированного курса
/// считает эталонное магнитное поле по всемирной магнитной модели и без точки
/// на земле отвечает отказом. Взять их у борта в цехе нельзя — фикса GPS
/// внутри здания не бывает, — поэтому раньше их вводил оператор, а незаполненное
/// поле обнаруживалось в момент калибровки, когда изделие уже в оснастке.
///
/// Точность здесь роли не играет: магнитное склонение на десятках километров
/// не меняется, и грубого определения по сети более чем достаточно. Поэтому
/// запрашивается низкая точность — она не требует спутникового приёмника и
/// отвечает быстро.
///
/// Отказ службы — штатный исход, а не сбой: определение местоположения может
/// быть выключено политикой цеха. Тогда остаётся ручной ввод, и об этом
/// говорится прямо.
/// </remarks>
public static class WorkstationLocator
{
    /// <summary>
    /// Сколько ждать ответа службы.
    /// </summary>
    /// <remarks>
    /// Больше ждать незачем: определение по сети отвечает за секунды, а если
    /// служба молчит, значит доступа нет, и ждать нужно не её, а оператора.
    /// </remarks>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Пытается определить координаты. Исключений не бросает: любой отказ —
    /// это <see cref="WorkstationFix.Found"/> равное <c>false</c> с причиной.
    /// </summary>
    public static async Task<WorkstationFix> TryDetectAsync(CancellationToken ct = default)
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (access != GeolocationAccessStatus.Allowed)
            {
                return new WorkstationFix(
                    false, 0, 0,
                    "служба определения местоположения Windows не разрешена для приложения");
            }

            var locator = new Geolocator
            {
                DesiredAccuracy = PositionAccuracy.Default,
                ReportInterval = 0,
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            var position = await locator.GetGeopositionAsync().AsTask(timeout.Token).ConfigureAwait(false);
            var point = position?.Coordinate?.Point?.Position;

            if (point is not { } coordinates)
            {
                return new WorkstationFix(false, 0, 0, "служба ответила, но координат не дала");
            }

            return new WorkstationFix(
                true,
                coordinates.Latitude,
                coordinates.Longitude,
                "координаты определены службой Windows");
        }
        catch (OperationCanceledException)
        {
            return new WorkstationFix(false, 0, 0, "служба определения местоположения не ответила вовремя");
        }
        catch (Exception ex)
        {
            // Служба живёт вне процесса и отказывает по-разному: выключена,
            // запрещена политикой, нет ни одного источника. Разбирать эти
            // случаи по типам исключений бессмысленно — исход один и тот же.
            return new WorkstationFix(false, 0, 0, "служба определения местоположения отказала: " + ex.Message);
        }
    }
}
