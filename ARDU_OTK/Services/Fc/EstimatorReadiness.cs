using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ARDU_OTK.Services.Fc;

/// <summary>
/// Почему борт работает не тем оценщиком положения, который ему настроен.
/// </summary>
/// <remarks>
/// Классификация нужна ровно затем же, зачем <see cref="CompassComplaintKind"/>:
/// «подождать», «дописать параметр», «эталон требует несуществующего» и
/// «на этой плате нет такого железа» — четыре разных действия оператора, а
/// прошивка называет их одной и той же строкой.
/// </remarks>
public enum EstimatorFaultKind
{
    /// <summary>Претензии нет: работает настроенный оценщик.</summary>
    None,

    /// <summary>Оценщик ещё поднимается после загрузки. Снимается временем.</summary>
    Settling,

    /// <summary>Оценщик настроен, но выключен своим флагом. Снимается записью и перезагрузкой.</summary>
    Disabled,

    /// <summary>Прошивка этой сборки такого оценщика не содержит. Записью не снимается.</summary>
    Missing,

    /// <summary>Конфигурация требует железа, которого на этой плате нет.</summary>
    HardwareMismatch,

    /// <summary>Оценщик не поднимается, пока держатся другие предполётные претензии.</summary>
    BlockedByComplaints,

    /// <summary>Причина по доступным признакам не определена.</summary>
    Unknown,
}

/// <summary>Разбор претензии к оценщику.</summary>
/// <param name="Kind">Чем эта претензия закрывается.</param>
/// <param name="Detail">Что именно обнаружено — для оператора и протокола.</param>
/// <param name="FixParameter">Имя параметра, записью которого претензия снимается; <c>null</c> — записью не снимается.</param>
/// <param name="FixValue">Значение, которое надо записать в <paramref name="FixParameter"/>.</param>
public sealed record EstimatorDiagnosis(
    EstimatorFaultKind Kind,
    string Detail,
    string? FixParameter = null,
    double FixValue = 0);

/// <summary>
/// Готовность оценщика положения: разбор претензии <c>not using configured
/// AHRS type</c> по состоянию борта.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 Здесь нет ни MAVLink, ни ввода-вывода — как и в <see cref="AcceptanceChecks"/>.
/// На входе снятые с борта числа и его же строки, на выходе вывод. Это сделано
/// ради проверяемости: правило, по которому приёмка решает судьбу изделия,
/// должно прогоняться без борта на столе.
/// </para>
/// <para>
/// 🔴 Сообщение прошивки означает одно: <c>AHRS_EKF_TYPE</c> называет один
/// оценщик, а положение борта считает другой (обычно резервный DCM). Само по
/// себе оно причины не называет, поэтому разбор идёт по состоянию борта, а не
/// по тексту.
/// </para>
/// </remarks>
public static class EstimatorReadiness
{
    /// <summary>Устойчивая часть сообщения прошивки. Префиксы <c>PreArm:</c> и <c>AHRS:</c> не в счёт.</summary>
    /// <remarks>
    /// Сравнение идёт вхождением подстроки, а не равенством: прошивка отдаёт
    /// строку с приставками (<c>PreArm: AHRS: not using configured AHRS type</c>),
    /// и сравнение целых строк не поймало бы ни одного реального случая.
    /// </remarks>
    public const string ComplaintFragment = "not using configured AHRS type";

    /// <summary>Какой оценщик назначен борту.</summary>
    public const string TypeParameter = "AHRS_EKF_TYPE";

    /// <summary>Какие инерциальные блоки обязан использовать EKF3.</summary>
    public const string ImuMaskParameter = "EK3_IMU_MASK";

    /// <summary>Идентификаторы акселерометров по экземплярам. Ноль или незнакомое имя — блока нет.</summary>
    /// <remarks>
    /// 🔴 Имена нерегулярны: первый экземпляр без цифры. Это не опечатка и не
    /// повод «выровнять» их циклом по индексу.
    /// </remarks>
    public static readonly string[] AccelIdParameters = ["INS_ACC_ID", "INS_ACC2_ID", "INS_ACC3_ID"];

    /// <summary>Имя флага включения для назначенного оценщика; <c>null</c> — у этого вида флага нет.</summary>
    public static string? EnableParameterFor(int ekfType) => ekfType switch
    {
        2 => "EK2_ENABLE",
        3 => "EK3_ENABLE",
        _ => null,
    };

