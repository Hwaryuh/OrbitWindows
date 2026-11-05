using Orbit.Core.Input;

namespace Orbit.Core.Typing;

/// <summary>What one key comes to: edits for the text box, then whether the key itself goes on to the app.</summary>
public sealed record TypingOutput(IReadOnlyList<FieldEdit> Edits, bool SendKey);

/// <summary>
/// One key at a time, from the queue: asks the engine with what the shadow knows, and turns its answer
/// into keystrokes. Runs on the typing thread, and sends nothing itself.
/// </summary>
public sealed class TypingSession(ITypingEngine engine)
{
    private const int VkBack = 0x08, VkTab = 0x09, VkReturn = 0x0D, VkHangul = 0x15, VkEscape = 0x1B;

    private readonly FieldShadow _shadow = new();

    public bool HasWord => _shadow.HasWord;

    /// <summary>What the box should have just before the caret, of the word held.</summary>
    public string Word => _shadow.Word;

    public TypingOutput Press(EngineKey key)
    {
        uint vk = (uint)key.Vk;
        // Shift only changes what later letters are, and they carry that themselves.
        if (vk is HotkeyMatcher.VkShift or HotkeyMatcher.VkLShift or HotkeyMatcher.VkRShift) return new TypingOutput([], true);
        if (!TheEngineTakes(key))
        {
            Forget();
            return new TypingOutput([], true);
        }

        var reply = engine.Key(key, _shadow.Context());
        var edits = new List<FieldEdit>(reply.Edits.Count);
        foreach (var edit in reply.Edits)
        {
            if (_shadow.Apply(edit) is not { } applied)
            {
                Forget();
                break;
            }
            if (!applied.IsEmpty) edits.Add(applied);
        }

        // 한/영 is the engine's switch key. It never goes on: the Microsoft IME's mode is held by Orbit.
        if (key.Vk == VkHangul) return new TypingOutput(edits, false);

        bool sendKey = reply.Replay || !reply.Handled;
        // The key reaches the app, which may do anything with it: the box is no longer known.
        if (sendKey) Forget();
        return new TypingOutput(edits, sendKey);
    }

    /// <summary>The box changed in a way Orbit didn't see, or may have. The word stays as shown.</summary>
    public void Forget()
    {
        if (_shadow.HasWord) engine.LetGo();
        _shadow.Reset();
    }

    /// <summary>
    /// Keys the engine has a say in. Ctrl going down makes it settle a word. A shortcut comes after
    /// that, so has nothing left to settle; arrows and function keys only tell it things this has no
    /// use for (the readings strip, kana, its own pause key), so they just end the word.
    /// </summary>
    private static bool TheEngineTakes(EngineKey key)
    {
        uint vk = (uint)key.Vk;
        if (vk is HotkeyMatcher.VkControl or HotkeyMatcher.VkLControl or HotkeyMatcher.VkRControl) return true;
        if (key.Ctrl || key.Alt || key.Win) return false;
        return key.Vk is VkBack or VkTab or VkReturn or VkEscape or VkHangul || TypingGate.Types(vk);
    }
}
