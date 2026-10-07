using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ARDU_OTK.Shared.Network;

/// <summary>
/// Шифрование пакета сети кодом сети: AES-256-GCM поверх подписанного файла.
/// </summary>
/// <remarks>
/// <para>
/// Пакет лежит в публичном репозитории, чтобы станция скачивала его без
/// установки программ и без учётных данных. Подпись защищает от подделки, но
/// не от чтения, а в пакете — эталоны (ноу-хау предприятия) и хеши паролей и
/// PIN. PIN из четырёх цифр по хешу подбирается за минуты; открытый пакет
/// раздавал бы PIN всем операторам любому, кто знает адрес репозитория.
/// </para>
/// <para>
/// Код сети — 120 случайных бит, поэтому ключ выводится HKDF без медленного
/// растяжения: перебирать нечего. Код сообщают станции один раз при установке.
/// </para>
/// <para>
/// Порядок — «подписать, затем зашифровать»: под шифром лежит ровно тот файл,
/// что проверяет <see cref="NetworkPackageFile.Verify"/>, и доверие по-прежнему
/// даёт только подпись. Знание кода сети не позволяет выпустить пакет.
/// </para>
/// </remarks>
public static class NetworkSeal
{
    public const string FormatName = "ardu-otk-network-sealed";

    public const int CurrentFormatVersion = 1;

    private const int CodeBytes = 15;

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static readonly byte[] Salt = "ARDU_OTK.network.seal.v1"u8.ToArray();

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Новый код сети: 24 знака группами по 4, без похожих букв и цифр (O/0, I/1).</summary>
    public static string GenerateCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(CodeBytes);
        var chars = new char[CodeBytes * 8 / 5];
        var buffer = 0;
        var bits = 0;
        var index = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                chars[index++] = Alphabet[(buffer >> bits) & 31];
            }
        }

        return string.Join('-', Enumerable.Range(0, chars.Length / 4).Select(i => new string(chars, i * 4, 4)));
    }

    /// <summary>Код без разделителей и в верхнем регистре; <c>null</c> — код набран неверно.</summary>
    public static string? NormalizeCode(string? code)
    {
        if (code is null)
        {
            return null;
        }

        var clean = new string(code.Where(static c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        return clean.Length == CodeBytes * 8 / 5 && clean.All(static c => Alphabet.Contains(c)) ? clean : null;
    }

    /// <summary>Шифрует подписанный файл пакета.</summary>
    public static string Seal(string signedPackage, string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(signedPackage);
        var key = DeriveKey(code, out var codeId);

        var plain = Encoding.UTF8.GetBytes(signedPackage);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using (var aes = new AesGcm(key, tag.Length))
        {
            aes.Encrypt(nonce, plain, cipher, tag, Associated(codeId));
        }

        return JsonSerializer.Serialize(
            new Envelope
            {
                CodeId = codeId,
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(cipher),
                Tag = Convert.ToBase64String(tag),
            },
            Options);
    }

    /// <summary>
    /// Расшифровывает пакет.
    /// </summary>
    /// <param name="error">Причина отказа для оператора.</param>
    /// <returns>Подписанный файл пакета либо <c>null</c>.</returns>
    public static string? TryOpen(string? sealedPackage, string? code, out string? error)
    {
        if (NormalizeCode(code) is null)
        {
            error = "Код сети набран неверно: 24 знака, латинские буквы и цифры.";
            return null;
        }

        Envelope? envelope;
        try
        {
            envelope = string.IsNullOrWhiteSpace(sealedPackage) ? null : JsonSerializer.Deserialize<Envelope>(sealedPackage, Options);
        }
        catch (JsonException)
        {
            envelope = null;
        }

        if (envelope is null || envelope.Format != FormatName)
        {
            error = "Это не пакет сети ОТК.";
            return null;
        }

        if (envelope.FormatVersion != CurrentFormatVersion)
        {
            error = $"Пакет сети формата {envelope.FormatVersion}, эта сборка понимает {CurrentFormatVersion}. Обновите приложение.";
            return null;
        }

        var key = DeriveKey(code!, out var codeId);
        if (envelope.CodeId != codeId)
        {
            error = "Код сети не подходит к пакету: проверьте код у администратора.";
            return null;
        }

        try
        {
            var nonce = Convert.FromBase64String(envelope.Nonce);
            var cipher = Convert.FromBase64String(envelope.Ciphertext);
            var tag = Convert.FromBase64String(envelope.Tag);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, cipher, tag, plain, Associated(codeId));
            error = null;
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            error = "Пакет сети повреждён: расшифровка не сходится.";
            return null;
        }
    }

    private static byte[] DeriveKey(string code, out string codeId)
    {
        var normalized = NormalizeCode(code) ?? throw new ArgumentException("Код сети набран неверно.", nameof(code));
        var ikm = Encoding.ASCII.GetBytes(normalized);

        // Отпечаток кода — из отдельного вывода HKDF: он лежит в файле открыто
        // и позволяет сказать «не тот код», не раскрывая ключа.
        codeId = Convert.ToHexString(HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 8, Salt, "code-id"u8.ToArray()))
            .ToLowerInvariant();
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, Salt, "aes-256-gcm"u8.ToArray());
    }

    private static byte[] Associated(string codeId) => Encoding.ASCII.GetBytes(FormatName + ":" + codeId);

    private sealed class Envelope
    {
        public string Format { get; set; } = FormatName;

        public int FormatVersion { get; set; } = CurrentFormatVersion;

        public string CodeId { get; set; } = string.Empty;

        public string Nonce { get; set; } = string.Empty;

        public string Ciphertext { get; set; } = string.Empty;

        public string Tag { get; set; } = string.Empty;
    }
}
