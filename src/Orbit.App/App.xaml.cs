using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using H.NotifyIcon;
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
    private TaskbarIcon? _tray;
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
        _hooks.Notice += (notice, x, y) => Dispatcher.BeginInvoke(() => OnHookNotice(notice, x, y));
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

    private void OnHookNotice(HookNotice notice, int x, int y)
    {
        switch (notice)
        {
            case HookNotice.RingOpen:
                _ring?.Release();
                _ring = new TestRingWindow();
                _ring.Open(x, y);
                break;
            case HookNotice.RingRelease:
                _ring?.Release();
                _ring = null;
                break;
            case HookNotice.HooksReinstalled:
                _ring?.Release();
                _ring = null;
                break;
        }
    }

    private TaskbarIcon CreateTray()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item(Strings.MenuCaptureTest, () => _ = _captureSpike?.RunAsync()));
        menu.Items.Add(Item(Strings.MenuMonitorReport, () => { LogMonitors(); Notify(Strings.AppName, Strings.MonitorReportSaved); }));
        menu.Items.Add(Item(Strings.MenuOpenLogs, () => Process.Start(new ProcessStartInfo(Log.Folder) { UseShellExecute = true })));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.MenuQuit, Shutdown));

        var tray = new TaskbarIcon
        {
            ToolTipText = Strings.AppName,
            Icon = TrayIcon(),
            ContextMenu = menu,
            MenuActivation = H.NotifyIcon.Core.PopupActivationMode.RightClick,
        };
        tray.ForceCreate(enablesEfficiencyMode: false);
        return tray;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>A plain ring on black until Orbit's icon is drawn: a PNG wrapped in a one-image .ico.</summary>
    private static System.Drawing.Icon TrayIcon()
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawEllipse(Brushes.Black, null, new Point(16, 16), 15, 15);
            context.DrawEllipse(null, new Pen(Brushes.White, 3), new Point(16, 16), 9, 9);
        }
        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);

        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((short)0); writer.Write((short)1); writer.Write((short)1);  // ICONDIR: icon, 1 image
            writer.Write((byte)32); writer.Write((byte)32); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((short)1); writer.Write((short)32);                         // planes, bits per pixel
            writer.Write((int)png.Length); writer.Write(6 + 16);                     // size, offset
            writer.Write(png.ToArray());
        }
        ico.Position = 0;
        return new System.Drawing.Icon(ico);
    }

    private void Notify(string title, string message) => _tray?.ShowNotification(title, message);

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
