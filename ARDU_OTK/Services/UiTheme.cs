using Microsoft.UI.Xaml;

namespace ARDU_OTK.Services;

/// <summary>Тема оформления, выбранная для рабочего места.</summary>
public enum AppTheme
{
    /// <summary>Как задано в Windows — значение по умолчанию.</summary>
    System,

    Light,

    Dark,
}

/// <summary>
/// Тема оформления приложения — единственное место, где она задаётся.
/// </summary>
/// <remarks>
/// 🔴 Тема — свойство рабочего места, а не машины. Раньше приложение вообще не
/// имело своей темы: цвета брались из настройки Windows, а она на цеховом ПК
/// задана под другую задачу и другого пользователя, и трогать её нельзя. Ровно
/// по этой же причине здесь уже живёт <see cref="UiScale"/>: системный масштаб
/// экрана менять не разрешено, а читать мелкий текст у стенда невозможно.
///
/// 🔴 Тема задаётся процессу целиком (<see cref="Application.RequestedTheme"/>),
/// а не корневому элементу окна, — и потому применяется с перезапуска. Причина
/// не в лени: цвета в приложении разбираются не только разметкой. Приборы,
/// макет OSD и журнал строят элементы кодом и берут кисти из
/// <c>Application.Current.Resources</c>, а этот словарь знает только тему
/// процесса. Переопредели тему у корневого элемента — разметка потемнеет, а
/// кисти, взятые кодом, останутся светлыми: рамки карточек станут невидимыми,
/// подписи — нечитаемыми. Полутёмный экран у стенда хуже светлого, поэтому
/// тема меняется целиком и один раз, на старте.
/// </remarks>
public static class UiTheme
{
    /// <summary>Тема по умолчанию — как в Windows.</summary>
    public const AppTheme Default = AppTheme.System;

    private const string FileName = "ui-theme.txt";

    /// <summary>Выбранная тема: она же сохранённая, она же будет при следующем запуске.</summary>
    public static AppTheme Current { get; private set; } = Default;

    /// <summary>Тема, с которой запущен текущий сеанс.</summary>
    public static AppTheme Applied { get; private set; } = Default;

    /// <summary>
    /// Выбранная тема отличается от той, с которой работает приложение сейчас:
    /// чтобы её увидеть, нужен перезапуск.
    /// </summary>
    public static bool RestartRequired => Current != Applied;

    /// <summary>
    /// Читает сохранённую тему. Отсутствие файла и любая порча содержимого дают
    /// системную тему: настройка вида не имеет права мешать запуску.
    /// </summary>
    public static AppTheme Load() => UiState.Read(FileName)?.ToLowerInvariant() switch
    {
        "dark" => AppTheme.Dark,
        "light" => AppTheme.Light,
        _ => Default,
    };

    /// <summary>Сохраняет тему на следующий запуск.</summary>
    public static void Save(AppTheme theme)
    {
        Current = theme;
        UiState.Write(FileName, Name(theme), "UiTheme.Save");
    }

    /// <summary>
    /// Задаёт тему процессу.
    /// </summary>
    /// <remarks>
    /// Вызывать разрешено только из конструктора <see cref="Application"/> и до
    /// загрузки разметки: после этого WinUI отвечает отказом на запись
    /// <see cref="Application.RequestedTheme"/>. Отказ здесь не фатален —
    /// приложение продолжит работу в системной теме, а настройки покажут, что
    /// выбранная тема ещё не применена.
    /// </remarks>
    public static void Apply(AppTheme theme, Application application)
    {
        Current = theme;
        Applied = theme;

        if (theme == AppTheme.System)
        {
            // Системную тему не переопределяем вовсе: тогда приложение само
            // идёт за настройкой Windows, в том числе при её смене на ходу.
            return;
        }

        try
        {
            application.RequestedTheme = theme == AppTheme.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light;
        }
        catch (Exception ex)
        {
            App.LogFatal("UiTheme.Apply", ex);

            // Тема осталась системной. Признаём это честно: настройки увидят
            // расхождение с выбранной и предложат перезапуск, а не покажут
            // тёмную тему включённой на светлом экране.
            Applied = AppTheme.System;
        }
    }

    private static string Name(AppTheme theme) => theme switch
    {
        AppTheme.Dark => "dark",
        AppTheme.Light => "light",
        _ => "system",
    };
}
