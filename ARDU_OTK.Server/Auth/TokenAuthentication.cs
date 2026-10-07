using System.Security.Claims;
using System.Text.Encodings.Web;
using ARDU_OTK.Server.Data;
using ARDU_OTK.Shared.Contracts;
using ARDU_OTK.Shared.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ARDU_OTK.Server.Auth;

/// <summary>
/// Вход по токену сессии из заголовка <c>Authorization: Bearer</c>.
/// </summary>
/// <remarks>
/// Токен сверяется с базой на каждый запрос, а не кэшируется: отзыв сессии
/// (смена пароля, роли, отключение учётки) обязан действовать сразу. Запросов
/// от стендов — единицы в минуту, выборка по первичному ключу ничего не стоит.
/// </remarks>
public sealed class TokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ServerStore store)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OtkToken";

    public const string StationClaim = "otk:station";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadToken(Request);
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }

        var session = await store.FindSessionAsync(token, Context.RequestAborted);
        if (session is null)
        {
            return AuthenticateResult.Fail("Сессия недействительна.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, session.User.Id.ToString()),
                new Claim(ClaimTypes.Name, session.User.Login),
                new Claim(ClaimTypes.GivenName, session.User.DisplayName),
                new Claim(ClaimTypes.Role, session.User.Role),
                new Claim(StationClaim, session.StationId.ToString()),
            ],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    /// <summary>Токен из заголовка либо <c>null</c>.</summary>
    public static string? ReadToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && header.Length > prefix.Length
            ? header[prefix.Length..].Trim()
            : null;
    }
}

/// <summary>Политики доступа — по одной на право из <see cref="OtkRoles"/>.</summary>
public static class OtkPolicies
{
    public const string ManageReferences = "otk.references.manage";

    public const string ManageUsers = "otk.users.manage";

    public const string ViewJournal = "otk.journal.view";

    public static void Add(Microsoft.AspNetCore.Authorization.AuthorizationOptions options)
    {
        options.AddPolicy(ManageReferences, p => p.RequireAssertion(c => HasRole(c.User, OtkRoles.CanManageReferences)));
        options.AddPolicy(ManageUsers, p => p.RequireAssertion(c => HasRole(c.User, OtkRoles.CanManageUsers)));
        options.AddPolicy(ViewJournal, p => p.RequireAssertion(c => HasRole(c.User, OtkRoles.CanViewJournal)));
    }

    // Политика спрашивает OtkRoles, а не перечисляет роли сама: список ролей
    // при праве — одна истина, и живёт она в общей библиотеке.
    private static bool HasRole(ClaimsPrincipal user, Func<string, bool> allows) =>
        user.FindFirstValue(ClaimTypes.Role) is { } role && allows(role);
}

/// <summary>Сессия текущего запроса.</summary>
public static class PrincipalExtensions
{
    public static SessionPrincipal ToSession(this ClaimsPrincipal user) => new(
        new UserInfo(
            Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
            user.FindFirstValue(ClaimTypes.Name)!,
            user.FindFirstValue(ClaimTypes.GivenName)!,
            user.FindFirstValue(ClaimTypes.Role)!),
        Guid.Parse(user.FindFirstValue(TokenAuthenticationHandler.StationClaim)!));
}
