using System.Text;

namespace Orbit.Core.Translate;

public sealed record TranslationLanguage(string Code, string Name);

/// <summary>The languages Translate offers and how text is told apart by script (Orbit: TranslationEngine.swift).</summary>
public static class Languages
{
    public static readonly IReadOnlyList<TranslationLanguage> All =
    [
        new("ko", "한국어"), new("en", "English"), new("ja", "日本語"), new("zh-CN", "中文 (简体)"), new("zh-TW", "中文 (繁體)"),
        new("es", "Español"), new("fr", "Français"), new("de", "Deutsch"), new("it", "Italiano"), new("pt", "Português"),
        new("ru", "Русский"), new("vi", "Tiếng Việt"), new("th", "ไทย"), new("id", "Bahasa Indonesia"),
    ];

    /// <summary>A code's name; region variants Google returns ("zh-tw", "pt-br") find their language.</summary>
    public static string Named(string code)
    {
        if (All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)) is { } exact) return exact.Name;
        var bare = Base(code);
        return All.FirstOrDefault(l => l.Code.StartsWith(bare, StringComparison.OrdinalIgnoreCase))?.Name ?? code.ToUpperInvariant();
    }

    /// <summary>Same language, whatever the region ("zh-CN" and "zh-tw" are both Chinese).</summary>
    public static bool Same(string a, string b) => string.Equals(Base(a), Base(b), StringComparison.OrdinalIgnoreCase);

    private static string Base(string code) => code.Split('-')[0].ToLowerInvariant();

    private const string Traditional = "國語學體廣門們這說話時來對開關東車馬鳥魚從會個後過還點電視號應當實發經";

    /// <summary>The language a text is most likely in, from its letters; null when unclear.</summary>
    public static string? Guess(string text)
    {
        int hangul = 0, kana = 0, han = 0, latin = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            int v = rune.Value;
            if (v is >= 0xAC00 and <= 0xD7A3 or >= 0x3131 and <= 0x318E) hangul++;
            else if (v is >= 0x3040 and <= 0x30FF) kana++;
            else if (v is >= 0x4E00 and <= 0x9FFF) han++;
            else if (v < 128 && Rune.IsLetter(rune)) latin++;
        }
        double total = Math.Max(1, hangul + kana + han + latin);
        if (hangul / total > 0.3) return "ko";
        if (kana > 0) return "ja";
        if (han / total > 0.3) return text.Any(c => Traditional.Contains(c)) ? "zh-TW" : "zh-CN";
        if (latin / total > 0.6) return "en";
        return null;
    }

    /// <summary>
    /// Korean, Japanese and Chinese told apart by script, more reliably than Google's auto-detect
    /// (which calls short Chinese text English); Latin-script text is left to the engine.
    /// </summary>
    public static string? Script(string text) => Guess(text) is { } g && g is "ko" or "ja" or "zh-CN" or "zh-TW" ? g : null;

    /// <summary>The primary language unless the text is already in it; then the secondary one.</summary>
    public static string Direction(string text, string primary, string secondary) =>
        Guess(text) is { } guess && (guess == primary || primary.StartsWith(guess, StringComparison.OrdinalIgnoreCase)) ? secondary : primary;
}
