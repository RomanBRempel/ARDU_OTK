using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ARDU_OTK.Shared.Contracts;
using ARDU_OTK.Shared.Security;

namespace ARDU_OTK.Shared.Network;

/// <summary>Исход проверки пакета сети.</summary>
public enum NetworkPackageStatus
{
    /// <summary>Пакет годен и новее принятого — применять.</summary>
    Accepted,

    /// <summary>Пакет годен, но не новее принятого — применять нечего.</summary>
    NotNewer,

    /// <summary>Пакет отвергнут; причина — в <see cref="NetworkPackageCheck.Reason"/>.</summary>
    Rejected,
}

/// <summary>Результат проверки пакета сети.</summary>
public sealed record NetworkPackageCheck(NetworkPackageStatus Status, NetworkSnapshot? Snapshot, string? Reason)
{
    public static NetworkPackageCheck Reject(string reason) => new(NetworkPackageStatus.Rejected, null, reason);
}

/// <summary>Что стенд уже принял: от этого зависит, годится ли новый пакет.</summary>
/// <param name="NetworkId"><c>null</c> — стенд ещё не принял ни одного пакета.</param>
public sealed record AcceptedNetworkState(Guid? NetworkId, long Serial)
{
    public static readonly AcceptedNetworkState None = new(null, 0);
}

/// <summary>
/// Файл пакета сети <c>network.otknet</c>: снимок и подпись администратора.
/// </summary>
/// <remarks>
/// <para>
/// Подписываются байты снимка ровно в том виде, в каком они лежат в файле
/// (<see cref="Payload"/>, base64). Подпись канонического JSON требовала бы
/// одинаковой сериализации у подписывающего и проверяющего на годы вперёд;
/// подпись готовых байт от сериализатора не зависит вовсе.
/// </para>
/// <para>
/// 🔴 Доверие — только к ключам, зашитым в сборку (<see cref="NetworkTrust"/>).
/// Ключ, принятый из той же папки, что и пакет, ничего бы не доказывал: кто
/// может положить в папку пакет, положил бы рядом и свой ключ.
/// </para>
/// </remarks>
public sealed class NetworkPackageFile
{
    /// <summary>Имя файла в папке обмена.</summary>
    public const string FileName = "network.otknet";

    public const string FormatName = "ardu-otk-network";

    public const int CurrentFormatVersion = 1;

    public string Format { get; set; } = FormatName;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>Отпечаток ключа подписи — см. <see cref="NetworkTrust.KeyIdOf"/>.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Снимок: JSON в UTF-8, закодированный base64.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>ECDSA P-256 / SHA-256 над байтами снимка, формат IEEE P1363, base64.</summary>
    public string Signature { get; set; } = string.Empty;

    private static readonly JsonSerializerOptions FileOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Собирает и подписывает файл пакета.</summary>
    /// <param name="signingKey">Закрытый ключ администратора, P-256.</param>
    public static string Sign(NetworkSnapshot snapshot, ECDsa signingKey)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(signingKey);

