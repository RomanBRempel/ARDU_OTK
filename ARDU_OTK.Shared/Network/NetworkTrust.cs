using System.Security.Cryptography;

namespace ARDU_OTK.Shared.Network;

/// <summary>
/// Ключи, которым стенд верит при приёме пакета сети.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 Открытые ключи зашиты в сборку, и это корень доверия всей сети ОТК.
/// Права на папку обмена (Google Диск) — лишь первый рубеж: даже если в папку
/// сможет писать посторонний, стенд не примет пакет, не подписанный этим ключом.
/// </para>
/// <para>
/// Закрытый ключ хранится только у администратора — см.
/// <c>ARDU_OTK.Services.Network.NetworkSigningKeyStore</c>. Смена ключа —
/// это новая запись здесь и выпуск приложения; старую запись убирают только
/// после того, как все стенды обновились.
/// </para>
/// </remarks>
public static class NetworkTrust
{
    /// <summary>Отпечаток ключа → SubjectPublicKeyInfo (P-256), base64.</summary>
    public static readonly IReadOnlyDictionary<string, string> Production = new Dictionary<string, string>
    {
        // Ключ администратора сети ОТК, выпущен 2026-10-07.
        [ProductionKeyId] = ProductionKey,
    };

    private const string ProductionKeyId = "5c563c26185fe3ca";

    private const string ProductionKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE4pyYEgvTi34E4/u/DFyqIm3vzeaW2gnM8cw15U8e6r94zI5Zlj36ExC9qwFXFbygmoaMRapnWWvvhejPSe5I+w==";

    /// <summary>Отпечаток ключа: первые 16 hex-знаков SHA-256 его SubjectPublicKeyInfo.</summary>
    public static string KeyIdOf(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return KeyIdOf(key.ExportSubjectPublicKeyInfo());
    }

    /// <inheritdoc cref="KeyIdOf(ECDsa)"/>
    public static string KeyIdOf(byte[] subjectPublicKeyInfo) =>
        Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo))[..16].ToLowerInvariant();
}
