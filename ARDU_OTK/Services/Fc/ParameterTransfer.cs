using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ARDU_OTK.Services.Fc;

/// <summary>Что именно расходится по одному имени.</summary>
public enum ParameterDiffKind
{
    /// <summary>Значения различаются. Это и есть то, что переносится.</summary>
    Differs,

    /// <summary>Имя есть в эталоне, а борт его не знает.</summary>
    MissingOnBoard,

    /// <summary>Борт знает имя, а эталон о нём молчит.</summary>
    NotInReference,
}

/// <summary>Расхождение по одному контролируемому параметру.</summary>
/// <param name="Name">Имя параметра.</param>
/// <param name="Kind">Вид расхождения.</param>
/// <param name="Expected">Значение эталона; <c>null</c> при <see cref="ParameterDiffKind.NotInReference"/>.</param>
/// <param name="Actual">Значение борта; <c>null</c> при <see cref="ParameterDiffKind.MissingOnBoard"/>.</param>
/// <param name="Type">Тип параметра на борту — по нему сравнивают и пишут.</param>
/// <param name="Visible">Параметр в секции «контроль и показ».</param>
/// <param name="Writable">Можно ли устранить расхождение записью.</param>
/// <param name="Detail">Пояснение для оператора и протокола.</param>
public sealed record ParameterDifference(
    string Name,
    ParameterDiffKind Kind,
    double? Expected,
    double? Actual,
    MavParamType Type,
    bool Visible,
    bool Writable,
    string Detail);

/// <summary>
/// План приведения борта к эталону: что сошлось, что расходится и что из этого
/// можно записать.
/// </summary>
/// <param name="Compared">Сколько контролируемых имён сопоставлено.</param>
/// <param name="Matched">Сколько из них совпало.</param>
/// <param name="Differences">Расхождения, в порядке имён.</param>
/// <param name="SkippedUncontrolled">Сколько имён пропущено как неконтролируемые.</param>
public sealed record ParameterTransferPlan(
    int Compared,
    int Matched,
    IReadOnlyList<ParameterDifference> Differences,
    int SkippedUncontrolled)
{
    /// <summary>Расхождения, устранимые записью.</summary>
    public IReadOnlyList<ParameterDifference> Writable =>
        Differences.Where(static d => d.Writable).ToArray();

    /// <summary>Борт совпал с эталоном по всем контролируемым именам.</summary>
    public bool IsClean => Differences.Count == 0;
}

/// <summary>
/// Сверка и перенос всей конфигурации изделия: не компасного блока, а всего,
/// что эталон объявил контролируемым.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 До появления этого класса переносился только компасный блок — тринадцать
/// имён на слот. Всё остальное, чем одна сборка отличается от другой (каналы
/// приёмника, выходы, полётные режимы, регуляторы), не сверялось и не
/// записывалось вовсе. Изделие, «прошедшее» приёмку, могло иметь чужую
/// раскладку пульта и другой набор режимов.
/// </para>
/// <para>
/// Класс не знает ни про UI, ни про реестр: на входе два набора значений и
/// карта ролей, на выходе — план. Это позволяет проверить сверку без стенда.
/// </para>
/// </remarks>
public static class ParameterTransfer
{
    /// <summary>
    /// Строит план: сравнивает борт с эталоном по всем контролируемым именам.
    /// </summary>
    /// <param name="reference">Значения эталона.</param>
    /// <param name="board">Полная таблица, снятая с целевого борта.</param>
    /// <param name="roles">Роли параметров этого эталона.</param>
    /// <remarks>
    /// <para>
    /// Имя, которого нет на борту, и имя, которого нет в эталоне, — разные
    /// факты. Первое означает, что эталон описывает другую прошивку либо
    /// другую сборку; второе — что борт умеет то, о чём эталон не высказался.
    /// Оба показываются, но записью устраняется только первое — да и то не
    /// всегда: несуществующее на борту имя записать нельзя.
    /// </para>
    /// <para>
    /// 🔴 Тип берётся с борта, а не из эталона. Целые сравниваются точно, без
    /// допуска: у перечислений и битовых масок «близко» не бывает, и допуск
    /// разрешил бы перепутать соседние режимы.
    /// </para>
    /// </remarks>
    public static ParameterTransferPlan Plan(
        IReadOnlyDictionary<string, double> reference,
        IReadOnlyDictionary<string, ParamValue> board,
        ParameterRoleMap roles)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(roles);

        var differences = new List<ParameterDifference>();
        var compared = 0;
        var matched = 0;
        var skipped = 0;

