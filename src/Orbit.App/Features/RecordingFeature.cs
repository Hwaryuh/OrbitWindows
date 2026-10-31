using System.IO;
using System.Windows;
using Orbit.App.Features.Capture;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.Capture;
using Orbit.Core.Capture.Recording;
using Orbit.Core.Display;
using Orbit.Core.Settings;

namespace Orbit.App.Features;

/// <summary>
/// Screen recording (Orbit: Recorder.swift), with app sound and the microphone mixed into one track: Record Area and Record Window pick
/// over the frozen monitors as a capture does, Record Screen takes the monitor under the pointer.
/// Any Record action while recording stops it. The file goes into the capture library and shows as
/// a preview card. No default shortcut, as in Orbit. Docs: docs/plan/06-recording.md.
/// </summary>
internal sealed class RecordingFeature : IFeature
{
    /// <summary>Shorter recordings are thrown away, as Orbit does (a stray double press).</summary>
    private static readonly TimeSpan Shortest = TimeSpan.FromSeconds(0.3);

    private enum Mode { Area, Window, Screen }

    private AppServices? _services;
    private ScreenRecorder? _recorder;
    private RecordingChrome? _chrome;
    private uint _dpi = 96;
    private bool _busy;
    private bool _disposed;

    public void Start(AppServices services)
    {
        _services = services;
        services.Actions.Register(OrbitAction.RecordArea, () => _ = RunAsync(Mode.Area));
        services.Actions.Register(OrbitAction.RecordWindow, () => _ = RunAsync(Mode.Window));
        services.Actions.Register(OrbitAction.RecordScreen, () => _ = RunAsync(Mode.Screen));
        services.Actions.Register(OrbitAction.StopRecording, () => _ = StopAsync());
    }

