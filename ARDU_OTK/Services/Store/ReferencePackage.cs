using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ARDU_OTK.Services.Fc;

namespace ARDU_OTK.Services.Store;

/// <summary>Скрипт в выгрузке эталона.</summary>
public sealed class ReferencePackageScript
{
    public string Path { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;
}

/// <summary>
/// Эталон одним файлом: всё, что делает его эталоном, а не набором чисел.
/// </summary>
/// <remarks>
/// 🔴 Эталон существовал только в базе того рабочего места, где его завели.
/// Передать его на другой стенд было нечем: файл <c>.param</c> несёт значения
/// и молчит обо всём остальном — о допусках, о раскладке контроля, о скриптах
/// изделия, о прошивке образца. Приняв такой файл, второй стенд сдавал бы
/// платы по другим правилам, считая, что по тем же самым. Поэтому выгрузка
/// содержит эталон целиком.
///
/// Хеш набора параметров переносится как есть и при загрузке пересчитывается
/// заново: файл ходит по сети и через флешки, и молча принятый повреждённый
/// эталон — это ложные прогоны на всём, что по нему сдадут.
/// </remarks>
public sealed class ReferencePackage
{
    /// <summary>Расширение файла выгрузки.</summary>
    public const string FileExtension = ".otkref";

    /// <summary>
    /// Версия формата.
    /// </summary>
    /// <remarks>
    /// Читатель отказывается от файла новее себя, а не разбирает его частично:
    /// неизвестное поле — это, скорее всего, новое правило контроля, и эталон
    /// без него будет тише и слабее исходного.
    /// </remarks>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;

    public string ParamFormat { get; set; } = string.Empty;

    public string ParamText { get; set; } = string.Empty;

    public string ParamHash { get; set; } = string.Empty;

    public int ParamCount { get; set; }

    public double HeadingVsJigDeg { get; set; }

    public double InterCompassSpreadDeg { get; set; }

    public bool TransferMotorComp { get; set; }

    /// <summary>Отступления технолога от умолчаний — в том же виде, что в базе.</summary>
    public string ParamRoles { get; set; } = string.Empty;

    public string Firmware { get; set; } = string.Empty;

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Кто и когда выгрузил — чтобы у файла было происхождение.</summary>
    public string ExportedBy { get; set; } = string.Empty;

    public DateTimeOffset ExportedUtc { get; set; }

    public List<ReferencePackageScript> Scripts { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    /// <summary>Собирает выгрузку из записи реестра и её скриптов.</summary>
    public static ReferencePackage FromReference(
        CalibrationReference reference,
        IReadOnlyList<ReferenceScript> scripts,
        string exportedBy)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(scripts);

        return new ReferencePackage
        {
            Version = CurrentVersion,
            Name = reference.Name,
            Description = reference.Description,
            SourceName = reference.SourceName,
            ParamFormat = reference.ParamFormat,
            ParamText = reference.ParamText,
            ParamHash = reference.ParamHash,
            ParamCount = reference.ParamCount,
            HeadingVsJigDeg = reference.HeadingVsJigDeg,
            InterCompassSpreadDeg = reference.InterCompassSpreadDeg,
            TransferMotorComp = reference.TransferMotorComp,
            ParamRoles = reference.ParamRoles,
            Firmware = reference.Firmware,
            CreatedBy = reference.CreatedBy,
            CreatedUtc = reference.CreatedUtc,
            ExportedBy = exportedBy,
            ExportedUtc = DateTimeOffset.UtcNow,
            Scripts = scripts
                .OrderBy(static s => s.Path, StringComparer.Ordinal)
                .Select(static s => new ReferencePackageScript
                {
                    Path = s.Path,
                    Text = s.Text,
                    Hash = s.Hash,
                })
                .ToList(),
        };
    }

    /// <summary>Текст файла выгрузки.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// Разбирает файл выгрузки и проверяет его пригодность.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Файл не разбирается, версия формата не объявлена либо новее понимаемой,
    /// хеш набора или скрипта отсутствует, либо содержимое с ним не сходится.
    /// </exception>
    public static ReferencePackage FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        ReferencePackage? package;
        try
        {
            package = JsonSerializer.Deserialize<ReferencePackage>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Файл эталона не разбирается: " + ex.Message, ex);
        }

        if (package is null)
        {
            throw new InvalidDataException("Файл эталона пуст.");
        }

        if (package.Version > CurrentVersion)
        {
            throw new InvalidDataException(
                $"Файл эталона версии {package.Version}, эта сборка понимает {CurrentVersion}. "
              + "Обновите приложение: принять файл частично нельзя — неизвестные поля это, скорее всего, "
              + "новые правила контроля, и эталон без них окажется слабее исходного.");
        }