        foreach (var pair in reference.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            var role = roles.Classify(pair.Key);
            if (!role.IsControlled)
            {
                skipped++;
                continue;
            }

            var visible = role.Control == ParameterControl.ControlledVisible;

            if (!board.TryGetValue(pair.Key, out var actual))
            {
                differences.Add(new ParameterDifference(
                    pair.Key,
                    ParameterDiffKind.MissingOnBoard,
                    pair.Value,
                    null,
                    MavParamType.Real32,
                    visible,
                    Writable: false,
                    "Эталон требует этот параметр, а борт его не знает. Записать несуществующее имя нельзя: "
                  + "либо на борту другая прошивка, либо эталон снят с другой сборки."));
                continue;
            }

            compared++;

            if (ReferenceParamFile.ValuesEqual(pair.Value, actual))
            {
                matched++;
                continue;
            }

            // Запрет записи не перекрывается ролью: роль решает, сверяем ли мы
            // имя, а не разрешено ли его писать.
            var writable = !CompassIdentity.IsNeverWrite(pair.Key);

            var values = string.Create(
                CultureInfo.InvariantCulture,
                $"Эталон {ReferenceParamFile.FormatCanonical(pair.Value)}, борт {actual.Value:G9}.");

            differences.Add(new ParameterDifference(
                pair.Key,
                ParameterDiffKind.Differs,
                pair.Value,
                actual.Value,
                actual.Type,
                visible,
                writable,
                writable
                    ? values
                    : values
                      + " Записывать это имя запрещено: оно называет конкретный экземпляр железа, и "
                      + "подложенное чужое значение пересаживает калибровку не на тот датчик."));
        }

        foreach (var pair in board.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            if (reference.ContainsKey(pair.Key) || !roles.IsControlled(pair.Key))
            {
                continue;
            }

            differences.Add(new ParameterDifference(
                pair.Key,
                ParameterDiffKind.NotInReference,
                null,
                pair.Value.Value,
                pair.Value.Type,
                roles.Classify(pair.Key).Control == ParameterControl.ControlledVisible,
                Writable: false,
                "Борт знает этот параметр, а эталон о нём молчит. Привести к эталону нечем: "
              + "эталон не говорит, каким значение должно быть."));
        }

        return new ParameterTransferPlan(compared, matched, differences, skipped);
    }

    /// <summary>
    /// Записывает на борт эталонные значения расходящихся параметров.
    /// </summary>
    /// <remarks>
    /// 🔴 Каждая запись подтверждается независимым обратным чтением по имени.
    /// Эхо <c>PARAM_SET</c> подтверждением не считается: оно широковещательное,
    /// приходит с недостоверным индексом и может относиться к записи, сделанной
    /// другой станцией. Провалившаяся запись не прерывает остальные — оператору
    /// нужен результат по всему списку, а не останов на первом упрямом имени.
    /// </remarks>
    /// <param name="link">Открытый канал связи.</param>
    /// <param name="differences">Что записывать. Незаписываемые пропускаются молча — их отобрал план.</param>
    /// <param name="progress">Куда сообщать ход; может быть <c>null</c>.</param>
    /// <param name="ct">Токен отмены.</param>
    public static async Task<IReadOnlyList<ParamWriteRecord>> ApplyAsync(
        IVehicleLink link,
        IReadOnlyList<ParameterDifference> differences,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(differences);

        var records = new List<ParamWriteRecord>(differences.Count);
        var done = 0;

        foreach (var difference in differences.Where(static d => d.Writable && d.Expected is not null))
        {
            ct.ThrowIfCancellationRequested();

            done++;
            progress?.Report($"Запись {difference.Name} ({done} из {differences.Count})…");

            var requested = difference.Expected!.Value;
            double? before = difference.Actual;

            try
            {
                // Тип перечитывается с борта, а не берётся из плана: между
                // построением плана и записью борт мог быть перезагружен, а
                // запись целого как вещественного молча теряет значение.
                var current = await link.ReadParamAsync(difference.Name, ct).ConfigureAwait(false);
                before = current.Value;

                await link.WriteParamAsync(
                    difference.Name,
                    ReferenceParamFile.ToBoardFloat(requested),
                    current.Type,
                    ct).ConfigureAwait(false);

                var readBack = await link.ReadParamAsync(difference.Name, ct).ConfigureAwait(false);

                records.Add(new ParamWriteRecord(
                    difference.Name,
                    before,
                    requested,
                    readBack.Value,
                    ReferenceParamFile.ValuesEqual(requested, readBack)
                        ? WriteOutcome.Verified
                        : WriteOutcome.Mismatch,
                    DateTimeOffset.UtcNow));
            }
            catch (VehicleLinkException)
            {
                records.Add(new ParamWriteRecord(
                    difference.Name,
                    before,
                    requested,
                    null,
                    WriteOutcome.Failed,
                    DateTimeOffset.UtcNow));
            }
        }

        return records;
    }
}
