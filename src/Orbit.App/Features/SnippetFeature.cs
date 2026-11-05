using System.Globalization;
using System.IO;
using System.Windows;
using Orbit.App.Features.Snippets;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.Clipboard;
using Orbit.Core.Input;
using Orbit.Core.Settings;
using Orbit.Core.Snippets;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Orbit.App.Features;

/// <summary>
/// Snippets (Orbit: Modules/Snippets): a keyword typed anywhere turns into its text, and Ctrl+Alt+X
/// opens a picker. The hook hands over key codes; the text they make is worked out here with the
/// layout of the window in front. A keyword is replaced only after the field shows it before the
/// caret (UI Automation), or, where the field can't be read, on a non-Korean layout where the keys
/// are the letters. The text goes in by the clipboard, which is put back afterwards. Never logs
/// what was typed.
/// </summary>
internal sealed class SnippetFeature : IFeature
{
    private const ushort KoreanLanguage = 0x0412;
    private const uint VkLeft = 0x25;

    private readonly SnippetBuffer _buffer = new();
    private AppServices? _services;
    private SnippetStore? _store;
    private SnippetPanel? _panel;
    private int _generation;
    private bool _busy;
    private int _hotkey;

    public SnippetStore Store => _store!;

    public void Start(AppServices services)
    {
        _services = services;
        _store = new SnippetStore(Path.Combine(AppPaths.RoamingFolder(), "snippets.json"),
        [
            new Snippet { Name = Strings.SnippetStarterDate, Keyword = ";date", Content = "{date:yyyy-MM-dd}", Folder = Strings.SnippetExamples },
            new Snippet { Name = Strings.SnippetStarterTime, Keyword = ";time", Content = "{time}", Folder = Strings.SnippetExamples },
            new Snippet { Name = Strings.SnippetStarterEmail, Keyword = ";email", Content = "you@example.com", Folder = Strings.SnippetExamples },
            new Snippet { Name = Strings.SnippetStarterReply, Keyword = ";hi", Content = Strings.SnippetStarterReplyContent, Folder = Strings.SnippetExamples },
        ]);
        services.Snippets = this;
        _store.Changed += UpdateListening;
        services.Preferences.Changed += key =>
        {
            if (key == Pref.SnippetsEnabled) UpdateListening();
        };
        services.Foreground.Changed += _buffer.Reset;
        UpdateListening();
        _hotkey = services.Shortcuts.Declare(new Hotkey(Modifiers.Ctrl | Modifiers.Alt, 0x58), OrbitAction.Snippets);
        services.Actions.Register(OrbitAction.Snippets, TogglePanel);
    }

    public void Dispose() => _panel?.Close();

    /// <summary>The hook reports keys only while there is something to expand.</summary>
    private void UpdateListening()
    {
        bool on = _services!.Preferences.Get<bool>(Pref.SnippetsEnabled) && _store!.Active.Count > 0;
        _services.Hooks.ReportTyping = on;
        if (!on) _buffer.Reset();
    }

    private SnippetTrigger Trigger => _services!.Preferences.Get<string>(Pref.SnippetsTrigger) == "delimiter" ? SnippetTrigger.Delimiter : SnippetTrigger.Immediate;

    public void OnHook(HookEvent e)
    {
        switch (e.Notice)
        {
            case HookNotice.HotkeyDown when e.Hotkey == _hotkey:
                TogglePanel();
                break;
            case HookNotice.TypingReset:
                _generation++;
                _buffer.Reset();
                break;
            case HookNotice.KeyTyped:
                Typed((uint)e.X, (TypedState)e.Y, (uint)e.Hotkey);
                break;
        }
    }

    // MARK: Typing

    private void Typed(uint vk, TypedState state, uint scan)
    {
        _generation++;
        // A shortcut, not typing (AltGr is Ctrl+Alt and types).
        if ((state & TypedState.AltGr) == 0 && (state & (TypedState.Ctrl | TypedState.Alt | TypedState.Win)) != 0)
        {
            _buffer.Reset();
            return;
        }
        switch (vk)
        {
            case 0x08:
                _buffer.Backspace();
                return;
            case 0x0D or 0x1B or 0x2E or >= 0x21 and <= 0x28:
                // Enter, Esc, Delete, Page Up/Down, End, Home and the arrows move away.
                _buffer.Reset();
                return;
        }
        var typed = TextOf(vk, scan, state, out bool korean);
        if (typed is null) return;
        // With automatic typing the keys may have come out as Hangul whatever the layout says, so an
        // unreadable field isn't safe to count back in either.
        korean |= _services!.Typing?.IsTaking == true;
        if (typed.Any(c => char.IsControl(c) && c != '\t'))
        {
            _buffer.Reset();
            return;
        }
        _buffer.Type(typed);
        if (_busy) return;

        var store = _store!;
        var match = _buffer.Match(store.Active.Select(s => s.Keyword), Trigger,
            // Mid-syllable a Backspace takes back a jamo, not a letter: wait for the Space that finishes it.
            keyword => korean && Dubeolsik.EndsComposing(keyword));
        if (match is not { } m || store.Active.FirstOrDefault(s => s.Keyword == m.Keyword) is not { } snippet) return;
        if (!Allowed()) return;
        _buffer.Reset();
        _ = ExpandAsync(snippet, m, korean, _generation);
    }

