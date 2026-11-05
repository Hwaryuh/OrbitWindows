namespace Orbit.Core.Typing;

/// <summary>What one key comes to: edits for the text box, then whether the key itself goes on to the app.</summary>
public sealed record TypingOutput(IReadOnlyList<FieldEdit> Edits, bool SendKey);

/// <summary>
/// One key at a time, from the queue: asks the engine with what the shadow knows, and turns its answer
/// into keystrokes. Runs on the typing thread, and sends nothing itself.
/// </summary>
public sealed class TypingSession(ITypingEngine engine)
{
    private const int VkHangul = 0x15;

    private readonly FieldShadow _shadow = new();

    public bool HasWord => _shadow.HasWord;

    public TypingOutput Press(EngineKey key)
    {
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
}
