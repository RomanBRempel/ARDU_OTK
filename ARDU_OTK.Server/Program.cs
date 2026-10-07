using System.Threading.RateLimiting;
using ARDU_OTK.Server.Auth;
using ARDU_OTK.Server.Data;
using ARDU_OTK.Server.Endpoints;
using ARDU_OTK.Server.Hosting;
using Microsoft.AspNetCore.HttpOverrides;

// Сервер сети ОТК.
//
// Запуск:              ARDU_OTK.Server
// Первый администратор: ARDU_OTK.Server admin --login <логин> --name "<ФИО>"
//                      (пароль — из переменной OTK_ADMIN_PASSWORD либо первой строкой stdin)
// Команда — только первый аргумент: слово «admin» может быть и значением
// (`--login admin`), и вырезать его отовсюду значило бы потерять логин.
var isAdminCommand = args.Length > 0 && args[0] == "admin";
var builder = WebApplication.CreateBuilder(isAdminCommand ? args[1..] : args);

builder.Host.UseWindowsService(o => o.ServiceName = "ARDU_OTK.Server");

var dataDir = ServerPaths.ResolveDataDir(builder.Configuration);
Directory.CreateDirectory(dataDir);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new ServerStore(
    Path.Combine(dataDir, ServerPaths.DatabaseFileName),
    sp.GetRequiredService<TimeProvider>()));

builder.Services
    .AddAuthentication(TokenAuthenticationHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TokenAuthenticationHandler>(
        TokenAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization(OtkPolicies.Add);
builder.Services.AddProblemDetails();

// 🔴 Сервер открыт в интернет. Блокировка логина после серии неудач защищает
// одну учётку; ограничение частоты по адресу не даёт перебирать пароли
// широким фронтом по многим логинам сразу.
var loginsPerMinute = builder.Configuration.GetValue("Otk:LoginsPerMinutePerAddress", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(OtkEndpoints.LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

// TLS завершает обратный прокси; без этого адрес клиента — адрес прокси, и
// ограничение частоты делило бы одну квоту на все стенды сети.
// По умолчанию заголовкам верят только от loopback. Прокси в соседнем
// контейнере перечисляется явно (Otk:KnownProxies, адреса через запятую):
// верить X-Forwarded-For от кого угодно — значит дать любому клиенту
// назваться чужим адресом и обойти ограничение частоты.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in (builder.Configuration["Otk:KnownProxies"] ?? string.Empty)
                 .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    }
});

var app = builder.Build();

var store = app.Services.GetRequiredService<ServerStore>();
await store.InitializeAsync();

if (isAdminCommand)
{
    return await AdminCommand.RunAsync(store, app.Configuration);
}

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

OtkEndpoints.Map(app);

app.Logger.LogInformation("Сервер ОТК {ServerId}, данные в {DataDir}", store.ServerId, dataDir);
await app.RunAsync();
return 0;

/// <summary>Точка входа — открыта для тестов (WebApplicationFactory).</summary>
public partial class Program;
