using System.Globalization;
using System.Numerics;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.Core.Tools;

/// <summary>Conversor de bases (binário, octal, decimal, hexadecimal) — inteiros de até 64 bits com sinal.</summary>
public static class NumberBases
{
    public static readonly (int Base, string Name)[] All = [(10, "DEC"), (16, "HEX"), (2, "BIN"), (8, "OCT")];

    public static bool TryParse(string? text, int numberBase, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim().Replace(" ", "").Replace("_", "");
        bool neg = t.StartsWith('-');
        if (neg) t = t[1..];
        if (numberBase == 16 && t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        if (numberBase == 2 && t.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        if (t.Length == 0 || t.Length > 64) return false;
        BigInteger acc = 0;
        foreach (char c in t)
        {
            int d = char.IsDigit(c) ? c - '0' : char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) - 'A' + 10 : -1;
            if (d < 0 || d >= numberBase) return false;
            acc = acc * numberBase + d;
            if (acc > long.MaxValue) return false;
        }
        value = (long)(neg ? -acc : acc);
        return true;
    }

    /// <summary>Formata; negativos ficam com sinal (não complemento de dois). Binário em grupos de 4.</summary>
    public static string Format(long value, int numberBase)
    {
        if (numberBase == 10) return value.ToString("#,0", L.Culture);
        bool neg = value < 0;
        ulong v = neg ? (ulong)(-(value + 1)) + 1 : (ulong)value;
        string digits = v == 0 ? "0" : "";
        while (v > 0) { int d = (int)(v % (ulong)numberBase); digits = (char)(d < 10 ? '0' + d : 'A' + d - 10) + digits; v /= (ulong)numberBase; }
        if (numberBase == 2 && digits.Length > 4)
        {
            digits = digits.PadLeft((digits.Length + 3) / 4 * 4, '0');
            digits = string.Join(" ", Enumerable.Range(0, digits.Length / 4).Select(i => digits.Substring(i * 4, 4)));
        }
        return (neg ? "-" : "") + digits;
    }
}

/// <summary>Calculadora de datas: diferença entre duas datas e somar/subtrair períodos.</summary>
public static class DateCalc
{
    public readonly record struct Span(int Years, int Months, int Days, int TotalDays, int Weekdays);

    public static Span Difference(DateOnly a, DateOnly b)
    {
        if (b < a) (a, b) = (b, a);
        int monthsTotal = (b.Year - a.Year) * 12 + b.Month - a.Month;
        if (a.AddMonths(monthsTotal) > b) monthsTotal--;
        int years = monthsTotal / 12, months = monthsTotal % 12;
        int days = b.DayNumber - a.AddMonths(monthsTotal).DayNumber;
        int total = b.DayNumber - a.DayNumber;
        int fullWeeks = total / 7;
        int weekdays = fullWeeks * 5;
        for (var d = a.AddDays(fullWeeks * 7); d < b; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) weekdays++;
        return new Span(years, months, days, total, weekdays);
    }

    public enum Unit { Days, Weeks, Months, Years }

    public static DateOnly Add(DateOnly d, int amount, Unit unit) => unit switch
    {
        Unit.Days => d.AddDays(amount),
        Unit.Weeks => d.AddDays(amount * 7),
        Unit.Months => d.AddMonths(amount),
        _ => d.AddYears(amount),
    };

    public static string Describe(Span s)
    {
        var parts = new List<string>();
        if (s.Years > 0) parts.Add(s.Years == 1 ? L.T("1 year") : L.F("{0} years", s.Years));
        if (s.Months > 0) parts.Add(s.Months == 1 ? L.T("1 month") : L.F("{0} months", s.Months));
        if (s.Days > 0 || parts.Count == 0) parts.Add(s.Days == 1 ? L.T("1 day") : L.F("{0} days", s.Days));
        return string.Join(", ", parts);
    }
}
