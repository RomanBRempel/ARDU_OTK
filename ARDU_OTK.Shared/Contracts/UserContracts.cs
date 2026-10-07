namespace ARDU_OTK.Shared.Contracts;

/// <summary>Пользователь в списке администратора.</summary>
public sealed record UserDto(
    Guid Id,
    string Login,
    string DisplayName,
    string Role,
    bool IsActive,
    bool HasPassword,
    bool HasPin,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>Заведение пользователя. Пароль обязателен: учётка без способа войти — мёртвая запись.</summary>
public sealed record CreateUserRequest(string Login, string DisplayName, string Role, string Password);

/// <summary>Правка пользователя; <c>null</c> — поле не меняется.</summary>
public sealed record UpdateUserRequest(string? DisplayName, string? Role, bool? IsActive);

/// <summary>Новый пароль.</summary>
public sealed record SetPasswordRequest(string Password);

/// <summary>Новый PIN; <c>null</c> снимает PIN.</summary>
public sealed record SetPinRequest(string? Pin);
