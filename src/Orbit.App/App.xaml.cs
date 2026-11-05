using Orbit.App.Design;
using System.Windows;
using System.Windows.Threading;
using Orbit.App.Features;
using Orbit.App.Features.Capture;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.Capture;
using Orbit.Core.Display;
using Orbit.Core.Input;
using Orbit.Core.Settings;

namespace Orbit.App;

/// <summary>
/// Starts what every feature shares (preferences, the hooks, capture, the tray), then the features
/// themselves (<see cref="IFeature"/>), and hands them the hooks' events.
/// </summary>
public partial class App : Application
{
    private Mutex? _instance;
    private Preferences? _preferences;
    private ForegroundTracker? _foreground;
    private HookThread? _hooks;
    private Tray? _tray;
    private AppServices? _services;
    private readonly ActionRunner _actions = new();
    private readonly List<IFeature> _features = [];

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Palette.Current().ApplyTo(Resources);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnWindowsSettingChanged;
        // A self-test shows nothing and runs no hooks, so it runs beside the user's own Orbit, and
        // doesn't touch the name that says one is running.
        bool selfTest = e.Args.Any(a => a.StartsWith("--selftest-", StringComparison.Ordinal));
        if (!selfTest)
        {
            _instance = new Mutex(true, @"Local\Orbit.Windows.Instance", out bool first);
            if (!first)
            {
                MessageBox.Show(Strings.AlreadyRunning, Strings.AppName);
                Shutdown();
                return;
            }
        }

        DispatcherUnhandledException += OnUnhandledException;
        Log.Info($"Orbit {typeof(App).Assembly.GetName().Version} starting on Windows {Environment.OSVersion.Version}");
        LogMonitors();
        foreach (var action in ActionText.Missing()) Log.Error($"Action {action.Id()} has no title");

        // These show nothing and run before the hooks and the tray.
        if (e.Args.Contains("--selftest-scratchpad") || e.Args.Contains("--selftest-shortcuts") || e.Args.Contains("--selftest-video") || e.Args.Contains("--selftest-settings") || e.Args.Contains("--selftest-controls"))
        {
            int at = Array.IndexOf(e.Args, "--render");
            string? render = at >= 0 && at + 1 < e.Args.Length ? e.Args[at + 1] : null;
            bool passed = false;
            try
            {
                passed = e.Args.Contains("--selftest-scratchpad")
                    ? Features.Scratchpad.ScratchpadSelfTest.Run(Preferences.OpenDefault(), render)
                    : e.Args.Contains("--selftest-shortcuts") ? Settings.ShortcutsSelfTest.Run(render)
                    : e.Args.Contains("--selftest-settings") ? Settings.SettingsSelfTest.Run(render)
                    : e.Args.Contains("--selftest-controls") ? ControlsSelfTest.Run(render)
                    : VideoEditorSelfTest.Run(render);
            }
            catch (Exception error)
            {
                // A failure, not a process left running with no window to close.
                Log.Error("A self-test failed with an exception", error);
            }
            Shutdown(passed ? 0 : 1);
            return;
        }

        _preferences = Preferences.OpenDefault();
        // Refresh an installed copy's sign-in entry (an update moves the executable); never add one
        // here: the installer's first run does. Apply leaves the Run value alone for a dev build.
        if (_preferences.Get<bool>(Pref.LaunchAtSignIn) && StartupRegistration.IsEnabled()) StartupRegistration.Apply(true);
        _foreground = new ForegroundTracker();
        _actions.Unavailable += action => Toast.Show(Strings.ActionNotReady(action.Title()));
        _tray = CreateTray();

        _hooks = new HookThread();
        _services = new AppServices(_preferences, _actions, _hooks, _foreground, Notify);
        _services.Clipboard.Start();
        _features.AddRange([new SettingsFeature(), new RingFeature(), new LauncherFeature(), new CaptureFeature(), new RecordingFeature(), new CleanUrlFeature(), new ClipboardFeature(), new TranslateFeature(), new SnippetFeature(), new KeepAwakeFeature(), new SystemFeature(), new DisplaysFeature(), new ScratchpadFeature(), new UpdateFeature(), new TypingFeature(), new ShortcutsFeature()]);
        foreach (var feature in _features) feature.Start(_services);
        _services.Shortcuts.PassesToFront = PassesToFront;
        _services.Shortcuts.Apply();
        _hooks.Notice += e => Dispatcher.BeginInvoke(() => OnHook(e));
        // Shortcuts the app in front keeps, worked out here so the hook never asks which app it is.
        _foreground.Changed += UpdatePassThrough;
        UpdatePassThrough();
        _hooks.Start();

        _services.Capture = await StartCaptureAsync();

        if (e.Args.Contains("--selftest-capture"))
        {
            // Captures every monitor both ways, logs the comparison and quits, without an overlay.
            bool passed = await CaptureSelfTest.RunAsync(_services.Capture);
            Shutdown(passed ? 0 : 1);
        }
        else if (e.Args.Contains("--selftest-record"))
        {
            bool passed = await RecordSelfTest.RunAsync(_services.Capture, e.Args.Contains("--keep"));
            Shutdown(passed ? 0 : 1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hooks?.Dispose();
        foreach (var feature in _features) feature.Dispose();
        _services?.Clipboard.Dispose();
        _foreground?.Dispose();
        _preferences?.Dispose();
        _services?.Capture?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        Log.Info("Orbit stopped");
        base.OnExit(e);
    }

    private void UpdatePassThrough()
    {
        _hooks!.Hotkeys.UpdatePassThrough(PassesToFront);
    }

    private bool PassesToFront(Hotkey hotkey) => PassThroughRule.Passes(PassThroughRule.Defaults, _foreground!.ForegroundProcess, hotkey);

    /// <summary>The Windows mode changed, or something that may include it: fixed palettes keep theirs, in their own resources.</summary>
    private void OnWindowsSettingChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General)
            Dispatcher.BeginInvoke(() => Palette.Current().ApplyTo(Resources));
    }

    private void OnHook(HookEvent e)
    {
        foreach (var feature in _features) feature.OnHook(e);
    }

    private static async Task<WgcCapture?> StartCaptureAsync()
    {
        if (!WgcCapture.IsSupported) return null;
        try
        {
            var wgc = new WgcCapture();
            bool borderless = await wgc.RequestBorderlessAsync();
            Log.Info($"Windows.Graphics.Capture ready, borderless {borderless}");
            return wgc;
        }
        catch (Exception error)
        {
            Log.Error("Windows.Graphics.Capture unavailable; GDI only", error);
            return null;
        }
    }

    private Tray CreateTray() => new(
    [
        (OrbitAction.Scratchpad.Title(), () => _actions.Perform(OrbitAction.Scratchpad)),
        (Strings.MenuSettings, () => _actions.Perform(OrbitAction.Settings)),
        null,
        (Strings.MenuMonitorReport, () => { LogMonitors(); Notify(Strings.AppName, Strings.MonitorReportSaved); }),
        (Strings.MenuOpenLogs, Shell.OpenLogFolder),
        null,
        (Strings.MenuQuit, Shutdown),
    ]);

    private void Notify(string title, string message) => _tray?.Notify(title, message);

    private static void LogMonitors()
    {
        foreach (var monitor in Monitors.All()) Log.Info($"Monitor {monitor}, work area {monitor.WorkArea}");
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled exception on the UI thread", e.Exception);
        Notify(Strings.AppName, Strings.SomethingFailed);
        e.Handled = true;
    }
}
