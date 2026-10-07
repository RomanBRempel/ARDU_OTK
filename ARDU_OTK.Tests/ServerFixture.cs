using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ARDU_OTK.Server.Data;
using ARDU_OTK.Shared.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ARDU_OTK.Tests;

/// <summary>Часы, которые тест двигает сам.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Сервер ОТК в памяти с собственной базой во временном каталоге.
/// Один экземпляр на тест: тесты не делят ни базу, ни квоту частоты входа.
/// </summary>
public sealed class OtkServer : WebApplicationFactory<Program>
{
    public const string AdminLogin = "admin";
    public const string AdminPassword = "admin-password-1";

    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "otk-tests-" + Guid.NewGuid().ToString("N"));

    public ManualTimeProvider Clock { get; } = new();

    public Guid StationId { get; } = Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Otk:DataDir", _dataDir);
        builder.UseSetting("Otk:LoginsPerMinutePerAddress", "1000");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public ServerStore Store => Services.GetRequiredService<ServerStore>();

    /// <summary>Заводит администратора тем же путём, что консоль сервера.</summary>
    public async Task<HttpClient> AdminAsync()
    {
        await Store.UpsertAdminAsync(AdminLogin, "Админ Админович", AdminPassword);
        return await LoginAsync(AdminLogin, AdminPassword);
    }

    public async Task<HttpResponseMessage> TryLoginAsync(string login, string password) =>
        await CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(login, password, StationId, "Стенд-1"));

    public async Task<HttpClient> LoginAsync(string login, string password)
    {
        var response = await TryLoginAsync(login, password);
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        return client;
    }

    public static async Task<UserDto> CreateUserAsync(HttpClient admin, string login, string role, string password = "user-password-1")
    {
        var response = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(login, "ФИО " + login, role, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>())!;
    }

    /// <summary>Пакет эталона в формате выгрузки стенда.</summary>
    public static string Package(string name, string scriptText = "-- lua\nprint('ok')\n", string? scriptHash = null)
    {
        var hash = scriptHash ?? Convert.ToHexString(
            SHA256.HashData(new UTF8Encoding(false).GetBytes(scriptText))).ToLowerInvariant();
        return JsonSerializer.Serialize(new
        {
            Version = 1,
            Name = name,
            Description = "",
            SourceName = "board.param",
            ParamFormat = "MissionPlanner",
            ParamText = "COMPASS_USE,1\n",
            ParamHash = new string('a', 64),
            ParamCount = 1,
            Scripts = new[] { new { Path = "APM/scripts/a.lua", Text = scriptText, Hash = hash } },
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
            // Временный каталог; оставшийся файл не влияет на другие тесты.
        }
    }
}
