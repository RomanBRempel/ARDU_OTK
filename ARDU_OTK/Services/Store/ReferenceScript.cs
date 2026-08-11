using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ARDU_OTK.Services.Fc;
using ARDU_OTK.Services.Fc.Mavlink;

namespace ARDU_OTK.Services.Store;

/// <summary>
/// Скрипт изделия: путь на карте борта и его содержимое.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 Эталон хранит <b>содержимое</b> скрипта, а не ссылку на файл — по той же
/// причине, по которой он хранит содержимое параметров. Файл на сетевом ресурсе
/// исчезает ровно тогда, когда по нему разбирают рекламацию.
/// </para>
/// <para>
/// Путь входит в тождество скрипта наравне с содержимым: прошивка исполняет всё,
/// что лежит в каталоге скриптов, и тот же текст под другим именем — это другой
/// скрипт, а два имени с одним текстом исполнятся дважды.
/// </para>
/// </remarks>
/// <param name="Path">Путь на карте борта, например <c>APM/scripts/heartbeat.lua</c>.</param>
/// <param name="Text">Содержимое целиком.</param>
/// <param name="Hash">SHA-256 канонических байт — см. <see cref="ComputeHash"/>.</param>
/// <param name="ByteCount">Размер в байтах канонического представления.</param>
public sealed record ReferenceScript(string Path, string Text, string Hash, int ByteCount)
{
    /// <summary>Каталог, из которого прошивка исполняет скрипты Lua.</summary>
    public const string ScriptsDirectory = "APM/scripts";

    /// <summary>Расширение исполняемого скрипта. Прошивка берёт только такие файлы.</summary>
    public const string ScriptExtension = ".lua";

    /// <summary>Имя файла без каталога — для показа оператору.</summary>
    public string FileName => Path[(Path.LastIndexOf('/') + 1)..];

    /// <summary>Короткая подпись: имя, размер и первые разряды хеша.</summary>
    public string Caption => string.Create(
        CultureInfo.InvariantCulture,
        $"{FileName} · {ByteCount} байт · {ReferenceParameters.ShortHash(Hash)}");

    /// <summary>
    /// Канонические байты: UTF-8 без метки порядка байт.
    /// </summary>
    /// <remarks>
    /// 🔴 Именно эти байты и записываются на борт, и по ним же считается хеш.
    /// Одно представление на хранение, сверку и запись — иначе сохранённый
    /// скрипт и записанный на борт разошлись бы на невидимой метке BOM, и
    /// сверка расходилась бы вечно при полностью одинаковом тексте.
    /// </remarks>
    public byte[] ToBytes() => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Text);

    /// <summary>SHA-256 канонических байт, строчными шестнадцатеричными цифрами.</summary>
    public static string ComputeHash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>
    /// Собирает скрипт из прочитанных байт.
    /// </summary>
    /// <remarks>
    /// 🔴 Содержимое обязано пережить оборот «байты → текст → байты» без
    /// изменений. Не пережившее — это не текст: двоичный файл, попавший в
    /// каталог скриптов, либо текст в кодировке, которой здесь быть не должно.
    /// Записать в эталон его искажённую копию хуже, чем отказаться: искажённый
    /// скрипт синтаксически верен и делает не то.
    /// </remarks>
    /// <exception cref="InvalidDataException">Содержимое не является текстом UTF-8.</exception>
    public static ReferenceScript FromBytes(string path, ReadOnlySpan<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = NormalizePath(path);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        string text;
        try
        {
            // BOM снимается здесь: дальше по приложению скрипт ходит без неё.
            var body = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF
                ? content[3..]
                : content;

            text = encoding.GetString(body);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException(
                $"Файл «{normalizedPath}» не является текстом UTF-8 и скриптом считаться не может.", ex);
        }

        var canonical = encoding.GetBytes(text);
        return new ReferenceScript(normalizedPath, text, ComputeHash(canonical), canonical.Length);
    }

    /// <summary>Читает скрипт с диска станции.</summary>
    /// <exception cref="FileNotFoundException">Файла нет.</exception>
    /// <exception cref="InvalidDataException">Файл не является текстом UTF-8.</exception>
    public static ReferenceScript Load(string filePath, string? boardPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Файл скрипта не найден: {filePath}", filePath);
        }

        var name = System.IO.Path.GetFileName(filePath);
        return FromBytes(boardPath ?? $"{ScriptsDirectory}/{name}", File.ReadAllBytes(filePath));
    }

    /// <summary>
    /// Приводит путь к виду, в котором его понимает борт: прямые разделители,
    /// без ведущего слэша.
    /// </summary>
    public static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/').Trim();
}

/// <summary>Исход сверки одного скрипта с эталоном.</summary>
public enum ScriptComparison
{
    /// <summary>Путь и содержимое совпали.</summary>
    Match,

    /// <summary>Скрипт эталона на борту есть, но содержимое другое.</summary>
    ContentDiffers,

    /// <summary>Скрипта эталона на борту нет.</summary>
    MissingOnBoard,

    /// <summary>Скрипт есть на борту и отсутствует в эталоне.</summary>
    ExtraOnBoard,

    /// <summary>Скрипт с борта не прочитан — судить нельзя.</summary>
    Unreadable,
}

