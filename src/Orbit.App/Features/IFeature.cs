using Orbit.Core.Actions;
using Orbit.Core.Capture;
using Orbit.Core.Input;
using Orbit.Core.Settings;

namespace Orbit.App.Features;

/// <summary>
/// One part of Orbit (the ring, the launcher, capture, ...). At start it registers its actions
/// and declares its default shortcuts with <see cref="AppServices"/>; afterwards it hears the hooks'
/// events, on the UI thread. The app puts the shortcuts in place once every feature has started
/// (<see cref="ShortcutRegistry.Apply"/>); the user can change them at any time after that.
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
    public Preferences Preferences { get; } = preferences;
    public ActionRunner Actions { get; } = actions;
    public ForegroundTracker Foreground { get; } = foreground;
    /// <summary>The hooks: the ring trigger's settings, and click and Esc reports while an overlay is open.</summary>
    public HookThread Hooks { get; } = hooks;

    /// <summary>Which keys each shortcut has, and the place to declare a feature's default.</summary>
    public ShortcutRegistry Shortcuts { get; } = new(preferences, actions, hooks.Hotkeys);

    /// <summary>Windows.Graphics.Capture, once it is ready; null before that or where it isn't available.</summary>
    public WgcCapture? Capture { get; set; }

    /// <summary>The capture preview cards, and through them the capture library; null until capture starts.</summary>
    public Capture.QuickAccess? CaptureCards { get; set; }

    /// <summary>Every clipboard change, read off the UI thread and raised on it.</summary>
    public Clipboard.ClipboardWatcher Clipboard { get; } = new();

    /// <summary>Clean URL; null until that feature starts. The history asks it first about every copy.</summary>
    public CleanUrlFeature? CleanUrl { get; set; }

    /// <summary>Keep Awake; null until that feature starts.</summary>
    public KeepAwakeFeature? KeepAwake { get; set; }

    /// <summary>Every screen's brightness; null until that feature starts.</summary>
    public Orbit.Core.Displays.DisplayManager? Displays { get; set; }

    /// <summary>Screen recording; null until that feature starts. V in the capture selection hands it the area.</summary>
    public RecordingFeature? Recording { get; set; }

    /// <summary>Snippets; null until that feature starts.</summary>
    public SnippetFeature? Snippets { get; set; }

    /// <summary>The clipboard history; null until that feature starts.</summary>
    public ClipboardFeature? ClipboardHistory { get; set; }

    /// <summary>A tray notification.</summary>
    public void Notify(string title, string message) => notify(title, message);

    /// <summary>Recording a key for a shortcut; null until that feature starts.</summary>
    public ShortcutsFeature? ShortcutRecorder { get; set; }
}
