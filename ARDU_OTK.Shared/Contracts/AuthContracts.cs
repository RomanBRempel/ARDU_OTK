namespace ARDU_OTK.Shared.Contracts;

/// <summary>Кто вошёл: то, что стенд показывает и пишет в прогон.</summary>
/// <param name="Id">Ключ пользователя. Глобальный: одинаков на сервере и во всех репликах.</param>
/// <param name="Login">Имя для входа.</param>
/// <param name="DisplayName">ФИО, как оно попадает в журнал работ.</param>
/// <param name="Role">Одна из <see cref="Security.OtkRoles"/>.</param>
public sealed record UserInfo(Guid Id, string Login, string DisplayName, string Role);

/// <summary>Вход по паролю.</summary>
/// <param name="StationId">
/// Ключ стенда. Сессия привязана к стенду: в журнале видно, где именно
/// работал пользователь, а отзыв стенда закрывает его сессии.
/// </param>
/// <param name="StationName">Имя стенда для журнала (обычно имя компьютера).</param>
public sealed record LoginRequest(string Login, string Password, Guid StationId, string StationName);

/// <summary>Выданная сессия.</summary>
/// <param name="Token">
/// Предъявляется заголовком <c>Authorization: Bearer</c>. Сервер хранит
/// только его SHA-256, поэтому утечка базы сервера не даёт действующих токенов.
/// </param>
public sealed record LoginResponse(string Token, DateTimeOffset ExpiresUtc, UserInfo User);