        // 🔴 Отсутствующая версия — это не «версия 1». Поле по умолчанию равно
        // CurrentVersion, поэтому файл вообще без поля выглядел бы своим;
        // ноль и отрицательное значение означают, что файл собран не этой
        // программой либо правился руками, и разбирать его по нашим правилам
        // нельзя — соответствие полей ничем не подтверждено.
        if (package.Version < 1)
        {
            throw new InvalidDataException(
                $"В файле эталона не указана версия формата (записано {package.Version}). "
              + "Файл собран не этой программой либо правился вручную: разбирать его по нашим правилам нельзя.");
        }

        if (string.IsNullOrWhiteSpace(package.Name))
        {
            throw new InvalidDataException("В файле эталона нет имени.");
        }

        if (string.IsNullOrWhiteSpace(package.ParamText))
        {
            throw new InvalidDataException("В файле эталона нет параметров.");
        }

        // 🔴 Хеш пересчитывается, а не принимается на слово. Файл ходит по сети
        // и через флешки; молча принятый повреждённый эталон делает ложными все
        // прогоны, которые по нему сдадут, и обнаружится это не скоро.
        var parsed = ReferenceParamFile.Parse(
            package.SourceName,
            ReferenceParameters.SplitLines(package.ParamText));

        // 🔴 Отсутствие хеша — отказ, а не разрешение принять без проверки.
        // Пока пустое поле пропускало сверку, защита снималась ровно тем
        // действием, от которого защищает: достаточно было стереть строку
        // хеша, и повреждённый или подправленный набор входил в реестр молча.
        // Выгрузка хеш пишет всегда, поэтому законного файла без него не
        // бывает.
        if (package.ParamHash.Length == 0)
        {
            throw new InvalidDataException(
                "В файле эталона нет хеша набора параметров: проверить, что набор дошёл неповреждённым, "
              + "нечем. Выгрузка всегда его записывает — значит, файл правился вручную.");
        }

        var actualHash = ReferenceParamFile.ComputeHash(parsed);
        if (!string.Equals(actualHash, package.ParamHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Набор параметров не сходится с хешем в файле: "
              + $"записан {ReferenceParameters.ShortHash(package.ParamHash)}, "
              + $"пересчитан {ReferenceParameters.ShortHash(actualHash)}. Файл повреждён или правился вручную.");
        }

        foreach (var script in package.Scripts)
        {
            if (script.Hash.Length == 0)
            {
                throw new InvalidDataException(
                    $"У скрипта «{script.Path}» в файле нет хеша: проверить его содержимое нечем. "
                  + "Скрипт исполняется на изделии наравне с остальными, и принимать его без проверки нельзя.");
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(script.Text);
            var hash = ReferenceScript.ComputeHash(bytes);

            if (!string.Equals(hash, script.Hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Скрипт «{script.Path}» не сходится с хешем в файле. Файл повреждён или правился вручную.");
            }
        }

        return package;
    }

    /// <summary>Готовит запись для реестра.</summary>
    /// <param name="name">Имя, под которым эталон ляжет в реестр: в базе оно уникально.</param>
    public NewCalibrationReference ToDraft(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var parameters = ReferenceParameters.FromStored(new CalibrationReference(
            Id: 0,
            Name: name,
            Description: Description,
            SourceName: SourceName,
            ParamFormat: ParamFormat,
            ParamText: ParamText,
            ParamHash: ParamHash,
            ParamCount: ParamCount,
            HeadingVsJigDeg: HeadingVsJigDeg,
            InterCompassSpreadDeg: InterCompassSpreadDeg,
            TransferMotorComp: TransferMotorComp,
            ParamRoles: ParamRoles,
            Firmware: Firmware,
            CreatedBy: CreatedBy,
            CreatedUtc: CreatedUtc,
            RetiredUtc: null,
            RunCount: 0));

        var roles = ParameterRoleMap.Create(
            TransferMotorComp,
            ParameterRoleMap.DeserializeOverrides(ParamRoles, out _));

        return new NewCalibrationReference(
            Name: name,
            Description: Description,
            Parameters: parameters,
            HeadingVsJigDeg: HeadingVsJigDeg,
            InterCompassSpreadDeg: InterCompassSpreadDeg,
            TransferMotorComp: TransferMotorComp,
            CreatedBy: CreatedBy)
        {
            Firmware = Firmware,
            Roles = roles,
            Scripts = Scripts
                .Select(static s => ReferenceScript.FromBytes(
                    s.Path,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(s.Text)))
                .ToArray(),
        };
    }

    /// <summary>Подпись файла для оператора: что именно он собирается принять.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"«{Name}»: параметров {ParamCount}, скриптов {Scripts.Count}, "
      + $"{(Firmware.Length == 0 ? "прошивка не записана" : Firmware)}, "
      + $"завёл {CreatedBy} {CreatedUtc.ToLocalTime():dd.MM.yyyy}");
}
