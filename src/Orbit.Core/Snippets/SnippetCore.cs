using System.Globalization;
using System.Text;

namespace Orbit.Core.Snippets;

/// <summary>A keyword and what it turns into (Orbit: SnippetCore.swift <c>Snippet</c>).</summary>
public sealed record Snippet
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Keyword { get; init; } = "";
    public string Content { get; init; } = "";
    public string Folder { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public DateTime Created { get; init; } = DateTime.Now;
    public int Used { get; init; }
}

public enum SnippetTrigger { Immediate, Delimiter }

/// <summary>A keyword the typing ended with, and the Space or Tab that set it off (already typed after it).</summary>
public readonly record struct SnippetMatch(string Keyword, char? Delimiter);

/// <summary>The last few characters typed, as the keyboard produced them (Orbit: <c>SnippetBuffer</c>).</summary>
public sealed class SnippetBuffer
{
    public const int Capacity = 48;

    private readonly StringBuilder _text = new();

    public string Text => _text.ToString();

    public void Type(string typed)
    {
        _text.Append(typed);
        if (_text.Length > Capacity) _text.Remove(0, _text.Length - Capacity);
    }

    public void Backspace()
    {
        if (_text.Length == 0) return;
        // A surrogate pair is one character.
        int remove = _text.Length >= 2 && char.IsLowSurrogate(_text[^1]) && char.IsHighSurrogate(_text[^2]) ? 2 : 1;
        _text.Remove(_text.Length - remove, remove);
    }

    public void Reset() => _text.Clear();

    public static bool IsDelimiter(char c) => c is ' ' or '\t';

    /// <summary>
    /// The longest keyword the buffer now ends with. With <see cref="SnippetTrigger.Delimiter"/>
    /// only once a Space or Tab follows it; <paramref name="waitForDelimiter"/> holds back keywords
    /// that would expand mid-composition, so they fire at the next Space or Tab instead.
    /// </summary>
    public SnippetMatch? Match(IEnumerable<string> keywords, SnippetTrigger trigger, Func<string, bool>? waitForDelimiter = null)
    {
        var text = Text;
        if (text.Length == 0) return null;
        waitForDelimiter ??= _ => false;
        var sorted = keywords.Where(k => k.Length > 0).OrderByDescending(k => k.Length).ToList();
        if (IsDelimiter(text[^1]))
        {
            var before = text[..^1];
            if (sorted.FirstOrDefault(k => before.EndsWith(k, StringComparison.Ordinal) && (trigger == SnippetTrigger.Delimiter || waitForDelimiter(k))) is { } k)
                return new SnippetMatch(k, text[^1]);
        }
        if (trigger != SnippetTrigger.Immediate) return null;
        return sorted.FirstOrDefault(k => text.EndsWith(k, StringComparison.Ordinal) && !waitForDelimiter(k)) is { } hit ? new SnippetMatch(hit, null) : null;
    }
}

/// <summary>
/// Fills in a snippet's placeholders (Orbit: <c>SnippetTemplate</c>): {clipboard}, {cursor},
/// {uuid}, {date}, {time}, {datetime} (with an optional pattern), {input:Name} and
/// {snippet:name} (five deep). Unknown braces stay as typed.
/// </summary>
public static class SnippetTemplate
{
    public sealed class Context
    {
        public string Clipboard { get; init; } = "";
        public DateTime Now { get; init; } = DateTime.Now;
        public IReadOnlyDictionary<string, string> Inputs { get; init; } = new Dictionary<string, string>();
        /// <summary>Another snippet's content by its name or keyword.</summary>
        public Func<string, string?> Lookup { get; init; } = _ => null;
        public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;
    }

    /// <param name="CursorFromEnd">Text elements between {cursor} and the end: how far to step back after inserting.</param>
    public readonly record struct Result(string Text, int CursorFromEnd);

    private const string Marker = "";
    private const int MaxDepth = 5;

    /// <summary>The {input:…} fields in order of appearance, each once, nested snippets included.</summary>
    public static IReadOnlyList<string> Inputs(string content, Func<string, string?>? lookup = null, int depth = 0)
    {
        lookup ??= _ => null;
        var names = new List<string>();
        foreach (var token in Tokens(content))
        {
            if (token.StartsWith("input:", StringComparison.OrdinalIgnoreCase))
            {
                var name = token[6..].Trim();
                if (name.Length > 0 && !names.Contains(name)) names.Add(name);
            }
            else if (token.StartsWith("snippet:", StringComparison.OrdinalIgnoreCase) && depth < MaxDepth && lookup(token[8..]) is { } inner)
            {
                foreach (var n in Inputs(inner, lookup, depth + 1)) if (!names.Contains(n)) names.Add(n);
            }
        }
        return names;
    }

