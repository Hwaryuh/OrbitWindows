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
        _instance = new Mutex(true, @"Local\Orbit.Windows.Instance", out bool first);
        if (!first)
        {
            MessageBox.Show(Strings.AlreadyRunning, Strings.AppName);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        Log.Info($"Orbit {typeof(App).Assembly.GetName().Version} starting on Windows {Environment.OSVersion.Version}");
        LogMonitors();
        foreach (var action in ActionText.Missing()) Log.Error($"Action {action.Id()} has no title");

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
        _features.AddRange([new SettingsFeature(), new RingFeature(), new LauncherFeature(), new CaptureFeature(), new CleanUrlFeature(), new ClipboardFeature(), new TranslateFeature(), new SnippetFeature()]);
        foreach (var feature in _features) feature.Start(_services);
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
        var process = _foreground!.ForegroundProcess;
        _hooks!.Hotkeys.SetPassThrough(_hooks.Hotkeys.Slots(hotkey => PassThroughRule.Passes(PassThroughRule.Defaults, process, hotkey)));
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
