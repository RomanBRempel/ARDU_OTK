using ARDU_OTK.Server.Data;

namespace ARDU_OTK.Server.Hosting;

/// <summary>
/// Заведение администратора с консоли сервера.
/// </summary>
/// <remarks>
/// Единственный путь, не требующий входа: им заводят первого администратора
/// и восстанавливают доступ, если пароль администратора утерян. Доступ к
/// консоли сервера и есть здесь предъявленное право.
/// Пароль не принимается аргументом: аргументы видны в списке процессов и
/// оседают в истории оболочки.
/// </remarks>
public static class AdminCommand
{
    public static async Task<int> RunAsync(ServerStore store, IConfiguration configuration)
    {
        var login = configuration["login"];
        if (string.IsNullOrWhiteSpace(login))
        {
            Console.Error.WriteLine("Использование: ARDU_OTK.Server admin --login <логин> [--name \"<ФИО>\"]");
            Console.Error.WriteLine("Пароль: переменная OTK_ADMIN_PASSWORD либо первая строка stdin.");
            return 2;
        }

        var password = Environment.GetEnvironmentVariable("OTK_ADMIN_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            Console.Error.Write("Пароль: ");
            password = Console.ReadLine();
        }

        try
        {
            var user = await store.UpsertAdminAsync(login, configuration["name"] ?? login, password ?? string.Empty);
            Console.WriteLine($"Администратор «{user.Login}» готов. Его прежние сессии закрыты.");
            return 0;
        }
        catch (StoreRuleException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