    public static Result Render(string content, Context context)
    {
        var text = Expand(content, context, 0);
        int at = text.IndexOf(Marker, StringComparison.Ordinal);
        if (at < 0) return new Result(text, 0);
        var tail = text[(at + Marker.Length)..].Replace(Marker, "");
        return new Result(text[..at] + tail, new StringInfo(tail).LengthInTextElements);
    }

    private static string Expand(string content, Context context, int depth)
    {
        var output = new StringBuilder();
        int i = 0;
        while (i < content.Length)
        {
            int close = content[i] == '{' ? content.IndexOf('}', i) : -1;
            if (close > i && Value(content[(i + 1)..close], context, depth) is { } value)
            {
                output.Append(value);
                i = close + 1;
            }
            else
            {
                output.Append(content[i]);
                i++;
            }
        }
        return output.ToString();
    }

    private static string? Value(string token, Context context, int depth)
    {
        var t = token.Trim();
        int colon = t.IndexOf(':');
        var head = (colon < 0 ? t : t[..colon]).ToLowerInvariant();
        string? arg = colon < 0 ? null : t[(colon + 1)..];
        switch (head)
        {
            case "clipboard":
                return context.Clipboard;
            case "cursor":
                return Marker;
            case "uuid":
                return Guid.NewGuid().ToString().ToUpperInvariant();
            case "date" or "time" or "datetime":
                if (!string.IsNullOrEmpty(arg)) return context.Now.ToString(DotNetPattern(arg), context.Culture);
                return context.Now.ToString(head switch { "date" => "d", "time" => "t", _ => "g" }, context.Culture);
            case "input":
                return arg is null ? null : context.Inputs.GetValueOrDefault(arg.Trim(), "");
            case "snippet":
                if (depth >= MaxDepth) return "";
                return arg is not null && context.Lookup(arg) is { } inner ? Expand(inner, context, depth + 1) : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Orbit's patterns are Unicode (macOS DateFormatter); .NET agrees on y M d H h m s, and differs
    /// for weekdays (EEEE → dddd) and the AM/PM mark (a → tt).
    /// </summary>
    public static string DotNetPattern(string unicode)
    {
        var output = new StringBuilder();
        int i = 0;
        while (i < unicode.Length)
        {
            char c = unicode[i];
            int run = 1;
            while (i + run < unicode.Length && unicode[i + run] == c) run++;
            output.Append(c switch
            {
                'E' => run >= 4 ? "dddd" : "ddd",
                'a' => "tt",
                _ => new string(c, run),
            });
            i += run;
        }
        return output.ToString();
    }

    private static IEnumerable<string> Tokens(string content)
    {
        int i = 0;
        while ((i = content.IndexOf('{', i)) >= 0)
        {
            int close = content.IndexOf('}', i);
            if (close < 0) yield break;
            yield return content[(i + 1)..close].Trim();
            i = close + 1;
        }
    }
}

/// <summary>
/// What a run of keys becomes on the standard Korean 2-set keyboard (두벌식): typed in Hangul
/// mode, ";addr" lands in the field as ";ㅁㅇㅇㄱ", and that is what has to be deleted.
/// </summary>
public static class Dubeolsik
{
    private static readonly Dictionary<char, char> Consonants = new()
    {
        ['r'] = 'ㄱ', ['R'] = 'ㄲ', ['s'] = 'ㄴ', ['e'] = 'ㄷ', ['E'] = 'ㄸ', ['f'] = 'ㄹ', ['a'] = 'ㅁ', ['q'] = 'ㅂ', ['Q'] = 'ㅃ',
        ['t'] = 'ㅅ', ['T'] = 'ㅆ', ['d'] = 'ㅇ', ['w'] = 'ㅈ', ['W'] = 'ㅉ', ['c'] = 'ㅊ', ['z'] = 'ㅋ', ['x'] = 'ㅌ', ['v'] = 'ㅍ', ['g'] = 'ㅎ',
    };
    private static readonly Dictionary<char, char> Vowels = new()
    {
        ['k'] = 'ㅏ', ['o'] = 'ㅐ', ['O'] = 'ㅒ', ['i'] = 'ㅑ', ['j'] = 'ㅓ', ['p'] = 'ㅔ', ['P'] = 'ㅖ', ['u'] = 'ㅕ',
        ['h'] = 'ㅗ', ['y'] = 'ㅛ', ['n'] = 'ㅜ', ['b'] = 'ㅠ', ['m'] = 'ㅡ', ['l'] = 'ㅣ',
    };
    private const string Initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
    private const string Medials = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
    private const string Finals = "\0ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";
    private static readonly Dictionary<string, char> VowelPairs = new()
    {
        ["ㅗㅏ"] = 'ㅘ', ["ㅗㅐ"] = 'ㅙ', ["ㅗㅣ"] = 'ㅚ', ["ㅜㅓ"] = 'ㅝ', ["ㅜㅔ"] = 'ㅞ', ["ㅜㅣ"] = 'ㅟ', ["ㅡㅣ"] = 'ㅢ',
    };
    private static readonly Dictionary<string, char> FinalPairs = new()
    {
        ["ㄱㅅ"] = 'ㄳ', ["ㄴㅈ"] = 'ㄵ', ["ㄴㅎ"] = 'ㄶ', ["ㄹㄱ"] = 'ㄺ', ["ㄹㅁ"] = 'ㄻ', ["ㄹㅂ"] = 'ㄼ', ["ㄹㅅ"] = 'ㄽ',
        ["ㄹㅌ"] = 'ㄾ', ["ㄹㅍ"] = 'ㄿ', ["ㄹㅎ"] = 'ㅀ', ["ㅂㅅ"] = 'ㅄ',
    };

    public static bool HasLetters(string s) => s.Any(c => Jamo(c) is not null);

    /// <summary>Whether the run ends inside a syllable the input method is still composing.</summary>
    public static bool EndsComposing(string s) => s.Length > 0 && Jamo(s[^1]) is not null;

    private static (char Jamo, bool Consonant)? Jamo(char c)
    {
        if (Consonants.TryGetValue(c, out var j)) return (j, true);
        if (Vowels.TryGetValue(c, out j) || Vowels.TryGetValue(char.ToLowerInvariant(c), out j)) return (j, false);
        if (Consonants.TryGetValue(char.ToLowerInvariant(c), out j)) return (j, true);
        return null;
    }

    public static string Compose(string keys)
    {
        var output = new StringBuilder();
        char? cho = null, jung = null, jong = null;

        void Commit()
        {
            int ci = cho is { } c ? Initials.IndexOf(c) : -1, vi = jung is { } v ? Medials.IndexOf(v) : -1;
            if (ci >= 0 && vi >= 0)
            {
                int fi = jong is { } f ? Finals.IndexOf(f) : 0;
                output.Append((char)(0xAC00 + (ci * 21 + vi) * 28 + Math.Max(0, fi)));
            }
            else
            {
                if (cho is { } c2) output.Append(c2);
                if (jung is { } v2) output.Append(v2);
                if (jong is { } f2) output.Append(f2);
            }
            cho = jung = jong = null;
        }

        foreach (var key in keys)
        {
            if (Jamo(key) is not var (j, consonant))
            {
                Commit();
                output.Append(key);
                continue;
            }
            if (consonant)
            {
                if (cho is not null && jung is not null && jong is null && Finals.IndexOf(j) > 0) jong = j;
                else if (jong is { } f && FinalPairs.TryGetValue($"{f}{j}", out var pair)) jong = pair;
                else
                {
                    Commit();
                    cho = j;
                }
            }
            else
            {
                if (jong is { } f)
                {
                    // The final consonant moves on to start the next syllable.
                    var next = f;
                    var split = FinalPairs.FirstOrDefault(p => p.Value == f);
                    if (split.Key is { } both)
                    {
                        jong = both[0];
                        next = both[1];
                    }
                    else jong = null;
                    Commit();
                    cho = next;
                    jung = j;
                }
                else if (jung is { } v)
                {
                    if (VowelPairs.TryGetValue($"{v}{j}", out var pair)) jung = pair;
                    else
                    {
                        Commit();
                        jung = j;
                    }
                }
                else jung = j;
            }
        }
        Commit();
        return output.ToString();
    }
}
