using System.Globalization;
using System.Text.Json;
using Orbit.Core.Settings;

namespace Orbit.Core.Launcher;

/// <summary>How well a query matches a title (Orbit: LauncherModel.swift, <c>Fuzzy</c>).</summary>
public static class Fuzzy
{
    /// <summary>A plain substring hit in a short title; title matches score at least about this.</summary>
    public const int Substring = 600;
    /// <summary>A keyword-prefix hit.</summary>
    public const int Keyword = 400;

    /// <summary>null: no match. Exact &gt; prefix &gt; word prefix &gt; initials &gt; substring &gt; keyword &gt; scattered letters.</summary>
    public static int? Score(string query, string title, string keywords = "")
    {
        var q = query.ToLowerInvariant();
        var t = title.ToLowerInvariant();
        if (q.Length == 0) return null;
        if (t == q) return 1000;
        if (t.StartsWith(q, StringComparison.Ordinal)) return 900 - t.Length;
        var words = t.Split([' ', '-', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w.StartsWith(q, StringComparison.Ordinal))) return 750 - t.Length;
        if (string.Concat(words.Select(w => w[0])).StartsWith(q, StringComparison.Ordinal)) return 700;
        if (t.Contains(q, StringComparison.Ordinal)) return 600 - t.Length;
        if (keywords.Length > 0 && keywords.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(k => k.StartsWith(q, StringComparison.Ordinal)))
            return 400;
        int i = 0;
        foreach (char ch in q)
        {
            int found = t.IndexOf(ch, i);
            if (found < 0) return null;
            i = found + 1;
        }
        return 200 - t.Length;
    }

    /// <summary>
    /// The lowest score worth showing, from the best one: once something matches by name, keyword
    /// and scattered-letter hits are noise; they are the fallback when nothing else fits.
    /// </summary>
    public static int Floor(int best) => best >= Substring ? Substring - 100 : best >= Keyword ? Keyword : 0;
}

/// <summary>
/// Results used before rank higher (Orbit: <c>Frecency</c>): +15 per use, at most 150. Kept in
/// the preferences as <c>launcher.frecency</c>, an id → uses table.
/// </summary>
public sealed class Frecency(Preferences preferences)
{
    public const string Key = "launcher.frecency";

    public void Bump(string id)
    {
        var table = Table();
        table[id] = table.GetValueOrDefault(id) + 1;
        preferences.Set(Key, table);
    }

    public int Boost(string id) => (int)Math.Min(150, Table().GetValueOrDefault(id) * 15);

    public bool HasHistory => Table().Count > 0;

    /// <summary>Forgets every use: the order starts over.</summary>
    public void Clear() => preferences.Set(Key, new Dictionary<string, double>());

    private Dictionary<string, double> Table() => preferences.Get<Dictionary<string, double>>(Key) ?? [];
}

/// <summary>
/// A small recursive-descent evaluator: + - * / % ^, parentheses, × and ÷ (Orbit: <c>Calculator</c>).
/// Only something that looks like arithmetic is evaluated: at least three characters with an operator.
/// </summary>
public static class Calculator
{
    private const int MaxLength = 200;
    private const int MaxDepth = 50;

    public static double? Evaluate(string input)
    {
        var s = input.Replace(" ", "").Replace('×', '*').Replace('÷', '/');
        if (s.Length < 3 || s.Length > MaxLength || s.IndexOfAny(['+', '-', '*', '/', '%', '^']) < 0) return null;
        if (s.Any(c => !"0123456789.+-*/%^()".Contains(c))) return null;
        var parser = new Parser(s);
        var value = parser.Expression();
        return value is { } v && parser.AtEnd && double.IsFinite(v) ? v : null;
    }

    /// <summary>Grouped digits, up to ten decimals.</summary>
    public static string Format(double value) => value.ToString("#,0.##########", CultureInfo.CurrentCulture);

    private sealed class Parser(string s)
    {
        private int _i;
        private int _depth;

        public bool AtEnd => _i == s.Length;

        public double? Expression()
        {
            if (++_depth > MaxDepth) return null;
            try
            {
                if (Term() is not { } v) return null;
                while (_i < s.Length && s[_i] is '+' or '-')
                {
                    char op = s[_i++];
                    if (Term() is not { } r) return null;
                    v = op == '+' ? v + r : v - r;
                }
                return v;
            }
            finally { _depth--; }
        }

        private double? Term()
        {
            if (Power() is not { } v) return null;
            while (_i < s.Length && s[_i] is '*' or '/' or '%')
            {
                char op = s[_i++];
                if (Power() is not { } r) return null;
                v = op switch { '*' => v * r, '/' => v / r, _ => v % r };
            }
            return v;
        }

        /// <summary>Right-associative: 2^3^2 is 2^9.</summary>
        private double? Power()
        {
            if (Unary() is not { } b) return null;
            if (_i < s.Length && s[_i] == '^')
            {
                _i++;
                if (++_depth > MaxDepth) return null;
                try { return Power() is { } e ? Math.Pow(b, e) : null; }
                finally { _depth--; }
            }
            return b;
        }

        private double? Unary()
        {
            if (_i < s.Length && s[_i] == '-')
            {
                _i++;
                if (++_depth > MaxDepth) return null;
                try { return -Unary(); }
                finally { _depth--; }
            }
            return Primary();
        }

        private double? Primary()
        {
            if (_i < s.Length && s[_i] == '(')
            {
                _i++;
                var v = Expression();
                if (_i >= s.Length || s[_i] != ')') return null;
                _i++;
                return v;
            }
            int start = _i;
            while (_i < s.Length && (char.IsAsciiDigit(s[_i]) || s[_i] == '.')) _i++;
            return double.TryParse(s.AsSpan(start, _i - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n : null;
        }
    }
}