    /// <summary>What the key types on the layout of the window in front; null for a dead key or nothing.</summary>
    private static unsafe string? TextOf(uint vk, uint scan, TypedState state, out bool korean)
    {
        var front = PInvoke.GetForegroundWindow();
        uint thread = PInvoke.GetWindowThreadProcessId(front, null);
        var layout = PInvoke.GetKeyboardLayout(thread);
        korean = ((nuint)layout.Value & 0xFFFF) == KoreanLanguage;
        Span<byte> keys = stackalloc byte[256];
        keys.Clear();
        if ((state & TypedState.Shift) != 0) keys[0x10] = keys[0xA0] = 0x80;
        if ((state & TypedState.CapsLock) != 0) keys[0x14] = 0x01;
        if ((state & TypedState.AltGr) != 0) keys[0x11] = keys[0xA2] = keys[0x12] = keys[0xA5] = 0x80;
        Span<char> buffer = stackalloc char[8];
        int n;
        fixed (byte* keyState = keys)
        fixed (char* text = buffer)
            // 0x4: leave the keyboard state (and a pending dead key) alone.
            n = ToUnicodeEx(vk, scan, keyState, text, buffer.Length, 0x4, (nint)layout.Value);
        return n > 0 ? new string(buffer[..n]) : null;
    }

    // Declared here: CsWin32 wraps the layout handle in a SafeHandle, which a window's layout is not.
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern unsafe int ToUnicodeEx(uint virtualKey, uint scanCode, byte* keyState, char* buffer, int bufferLength, uint flags, nint layout);

    private bool Allowed()
    {
        var process = _services!.Foreground.ForegroundProcess;
        // Orbit's own windows read as "".
        if (process.Length == 0) return false;
        var prefs = _services.Preferences;
        if (ClipboardRules.IsIgnored(process, prefs.Get<string[]>(Pref.SnippetsExcludedApps))) return false;
        return prefs.Get<bool>(Pref.SnippetsInTerminals) || !Processes.Terminals.Contains(process);
    }

    // MARK: Expanding

    private async Task ExpandAsync(Snippet snippet, SnippetMatch match, bool korean, int generation)
    {
        _busy = true;
        try
        {
            // The hook sees a key before the app does: let the app and its input method catch up.
            await Task.Delay(korean ? 80 : 45);
            if (generation != _generation) return;
            var window = PInvoke.GetForegroundWindow();

            // Questions first: nothing is checked or deleted until the answers are in.
            var inputs = new Dictionary<string, string>();
            var fields = SnippetTemplate.Inputs(snippet.Content, _store!.ContentNamed);
            if (fields.Count > 0)
            {
                if (await InputPrompt.AskAsync(PromptTitle(snippet), fields) is not { } values) return;
                inputs = values;
                FrontWindow.Take(window);
                await Task.Delay(80);
                generation = _generation;
            }

            string delimiter = match.Delimiter is { } d ? d.ToString() : "";
            bool inTerminals = _services!.Preferences.Get<bool>(Pref.SnippetsInTerminals);
            if (await DeleteCountAsync(snippet.Keyword + delimiter, Dubeolsik.Compose(snippet.Keyword) + delimiter, korean, inTerminals) is not var (count, element)) return;
            if (generation != _generation) return;
            var rendered = Render(snippet, inputs);
            // A delimiter after a {cursor} would land at the end, away from where the caret goes.
            var text = rendered.Text + (rendered.CursorFromEnd > 0 ? "" : delimiter);
            if (await ReplaceAsync(text, rendered.CursorFromEnd, window, count, element, generation)) _store.MarkUsed(snippet.Id);
        }
        finally
        {
            _busy = false;
        }
    }

    private static string PromptTitle(Snippet snippet) => snippet.Name.Length > 0 ? snippet.Name : snippet.Keyword;

