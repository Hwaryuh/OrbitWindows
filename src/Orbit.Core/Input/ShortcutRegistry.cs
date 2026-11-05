using Orbit.Core.Actions;
using Orbit.Core.Settings;

namespace Orbit.Core.Input;

/// <summary>
/// Which keys each shortcut has. Features declare their defaults as they start and keep handling
/// the ids they get back; every other available action gets a place when the shortcuts are applied,
/// so the user can give any action keys. The user's changes are stored as a difference from the
/// defaults (<see cref="ShortcutMap"/>) and take effect at once. Used on the UI thread.
/// </summary>
public sealed class ShortcutRegistry(Preferences preferences, ActionRunner actions, HotkeyMatcher matcher)
{
    /// <summary>The place of the ring's hold shortcut, which is not an action.</summary>
    public const string Ring = "ring";

    private sealed record Slot(string Key, Hotkey? Default, int Id, OrbitAction? RunsAction);

    private readonly List<Slot> _slots = [];
    private Dictionary<string, Hotkey> _current = [];
    private int _nextId = 1;

    /// <summary>The shortcuts the app in front keeps; set by the app before <see cref="Apply"/>.</summary>
    public Func<Hotkey, bool>? PassesToFront { get; set; }

    /// <summary>
    /// Declares a feature's default shortcut. The id comes back in <see cref="HookEvent.Hotkey"/>
    /// whichever keys the user gives it. With an action, the launcher shows the keys beside that command.
    /// </summary>
    public int Declare(Hotkey hotkey, OrbitAction? action = null, string? key = null)
    {
        int id = _nextId++;
        _slots.Add(new Slot(key ?? action?.Id() ?? throw new ArgumentException("A shortcut needs an action or a key"), hotkey, id, null));
        return id;
    }

    /// <summary>
    /// Puts the current shortcuts in place. Every action nobody declared one for gets a place too;
    /// <see cref="ActionFor"/> says what those run.
    /// </summary>
    public void Apply()
    {
        foreach (var action in OrbitActions.All)
            if (actions.IsAvailable(action) && _slots.All(s => s.Key != action.Id()))
                _slots.Add(new Slot(action.Id(), null, _nextId++, action));
        var overrides = Overrides();
        _current = ShortcutMap.Resolve(_slots.Select(s => new ShortcutSlot(s.Key, s.Default)), overrides);
        matcher.Replace(_slots.Where(s => _current.ContainsKey(s.Key)).Select(s => (s.Id, _current[s.Key])).ToList(), PassesToFront);
    }

    private Dictionary<string, Hotkey?> Overrides() => ShortcutMap.Parse(preferences.Get<string>(Pref.Shortcuts));

    /// <summary>The shortcut that runs an action now, if it has one.</summary>
    public Hotkey? For(OrbitAction action) => For(action.Id());

    public Hotkey? For(string key) => _current.TryGetValue(key, out var hotkey) ? hotkey : null;

    /// <summary>Which action a shortcut with no feature of its own runs.</summary>
    public OrbitAction? ActionFor(int id) => _slots.FirstOrDefault(s => s.Id == id)?.RunsAction;

    /// <summary>The place holding these keys now, other than <paramref name="except"/>.</summary>
    public string? Holder(Hotkey hotkey, string except) =>
        _current.FirstOrDefault(s => s.Value == hotkey && s.Key != except).Key;

    /// <summary>Every shortcut's place, in the order they were declared, then each other action.</summary>
    public IReadOnlyList<string> Keys() => _slots.Select(s => s.Key).ToList();

    public bool IsChanged(string key) => Overrides().ContainsKey(key);

    /// <summary>Gives a place these keys, or none (null). Choosing its default stores no difference.</summary>
    public void Set(string key, Hotkey? hotkey)
    {
        var overrides = Overrides();
        if (hotkey is not null && _slots.FirstOrDefault(s => s.Key == key)?.Default == hotkey) overrides.Remove(key);
        else overrides[key] = hotkey;
        Save(overrides);
    }

    /// <summary>Back to the default keys, or none.</summary>
    public void Reset(string key)
    {
        var overrides = Overrides();
        overrides.Remove(key);
        Save(overrides);
    }

    private void Save(Dictionary<string, Hotkey?> overrides)
    {
        preferences.Set(Pref.Shortcuts, ShortcutMap.Serialize(overrides));
        Apply();
    }
}
