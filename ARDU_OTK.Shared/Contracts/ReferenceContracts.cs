using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ARDU_OTK.Shared.Contracts;

/// <summary>
/// Опубликованный эталон, как его хранит сервер и получают стенды.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 Опубликованный эталон неизменяем по содержимому. Меняется только
/// признак обращения (<see cref="RetiredUtc"/>). Изменить эталон — значит
/// опубликовать новый и вывести старый: иначе стенды молча начали бы сдавать
/// платы по другим правилам под тем же именем, и прогоны «по эталону X»
/// за разные дни означали бы разное.
/// </para>
/// <para>
/// Содержимое передаётся файлом выгрузки <c>.otkref</c> целиком
/// (<see cref="PackageJson"/>) — тем же форматом, что ходил между стендами
/// вручную. Второй формат того же эталона разошёлся бы с первым.
/// </para>
/// </remarks>
/// <param name="Id">Глобальный ключ эталона.</param>
/// <param name="Name">Имя; уникально во всей сети, включая выведенные.</param>
/// <param name="ParamHash">Канонический хеш набора параметров, из пакета.</param>
/// <param name="PackageJson">Файл выгрузки эталона целиком.</param>
/// <param name="PublishedBy">Кто опубликовал.</param>
/// <param name="RetiredUtc">Когда выведен из обращения; <c>null</c> — действующий.</param>
/// <param name="Seq">Номер изменения на сервере — см. <see cref="SyncPullResponse"/>.</param>
public sealed record ReferenceRecord(
    Guid Id,
    string Name,
    string ParamHash,
    string PackageJson,
    Guid PublishedBy,
    string PublishedByName,
    DateTimeOffset PublishedUtc,
    DateTimeOffset? RetiredUtc,
    long Seq);

/// <summary>Публикация эталона: файл выгрузки целиком.</summary>
public sealed record PublishReferenceRequest(string PackageJson);

/// <summary>
/// Заголовок файла выгрузки эталона: то, что сервер обязан проверить, не
/// разбирая параметры.
/// </summary>
/// <remarks>
/// <para>
/// Сервер проверяет форму пакета и хеши скриптов, но не пересчитывает
/// канонический хеш параметров: разбор параметров ArduPilot живёт в
/// приложении стенда, и его копия на сервере — второй экземпляр той же
/// истины. Полную проверку пакет проходит дважды на стендах: при выгрузке
/// у администратора и при приёме каждой репликой.
/// </para>
/// <para>
/// Поля и их имена совпадают с <c>ARDU_OTK.Services.Store.ReferencePackage</c>
/// стенда. Сериализатор настроен на те же имена свойств.
/// </para>
/// </remarks>
public sealed class ReferencePackageHeader
{
    /// <summary>Версия формата, которую понимает эта сборка.</summary>
    public const int SupportedVersion = 1;

    public int Version { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ParamText { get; set; } = string.Empty;

    public string ParamHash { get; set; } = string.Empty;

    public List<Script> Scripts { get; set; } = [];

    public sealed class Script
    {
        public string Path { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public string Hash { get; set; } = string.Empty;
    }

    /// <summary>
    /// Разбирает и проверяет пакет.
    /// </summary>
    /// <param name="error">Причина отказа для администратора.</param>
    /// <returns>Заголовок либо <c>null</c>, если пакет негоден.</returns>
    public static ReferencePackageHeader? TryParse(string? json, out string? error)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Пакет эталона пуст.";
            return null;
        }

        ReferencePackageHeader? header;
        try
        {
            header = JsonSerializer.Deserialize<ReferencePackageHeader>(json);
        }
        catch (JsonException ex)
        {
            error = "Пакет эталона не разбирается: " + ex.Message;
            return null;
        }

        if (header is null)
        {
            error = "Пакет эталона пуст.";
            return null;
        }

        // Ноль — поле версии отсутствует: такой пакет собран не программой ОТК.
        if (header.Version < 1 || header.Version > SupportedVersion)
        {
            error = $"Версия пакета {header.Version}, сервер понимает {SupportedVersion}.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(header.Name))
        {
            error = "В пакете нет имени эталона.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(header.ParamText))
        {
            error = "В пакете нет параметров.";
            return null;
        }

        if (!IsSha256Hex(header.ParamHash))
        {
            error = "В пакете нет хеша параметров либо он не SHA-256.";
            return null;
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var script in header.Scripts)
        {
            if (string.IsNullOrWhiteSpace(script.Path) || !paths.Add(script.Path))
            {
                error = $"Скрипт без пути либо путь повторяется: «{script.Path}».";
                return null;
            }

            // Тот же расчёт, что у стенда: SHA-256 UTF-8 без BOM, строчные hex.
            var actual = Convert.ToHexString(
                SHA256.HashData(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(script.Text)))
                .ToLowerInvariant();
            if (!string.Equals(actual, script.Hash, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Скрипт «{script.Path}» не сходится со своим хешем.";
                return null;
            }
        }

        error = null;
        return header;
    }

    private static bool IsSha256Hex(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigit);
}
