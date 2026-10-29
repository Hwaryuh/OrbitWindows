using Orbit.Core.Actions;
using Orbit.Core.Capture;
using Orbit.Core.Input;
using Orbit.Core.Settings;

namespace Orbit.App.Features;

/// <summary>
/// One part of Orbit (the ring, the launcher, capture, ...). At start it registers its actions
/// and shortcuts with <see cref="AppServices"/>; afterwards it hears the hooks' events, on the UI
/// thread. Started before the hooks are, since shortcuts can't be added while they run.
/// </summary>
internal interface IFeature : IDisposable
{
    void Start(AppServices services);

    void OnHook(HookEvent e) { }

    void IDisposable.Dispose() { }
}

/// <summary>What the app gives every feature.</summary>
internal sealed class AppServices(Preferences preferences, ActionRunner actions, HookThread hooks,
    ForegroundTracker foreground, Action<string, string> notify)
{
    private int _nextHotkey = 1;

    public Preferences Preferences { get; } = preferences;
    public ActionRunner Actions { get; } = actions;
    public ForegroundTracker Foreground { get; } = foreground;
    /// <summary>The hooks: the ring trigger's settings, and click and Esc reports while an overlay is open.</summary>
    public HookThread Hooks { get; } = hooks;

    /// <summary>Windows.Graphics.Capture, once it is ready; null before that or where it isn't available.</summary>
    public WgcCapture? Capture { get; set; }

    /// <summary>The capture preview cards, and through them the capture library; null until capture starts.</summary>
    public Capture.QuickAccess? CaptureCards { get; set; }

    /// <summary>Every clipboard change, read off the UI thread and raised on it.</summary>
    public Clipboard.ClipboardWatcher Clipboard { get; } = new();

    /// <summary>The clipboard history; null until that feature starts.</summary>
    public ClipboardFeature? ClipboardHistory { get; set; }

    /// <summary>A tray notification.</summary>
    public void Notify(string title, string message) => notify(title, message);

    private readonly Dictionary<OrbitAction, Hotkey> _shortcuts = [];

    /// <summary>
    /// Takes a shortcut. The id comes back in <see cref="HookEvent.Hotkey"/>. With an action, the
    /// launcher shows the keys beside that command.
    /// </summary>
    public int AddHotkey(Hotkey hotkey, OrbitAction? action = null)
    {
        int id = _nextHotkey++;
        Hooks.Hotkeys.Register(id, hotkey);
        if (action is { } a) _shortcuts[a] = hotkey;
        return id;
    }

    /// <summary>The shortcut that runs an action, if one was taken for it.</summary>
    public Hotkey? ShortcutFor(OrbitAction action) => _shortcuts.TryGetValue(action, out var hotkey) ? hotkey : null;
}