/// <summary>Расхождение по одному скрипту.</summary>
/// <param name="Path">Путь на борту.</param>
/// <param name="Outcome">Что именно не так.</param>
/// <param name="Detail">Пояснение для оператора и протокола.</param>
/// <param name="Expected">Скрипт эталона; <c>null</c> для лишнего на борту.</param>
/// <param name="ActualHash">SHA-256 того, что лежит на борту; пусто, если файла нет.</param>
public sealed record ScriptDifference(
    string Path,
    ScriptComparison Outcome,
    string Detail,
    ReferenceScript? Expected,
    string ActualHash);

/// <summary>
/// Снятие и сверка скриптов борта по MAVFTP.
/// </summary>
/// <remarks>
/// 🔴 Класс сознательно не знает ни про реестр, ни про UI: он работает с
/// каналом и списком скриптов. Это позволяет проверить сверку без стенда, а
/// чтение с борта — отдельным пробником, не поднимая приложение.
/// </remarks>
public static class ScriptTransfer
{
    /// <summary>
    /// Читает все скрипты из каталога скриптов борта.
    /// </summary>
    /// <remarks>
    /// Нечитаемый файл не рушит снимок и не пропадает молча: он возвращается
    /// в <paramref name="unreadable"/>. Тихо пропущенный скрипт означал бы
    /// эталон, в котором его нет, — и целевой борт, «совпавший» с эталоном при
    /// лишнем исполняемом файле на карте.
    /// </remarks>
    public static async Task<IReadOnlyList<ReferenceScript>> ReadAllAsync(
        IVehicleFileTransfer transfer,
        IProgress<string>? progress,
        List<string> unreadable,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(unreadable);

        progress?.Report($"Чтение каталога {ReferenceScript.ScriptsDirectory}…");

        IReadOnlyList<MavFtpEntry> entries;
        try
        {
            entries = await transfer
                .ListDirectoryAsync(ReferenceScript.ScriptsDirectory, ct)
                .ConfigureAwait(false);
        }
        catch (VehicleLinkException ex)
        {
            // Каталога может не быть вовсе — на борту без скриптов это норма,
            // а не отказ.
            unreadable.Add($"{ReferenceScript.ScriptsDirectory}: {ex.Message}");
            return Array.Empty<ReferenceScript>();
        }

        var scripts = new List<ReferenceScript>();

        foreach (var entry in entries.Where(static e => !e.IsDirectory)
                     .OrderBy(static e => e.Name, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            if (!entry.Name.EndsWith(ReferenceScript.ScriptExtension, StringComparison.OrdinalIgnoreCase))
            {
                // Прошивка исполняет только .lua. Прочее в каталоге —
                // заготовки и отключённые копии; в эталон они не входят.
                continue;
            }

            var path = $"{ReferenceScript.ScriptsDirectory}/{entry.Name}";
            progress?.Report($"Чтение {entry.Name}…");

            try
            {
                var content = await transfer.ReadFileAsync(path, null, ct).ConfigureAwait(false);
                scripts.Add(ReferenceScript.FromBytes(path, content));
            }
            catch (Exception ex) when (ex is VehicleLinkException or InvalidDataException)
            {
                unreadable.Add($"{entry.Name}: {ex.Message}");
            }
        }

        return scripts;
    }

    /// <summary>
    /// Сверяет скрипты борта с эталоном.
    /// </summary>
    /// <remarks>
    /// Лишний скрипт на борту — такое же расхождение, как недостающий:
    /// прошивка исполняет всё, что лежит в каталоге, и изделие с лишним
    /// скриптом ведёт себя не как эталонное при полностью совпавших параметрах.
    /// </remarks>
    public static IReadOnlyList<ScriptDifference> Compare(
        IReadOnlyList<ReferenceScript> expected,
        IReadOnlyList<ReferenceScript> actual,
        IReadOnlyList<string> unreadable)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(unreadable);

        var differences = new List<ScriptDifference>();
        var onBoard = actual.ToDictionary(static s => s.Path, StringComparer.OrdinalIgnoreCase);

        foreach (var script in expected)
        {
            if (!onBoard.TryGetValue(script.Path, out var found))
            {
                differences.Add(new ScriptDifference(
                    script.Path,
                    ScriptComparison.MissingOnBoard,
                    $"Скрипта нет на борту. Эталон требует {script.ByteCount} байт, "
                  + $"SHA-256 {ReferenceParameters.ShortHash(script.Hash)}.",
                    script,
                    string.Empty));
                continue;
            }

            if (!string.Equals(found.Hash, script.Hash, StringComparison.Ordinal))
            {
                differences.Add(new ScriptDifference(
                    script.Path,
                    ScriptComparison.ContentDiffers,
                    $"Содержимое другое: эталон {script.ByteCount} байт "
                  + $"({ReferenceParameters.ShortHash(script.Hash)}), борт {found.ByteCount} байт "
                  + $"({ReferenceParameters.ShortHash(found.Hash)}).",
                    script,
                    found.Hash));
            }
        }

        var expectedPaths = expected.Select(static s => s.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var script in actual.Where(s => !expectedPaths.Contains(s.Path)))
        {
            differences.Add(new ScriptDifference(
                script.Path,
                ScriptComparison.ExtraOnBoard,
                $"Скрипта нет в эталоне, а прошивка его исполняет: {script.ByteCount} байт "
              + $"({ReferenceParameters.ShortHash(script.Hash)}).",
                null,
                script.Hash));
        }

        foreach (var problem in unreadable)
        {
            differences.Add(new ScriptDifference(
                problem,
                ScriptComparison.Unreadable,
                "Файл с борта не прочитан — судить о совпадении нельзя.",
                null,
                string.Empty));
        }

        return differences;
    }
}
