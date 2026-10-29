using System.IO;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Rectangle = System.Windows.Shapes.Rectangle;
using Orbit.App.Features.Capture;
using Orbit.App.Features.Capture.Editor;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.Capture;
using Orbit.Core.Display;
using Orbit.Core.Input;
using Orbit.Core.Launcher;
using Orbit.Core.Settings;

namespace Orbit.App.Features;

/// <summary>
/// Screen capture (Orbit: Modules/Capture/CaptureService.swift): area, window, full screen and
/// text, and the last area again. Every monitor is frozen first; only then does the selection
/// show. Each picture goes into the library (recognized text only onto the clipboard, as Orbit);
/// then it is copied (capture.autoCopy) and shown as a preview card, or what the key picked in the
/// selection happens. Shortcuts: docs/plan/04-shortcuts.md.
/// </summary>
internal sealed class CaptureFeature : IFeature
{
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(1);

    private enum Mode { Area, Window, Screen, Text, Repeat }

    private readonly Dictionary<int, OrbitAction> _hotkeys = [];
    private AppServices? _services;
    private CaptureStore? _store;
    private QuickAccess? _cards;
    private bool _busy;

    public void Start(AppServices services)
    {
        _services = services;
        _store = new CaptureStore(services.Preferences);
        _cards = new QuickAccess(services.Preferences, new CaptureActions(_store));
        _cards.Pin = item => PinWindow.Pin(item, _cards);
        _cards.Edit = item => EditorWindow.Open(item, _cards, services.Preferences);
        Register(OrbitAction.CaptureArea, Mode.Area, 0x34);
        Register(OrbitAction.CaptureWindow, Mode.Window, 0x35);
        Register(OrbitAction.CaptureScreen, Mode.Screen, 0x33);
        Register(OrbitAction.CaptureText, Mode.Text, 0x32);
        Register(OrbitAction.CaptureRepeat, Mode.Repeat, key: null);
    }

    /// <param name="key">The digit with Ctrl+Alt, as Orbit's ⌥⇧ digits; null for no shortcut.</param>
    private void Register(OrbitAction action, Mode mode, uint? key)
    {
        var services = _services!;
        services.Actions.Register(action, () => _ = RunAsync(mode));
        if (key is { } vk) _hotkeys[services.AddHotkey(new Hotkey(Modifiers.Ctrl | Modifiers.Alt, vk), action)] = action;
    }

    public void OnHook(HookEvent e)
    {
        if (e.Notice == HookNotice.HotkeyDown && _hotkeys.TryGetValue(e.Hotkey, out var action)) _services!.Actions.Perform(action);
    }