    private async Task RunAsync(Mode mode)
    {
        if (_recorder is not null)
        {
            await StopAsync();
            return;
        }
        if (_busy) return;
        _busy = true;
        var services = _services!;
        try
        {
            if (services.Capture is not { } capture || services.CaptureCards is not { } cards)
            {
                Log.Warn("Recording needs Windows.Graphics.Capture, which is not available");
                Toast.Show(Strings.RecordingStartFailed);
                return;
            }
            if (await PickAsync(mode, capture) is not ({ } source, { } monitor)) return;

            var prefs = services.Preferences;
            bool wantMic = prefs.Get<bool>(Pref.RecordMic);
            var options = new RecordOptions(prefs.Get<int>(Pref.RecordFps) >= 60 ? 60 : 30,
                prefs.Get<bool>(Pref.RecordCursor), capture.Borderless, prefs.Get<bool>(Pref.RecordAppSound), wantMic);
            var draft = cards.Actions.Store.RecordingDraft(DateTime.Now);
            var recorder = await ScreenRecorder.StartAsync(source, options, draft);
            if (_disposed)
            {
                // Orbit quit while the recording was starting.
                await recorder.DiscardAsync();
                return;
            }
            _recorder = recorder;
            _dpi = monitor.Dpi;
            recorder.SourceClosed += () => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_recorder != recorder) return;
                Toast.Show(Strings.RecordingWindowClosed);
                _ = StopAsync();
            });
            recorder.MicFailed += state => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_recorder != recorder) return;
                MicToast(state);
                _chrome?.Refresh();
            });
            if (wantMic && recorder.MicState != MicState.Ready) MicToast(recorder.MicState);
            var sound = new SoundSwitches(
                () => recorder.AppSound,
                on =>
                {
                    recorder.AppSound = on;
                    prefs.Set(Pref.RecordAppSound, on);
                },
                () => recorder.Microphone,
                on =>
                {
                    recorder.Microphone = on;
                    prefs.Set(Pref.RecordMic, on);
                },
                () => recorder.MicState != MicState.Missing);
            _chrome = RecordingChrome.Show(monitor, source.Bounds, source.IsWindow ? null : source.Bounds,
                () => _ = StopAsync(), () => _ = DiscardAsync(), sound);
        }
        catch (RecordingException e)
        {
            Log.Warn($"Recording did not start: {e.Why} ({e.Message})");
            switch (e.Why)
            {
                case RecordingException.Reason.TooBig:
                    Toast.Show(Strings.RecordingTooBig, Strings.RecordingTooBigDetail);
                    break;
                case RecordingException.Reason.TooSmall:
                    Toast.Show(Strings.RecordingTooSmall);
                    break;
                default:
                    Toast.Show(Strings.RecordingStartFailed);
                    break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("Recording could not start", e);
            Toast.Show(Strings.RecordingStartFailed);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Why the microphone is not in the recording (Orbit: "Microphone access is off").</summary>
    private static void MicToast(MicState state) => Toast.Show(Strings.RecordMicUnavailable, state switch
    {
        MicState.Denied => Strings.RecordMicDenied,
        MicState.Missing => Strings.RecordMicMissing,
        _ => null,
    });

    /// <summary>What to record and the monitor it is on; null when the selection was cancelled.</summary>
    private static async Task<(RecordSource Source, MonitorInfo Monitor)?> PickAsync(Mode mode, WgcCapture capture)
    {
        if (mode == Mode.Screen)
        {
            var (x, y) = Monitors.Cursor();
            var monitor = Monitors.FromPoint(x, y) ?? Monitors.All()[0];
            return (RecordSource.Monitor(monitor), monitor);
        }
        if (await CaptureFeature.FreezeAsync(capture) is not ({ } monitors, { } frames)) return null;
        var result = await SelectionSession.RunAsync(monitors, frames, CaptureFeature.Targets(), mode == Mode.Window, textMode: false,
            lastArea: null, keyHints: false, recording: true);
        if (result is null) return null;
        if (result.Window is { } window) return (RecordSource.Window(window.Handle, window.Bounds), result.Monitor);
        if (result.Area == result.Monitor.Bounds) return (RecordSource.Monitor(result.Monitor), result.Monitor);
        return (RecordSource.Area(result.Monitor, result.Area), result.Monitor);
    }

    private async Task StopAsync()
    {
        if (TakeRecorder() is not { } recorder) return;
        var cards = _services!.CaptureCards!;
        // No new recording until this one is in the library (its draft folder is cleared on start).
        _busy = true;
        try
        {
            var result = await recorder.StopAsync();
            if (result is null || result.Length < Shortest)
            {
                if (result is not null) AppPaths.TryDelete(result.Path);
                Toast.Show(Strings.RecordingTooShort);
                return;
            }
            var path = cards.Actions.Store.AddRecording(result.Path);
            var picture = result.LastFrame is { } frame ? FrameImages.ToBitmap(frame, _dpi) : VideoThumbnails.Read(path);
            if (picture is null)
            {
                Toast.Show(Strings.RecordingSaved);
                return;
            }
            cards.Add(new CaptureItem(path, picture, result.Length));
        }
        catch (RecordingException e) when (e.Why == RecordingException.Reason.NothingRecorded)
        {
            Toast.Show(Strings.RecordingTooShort);
        }
        catch (RecordingException e)
        {
            Log.Error("Recording could not be saved", e);
            Toast.Show(Strings.RecordingSaveFailed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("Recording could not be moved into the library", e);
            Toast.Show(Strings.RecordingSaveFailed);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DiscardAsync()
    {
        if (TakeRecorder() is not { } recorder) return;
        _busy = true;
        try
        {
            await recorder.DiscardAsync();
        }
        catch (RecordingException e)
        {
            Log.Warn($"Discarding the recording did not finish cleanly ({e.Message})");
        }
        finally
        {
            _busy = false;
        }
        Toast.Show(Strings.RecordingDiscarded);
    }

    /// <summary>The running recorder, now no longer running as far as the actions are concerned; its controls go.</summary>
    private ScreenRecorder? TakeRecorder()
    {
        var recorder = _recorder;
        _recorder = null;
        _chrome?.Close();
        _chrome = null;
        return recorder;
    }

    /// <summary>Quitting while recording keeps what was recorded, in the library (no card: Orbit is going).</summary>
    public void Dispose()
    {
        _disposed = true;
        if (TakeRecorder() is not { } recorder) return;
        try
        {
            var result = Task.Run(recorder.StopAsync).GetAwaiter().GetResult();
            if (result is not null) _services?.CaptureCards?.Actions.Store.AddRecording(result.Path);
        }
        catch (Exception e) when (e is RecordingException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"The recording could not be kept at exit ({e.GetType().Name})");
        }
    }
}
