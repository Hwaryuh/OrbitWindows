using System.Globalization;

namespace Orbit.Core.Launcher;

/// <summary>
/// Amounts and "10 km to mi" read out of a launcher query, for conversions and currency
/// (Orbit: SmartAnswers.swift, <c>amount</c>, <c>split</c>, <c>candidates</c>, <c>format</c>).
/// </summary>
public static class QueryWords
{
    private static readonly (string Suffix, double Factor)[] Magnitudes =
        [("억", 1e8), ("만", 1e4), ("천", 1e3), ("백", 1e2), ("k", 1e3), ("m", 1e6)];
    private static readonly string[] KoreanMagnitudes = ["억", "만", "천", "백"];
    private static readonly string[] Joiners = ["to", "in", "into", "->", "→", "=", "에서", "으로", "로"];
    private static readonly string[] Particles = ["으로", "에서", "로", "을", "를", "은", "는", "이", "가"];

    /// <summary>"1,200", "3.5", "5만", "2천", "1.2억", "10k".</summary>
    public static double? Amount(string s)
    {
        var t = s.Replace(",", "").Trim().ToLowerInvariant();
        double factor = 1;
        foreach (var (suffix, f) in Magnitudes)
        {
            if (t.EndsWith(suffix, StringComparison.Ordinal) && t.Length > suffix.Length)
            {
                t = t[..^suffix.Length];
                factor = f;
                break;
            }
        }
        return double.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v * factor : null;
    }

    /// <summary>"10 km to mi", "10km in miles", "5만원을 달러로": the amount, the unit it is in, and the one asked for.</summary>
    public static (double Value, string From, string? To)? Split(string query)
    {
        var text = query.Trim();
        int i = 0;
        while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] is ',' or '.')) i++;
        if (i == 0) return null;
        var number = text[..i];
        var rest = text[i..].Trim();
        // Korean magnitudes, but only when a unit follows ("5만원", not "5만").
        foreach (var m in KoreanMagnitudes)
        {
            if (rest.StartsWith(m, StringComparison.Ordinal) && rest.Length > m.Length)
            {
                number += m;
                rest = rest[m.Length..].Trim();
                break;
            }
        }
        if (Amount(number) is not { } value || rest.Length == 0) return null;

        var words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int joiner = Array.FindIndex(words, w => Joiners.Contains(w.ToLowerInvariant()));
        if (joiner >= 0)
        {
            var from = string.Join(' ', words[..joiner]);
            var to = string.Join(' ', words[(joiner + 1)..]);
            if (from.Length == 0) return null;
            return (value, from, to.Length == 0 ? null : to);
        }
        if (words.Length > 2) return null;
        return (value, words[0], words.Length > 1 ? words[1] : null);
    }

    /// <summary>The word itself, then the word without a trailing Korean particle ("원을" → "원", "달러로" → "달러").</summary>
    public static IEnumerable<string> Candidates(string word)
    {
        yield return word;
        foreach (var particle in Particles)
        {
            if (word.EndsWith(particle, StringComparison.Ordinal) && word.Length > particle.Length)
                yield return word[..^particle.Length];
        }
    }

    /// <summary>Grouped digits; 2 decimals from 100 up, else up to <paramref name="digits"/>.</summary>
    public static string Format(double value, int digits = 4)
    {
        int decimals = Math.Abs(value) >= 100 ? Math.Min(2, digits) : digits;
        return value.ToString(decimals == 0 ? "#,0" : "#,0." + new string('#', decimals), CultureInfo.CurrentCulture);
    }
}