    private async Task RunAsync(Mode mode)
    {
        if (_busy) return;
        _busy = true;
        var services = _services!;
        var store = _store!;
        try
        {
            var monitors = Monitors.All();
            var frames = await ScreenFreeze.AllAsync(services.Capture, monitors);
            if (!ScreenFreeze.SameLayout(monitors, Monitors.All()))
            {
                Log.Warn("Capture cancelled: the monitor layout changed while capturing");
                Toast.Show(Strings.CaptureCancelled);
                return;
            }
            var lastArea = CaptureStore.Usable(store.LastArea, monitors);

            if (mode == Mode.Screen)
            {
                var (x, y) = Monitors.Cursor();
                int index = Math.Max(0, monitors.ToList().FindIndex(m => m.Bounds.Contains(x, y)));
                Flash.On(monitors[index]);
                Deliver(frames[index], monitors[index].Dpi, null);
                return;
            }
            if (mode == Mode.Repeat)
            {
                if (lastArea is { } area && Array.FindIndex(frames, f => f.Bounds.Contains(area.X, area.Y)) is var i and >= 0)
                {
                    Deliver(frames[i].Crop(area), monitors[i].Dpi, null);
                    return;
                }
                Toast.Show(Strings.CaptureNoLastArea);
                mode = Mode.Area;
            }

            var targets = mode is Mode.Area or Mode.Window
                ? OpenWindows.List().Where(w => !w.Minimized && w.Bounds.Width > 40 && w.Bounds.Height > 40).ToList()
                : [];
            var result = await SelectionSession.RunAsync(monitors, frames, targets, mode == Mode.Window, mode == Mode.Text,
                lastArea, services.Preferences.Get<bool>(Pref.CaptureKeyHints));
            if (result is null) return;
            if (mode != Mode.Text && result.Window is null) store.LastArea = new LastArea(result.Area, result.Monitor.DeviceName);

            var image = result.Window is { } window ? await WindowImageAsync(window, result) : result.Frame.Crop(result.Area);
            if (mode == Mode.Text || result.Action == CaptureKeyAction.Text) await CaptureActions.RecognizeAsync(image);
            else Deliver(image, result.Monitor.Dpi, result.Action);
        }
        catch (Exception e)
        {
            Log.Error("Capture failed", e);
            Toast.Show(Strings.CaptureFailed);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>The window on its own (no overlapping windows); what of it the frozen monitor shows if that fails.</summary>
    private async Task<ScreenFrame> WindowImageAsync(OpenWindow window, SelectionResult result)
    {
        if (_services!.Capture is { } wgc)
        {
            try
            {
                return await wgc.CaptureWindowAsync(window.Handle, window.Bounds, WindowTimeout);
            }
            catch (Exception e)
            {
                Log.Warn($"Window capture failed ({e.GetType().Name}); using the frozen screen");
            }
        }
        return result.Frame.Crop(result.Area);
    }

    /// <summary>
    /// Into the library, then copied, saved or pinned as the key picked; else copied
    /// (capture.autoCopy) and shown as a card (Orbit: <c>deliver</c>, <c>finish</c>).
    /// </summary>
    /// <param name="dpi">The monitor's: the picture keeps it, so the editor knows its points.</param>
    private void Deliver(ScreenFrame frame, uint dpi, CaptureKeyAction? action)
    {
        var store = _store!;
        var cards = _cards!;
        var bitmap = FrameImages.ToBitmap(frame, dpi);
        var png = FrameImages.Png(bitmap);
        string path;
        try
        {
            path = store.Write(png, DateTime.Now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("Capture could not be written to the library", e);
            Toast.Show(Strings.CaptureSaveFailed);
            return;
        }
        Log.Info($"Captured {frame.Width}x{frame.Height} via {frame.Source}{(action is { } a ? $", {a}" : "")}");
        var item = new CaptureItem(path, bitmap);

        switch (action)
        {
            case CaptureKeyAction.Copy:
                cards.Actions.Copy(item);
                break;
            case CaptureKeyAction.Save:
                cards.Actions.Save(item);
                break;
            case CaptureKeyAction.Pin:
                PinWindow.Pin(item, cards);
                break;
            case CaptureKeyAction.Edit:
                EditorWindow.Open(item, cards, _services!.Preferences);
                break;
            default:
                if (_services!.Preferences.Get<bool>(Pref.CaptureAutoCopy) && !FrameImages.Copy(bitmap, png)) Toast.Show(Strings.CopyFailed);
                cards.Add(item);
                break;
        }
    }

    /// <summary>A white flash over the monitor that was captured whole, as Orbit's.</summary>
    private sealed class Flash : OverlayWindow
    {
        private Flash() : base(activating: false, transparent: true, clickThrough: true)
        {
            Content = new Rectangle { Fill = Brushes.White };
            Opacity = 0;
        }

        public static void On(MonitorInfo monitor)
        {
            var flash = new Flash();
            flash.ShowAt(monitor.Bounds);
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.35, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(50))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
            fade.Completed += (_, _) => flash.Close();
            flash.BeginAnimation(OpacityProperty, fade);
        }
    }
}
