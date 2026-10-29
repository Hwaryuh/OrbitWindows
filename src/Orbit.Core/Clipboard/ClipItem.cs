using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Orbit.Core.Clipboard;

public enum ClipKind { Text, Link, Image, File, Color }

/// <summary>
/// One entry of the clipboard history (Orbit: ClipItem.swift). Pictures and rich text live in
/// side files in the Clips folder; the history file keeps their names.
/// </summary>
public sealed record ClipItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required ClipKind Kind { get; init; }
    public string? Text { get; init; }
    public string? ImageFile { get; init; }
    public string? RtfFile { get; init; }
    public string? HtmlFile { get; init; }
    public IReadOnlyList<string>? FilePaths { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public string? OcrText { get; init; }
    /// <summary>The copying program's name, as File Explorer shows it.</summary>
    public string? AppName { get; init; }
    /// <summary>Its file name, lowercase ("chrome.exe").</summary>
    public string? AppProcess { get; init; }
    public DateTime Date { get; init; } = DateTime.Now;
    public bool Pinned { get; init; }
    public required string Signature { get; init; }

    public string Title => Kind switch
    {
        ClipKind.Image => $"{ImageWidth} × {ImageHeight}",
        ClipKind.File => string.Join(", ", (FilePaths ?? []).Select(Path.GetFileName)),
        _ => (Text ?? "").Trim(),
    };

    /// <summary>What a search looks through.</summary>
    public string Searchable => string.Join(" ", new[] { Text, OcrText, AppName, FilePaths is null ? null : string.Join(" ", FilePaths) }.Where(s => s is not null));

    /// <summary>Text set in a monospaced face: at least two code markers (Orbit: <c>looksLikeCode</c>).</summary>
    public bool LooksLikeCode => Kind == ClipKind.Text && Text is { } t && CodeMarkers.Count(t.Contains) >= 2;

    /// <summary>The side files this item owns.</summary>
    public IEnumerable<string> Files => new[] { ImageFile, RtfFile, HtmlFile }.OfType<string>();

    private static readonly string[] CodeMarkers = ["{", "}", ";", "=>", "func ", "const ", "import ", "</", "def ", "let "];

    private static readonly Regex HexColor = new("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.CultureInvariant);

    /// <summary>A copied string's kind: a #hex color, a single http(s) link, or text (Orbit: <c>classify</c>).</summary>
    public static ClipKind Classify(string text)
    {
        var t = text.Trim();
        if (HexColor.IsMatch(t)) return ClipKind.Color;
        if (!t.Contains(' ') && !t.Contains('\n') && Uri.TryCreate(t, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Host.Length > 0)
            return ClipKind.Link;
        return ClipKind.Text;
    }

    /// <summary>The ARGB of a color item (#RGB doubled), or null.</summary>
    public static uint? ColorValue(string? text)
    {
        if (text is null || !HexColor.IsMatch(text.Trim())) return null;
        var hex = text.Trim()[1..];
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}"));
        return 0xFF000000 | Convert.ToUInt32(hex, 16);
    }

    public static string SignatureOf(string text) => SignatureOf(Encoding.UTF8.GetBytes(text));

    /// <summary>The first 12 bytes of SHA-256, as Orbit's, in hex.</summary>
    public static string SignatureOf(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data)[..12]);
}
