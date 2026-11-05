using Orbit.Core.Input;

namespace Orbit.Core.Typing;

public enum GateAction
{
    /// <summary>The event goes on to the app untouched.</summary>
    Pass,

    /// <summary>The event is kept from the app and joins the queue.</summary>
    Queue,

    /// <summary>The event is kept from the app and dropped: the up of a key the engine took.</summary>
    Drop,
}

/// <summary>
/// The hook's decision for a keyboard event, on the hook thread: no allocation and no engine. While
/// anything is on its way every event joins one queue, modifiers and ups too. So order can't change,
/// and Windows never sees Ctrl held while queued letters go out.
/// </summary>
public sealed class TypingGate
{
    private const uint VkHangul = 0x15;

    // Per key, since its last up. Once any down of a press has reached the app its up must too, or the
    // key stays down there, whatever later repeats do: so Reached is never overwritten by Taken.
    private const int Unseen = 0, Taken = 1, Reached = 2;
    private readonly int[] _keys = new int[256];
    private long _queued;
    private long _finished;

    /// <summary>Set by the typing thread: not paused, and in a text box.</summary>
    public volatile bool Active;

    /// <summary>Set by the typing thread: a word is held that a later key could still change.</summary>
    public volatile bool HasWord;

    /// <summary>A queued key is not yet fully sent.</summary>
    public bool Busy => Volatile.Read(ref _queued) != Volatile.Read(ref _finished);

    /// <param name="vk">The virtual key.</param>
    /// <param name="down">Going down, not up. Repeats are downs.</param>
    /// <param name="shortcutHeld">Ctrl, Alt or Win is down in Windows' view.</param>
    public GateAction Decide(uint vk, bool down, bool shortcutHeld)
    {
        vk &= 0xFF;
        var action = Choose(vk, down, shortcutHeld);
        if (action == GateAction.Queue)
        {
            // Compared, not written: the typing thread may be marking this key Reached right now.
            if (down) Interlocked.CompareExchange(ref _keys[vk], Taken, Unseen);
            Interlocked.Increment(ref _queued);
        }
        else
        {
            Volatile.Write(ref _keys[vk], action == GateAction.Pass && down ? Reached : Unseen);
        }
        return action;
    }

    private GateAction Choose(uint vk, bool down, bool shortcutHeld)
    {
        if (Busy) return GateAction.Queue;
        if (!down) return Volatile.Read(ref _keys[vk]) == Taken ? GateAction.Drop : GateAction.Pass;
        if (!Active) return GateAction.Pass;
        if (IsShortcutModifier(vk)) return HasWord ? GateAction.Queue : GateAction.Pass;
        if (IsShift(vk)) return GateAction.Pass;
        if (shortcutHeld) return GateAction.Pass;
        if (vk == VkHangul || Types(vk)) return GateAction.Queue;
        return HasWord ? GateAction.Queue : GateAction.Pass;
    }

    /// <summary>The typing thread finished this entry: its output, if any, has been sent.</summary>
    public void Finished() => Interlocked.Increment(ref _finished);

    /// <summary>The typing thread sent this key on, so its up belongs to the app.</summary>
    public void SentOn(uint vk) => Volatile.Write(ref _keys[vk & 0xFF], Reached);

    /// <summary>Whether the typing thread should drop a queued up: no down of this press reached the app.</summary>
    public bool TakeUp(uint vk) => Interlocked.Exchange(ref _keys[vk & 0xFF], Unseen) == Taken;

    private static bool IsShift(uint vk) => vk is HotkeyMatcher.VkShift or HotkeyMatcher.VkLShift or HotkeyMatcher.VkRShift;

    private static bool IsShortcutModifier(uint vk) => HotkeyMatcher.IsModifier(vk) && !IsShift(vk);

    /// <summary>Keys that put a character in: letters, digits, the number pad's, punctuation, Space.</summary>
    private static bool Types(uint vk) =>
        vk == HotkeyMatcher.VkSpace
        || vk is >= 0x30 and <= 0x39
        || vk is >= 0x41 and <= 0x5A
        || vk is >= 0x60 and <= 0x6F and not 0x6C
        || vk is >= 0xBA and <= 0xC0
        || vk is >= 0xDB and <= 0xDF
        || vk == 0xE2;
}
