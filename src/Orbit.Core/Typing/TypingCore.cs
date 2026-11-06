using Orbit.Core.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Orbit.Core.Typing;

/// <summary>A keyboard event as the hook saw it. A value, so the hook allocates nothing.</summary>
public readonly record struct KeyEvent(uint Vk, ushort Scan, bool Down, bool Extended, bool Shift, bool Ctrl, bool Alt, bool Win, bool CapsLock, long At);

/// <summary>The focused box as far as it can be read. Null from a read means it couldn't be: too slow, or it failed.</summary>
/// <param name="IsTerminal">A terminal inside an app, which the program's name doesn't show. What it asks for can't be seen, passwords included.</param>
public sealed record FieldSnapshot(bool IsPassword, bool TakesText, string? BeforeCaret, bool HasSelection, int[]? Element, bool IsTerminal = false);

/// <summary>Why keys are left alone at the moment.</summary>
public enum TypingPause
{
    None,
    Off,
    OwnWindow,
    ExcludedApp,
    Terminal,
    NotText,
    Password,
    ImeNotResponding,
}

/// <param name="Pause">Anything but None leaves keys alone here, whatever the box says.</param>
/// <param name="UsKeys">The layout places characters as a US keyboard does.</param>
/// <param name="CheckEveryEdit">
/// The app changes text on its own as it comes (Chromium's inline autocomplete), and tells accessibility
/// late, so a selection can be seen in time but the text just typed can't be compared.
/// </param>
/// <param name="AllowTerminals">The user turned typing on for terminals too.</param>
/// <param name="MayTakeText">
/// The focused window shows a caret, or is a kind that never does (Chromium), so a box that can't be read
/// may still be one. False: nothing to type into unless a read says so. A game's window is this.
/// </param>
public sealed record TypingPlace(TypingPause Pause, bool UsKeys, bool CheckEveryEdit, bool AllowTerminals = false, bool MayTakeText = true);

/// <summary>Everything about Windows the typing logic needs, so it can be tested without any of it.</summary>
public interface ITypingHost
{
    /// <summary>The window with the keyboard's focus. Cheap enough for every key.</summary>
    nint FocusWindow { get; }

    /// <summary>The program in front, as far as settings go. Doesn't read the box.</summary>
    TypingPlace Place();

    /// <param name="length">How much text before the caret to read.</param>
    FieldSnapshot? ReadField(int length);

    /// <summary>Holds the Microsoft IME in alphanumeric mode where it is the layout. False: it didn't answer, or didn't change.</summary>
    bool HoldIme();

    /// <summary>Gives the focused window back the mode it had.</summary>
    void ReleaseIme();

    /// <summary>All were accepted.</summary>
    bool Send(INPUT[] events);

    /// <summary>Shift is down in Windows' view.</summary>
    bool ShiftDown { get; }

    /// <summary>Ctrl, Alt or Win is down in Windows' view.</summary>
    bool ShortcutDown { get; }

    void Wait(int milliseconds);
}

/// <summary>
/// What a queued event comes to, on the typing thread. Works out the events and sends nothing: the
/// worker sends them once it knows the answer isn't stale.
/// </summary>
public sealed class TypingCore(ITypingEngine engine, ITypingHost host, TypingGate gate)
{
    /// <summary>A word left this long may no longer be what the box has.</summary>
    private const long IdleMs = 10_000;

    /// <summary>Some apps report a beat after the keys land.</summary>
    private const int RecheckMs = 40;

    private readonly TypingSession _session = new(engine);
    private TypingPlace _place = new(TypingPause.Off, false, false);
    private int[]? _element;
    private nint _focusWindow;
    private long _lastKey;
    // This focus has been read and isn't a password, so one slow read later doesn't have to stop typing.
    private bool _boxCleared;
    // The box couldn't be read at this word's start: the rest of it goes as typed too, or one word
    // would come out half through the engine and half not.
    private bool _wordAsTyped;

    public bool Enabled { get; set; }

    public TypingPause Pause { get; private set; } = TypingPause.Off;

    public event Action? PauseChanged;

    /// <summary>The word stays as shown, and is let go.</summary>
    public void Forget()
    {
        _session.Forget();
        gate.HasWord = false;
        _wordAsTyped = false;
    }

    /// <summary>The last look wanted the box and couldn't read it, so what it decided is a guess worth checking again.</summary>
    public bool Unread { get; private set; }

    /// <summary>Focus or the program in front moved, or the setting changed: are keys Orbit's here?</summary>
    /// <param name="sameBoxKeepsWord">Only focus moved. A focus event for the box already held isn't a move.</param>
    public void Look(bool sameBoxKeepsWord)
    {
        _focusWindow = host.FocusWindow;
        _place = Enabled ? host.Place() : new TypingPlace(TypingPause.Off, false, false);
        var pause = _place.Pause;
        FieldSnapshot? field = null;
        Unread = false;
        if (pause == TypingPause.None)
        {
            field = host.ReadField(1);
            Unread = field is null;
            if (LeftAloneFor(field) is { } reason) pause = reason;
            // An unreadable window with no caret: a read refused while the last one still runs (just after
            // Alt+Tab) must not leave a game's keys going through the queue until the next focus event.
            else if (field is { TakesText: false } || (field is null && !_place.MayTakeText)) pause = TypingPause.NotText;
            else if (!host.HoldIme()) pause = TypingPause.ImeNotResponding;
        }

        bool sameBox = sameBoxKeepsWord && field?.Element is { } now && _element is { } before && now.AsSpan().SequenceEqual(before);
        if (!sameBox || pause != TypingPause.None) Forget();
        _element = field?.Element;
        _boxCleared = field is not null && LeftAloneFor(field) is null;

        SetPause(pause);
    }

