using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Orbit.App.Overlays;
using Orbit.App.Spikes;
using Orbit.Core;
using Orbit.Core.Capture;
using Orbit.Core.Display;
using Orbit.Core.Input;

namespace Orbit.App;

public partial class App : Application
{
    private Mutex? _instance;
    private HookThread? _hooks;
    private Tray? _tray;
    private WgcCapture? _wgc;
    private CaptureSpike? _captureSpike;
    private TestRingWindow? _ring;

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

        _tray = CreateTray();
        _hooks = new HookThread();
        // Defaults from docs/plan/04-shortcuts.md. 0x20 is Space.
        _hooks.Hotkeys.Register(HotkeyRing, new Hotkey(Modifiers.Ctrl | Modifiers.Alt, 0x20));
        _hooks.Hotkeys.Register(HotkeyLauncher, new Hotkey(Modifiers.Alt, 0x20));
        _hooks.Notice += e => Dispatcher.BeginInvoke(() => OnHookNotice(e));
        _hooks.Start();

        if (WgcCapture.IsSupported)
        {
            try
            {
                _wgc = new WgcCapture();
                bool borderless = await _wgc.RequestBorderlessAsync();
                Log.Info($"Windows.Graphics.Capture ready, borderless {borderless}");
            }
            catch (Exception error)
            {
                Log.Error("Windows.Graphics.Capture unavailable; GDI only", error);
                _wgc = null;
            }
        }
        _captureSpike = new CaptureSpike(_wgc, Notify);

        if (e.Args.Contains("--selftest-capture"))
        {
            // Captures every monitor both ways, logs the comparison and quits, without an overlay.
            bool passed = await _captureSpike.SelfTestAsync();
            Shutdown(passed ? 0 : 1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hooks?.Dispose();
        _wgc?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        Log.Info("Orbit stopped");
        base.OnExit(e);
    }

    private const int HotkeyRing = 1;
    private const int HotkeyLauncher = 2;

    private void OnHookNotice(HookEvent e)
    {
        switch (e.Notice)
        {
            case HookNotice.RingOpen:
                OpenRing(e.X, e.Y);
                break;
            case HookNotice.HotkeyDown when e.Hotkey == HotkeyRing:
                var (x, y) = Monitors.Cursor();
                OpenRing(x, y);
                break;
            case HookNotice.HotkeyDown when e.Hotkey == HotkeyLauncher:
                Log.Info("Launcher shortcut pressed (no launcher yet)");
                break;
            case HookNotice.RingRelease:
            case HookNotice.HotkeyUp when e.Hotkey == HotkeyRing:
            case HookNotice.HooksReinstalled:
                _ring?.Release();
                _ring = null;
                break;
        }
    }

    private void OpenRing(int x, int y)
    {
        _ring?.Release();
        _ring = new TestRingWindow();
        _ring.Open(x, y);
    }

    private Tray CreateTray() => new(
    [
        (Strings.MenuCaptureTest, () => _ = _captureSpike?.RunAsync()),
        (Strings.MenuMonitorReport, () => { LogMonitors(); Notify(Strings.AppName, Strings.MonitorReportSaved); }),
        (Strings.MenuOpenLogs, () => Process.Start(new ProcessStartInfo(Log.Folder) { UseShellExecute = true })),
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
