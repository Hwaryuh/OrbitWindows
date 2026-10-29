using System.IO;
using System.Windows;

namespace Orbit.App.Features.Snippets;

/// <summary>
/// The user's clipboard kept aside while a snippet borrows it, then put back (Orbit:
/// SnippetEngine's snapshot and restore). Best effort: the common formats are copied (text, rich
/// text, HTML, pictures, files); others are lost. It goes back only while the clipboard still holds
/// Orbit's write, so a copy made meanwhile is never overwritten, and marked so the history doesn't
/// record it again.
/// </summary>
internal static class ClipboardSnapshot
{
    private static readonly string[] Formats =
    [
        DataFormats.UnicodeText, DataFormats.Text, DataFormats.Rtf, DataFormats.Html, "PNG", DataFormats.Bitmap, DataFormats.FileDrop,
        Core.Clipboard.ClipboardRules.ExcludeFormat, Core.Clipboard.ClipboardRules.HistoryFormat,
    ];

    /// <summary>What the clipboard held: <see cref="Readable"/> is false when it couldn't be read (then nothing may be borrowed).</summary>
    public sealed record Saved(bool Readable, DataObject? Data, string? TextOnly);

    public static Saved Take()
    {
        try
        {
            var current = System.Windows.Clipboard.GetDataObject();
            if (current is null) return new Saved(true, null, null);
            var copy = new DataObject();
            var found = new List<string>();
            foreach (var format in Formats)
            {
                if (!current.GetDataPresent(format, false)) continue;
                var value = current.GetData(format, false);
                if (value is null) continue;
                // Streams are read now: the original owner may be gone by the time it goes back.
                copy.SetData(format, value is MemoryStream stream ? new MemoryStream(stream.ToArray()) : value);
                found.Add(format);
            }
            if (found.Count == 0) return new Saved(true, null, null);
            bool textOnly = found.All(f => f == DataFormats.UnicodeText || f == DataFormats.Text);
            return new Saved(true, copy, textOnly ? copy.GetData(DataFormats.UnicodeText) as string : null);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or OutOfMemoryException or InvalidOperationException)
        {
            Core.Log.Warn($"The clipboard could not be kept aside ({e.GetType().Name})");
            return new Saved(false, null, null);
        }
    }

    /// <summary>
    /// Puts <paramref name="saved"/> back if the clipboard is still change <paramref name="ours"/>.
    /// Plain text goes back under one open clipboard, checked and written together; other formats
    /// go through WPF, which opens the clipboard itself, so a copy made in that instant could be lost.
    /// </summary>
    public static void Restore(Saved saved, uint ours)
    {
        if (saved.Data is null) return;
        if (saved.TextOnly is { } text)
        {
            Clipboards.SetTextIfUnchanged(text, ours);
            return;
        }
        if (Clipboards.Sequence() != ours) return;
        Clipboards.Set(saved.Data, out _, excludeFromHistory: true);
    }
}
