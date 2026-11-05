using System.Text;

namespace Orbit.Core.Typing;

/// <summary>An edit of the text box as keystrokes: this many Backspaces, then this text.</summary>
public readonly record struct FieldEdit(int Deletes, string Text)
{
    public bool IsEmpty => Deletes == 0 && Text.Length == 0;
}

/// <summary>
/// What Orbit believes it has typed of the word being held, offered to the engine as if it were the
/// text box. Nothing else about the box is known, and nothing else is offered: an edit reaching past
/// the held word is refused, so a wrong belief can cost at most this word (<see cref="MaxDeletes"/>).
/// Positions are UTF-16 units and only keep the engine's arithmetic consistent; they aren't the box's.
/// </summary>
public sealed class FieldShadow
{
    /// <summary>The most one edit may take back, in code points.</summary>
    public const int MaxDeletes = 32;

    private string _word = "";
    private int _wordStart;
    private int _markedStart = -1;
    private int _markedLength;

    /// <summary>Whether a word is on show that the engine may still change.</summary>
    public bool HasWord => _markedLength > 0;

    private int Caret => _wordStart + _word.Length;

    public EngineContext Context() => new(
        [Caret, 0],
        _markedLength > 0 ? [_markedStart, _markedLength] : null,
        _word,
        _wordStart);

    /// <summary>What was typed stays as it is, and is no longer known: next comes a new word.</summary>
    public void Reset()
    {
        _wordStart = Caret;
        _word = "";
        _markedStart = -1;
        _markedLength = 0;
    }

    /// <summary>Applies the engine's edit. Null when it reaches past the held word: the caller resets.</summary>
    public FieldEdit? Apply(EngineEdit edit)
    {
        int start, length;
        if (edit.Replace is [var at, var count])
        {
            start = at;
            length = count;
        }
        else if (_markedLength > 0)
        {
            start = _markedStart;
            length = _markedLength;
        }
        else
        {
            start = Caret;
            length = 0;
        }
        if (start < _wordStart || length < 0 || start + length != Caret) return null;

        int offset = start - _wordStart;
        var result = Trim(_word[offset..], edit.Text);
        if (result.Deletes > MaxDeletes) return null;

        if (edit.Kind == EngineEdit.Mark && edit.Text.Length > 0)
        {
            _word = _word[..offset] + edit.Text;
            _markedStart = start;
            _markedLength = edit.Text.Length;
        }
        else
        {
            // Put in for good: the word starts again after it.
            _wordStart = start + edit.Text.Length;
            _word = "";
            _markedStart = -1;
            _markedLength = 0;
        }
        return result;
    }

    /// <summary>Only what differs: what both start with stays in the box untouched.</summary>
    internal static FieldEdit Trim(string before, string after)
    {
        int same = 0;
        int limit = Math.Min(before.Length, after.Length);
        while (same < limit && before[same] == after[same]) same++;
        // Not through a surrogate pair: the rest of it would be half a character.
        if (same > 0 && char.IsHighSurrogate(before[same - 1])) same--;
        return new FieldEdit(CodePoints(before.AsSpan(same)), after[same..]);
    }

    /// <summary>One Backspace takes one code point.</summary>
    private static int CodePoints(ReadOnlySpan<char> text)
    {
        int count = 0;
        foreach (Rune _ in text.EnumerateRunes()) count++;
        return count;
    }
}
