namespace Orbit.Core.Clipboard;

/// <summary>
/// What the clipboard history never keeps (docs/plan/03-milestones.md, Clipboard rules): content a
/// password manager marks as private, copies from ignored programs, and copies while paused.
/// </summary>
public static class ClipboardRules
{
    /// <summary>
    /// Windows' own markers for content that history must not keep (the counterparts of
    /// nspasteboard.org's Concealed and Transient types), and Orbit's own writes.
    /// </summary>
    public const string ExcludeFormat = "ExcludeClipboardContentFromMonitorProcessing";
    public const string ViewerIgnoreFormat = "Clipboard Viewer Ignore";
    public const string HistoryFormat = "CanIncludeInClipboardHistory";
    /// <summary>On every clipboard Orbit writes, so its own writes are told apart without racing on sequence numbers.</summary>
    public const string OwnFormat = "Orbit.OwnWrite";

    /// <summary>Largest RTF or HTML kept beside a text (Orbit skips RTF over 2 MB).</summary>
    public const int RichTextLimit = 2_000_000;
    /// <summary>Largest picture kept, as decoded 32-bit pixels.</summary>
    public const long ImagePixelBytesLimit = 50L * 1024 * 1024;

    /// <summary>
    /// Whether to leave a clipboard alone. <paramref name="historyFlag"/> is the DWORD in
    /// <see cref="HistoryFormat"/>, if that format is there.
    /// </summary>
    public static bool IsPrivate(IReadOnlyCollection<string> formats, int? historyFlag) =>
        formats.Contains(ExcludeFormat) || formats.Contains(ViewerIgnoreFormat) || historyFlag == 0;

    public static bool IsOwn(IReadOnlyCollection<string> formats) => formats.Contains(OwnFormat);

    public static bool IsIgnored(string? process, IEnumerable<string> ignored) =>
        !string.IsNullOrEmpty(process) && ignored.Any(i => string.Equals(i, process, StringComparison.OrdinalIgnoreCase));

    /// <summary>Recording is paused until this time (Unix seconds in clipboard.pausedUntil; 0 or past means not).</summary>
    public static bool IsPaused(double pausedUntil, DateTimeOffset now) => pausedUntil > now.ToUnixTimeSeconds();

    /// <summary>Whether a picture of this size may be kept, checked before it is decoded.</summary>
    public static bool ImageFits(long width, long height) =>
        width > 0 && height > 0 && width <= 100_000 && height <= 100_000 && width * height * 4 <= ImagePixelBytesLimit;
}
