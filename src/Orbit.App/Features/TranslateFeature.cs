using System.Net.Http;
using Orbit.App.Features.Clipboard;
using Orbit.App.Features.Translate;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.Clipboard;
using Orbit.Core.Input;
using Orbit.Core.Settings;
using Orbit.Core.Translate;

namespace Orbit.App.Features;

/// <summary>
/// Translate (Orbit: Modules/Translate): Ctrl+C twice, when the second press changes the clipboard,
/// opens a translation card by the pointer; so do Ctrl+Alt+T and the action, with the clipboard
/// as it is. Private clipboards (password managers) are never sent. Never logs text.
/// </summary>
internal sealed class TranslateFeature : IFeature
{
    private static readonly HttpClient Http = CreateHttp();

    private AppServices? _services;
    private DoubleCopy? _detector;
    private ClipboardCopy? _lastCopy;
    private TranslateCard? _card;
    private int _hotkey;

    public Translator Translator { get; } = new(Http);

    public void Start(AppServices services)
    {
        _services = services;
        var prefs = services.Preferences;
        _detector = new DoubleCopy(() => prefs.Get<double>(Pref.TranslateInterval));
        services.Hooks.ReportCopies = prefs.Get<bool>(Pref.TranslateDoubleCopy);
        prefs.Changed += key =>
        {
            if (key == Pref.TranslateDoubleCopy) services.Hooks.ReportCopies = prefs.Get<bool>(Pref.TranslateDoubleCopy);
        };
        services.Clipboard.Copied += OnCopied;
        _hotkey = services.Shortcuts.Declare(new Hotkey(Modifiers.Ctrl | Modifiers.Alt, 0x54), OrbitAction.Translate);
        services.Actions.Register(OrbitAction.Translate, TranslateClipboard);
    }

    public void OnHook(HookEvent e)
    {
        switch (e.Notice)
        {
            case HookNotice.HotkeyDown when e.Hotkey == _hotkey:
                TranslateClipboard();
                break;
            case HookNotice.CopyPressed when _detector!.CopyPressed(e.At):
                Fire();
                break;
        }
    }

    public void Dispose()
    {
        _card?.Dismiss();
        _card?.Close();
    }

    private void OnCopied(ClipboardCopy copy)
    {
        // Orbit's own writes (a cleaned link, a copied translation) are never the user's copy.
        if (copy.Own) return;
        _lastCopy = copy;
        if (_services!.Preferences.Get<bool>(Pref.TranslateDoubleCopy) && _detector!.ClipboardChanged(copy.ObservedAt)) Fire();
    }

    /// <summary>Ctrl+C twice: the copy the second press made.</summary>
    private void Fire()
    {
        if (_lastCopy is not { } copy) return;
        // Something newer is on the clipboard (its notice not handled yet): never translate the older copy.
        if (Clipboards.Sequence() != copy.Sequence) return;
        if (copy.Private)
        {
            Toast.Show(Strings.TranslatePrivate);
            return;
        }
        if (string.IsNullOrWhiteSpace(copy.Text))
        {
            Toast.Show(Strings.TranslateCopyFirst);
            return;
        }
        Show(copy.Text);
    }

    /// <summary>The shortcut and the action: whatever text is on the clipboard now.</summary>
    private void TranslateClipboard()
    {
        string? text = null;
        try
        {
            var data = System.Windows.Clipboard.GetDataObject();
            var formats = data?.GetFormats(false) ?? [];
            int? flag = formats.Contains(ClipboardRules.HistoryFormat) && data!.GetData(ClipboardRules.HistoryFormat) is System.IO.MemoryStream { Length: >= 4 } stream
                ? BitConverter.ToInt32(stream.ToArray(), 0) : null;
            if (ClipboardRules.IsPrivate(formats, flag))
            {
                Toast.Show(Strings.TranslatePrivate);
                return;
            }
            if (data?.GetData(System.Windows.DataFormats.UnicodeText) is string found) text = found;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            Toast.Show(Strings.TranslateCopyFirst);
            return;
        }
        Show(text);
    }

    private void Show(string text)
    {
        _card ??= new TranslateCard(this, _services!);
        _card.Translate(text.Trim(), _services!.Foreground.LastExternal);
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Orbit");
        return http;
    }
}
