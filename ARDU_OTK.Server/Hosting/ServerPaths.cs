namespace ARDU_OTK.Server.Hosting;

/// <summary>
/// Где сервер хранит данные.
/// </summary>
/// <remarks>
/// Порядок: ключ конфигурации <c>Otk:DataDir</c> (переменная окружения
/// <c>Otk__DataDir</c>), затем каталог по умолчанию платформы. Каталог
/// установки не подходит: обновление сервера заменяет его целиком.
/// </remarks>
public static class ServerPaths
{
    public const string DatabaseFileName = "ardu_otk_server.db";

    public static string ResolveDataDir(IConfiguration configuration)
    {
        var configured = configuration["Otk:DataDir"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ARDU_OTK.Server")
            : "/var/lib/ardu-otk";
    }
}
