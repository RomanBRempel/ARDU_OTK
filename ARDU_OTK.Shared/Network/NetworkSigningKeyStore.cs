using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace ARDU_OTK.Shared.Network;

/// <summary>
/// Закрытый ключ подписи пакетов сети на компьютере администратора.
/// </summary>
/// <remarks>
/// <para>
/// Ключ лежит в <c>%LOCALAPPDATA%\ARDU_OTK.Keys</c>, зашифрованный DPAPI под
/// учётной записью Windows администратора. Каталог отдельный от данных ОТК:
/// данные бывают отладочными и установленными (<c>AppPaths</c>), а ключ у
/// администратора один, и выпущенный из отладочной копии пакет обязан быть
/// подписан тем же ключом, что и из установленной.
/// </para>
/// <para>
/// 🔴 Утрата ключа — это выпуск новой версии приложения с новым открытым
/// ключом (<see cref="NetworkTrust"/>) и обновление всех стендов. Поэтому
/// резервная копия, зашифрованная паролем (<see cref="ExportBackup"/>), — не
/// удобство, а обязательная часть заведения ключа.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class NetworkSigningKeyStore
{
    private static readonly byte[] Entropy = "ARDU_OTK.network-signing.v1"u8.ToArray();

    /// <summary>Путь к файлу ключа.</summary>
    public static string KeyPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ARDU_OTK.Keys",
        "network-signing.key");

    /// <summary>На этом компьютере есть ключ администратора сети.</summary>
    public static bool Exists => File.Exists(KeyPath);

    /// <summary>Загружает ключ; <c>null</c> — ключа на этом компьютере нет.</summary>
    /// <exception cref="CryptographicException">Файл ключа повреждён либо зашифрован под другой учётной записью.</exception>
    public static ECDsa? Load()
    {
        if (!Exists)
        {
            return null;
        }

        var pkcs8 = ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    /// <summary>
    /// Создаёт ключ. Существующий ключ не перезаписывается никогда: его потеря
    /// отрезала бы все стенды от сети до выпуска нового приложения.
    /// </summary>
    public static ECDsa Create()
    {
        if (Exists)
        {
            throw new InvalidOperationException($"Ключ сети уже есть: {KeyPath}. Перезаписывать его нельзя.");
        }

        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Save(key);
        return key;
    }

    /// <summary>
    /// Резервная копия ключа: PKCS#8, зашифрованный паролем (PBES2, AES-256, 600 000 итераций).
    /// </summary>
    public static byte[] ExportBackup(ECDsa key, string password)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrEmpty(password);
        return key.ExportEncryptedPkcs8PrivateKey(
            password,
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));
    }

    /// <summary>Восстанавливает ключ из резервной копии на этот компьютер.</summary>
    public static ECDsa ImportBackup(byte[] backup, string password)
    {
        if (Exists)
        {
            throw new InvalidOperationException($"Ключ сети уже есть: {KeyPath}. Перезаписывать его нельзя.");
        }

        var key = ECDsa.Create();
        key.ImportEncryptedPkcs8PrivateKey(password, backup, out _);
        Save(key);
        return key;
    }

    private static void Save(ECDsa key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
        var pkcs8 = key.ExportPkcs8PrivateKey();
        try
        {
            File.WriteAllBytes(KeyPath, ProtectedData.Protect(pkcs8, Entropy, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }
}
