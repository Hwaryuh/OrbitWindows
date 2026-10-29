namespace Orbit.Core.Scratchpad;

/// <summary>Find in the pad: case-insensitive, from the selection, around the end.</summary>
public static class ScratchpadSearch
{
    /// <summary>Where the next (or previous) match starts; -1 when there is none.</summary>
    public static int Next(string text, string query, int selectionStart, int selectionLength, bool forward)
    {
        if (query.Length == 0 || text.Length < query.Length) return -1;
        if (forward)
        {
            int from = Math.Clamp(selectionStart + selectionLength, 0, text.Length);
            int at = text.IndexOf(query, from, StringComparison.OrdinalIgnoreCase);
            return at >= 0 ? at : text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        }
        int before = -1, last = -1;
        for (int at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase); at >= 0; at = at + 1 < text.Length ? text.IndexOf(query, at + 1, StringComparison.OrdinalIgnoreCase) : -1)
        {
            if (at < selectionStart) before = at;
            last = at;
        }
        return before >= 0 ? before : last;
    }
}
