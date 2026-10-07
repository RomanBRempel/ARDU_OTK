namespace ARDU_OTK.Shared.Security;

/// <summary>
/// Требования к паролю и PIN. Одни и те же на сервере и в форме стенда:
/// форма, пропускающая то, что сервер отвергнет, учит пользователя, что
/// ошибка — в сети.
/// </summary>
public static class SecretPolicy
{
    public const int MinPasswordLength = 8;

    public const int MinPinLength = 4;

    public const int MaxPinLength = 8;

    /// <summary>Причина отказа либо <c>null</c>, если пароль годен.</summary>
    public static string? ValidatePassword(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < MinPasswordLength
            ? $"Пароль короче {MinPasswordLength} символов."
            : null;

    /// <summary>Причина отказа либо <c>null</c>, если PIN годен.</summary>
    public static string? ValidatePin(string? pin)
    {
        if (string.IsNullOrEmpty(pin) || pin.Length < MinPinLength || pin.Length > MaxPinLength)
        {
            return $"PIN — от {MinPinLength} до {MaxPinLength} цифр.";
        }

        return pin.All(char.IsAsciiDigit) ? null : "PIN состоит только из цифр.";
    }
}