    /// <summary>The events this one comes to, in order.</summary>
    public KeyPlan Plan(KeyEvent key)
    {
        if (!key.Down) return gate.TakeUp(key.Vk) ? KeyPlan.Nothing : KeyPlan.Up(key);
        if (!gate.Active) return KeyPlan.Down(key);

        if (_session.HasWord && key.At - _lastKey > IdleMs) Forget();
        _lastKey = key.At;

        // Focus may have moved with its event still on the way. A held word doesn't carry over, so the
        // box gets read below before this key goes anywhere near the engine.
        nint focus = host.FocusWindow;
        if (focus != _focusWindow)
        {
            _focusWindow = focus;
            _boxCleared = false;
            Forget();
        }

        if (_wordAsTyped)
        {
            if (key.Vk == HotkeyMatcher.VkSpace || !TypingGate.Types(key.Vk)) _wordAsTyped = false;
            return KeyPlan.Down(key);
        }

        // A new word: read the box first, so a password never reaches the engine.
        if (!_session.HasWord && TypingGate.Types(key.Vk) && !key.Ctrl && !key.Alt && !key.Win)
        {
            var field = host.ReadField(1);
            // Never read here, and can't tell now: this word goes as typed, as it might be a password.
            if (field is null && !_boxCleared)
            {
                Log.Info("Typing left a word as typed: the box can't be read");
                _wordAsTyped = true;
                return KeyPlan.Down(key);
            }
            if (LeftAloneFor(field) is { } reason)
            {
                SetPause(reason);
                return KeyPlan.Down(key);
            }
            if (field is not null) _boxCleared = true;
            if (!host.HoldIme())
            {
                SetPause(TypingPause.ImeNotResponding);
                return KeyPlan.Down(key);
            }
        }

        string before = _session.Word;
        var output = _session.Press(new EngineKey((int)key.Vk, key.Scan, key.Extended, key.Shift, key.Ctrl, key.Alt, key.Win, key.CapsLock));
        var edits = output.Edits;

        if (edits.Count > 0 && (host.ShortcutDown || !Confirm(before, ref edits)))
        {
            // Not safe to edit: the key still has to get somewhere.
            Forget();
            return KeyPlan.Down(key);
        }
        var events = edits.Count > 0 ? KeyOutput.Build(edits, host.ShiftDown, key.CapsLock, _place.UsKeys) : [];
        return output.SendKey ? KeyPlan.Then(events, key) : new KeyPlan(events, false);
    }

    /// <summary>The worker's report on the plan's events.</summary>
    public void Sent(bool accepted)
    {
        if (!accepted)
        {
            Log.Warn("Typing events were not accepted (an elevated window, or the secure desktop)");
            Forget();
        }
        gate.HasWord = _session.HasWord;
    }

    /// <summary>
    /// Whether the box has what the shadow says, before anything is taken back. An edit of one
    /// Backspace goes unchecked where text doesn't change by itself: those run on nearly every key
    /// of Hangul, and can only cost one character. Where accessibility reports late only a selection
    /// is looked for: comparing there refuses every edit, as the last keys sent aren't reported yet.
    /// </summary>
    private bool Confirm(string before, ref IReadOnlyList<FieldEdit> edits)
    {
        int deletes = edits.Sum(e => e.Deletes);
        if (deletes == 0 || (deletes == 1 && !_place.CheckEveryEdit)) return true;

        var field = host.ReadField(before.Length + 2);
        if (_place.CheckEveryEdit)
        {
            TakeSelection(field, ref edits);
            return true;
        }
        if (field is { BeforeCaret: { } text } && !text.EndsWith(before, StringComparison.Ordinal))
        {
            host.Wait(RecheckMs);
            field = host.ReadField(before.Length + 2);
            if (field is { BeforeCaret: { } again } && !again.EndsWith(before, StringComparison.Ordinal))
            {
                Log.Info($"Typing left a word alone: the box doesn't end with it ({again.Length} read, {before.Length} expected)");
                return false;
            }
        }
        TakeSelection(field, ref edits);
        return true;
    }

    /// <summary>A selection (inline autocomplete) would take the first Backspace.</summary>
    private static void TakeSelection(FieldSnapshot? field, ref IReadOnlyList<FieldEdit> edits)
    {
        if (field is { HasSelection: true } && edits[0] is var first)
        {
            Log.Info("Typing took a selection first");
            edits = [first with { Deletes = first.Deletes + 1 }, .. edits.Skip(1)];
        }
    }

    /// <summary>Boxes whose keys must never reach the engine, whatever was seen of them before.</summary>
    private TypingPause? LeftAloneFor(FieldSnapshot? field) => field switch
    {
        { IsPassword: true } => TypingPause.Password,
        { IsTerminal: true } when !_place.AllowTerminals => TypingPause.Terminal,
        _ => null,
    };

    /// <summary>The one way in or out of taking keys, so the IME is given back on every path.</summary>
    private void SetPause(TypingPause pause)
    {
        if (pause != TypingPause.None && Pause == TypingPause.None) host.ReleaseIme();
        gate.Active = pause == TypingPause.None;
        if (Pause == pause) return;
        Pause = pause;
        PauseChanged?.Invoke();
    }
}

/// <summary>Events to send, and how the key's own part in them affects its up.</summary>
public readonly record struct KeyPlan(INPUT[] Events, bool ReachesApp)
{
    public static KeyPlan Nothing => new([], false);

    public static KeyPlan Down(KeyEvent key) => new([KeyOutput.Raw(key)], true);

    public static KeyPlan Up(KeyEvent key) => new([KeyOutput.Raw(key)], false);

    public static KeyPlan Then(INPUT[] events, KeyEvent key) => new([.. events, KeyOutput.Raw(key)], true);
}
