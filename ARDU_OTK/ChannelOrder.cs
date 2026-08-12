using System;
using System.Globalization;

namespace ARDU_OTK;

/// <summary>
/// Разбор имён вида <c>RC5_MIN</c> и <c>SERVO12_FUNCTION</c> на канал и поле.
/// </summary>
/// <remarks>
/// 🔴 Канал — это одна вещь, а не пять параметров. Разложенный по алфавиту
/// список имён рвёт канал на строки <c>RC5_DZ</c>, <c>RC5_MAX</c>,
/// <c>RC5_MIN</c>, <c>RC5_OPTION</c>, <c>RC5_TRIM</c>, и чтобы прочесть, что
/// такое пятый канал, оператор собирает их глазами обратно. Mission Planner
/// показывает канал строкой по той же причине.
///
/// Порядок полей задан вручную и повторяет экран Mission Planner: сначала
/// назначение, потом границы и середина. Алфавитный порядок поставил бы
/// <c>MAX</c> перед <c>MIN</c>, и колонка границ читалась бы задом наперёд.
/// </remarks>
public static class ChannelOrder
{
    private static readonly string[] Prefixes = ["RC", "SERVO"];

    /// <summary>Поля в порядке показа. Незнакомое поле уходит в конец.</summary>
    private static readonly string[] FieldOrder =
        ["FUNCTION", "OPTION", "MIN", "TRIM", "MAX", "REVERSED"];

    /// <summary>
    /// Поля, которые в строку не выводятся, оставаясь под контролем.
    /// </summary>
    /// <remarks>
    /// Мёртвая зона — настройка чувствительности стика, а не признак изделия:
    /// в строке, по которой опознают сборку, она занимает место и ничего не
    /// сообщает. Из-под контроля при этом не выходит — расхождение по ней
    /// покажет обычная строка расхождения.
    /// </remarks>
    private static readonly string[] HiddenFields = ["DZ"];

    /// <summary>
    /// Поля, у которых подпись не нужна: значение само себя называет.
    /// </summary>
    /// <remarks>
    /// «OPTION Do Nothing» и «FUNCTION Aileron» — это слово, повторённое
    /// шестнадцать раз подряд в столбце, где и так стоит только назначение.
    /// </remarks>
    private static readonly string[] UnlabelledFields = ["FUNCTION", "OPTION"];

    /// <summary>Поле показывается в сводной строке канала.</summary>
    public static bool IsShown(string label) =>
        Array.IndexOf(HiddenFields, label) < 0;

    /// <summary>Поле выводится без подписи.</summary>
    public static bool IsUnlabelled(string label) =>
        Array.IndexOf(UnlabelledFields, label) >= 0;

    /// <summary>Поле показывается флажком, а не парой «подпись — значение».</summary>
    public static bool IsFlag(string label) =>
        string.Equals(label, "REVERSED", StringComparison.Ordinal);

    /// <summary>
    /// Разбирает имя на заголовок канала и подпись поля.
    /// </summary>
    /// <returns>
    /// <c>false</c>, если имя не относится к каналу или выходу: <c>RCMAP_ROLL</c>,
    /// <c>SERVO_RNG_ENABLE</c> и подобные разбору не подлежат и остаются
    /// обычными строками.
    /// </returns>
    public static bool TrySplit(string name, out string title, out string label)
    {
        title = string.Empty;
        label = string.Empty;

        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var prefix in Prefixes)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            int i = prefix.Length;
            while (i < name.Length && char.IsAsciiDigit(name[i]))
            {
                i++;
            }

            // Цифры обязаны быть, и сразу за ними — подчёркивание с полем.
            if (i == prefix.Length || i >= name.Length || name[i] != '_' || i + 1 >= name.Length)
            {
                continue;
            }

            title = name[..i];
            label = name[(i + 1)..];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Порядок каналов: сперва вид (<c>RC</c> перед <c>SERVO</c>), потом номер
    /// числом.
    /// </summary>
    /// <remarks>
    /// Именно числом. По алфавиту десятый канал встаёт между первым и вторым,
    /// и список каналов перестаёт быть списком каналов.
    /// </remarks>
    public static int Compare(string left, string right)
    {
        var (leftPrefix, leftNumber) = Parse(left);
        var (rightPrefix, rightNumber) = Parse(right);

        int byPrefix = string.CompareOrdinal(leftPrefix, rightPrefix);
        return byPrefix != 0 ? byPrefix : leftNumber.CompareTo(rightNumber);
    }

    /// <summary>Порядок полей внутри строки канала.</summary>
    public static int CompareField(string left, string right)
    {
        int leftIndex = Array.IndexOf(FieldOrder, left);
        int rightIndex = Array.IndexOf(FieldOrder, right);

        if (leftIndex < 0)
        {
            leftIndex = FieldOrder.Length;
        }

        if (rightIndex < 0)
        {
            rightIndex = FieldOrder.Length;
        }

        return leftIndex != rightIndex
            ? leftIndex.CompareTo(rightIndex)
            : string.CompareOrdinal(left, right);
    }

    private static (string Prefix, int Number) Parse(string title)
    {
        int i = 0;
        while (i < title.Length && !char.IsAsciiDigit(title[i]))
        {
            i++;
        }

        return int.TryParse(
            title[i..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int number)
            ? (title[..i], number)
            : (title, 0);
    }
}