    /// <summary>
    /// How many characters to take back: the keyword as the field shows it before the caret (typed
    /// letters, or their 2-set composition). Null leaves it alone: a password, a selection, a field
    /// ending otherwise, or an unreadable field on the Korean layout, or a terminal inside an app when
    /// terminals are off (the program's name, which <see cref="Allowed"/> goes by, doesn't show those).
    /// Each read is checked.
    /// </summary>
    private static async Task<(int Count, int[]? Element)?> DeleteCountAsync(string literal, string composed, bool korean, bool inTerminals)
    {
        int length = Math.Max(literal.Length, composed.Length) + 2;
        var field = await FieldReader.ReadAsync(length);
        if (field is { BeforeCaret: { } early } && !early.EndsWith(literal, StringComparison.Ordinal) && !early.EndsWith(composed, StringComparison.Ordinal))
        {
            // Some apps report a beat after the key lands.
            await Task.Delay(70);
            field = await FieldReader.ReadAsync(length);
        }
        if (field is { IsPassword: true } or { HasSelection: true }) return null;
        if (field is { IsTerminal: true } && !inTerminals) return null;
        if (field is { BeforeCaret: { } before })
        {
            if (before.EndsWith(literal, StringComparison.Ordinal)) return (Elements(literal), field.Value.Element);
            if (before.EndsWith(composed, StringComparison.Ordinal)) return (Elements(composed), field.Value.Element);
            Log.Info("Snippet left alone: the field doesn't end with the keyword");
            return null;
        }
        if (korean)
        {
            Log.Info("Snippet left alone: the field can't be read and the Korean keyboard may have composed the keyword");
            return null;
        }
        // The field can't be read; on this layout the keys are the letters.
        return (Elements(literal), field?.Element);
    }

    private static int Elements(string s) => new StringInfo(s).LengthInTextElements;

    private SnippetTemplate.Result Render(Snippet snippet, IReadOnlyDictionary<string, string> inputs)
    {
        string clipboardText = "";
        try
        {
            if (System.Windows.Clipboard.ContainsText()) clipboardText = System.Windows.Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
        return SnippetTemplate.Render(snippet.Content, new SnippetTemplate.Context { Clipboard = clipboardText, Inputs = inputs, Lookup = _store!.ContentNamed });
    }

    /// <summary>
    /// Deletes <paramref name="deleting"/> characters before the caret and pastes
    /// <paramref name="text"/> in one go, then steps the caret back. Right before the keys, after
    /// the last wait: the same window in front, the same field focused (when it was known), no
    /// typing since <paramref name="generation"/>. The user's clipboard comes back on every path.
    /// </summary>
    private async Task<bool> ReplaceAsync(string text, int cursorFromEnd, HWND window, int deleting, int[]? element, int? generation)
    {
        var saved = ClipboardSnapshot.Take();
        if (!saved.Readable) return false;
        var data = new DataObject();
        data.SetText(text);
        if (!Clipboards.Set(data, out uint ours, excludeFromHistory: true))
        {
            Toast.Show(Strings.CopyFailed);
            return false;
        }
        bool sent = false;
        try
        {
            if (element is not null && await FieldReader.FocusedAsync() is var focused && (focused is null || !focused.SequenceEqual(element)))
            {
                Log.Info("Snippet left alone: the focus moved or couldn't be checked");
                return false;
            }
            if (PInvoke.GetForegroundWindow() != window || (generation is { } g && g != _generation) || Clipboards.Sequence() != ours)
            {
                Log.Info("Snippet left alone: something changed before it could go in");
                return false;
            }
            sent = Paster.SendReplace(deleting);
            if (!sent)
            {
                Log.Warn("Snippet keys were not accepted (an elevated window, or the secure desktop)");
                Toast.Show(Strings.ClipBlocked);
                return false;
            }
            Log.Info("Snippet expanded");
            if (cursorFromEnd > 0)
            {
                int now = _generation;
                await Task.Delay(160);
                // Only into the same place, with nothing typed meanwhile.
                if (PInvoke.GetForegroundWindow() == window && now == _generation) Paster.SendPresses(VkLeft, cursorFromEnd);
            }
            return true;
        }
        finally
        {
            // Time for the app to read the clipboard before the user's own comes back.
            if (sent) await Task.Delay(650);
            ClipboardSnapshot.Restore(saved, ours);
        }
    }

    /// <summary>The picker: the snippet into the window that was in front, asking its inputs first. Deletes nothing.</summary>
    public async Task InsertAsync(Snippet snippet, HWND window)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var inputs = new Dictionary<string, string>();
            var fields = SnippetTemplate.Inputs(snippet.Content, _store!.ContentNamed);
            if (fields.Count > 0)
            {
                if (await InputPrompt.AskAsync(PromptTitle(snippet), fields) is not { } values) return;
                inputs = values;
            }
            FrontWindow.Take(window);
            await Task.Delay(80);
            var rendered = Render(snippet, inputs);
            if (await ReplaceAsync(rendered.Text, rendered.CursorFromEnd, window, 0, null, null)) _store.MarkUsed(snippet.Id);
        }
        finally
        {
            _busy = false;
        }
    }

    private void TogglePanel()
    {
        _panel ??= new SnippetPanel(this, _services!);
        if (_panel.IsVisible) _panel.Dismiss();
        else _panel.Present();
    }

    /// <summary>A snippet rendered and copied, no inputs asked (the picker's Ctrl+Enter).</summary>
    public void CopyRendered(Snippet snippet)
    {
        string clip = "";
        try
        {
            if (System.Windows.Clipboard.ContainsText()) clip = System.Windows.Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
        var text = SnippetTemplate.Render(snippet.Content, new SnippetTemplate.Context { Clipboard = clip, Lookup = _store!.ContentNamed }).Text;
        Clipboards.CopyText(text, Strings.Copied);
    }
}
