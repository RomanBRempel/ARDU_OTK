using Microsoft.UI.Xaml;

namespace ARDU_OTK.Services;

/// <summary>
/// Масштаб шрифтов интерфейса — единственная точка, где задан размер текста.
/// </summary>
/// <remarks>
/// 🔴 Размеры шрифтов раньше были литералами, разбросанными по разметке, и
/// поэтому крутилки «сделать крупнее» не существовало в принципе: менять было
/// нечего — каждое число жило само по себе. Здесь размеры сведены в набор
/// ресурсов с базовыми значениями, а разметка ссылается только на них. Ручка
/// масштаба умножает базу, а не текущее значение: иначе повторное применение
/// накапливало бы множитель и текст рос бы с каждым заходом в настройки.
///
/// Переопределяются и ключи самого WinUI: без них крупнее становился бы только
/// собственный текст приложения, а подписи кнопок, списков и полей ввода
/// остались бы прежними — то есть на большом экране нечитаемой осталась бы
/// ровно та половина интерфейса, ради которой всё и делается.
/// </remarks>
public static class UiScale
{
    /// <summary>Масштаб по умолчанию — размеры, заданные при вёрстке.</summary>
    public const double Default = 1.0;

    public const double Min = 1.0;

    public const double Max = 2.0;

    /// <summary>
    /// Базовые размеры. Ключи WinUI перечислены явно вместе со своими
    /// штатными значениями: прочитать их из системного словаря нельзя, пока
    /// приложение не загрузило разметку, а масштаб нужен раньше.
    /// </summary>
    private static readonly (string Key, double Base)[] Sizes =
    {
        // Ключи WinUI: текст внутри контролов и системные стили текста.
        ("ControlContentThemeFontSize", 14),
        ("ContentControlFontSize", 14),
        ("CaptionTextBlockFontSize", 12),
        ("BodyTextBlockFontSize", 14),
        ("BodyStrongTextBlockFontSize", 14),
        ("BodyLargeTextBlockFontSize", 18),
        ("SubtitleTextBlockFontSize", 20),
        ("TitleTextBlockFontSize", 28),

        // Собственные ключи приложения — ими размечены таблицы и приборы.
        ("AppTinyFontSize", 11),
        ("AppCaptionFontSize", 12),
        ("AppSmallFontSize", 13),
        ("AppBodyFontSize", 14),
        ("AppMediumFontSize", 16),
        ("AppNumericFontSize", 17),
    };

    /// <summary>
    /// Файл с масштабом. Намеренно не в базе приёмки: масштаб нужен до того,
    /// как приложение откроет базу и загрузит первую страницу, а обращение к
    /// SQLite на этом этапе асинхронно и может отказать. Это настройка
    /// монитора рабочего места, а не данные сдачи.
    /// </summary>
    private static string StatePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ARDU_OTK.Data",
        "ui-scale.txt");

    /// <summary>Текущий множитель.</summary>
    public static double Current { get; private set; } = Default;

    /// <summary>Масштаб изменён и уже применён к ресурсам.</summary>
    public static event EventHandler? Changed;

    /// <summary>Приводит любое значение к допустимому диапазону.</summary>
    public static double Clamp(double factor) =>
        double.IsFinite(factor) ? Math.Clamp(factor, Min, Max) : Default;

    /// <summary>
    /// Читает сохранённый масштаб. Отсутствие файла и любая порча содержимого
    /// дают масштаб по умолчанию: настройка вида не имеет права мешать запуску.
    /// </summary>
    public static double Load()
    {
        try
        {
            string path = StatePath;
            if (!System.IO.File.Exists(path))
            {
                return Default;
            }

            string text = System.IO.File.ReadAllText(path).Trim();
            return double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double factor)
                ? Clamp(factor)
                : Default;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return Default;
        }
    }

    /// <summary>Сохраняет масштаб на следующий запуск.</summary>
    public static void Save(double factor)
    {
        try
        {
            string path = StatePath;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(
                path,
                Clamp(factor).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // Не сохранилось — масштаб доживёт до конца сеанса и вернётся к
            // прежнему после перезапуска. Ронять приложение из-за этого нельзя.
            App.LogFatal("UiScale.Save", ex);
        }
    }

    /// <summary>
    /// Записывает масштабированные размеры в ресурсы приложения.
    /// </summary>
    /// <remarks>
    /// Уже построенные элементы своих размеров не пересчитывают: ресурс
    /// подставляется при загрузке разметки. Поэтому вызывающий обязан
    /// пересоздать страницу — см. <see cref="Changed"/>.
    /// </remarks>
    public static void Apply(double factor)
    {
        factor = Clamp(factor);
        Current = factor;

        ResourceDictionary resources = Application.Current.Resources;
        foreach ((string key, double baseSize) in Sizes)
        {
            resources[key] = Math.Round(baseSize * factor, 1);
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }
}
