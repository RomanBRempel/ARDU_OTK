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

    /// <summary>Корень настроечных файлов прошивки на карте.</summary>
    public const string FilesRoot = "APM";

    /// <summary>Расширение исполняемого скрипта. Прошивка берёт только такие файлы.</summary>
    public const string ScriptExtension = ".lua";

    /// <summary>
    /// Расширения файлов, входящих в эталон.
    /// </summary>
    /// <remarks>
    /// 🔴 Не только <c>.lua</c>. Прошивка и сами скрипты читают с карты
    /// текстовые настройки — позывной в <c>APM/callsign.txt</c>, таблицы и
    /// пороги рядом со скриптами, — и изделие с другим содержимым такого файла
    /// ведёт себя не как эталонное при полностью совпавших параметрах и
    /// одинаковых скриптах. Сверка только исполняемых файлов объявляла такое
    /// изделие сошедшимся.
    /// </remarks>
    public static readonly string[] FileExtensions =
        [".lua", ".txt", ".param", ".json", ".cfg", ".ini", ".csv"];

    /// <summary>
    /// Каталоги на карте, в которые снимок не заходит.
    /// </summary>
    /// <remarks>
    /// Журналы, карта местности и резервная копия хранилища параметров — это
    /// не конфигурация изделия, а его следы: они меняются на каждом включении,
    /// весят десятки мегабайт и по MAVFTP читались бы часами. Их совпадения от
    /// изделия никто не требует.
    /// </remarks>
    public static readonly string[] SkippedDirectories = ["LOGS", "TERRAIN", "STRG_BAK", "logs"];

    /// <summary>
    /// Наибольший размер файла, попадающего в эталон.
    /// </summary>
    /// <remarks>
    /// Настроечный файл столько не весит. Всё, что больше, — это данные, и
    /// протаскивать их через MAVFTP по 200 байт за пакет бессмысленно; такой
    /// файл отмечается непрочитанным, а не пропускается молча.
    /// </remarks>
    public const int MaxFileBytes = 256 * 1024;

    /// <summary>Файл относится к настройкам изделия и входит в эталон.</summary>
    public static bool IsConfigFile(string name) =>
        FileExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    /// <summary>Путь на карте борта, предлагаемый для файла, добавляемого с диска станции.</summary>
    public static string DefaultBoardPath(string fileName) => $"{ScriptsDirectory}/{fileName}";

    /// <summary>
    /// Проверяет путь на карте борта, под которым файл кладут в эталон.
    /// </summary>
    /// <remarks>
    /// 🔴 Эталон вправе содержать только то, что снимок с борта способен
    /// прочитать: снимок обходит <see cref="FilesRoot"/> и берёт файлы с
    /// расширениями из <see cref="FileExtensions"/>. Запись эталона вне этого
    /// набора не может совпасть никогда — сверка вечно докладывала бы «файла нет
    /// на борту» при лежащем на карте файле, и отличить настоящую пропажу от
    /// невидимой для снимка записи было бы нечем. Поэтому набор проверяется на
    /// входе в эталон, а не на сверке.
    /// </remarks>
    /// <param name="path">Путь, введённый оператором.</param>
    /// <param name="normalized">Приведённый путь; пусто при отказе.</param>
    /// <param name="problem">Что именно не так; пусто при успехе.</param>
    public static bool TryNormalizeBoardPath(string? path, out string normalized, out string problem)
    {
        normalized = string.Empty;
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            problem = "Путь пуст. Укажите, где файл лежит на карте борта.";
            return false;
        }

        var candidate = NormalizePath(path);

        if (candidate.EndsWith('/'))
        {
            problem = "Путь оканчивается каталогом. Нужен путь до файла вместе с именем.";
            return false;
        }

        if (candidate.Split('/').Any(static part => part is "" or "." or ".."))
        {
            problem = "В пути пустой сегмент либо «.»/«..». Такой путь борт не примет.";
            return false;
        }

        if (!candidate.StartsWith(FilesRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            problem = $"Путь обязан начинаться с «{FilesRoot}/»: снимок с борта берёт файлы только оттуда, "
                    + "и запись вне этого дерева сверить будет нечем.";
            return false;
        }

        var name = candidate[(candidate.LastIndexOf('/') + 1)..];
        if (!IsConfigFile(name))
        {
            problem = $"Расширение файла «{name}» не входит в набор эталона "
                    + $"({string.Join(", ", FileExtensions)}). Снимок с борта такой файл не читает, "
                    + "и сверка вечно докладывала бы, что его нет на борту.";
            return false;
        }

        var parts = candidate.Split('/');
        if (parts.Length - 2 > ScriptTransfer.MaxDepth)
        {
            problem = $"Файл лежит глубже {ScriptTransfer.MaxDepth} каталогов от «{FilesRoot}» — "
                    + "туда снимок с борта не заходит.";
            return false;
        }

        var skipped = parts
            .FirstOrDefault(part => SkippedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase));
        if (skipped is not null)
        {
            problem = $"Каталог «{skipped}» снимок с борта не обходит: там лежат следы работы изделия, "
                    + "а не его настройка.";
            return false;
        }

        normalized = candidate;
        return true;
    }

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

        var scripts = new List<ReferenceScript>();
        await ReadDirectoryAsync(
            transfer,
            ReferenceScript.FilesRoot,
            depth: 0,
            scripts,
            progress,
            unreadable,
            ct).ConfigureAwait(false);

        return scripts;
    }

    /// <summary>
    /// Глубина обхода каталогов от <c>APM</c>.
    /// </summary>
    /// <remarks>
    /// Настроечные файлы лежат либо в самом <c>APM</c>, либо в <c>APM/scripts</c>
    /// и его подкаталогах модулей. Дальше на карте начинаются данные, а не
    /// настройки; предел обхода не даёт снимку уйти в чужое дерево, если карту
    /// использовали ещё для чего-нибудь.
    /// </remarks>
    public const int MaxDepth = 3;

    private static async Task ReadDirectoryAsync(
        IVehicleFileTransfer transfer,
        string directory,
        int depth,
        List<ReferenceScript> scripts,
        IProgress<string>? progress,
        List<string> unreadable,
        CancellationToken ct)
    {
        progress?.Report($"Чтение каталога {directory}…");

        IReadOnlyList<MavFtpEntry> entries;
        try
        {
            entries = await transfer.ListDirectoryAsync(directory, ct).ConfigureAwait(false);
        }
        catch (VehicleLinkException ex)
        {
            // Каталога может не быть вовсе — на борту без скриптов это норма,
            // а не отказ. Но умолчать о нём нельзя: пустой снимок и снимок
            // непрочитанного каталога — разные вещи.
            unreadable.Add($"{directory}: {ex.Message}");
            return;
        }

        foreach (var entry in entries.Where(static e => !e.IsDirectory)
                     .OrderBy(static e => e.Name, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            if (!ReferenceScript.IsConfigFile(entry.Name))
            {
                // Двоичное и служебное содержимое карты настройкой изделия не
                // является: журналы, дампы, файлы прошивки.
                continue;
            }

            var path = $"{directory}/{entry.Name}";

            if (entry.Size > ReferenceScript.MaxFileBytes)
            {
                unreadable.Add(
                    $"{path}: {entry.Size} байт — больше предела {ReferenceScript.MaxFileBytes}; "
                  + "настроечный файл столько не весит.");
                continue;
            }

            progress?.Report($"Чтение {path}…");

            try
            {
                var content = await transfer.ReadFileAsync(path, null, ct).ConfigureAwait(false);
                scripts.Add(ReferenceScript.FromBytes(path, content));
            }
            catch (Exception ex) when (ex is VehicleLinkException or InvalidDataException)
            {
                unreadable.Add($"{path}: {ex.Message}");
            }
        }

        if (depth >= MaxDepth)
        {
            return;
        }

        foreach (var entry in entries.Where(static e => e.IsDirectory)
                     .OrderBy(static e => e.Name, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            if (entry.Name is "." or ".."
                || ReferenceScript.SkippedDirectories.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            await ReadDirectoryAsync(
                transfer,
                $"{directory}/{entry.Name}",
                depth + 1,
                scripts,
                progress,
                unreadable,
                ct).ConfigureAwait(false);
        }
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
