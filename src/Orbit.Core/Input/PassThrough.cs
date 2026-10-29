namespace Orbit.Core.Input;

/// <summary>
/// A shortcut range that goes to one app instead of Orbit while that app is in front
/// (docs/plan/04-shortcuts.md, per-app pass-through): the modifiers exactly, and the key in
/// <see cref="FirstKey"/>..<see cref="LastKey"/>. <see cref="Process"/> is the file name, any case.
/// </summary>
public sealed record PassThroughRule(string Process, Modifiers Modifiers, uint FirstKey, uint LastKey)
{
    /// <summary>
    /// Word (Ctrl+Alt+1–3, headings) and OneNote (Ctrl+Alt+1–6, headings) keep Ctrl+Alt+digit:
    /// there the heading wins and capture is on the ring, the tray and the launcher.
    /// </summary>
    public static readonly IReadOnlyList<PassThroughRule> Defaults =
    [
        new("winword.exe", Modifiers.Ctrl | Modifiers.Alt, 0x30, 0x39),
        new("onenote.exe", Modifiers.Ctrl | Modifiers.Alt, 0x30, 0x39),
    ];

    public bool Matches(string process, Hotkey hotkey) =>
        string.Equals(process, Process, StringComparison.OrdinalIgnoreCase)
        && hotkey.Modifiers == Modifiers && hotkey.Key >= FirstKey && hotkey.Key <= LastKey;

    /// <summary>Whether any rule gives <paramref name="hotkey"/> to <paramref name="process"/>.</summary>
    public static bool Passes(IEnumerable<PassThroughRule> rules, string process, Hotkey hotkey) =>
        process.Length > 0 && rules.Any(r => r.Matches(process, hotkey));
}
