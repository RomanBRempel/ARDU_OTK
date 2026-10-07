using ARDU_OTK.Server.Auth;
using ARDU_OTK.Server.Data;
using ARDU_OTK.Shared.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ARDU_OTK.Server.Endpoints;

/// <summary>API сервера ОТК.</summary>
public static class OtkEndpoints
{
    /// <summary>Политика ограничения частоты для входа.</summary>
    public const string LoginRateLimit = "login";

    /// <summary>Наибольший размер страницы синхронизации.</summary>
    public const int MaxPullLimit = 500;

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", (ServerStore store) => Results.Ok(new { status = "ok", serverId = store.ServerId }))
            .AllowAnonymous();

        MapAuth(api.MapGroup("/auth"));
        MapUsers(api.MapGroup("/users").RequireAuthorization(OtkPolicies.ManageUsers));
        MapReferences(api.MapGroup("/references").RequireAuthorization(OtkPolicies.ManageReferences));

        api.MapGet("/sync/pull", async (long? since, int? limit, ServerStore store, CancellationToken ct) =>
                TypedResults.Ok(await store.PullAsync(
                    Math.Max(0, since ?? 0),
                    Math.Clamp(limit ?? MaxPullLimit, 1, MaxPullLimit),
                    ct)))
            .RequireAuthorization();
    }

    private static void MapAuth(RouteGroupBuilder auth)
    {
        auth.MapPost("/login", async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> (LoginRequest request, ServerStore store, CancellationToken ct) =>
            {
                var (outcome, response) = await store.LoginAsync(request, ct);
                return outcome switch
                {
                    LoginOutcome.Success => TypedResults.Ok(response!),
                    LoginOutcome.LockedOut => TypedResults.Problem(
                        $"Слишком много неудачных попыток. Вход заблокирован на {ServerStore.LockoutDuration.TotalMinutes:0} мин.",
                        statusCode: StatusCodes.Status429TooManyRequests),
                    _ => TypedResults.Problem("Неверный логин или пароль.", statusCode: StatusCodes.Status401Unauthorized),
                };
            })
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimit);

        auth.MapPost("/logout", async (HttpRequest request, ServerStore store, CancellationToken ct) =>
            {
                await store.LogoutAsync(TokenAuthenticationHandler.ReadToken(request)!, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization();

        auth.MapGet("/me", (HttpContext context) => TypedResults.Ok(context.User.ToSession().User))
            .RequireAuthorization();
    }

    private static void MapUsers(RouteGroupBuilder users)
    {
        users.MapGet("/", async (ServerStore store, CancellationToken ct) => TypedResults.Ok(await store.ListUsersAsync(ct)));

        users.MapPost("/", (CreateUserRequest request, HttpContext context, ServerStore store, CancellationToken ct) =>
            Guarded(async () => TypedResults.Ok(await store.CreateUserAsync(request, context.User.ToSession(), ct))));

        users.MapPatch("/{id:guid}", (Guid id, UpdateUserRequest request, HttpContext context, ServerStore store, CancellationToken ct) =>
            Guarded(async () => TypedResults.Ok(await store.UpdateUserAsync(id, request, context.User.ToSession(), ct))));

        users.MapPut("/{id:guid}/password", (Guid id, SetPasswordRequest request, HttpContext context, ServerStore store, CancellationToken ct) =>
            Guarded(async () =>
            {
                await store.SetPasswordAsync(id, request.Password, context.User.ToSession(), ct);
                return TypedResults.NoContent();
            }));

        users.MapPut("/{id:guid}/pin", (Guid id, SetPinRequest request, HttpContext context, ServerStore store, CancellationToken ct) =>
            Guarded(async () =>
            {
                await store.SetPinAsync(id, request.Pin, context.User.ToSession(), ct);
                return TypedResults.NoContent();
            }));
    }

    private static void MapReferences(RouteGroupBuilder references)
    {
        references.MapPost("/", (PublishReferenceRequest request, HttpContext context, ServerStore store, CancellationToken ct) =>
            Guarded(async () => TypedResults.Ok(await store.PublishReferenceAsync(request.PackageJson, context.User.ToSession(), ct))));

        references.MapPost("/{id:guid}/retire", (Guid id, HttpContext context, ServerStore store, CancellationToken ct) =>
            Guarded(async () => TypedResults.Ok(await store.SetReferenceRetiredAsync(id, retired: true, context.User.ToSession(), ct))));

        references.MapPost("/{id:guid}/restore", (Guid id, HttpContext context, ServerStore store, CancellationToken ct) =>
            Guarded(async () => TypedResults.Ok(await store.SetReferenceRetiredAsync(id, retired: false, context.User.ToSession(), ct))));
    }

    /// <summary>Переводит отказы хранилища в ответы: правило — 400, нет записи — 404.</summary>
    private static async Task<IResult> Guarded<T>(Func<Task<T>> action)
        where T : IResult
    {
        try
        {
            return await action();
        }
        catch (StoreRuleException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (KeyNotFoundException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
    }
}
