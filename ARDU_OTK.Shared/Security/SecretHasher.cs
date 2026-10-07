using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ARDU_OTK.Shared.Security;

/// <summary>
/// Хеш пароля или PIN: PBKDF2-SHA256 с солью.
/// </summary>
/// <remarks>
/// <para>
/// Формат строки: <c>pbkdf2-sha256$&lt;итерации&gt;$&lt;соль base64&gt;$&lt;хеш base64&gt;</c>.
/// Число итераций хранится в самой строке, поэтому его можно поднять, не
/// инвалидируя уже выданные пароли: старые хеши проверяются со своим числом.
/// </para>
/// <para>
/// 🔴 Сравнение — за постоянное время (<see cref="CryptographicOperations.FixedTimeEquals"/>).
/// Обычное сравнение строк выходит на первом несовпавшем байте, и время
/// ответа подсказывает, сколько байт угадано.
/// </para>
/// </remarks>
public static class SecretHasher
{
    private const string Scheme = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Итерации по умолчанию — рекомендация OWASP для PBKDF2-SHA256.</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>Хеширует секрет со свежей солью.</summary>
    public static string Hash(string secret, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(secret, salt, iterations);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Scheme}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    /// <summary>
    /// Проверяет секрет по хешу.
    /// </summary>
    /// <returns>
    /// <c>false</c> и на неверный секрет, и на повреждённый хеш: вход по
    /// неразборчивому хешу — это вход без проверки.
    /// </returns>
    public static bool Verify(string secret, string? stored)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(stored))
        {
            return false;
        }

        var parts = stored.Split('$');
        if (parts.Length != 4
            || parts[0] != Scheme
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations < 1)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expected.Length != HashBytes)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Derive(secret, salt, iterations), expected);
    }

    private static byte[] Derive(string secret, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}