        var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, PayloadOptions);
        var file = new NetworkPackageFile
        {
            KeyId = NetworkTrust.KeyIdOf(signingKey),
            Payload = Convert.ToBase64String(payload),
            Signature = Convert.ToBase64String(
                signingKey.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
        };
        return JsonSerializer.Serialize(file, FileOptions);
    }

    /// <summary>
    /// Проверяет файл пакета против доверенных ключей и принятого состояния.
    /// </summary>
    /// <param name="trustedKeys">Отпечаток ключа → открытый ключ (SubjectPublicKeyInfo, base64).</param>
    public static NetworkPackageCheck Verify(
        string? fileText,
        IReadOnlyDictionary<string, string> trustedKeys,
        AcceptedNetworkState accepted)
    {
        ArgumentNullException.ThrowIfNull(trustedKeys);
        ArgumentNullException.ThrowIfNull(accepted);

        if (string.IsNullOrWhiteSpace(fileText))
        {
            return NetworkPackageCheck.Reject("Файл пакета сети пуст.");
        }

        NetworkPackageFile? file;
        try
        {
            file = JsonSerializer.Deserialize<NetworkPackageFile>(fileText, FileOptions);
        }
        catch (JsonException ex)
        {
            return NetworkPackageCheck.Reject("Файл пакета сети не разбирается: " + ex.Message);
        }

        if (file is null || file.Format != FormatName)
        {
            return NetworkPackageCheck.Reject("Это не пакет сети ОТК.");
        }

        if (file.FormatVersion != CurrentFormatVersion)
        {
            return NetworkPackageCheck.Reject(
                $"Пакет сети формата {file.FormatVersion}, эта сборка понимает {CurrentFormatVersion}. Обновите приложение.");
        }

        if (!trustedKeys.TryGetValue(file.KeyId, out var publicKey))
        {
            return NetworkPackageCheck.Reject(
                $"Пакет подписан неизвестным ключом ({file.KeyId}). Принимаются только пакеты администратора сети.");
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload = Convert.FromBase64String(file.Payload);
            signature = Convert.FromBase64String(file.Signature);
        }
        catch (FormatException)
        {
            return NetworkPackageCheck.Reject("Пакет сети повреждён: данные или подпись не в base64.");
        }

        using (var key = ECDsa.Create())
        {
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (!key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return NetworkPackageCheck.Reject(
                    "Подпись пакета сети не сходится: файл изменён после выпуска. Принимать его нельзя.");
            }
        }

        NetworkSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<NetworkSnapshot>(payload, PayloadOptions);
        }
        catch (JsonException ex)
        {
            return NetworkPackageCheck.Reject("Содержимое пакета не разбирается: " + ex.Message);
        }

        if (snapshot is null)
        {
            return NetworkPackageCheck.Reject("Пакет сети пуст.");
        }

        if (accepted.NetworkId is { } networkId && networkId != snapshot.NetworkId)
        {
            return NetworkPackageCheck.Reject(
                "Пакет другой сети ОТК. Стенд уже подключён к своей сети; чужой пакет перетёр бы её эталоны.");
        }

        if (snapshot.Serial <= accepted.Serial)
        {
            return new NetworkPackageCheck(NetworkPackageStatus.NotNewer, snapshot, null);
        }

        var contentError = ValidateContent(snapshot);
        return contentError is null
            ? new NetworkPackageCheck(NetworkPackageStatus.Accepted, snapshot, null)
            : NetworkPackageCheck.Reject(contentError);
    }

    /// <summary>
    /// Проверка содержимого. Подпись говорит, что пакет выпустил администратор,
    /// но не что выпущенное годно: ошибка программы при выпуске подписывается
    /// так же исправно, как верные данные.
    /// </summary>
    private static string? ValidateContent(NetworkSnapshot snapshot)
    {
        if (snapshot.Serial < 1)
        {
            return "Номер выпуска пакета не положителен.";
        }

        var ids = new HashSet<Guid>();
        var logins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in snapshot.Users)
        {
            if (!ids.Add(user.Id) || string.IsNullOrWhiteSpace(user.Login) || !logins.Add(user.Login.Trim()))
            {
                return $"Пользователь «{user.Login}»: пустой или повторяющийся логин либо ключ.";
            }

            if (!OtkRoles.IsKnown(user.Role))
            {
                return $"Пользователь «{user.Login}»: неизвестная роль «{user.Role}». Обновите приложение.";
            }
        }

        // Сеть без действующего администратора некому вести: следующий пакет
        // выпустить будет некому, и стенды застрянут на этом навсегда.
        if (!snapshot.Users.Any(static u => u.IsActive && u.Role == OtkRoles.Admin))
        {
            return "В пакете нет ни одного действующего администратора.";
        }

        var referenceIds = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in snapshot.References)
        {
            if (!referenceIds.Add(reference.Id) || !names.Add(reference.Name.Trim()))
            {
                return $"Эталон «{reference.Name}» повторяется в пакете.";
            }

            var header = ReferencePackageHeader.TryParse(reference.PackageJson, out var error);
            if (header is null)
            {
                return $"Эталон «{reference.Name}»: {error}";
            }

            if (!string.Equals(header.ParamHash, reference.ParamHash, StringComparison.OrdinalIgnoreCase))
            {
                return $"Эталон «{reference.Name}»: хеш параметров в описании и в самом эталоне различается.";
            }
        }

        return null;
    }
}
