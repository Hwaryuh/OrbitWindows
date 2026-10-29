using Orbit.App.Features.Clipboard;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.Clipboard;
using Orbit.Core.Settings;

namespace Orbit.App.Features;

/// <summary>
/// Clean URL (docs/plan/03-milestones.md; behavior after Vorssaint, GPL, no code taken): takes
/// tracking parameters out of a link on the clipboard, by the action or, when cleanUrl.auto is on,
/// as soon as a link is copied. The cleaned link is written as Orbit's own text (rich formats are
/// dropped rather than rewritten), and only while the clipboard still holds the copy that was read.
/// </summary>
internal sealed class CleanUrlFeature : IFeature
{
    private AppServices? _services;

    public void Start(AppServices services)
    {
        _services = services;
        services.CleanUrl = this;
        services.Actions.Register(OrbitAction.CleanUrl, CleanClipboard);
    }

    private UrlCleaner Cleaner()
    {
        var prefs = _services!.Preferences;
        return new UrlCleaner(prefs.Get<string[]>(Pref.CleanUrlAdded), prefs.Get<string[]>(Pref.CleanUrlDisabled));
    }

    /// <summary>The action: the link on the clipboard now, cleaned.</summary>
    private void CleanClipboard()
    {
        string? text = null;
        uint sequence = Clipboards.Sequence();
        try
        {
            if (System.Windows.Clipboard.ContainsText()) text = System.Windows.Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            Toast.Show(Strings.CleanUrlNoLink);
            return;
        }
        if (text is null || ClipItem.Classify(text) != ClipKind.Link)
        {
            Toast.Show(Strings.CleanUrlNoLink);
            return;
        }
        if (Cleaner().Clean(text) is not { } cleaned)
        {
            Toast.Show(Strings.CleanUrlNothing);
            return;
        }
        if (Write(cleaned, sequence)) Toast.Show(Strings.CleanUrlRemoved(cleaned.Removed.Count), Shorten(cleaned.Url));
        else Toast.Show(Strings.CopyFailed);
    }

    /// <summary>
    /// A copy of a tracking link while automatic cleaning is on: the clean link goes onto the
    /// clipboard and comes back as the copy to record. Anything else comes back as it was.
    /// </summary>
    public ClipboardCopy AutoClean(ClipboardCopy copy)
    {
        if (!_services!.Preferences.Get<bool>(Pref.CleanUrlAuto) || copy.Own || copy.Private || copy.Text is not { } text) return copy;
        if (Cleaner().Clean(text) is not { } cleaned) return copy;
        // Nothing is written when another copy came meanwhile, or the clipboard stayed locked: the original is recorded.
        if (!Write(cleaned, copy.Sequence)) return copy;
        _services.Preferences.Set(Pref.CleanUrlLastRemoved, cleaned.Removed.ToArray());
        Log.Info($"Clean URL removed {cleaned.Removed.Count} parameters");
        Toast.Show(Strings.CleanUrlRemoved(cleaned.Removed.Count), Shorten(cleaned.Url));
        return copy with { Text = cleaned.Url, Rtf = null, Html = null };
    }

    private static bool Write(CleanedUrl cleaned, uint expected) => Clipboards.SetTextIfUnchanged(cleaned.Url, expected);

    private static string Shorten(string url) => url.Length > 80 ? url[..80] + "…" : url;
}
