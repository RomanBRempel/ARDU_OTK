namespace ARDU_OTK.Services;

/// <summary>
/// Файлы настроек вида рабочего места: масштаб шрифтов и тема оформления.
/// </summary>
/// <remarks>
/// 🔴 Одно место чтения и записи, а не по копии на каждую настройку. У всех
/// настроек вида одна и та же политика отказа: файла может не быть, он может
/// быть испорчен, каталог может быть закрыт правами — и ни один из этих случаев
/// не имеет права помешать запуску. Две копии такой политики расходятся при
/// первой же правке: одну поправят, вторая продолжит валить приложение на
/// испорченном файле, причём на чужом рабочем месте и без отладчика.
///
/// Хранится рядом с данными, а не в базе приёмки: вид нужен раньше, чем
/// приложение откроет SQLite, — обращение к ней на этом этапе асинхронно и
/// может отказать. И это настройка монитора рабочего места, а не данные сдачи:
/// переезжать вместе с реестром прогонов ей незачем.
/// </remarks>
internal static class UiState
{
    private static string PathFor(string fileName) => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ARDU_OTK.Data",
        fileName);

    /// <summary>
    /// Читает значение настройки. Отсутствие файла и любая ошибка чтения дают
    /// <c>null</c> — разбирать значение и подставлять умолчание обязан
    /// вызывающий, потому что умолчание у каждой настройки своё.
    /// </summary>
    internal static string? Read(string fileName)
    {
        try
        {
            string path = PathFor(fileName);
            return System.IO.File.Exists(path)
                ? System.IO.File.ReadAllText(path).Trim()
                : null;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Сохраняет значение настройки на следующий запуск.</summary>
    /// <param name="context">Имя операции для журнала аварий.</param>
    internal static void Write(string fileName, string value, string context)
    {
        try
        {
            string path = PathFor(fileName);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, value);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // Не сохранилось — настройка доживёт до конца сеанса и вернётся к
            // прежней после перезапуска. Ронять приложение из-за этого нельзя.
            App.LogFatal(context, ex);
        }
    }
}