    /// <summary>Название оценщика для протокола.</summary>
    public static string DescribeType(int ekfType) => ekfType switch
    {
        0 => "DCM (AHRS_EKF_TYPE = 0)",
        2 => "EKF2 (AHRS_EKF_TYPE = 2)",
        3 => "EKF3 (AHRS_EKF_TYPE = 3)",
        11 => "внешний AHRS (AHRS_EKF_TYPE = 11)",
        _ => "AHRS_EKF_TYPE = " + ekfType.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Относится ли строка борта к претензии на оценщик.</summary>
    public static bool IsEstimatorComplaint(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Contains(ComplaintFragment, StringComparison.OrdinalIgnoreCase);

    /// <summary>Есть ли претензия на оценщик среди сообщений борта.</summary>
    public static bool HasEstimatorComplaint(IEnumerable<StatusTextEvent>? messages) =>
        messages?.Any(static m => IsEstimatorComplaint(m?.Text)) == true;

    /// <summary>
    /// Разбирает, почему настроенный оценщик не работает.
    /// </summary>
    /// <param name="ekfType">Значение <see cref="TypeParameter"/>, снятое с борта.</param>
    /// <param name="enableName">Имя флага включения; <c>null</c> — у этого вида его нет.</param>
    /// <param name="enableValue">Значение флага; <c>null</c> — борт такого имени не знает.</param>
    /// <param name="imuMask">Значение <see cref="ImuMaskParameter"/>; <c>null</c> — борт имени не знает.</param>
    /// <param name="presentAccelIds">Идентификаторы акселерометров по экземплярам, в порядке экземпляров.</param>
    /// <param name="otherComplaints">Прочие претензии того же отчёта, без префиксов.</param>
    public static EstimatorDiagnosis Diagnose(
        int ekfType,
        string? enableName,
        double? enableValue,
        double? imuMask,
        IReadOnlyList<uint> presentAccelIds,
        IReadOnlyList<string> otherComplaints)
    {
        ArgumentNullException.ThrowIfNull(presentAccelIds);
        ArgumentNullException.ThrowIfNull(otherComplaints);

        var type = DescribeType(ekfType);

        // 🔴 Незнакомое борту имя флага — это не сбой чтения, а факт о сборке:
        // такого оценщика в прошивке нет. Записью это не снимается, и молча
        // подменять AHRS_EKF_TYPE на другой вид нельзя — плата разойдётся с
        // эталоном по параметру, который оператор считает сверенным.
        if (enableName is not null && enableValue is null)
        {
            return new EstimatorDiagnosis(
                EstimatorFaultKind.Missing,
                $"Эталон назначил {type}, но борт не знает параметра {enableName}: в этой сборке прошивки такого "
              + "оценщика нет. Записью не снимается — нужен эталон под прошивку борта либо прошивка под эталон.");
        }

        if (enableName is not null && enableValue is { } enable && Math.Abs(enable) < 0.5)
        {
            return new EstimatorDiagnosis(
                EstimatorFaultKind.Disabled,
                $"Эталон назначил {type}, а {enableName} = 0: назначенный оценщик выключен, положение считает "
              + "резервный. Снимается записью " + enableName + " = 1 и перезагрузкой.",
                enableName,
                1);
        }

        // Состав инерциальных блоков — свойство этой платы, а не изделия.
        // Маска, перенесённая с эталона, законно требует блока, которого здесь
        // нет: тогда оценщик не стартует, а на эталоне той же претензии не
        // возникает никогда.
        if (ekfType == 3 && imuMask is { } mask)
        {
            var required = (uint)Math.Max(0, Math.Round(mask));
            var missing = new List<int>();

            for (var bit = 0; bit < presentAccelIds.Count; bit++)
            {
                if ((required & (1u << bit)) != 0 && presentAccelIds[bit] == 0)
                {
                    missing.Add(bit + 1);
                }
            }

            if (missing.Count > 0)
            {
                var names = string.Join(
                    ", ",
                    missing.Select(i => AccelIdParameters[Math.Min(i - 1, AccelIdParameters.Length - 1)] + " = 0"));

                return new EstimatorDiagnosis(
                    EstimatorFaultKind.HardwareMismatch,
                    $"{ImuMaskParameter} = {required.ToString(CultureInfo.InvariantCulture)} требует инерциальные "
                  + $"блоки № {string.Join(", ", missing)}, а плата их не опознала ({names}). Маска перенесена с "
                  + "эталона и описывает его состав железа, а не этой платы. Записью флагов не снимается: приведите "
                  + $"{ImuMaskParameter} к фактическому составу платы или возьмите эталон той же сборки.");
            }
        }

        // 🔴 Оценщик не поднимается поверх неисправного датчика и поверх
        // некалиброванных акселерометров. Калибровка акселерометров с эталона
        // намеренно не переносится — она измеряет микросхемы этой платы, — и
        // именно поэтому претензия к оценщику встречается на целевой плате и
        // не встречается на эталоне.
        if (otherComplaints.Count > 0)
        {
            return new EstimatorDiagnosis(
                EstimatorFaultKind.BlockedByComplaints,
                $"Назначенный оценщик — {type}, флаг включения выставлен, состав блоков сходится, но борт держит "
              + "другие претензии: " + string.Join("; ", otherComplaints)
              + ". Оценщик не стартует, пока они не сняты. Калибровка акселерометров и уровня выполняется на самой "
              + "плате: с эталона она не переносится.");
        }

        return new EstimatorDiagnosis(
            EstimatorFaultKind.Unknown,
            $"Назначен {type}, флаг включения выставлен, других претензий борт не назвал, но положение по-прежнему "
          + "считает не назначенный оценщик. Причина по доступным признакам не определена — разберите вручную.");
    }

    /// <summary>Убирает префиксы прошивки и пробелы, оставляя суть претензии.</summary>
    public static string StripPrefixes(string text)
    {
        var t = (text ?? string.Empty).Trim();

        foreach (var prefix in new[] { "PreArm:", "AHRS:" })
        {
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                t = t[prefix.Length..].Trim();
            }
        }

        return t;
    }
}
