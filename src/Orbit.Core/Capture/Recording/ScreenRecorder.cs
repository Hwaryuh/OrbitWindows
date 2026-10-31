using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Orbit.Core.Display;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D10;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.System.WinRT.Direct3D11;
using WinRT;

namespace Orbit.Core.Capture.Recording;

/// <summary>What to record: a monitor (whole, or <see cref="Crop"/> of it in its own pixels) or one window.</summary>
public sealed record RecordSource(nint Handle, bool IsWindow, PixelRect Bounds, PixelRect? Crop)
{
    public static RecordSource Monitor(MonitorInfo monitor) => new(monitor.Handle, false, monitor.Bounds, null);

    /// <param name="area">Physical pixels of the virtual screen, inside <paramref name="monitor"/>.</param>
    public static RecordSource Area(MonitorInfo monitor, PixelRect area) => new(monitor.Handle, false, area, area.RelativeTo(monitor.Bounds));

    public static RecordSource Window(nint window, PixelRect bounds) => new(window, true, bounds, null);
}

/// <param name="AppSound">Other programs' sound mixed in (it can be switched on later either way).</param>
/// <param name="Mic">The microphone mixed in.</param>
public sealed record RecordOptions(int Fps, bool Cursor, bool Borderless, bool AppSound = false, bool Mic = false);

/// <summary>A finished recording: its file, length, frame counts and its last picture.</summary>
public sealed record RecordingResult(string Path, TimeSpan Length, int Frames, int Dropped, bool Hardware, ScreenFrame? LastFrame);

/// <summary>Why a recording could not start or could not be saved.</summary>
public sealed class RecordingException(RecordingException.Reason reason, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public enum Reason { TooSmall, TooBig, Unavailable, WriteFailed, NothingRecorded }

    public Reason Why { get; } = reason;
}

/// <summary>
/// Screen recording (Orbit: Recorder.swift and RecordingWriter.swift), with one sound track that
/// mixes app sound and the microphone (<see cref="SoundRecorder"/>).
/// Windows.Graphics.Capture delivers frames on a free-threaded pool; each kept frame is copied
/// (and cut to the area) into a new texture there and the capture frame is given back at once.
/// The copies go through a short queue to one writer thread, which owns the MP4 writer and also
/// writes the sound chunks; when the encoder falls behind, new frames are dropped rather than queued. Docs: docs/plan/06-recording.md.
/// </summary>
public sealed class ScreenRecorder
{
    private const int QueueLength = 3;
    private static readonly TimeSpan FinishTimeout = TimeSpan.FromSeconds(10);
    private const int QueueWait = 20; // ms
    /// <summary>A still screen's frame is written again after this long (100 ns ticks)…</summary>
    private const long RepeatAfter = TimeSpan.TicksPerSecond;
    /// <summary>…at a time this far behind now, so a frame still being copied comes after it.</summary>
    private const long RepeatLag = TimeSpan.TicksPerMillisecond * 100;

    private readonly object _gate = new();
    private readonly BlockingCollection<Pending> _queue = new(QueueLength);
    private readonly TaskCompletionSource<RecordingResult?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly FramePacer _pacer;
    private readonly string _path;
    private readonly int _width, _height;
    private readonly PixelRect? _crop;
    private readonly PixelRect _bounds;

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private IDirect3DDevice _winrtDevice = null!;
    private ID3D11Texture2D? _black;
    private GraphicsCaptureItem _item = null!;
    private Direct3D11CaptureFramePool _pool = null!;
    private GraphicsCaptureSession _session = null!;
    private SizeInt32 _poolSize;
    private Mp4Writer _writer = null!;
    private SoundRecorder _sound = null!;
    private long _soundWritten; // frames of sound in the file, from the first frame (writer thread)
    private Thread _thread = null!;
    private bool _stopping;
    private bool _discard;
    private bool _closed;
    private long _stopAt;
    private int _dropped;

    /// <summary>The captured window was closed (or the monitor went away): stop and keep what was recorded.</summary>
    public event Action? SourceClosed;

    /// <summary>The microphone could not be opened when it was switched on (raised on the sound thread).</summary>
    public event Action<MicState>? MicFailed;

    /// <summary>Other programs' sound in the mix; can be switched while recording.</summary>
    public bool AppSound
    {
        get => _sound.AppSound;
        set => _sound.AppSound = value;
    }

    /// <summary>The microphone in the mix; can be switched while recording.</summary>
    public bool Microphone
    {
        get => _sound.Microphone;
        set => _sound.Microphone = value;
    }

    /// <summary>Whether the microphone can be switched on.</summary>
    public MicState MicState => _sound.Mic;

    public string Path => _path;
    public PixelRect Bounds => _bounds;
    public int Width => _width;
    public int Height => _height;

    private ScreenRecorder(string path, int width, int height, PixelRect? crop, PixelRect bounds, int fps)
    {
        _path = path;
        _width = width;
        _height = height;
        _crop = crop;
        _bounds = bounds;
        _pacer = new FramePacer(fps);
    }

    /// <summary>
    /// Starts recording into <paramref name="path"/> (written there as it goes). Everything COM is
    /// made on a thread-pool (MTA) thread, where the capture callbacks and the writer run as well.
    /// </summary>
    public static Task<ScreenRecorder> StartAsync(RecordSource source, RecordOptions options, string path) =>
        Task.Run(() => Start(source, options, path));

    private static ScreenRecorder Start(RecordSource source, RecordOptions options, string path)
    {
        GraphicsCaptureItem item;
        try
        {
            item = CaptureInterop.CreateItem(source.Handle, source.IsWindow);
        }
        catch (Exception e) when (e is COMException or ArgumentException)
        {
            throw new RecordingException(RecordingException.Reason.Unavailable, "The capture item could not be created", e);
        }
        int width = source.Crop?.Width ?? item.Size.Width;
        int height = source.Crop?.Height ?? item.Size.Height;
        switch (RecordingSize.Fit(width, height, out int evenWidth, out int evenHeight))
        {
            case RecordingSize.Verdict.TooSmall:
                throw new RecordingException(RecordingException.Reason.TooSmall, $"{width}x{height} is too small to record");
            case RecordingSize.Verdict.TooBig:
                throw new RecordingException(RecordingException.Reason.TooBig, $"{width}x{height} is too big to record");
        }
        var crop = source.Crop is { } c ? c with { Width = evenWidth, Height = evenHeight } : (PixelRect?)null;
        var recorder = new ScreenRecorder(path, evenWidth, evenHeight, crop, source.Bounds, options.Fps) { _item = item };
        try
        {
            recorder.Open(options);
            return recorder;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException)
        {
            recorder.Close();
            AppPaths.TryDelete(path);
            throw new RecordingException(RecordingException.Reason.Unavailable, "Recording could not be set up", e);
        }
    }

    private void Open(RecordOptions options)
    {
        _device = CaptureInterop.CreateDevice(D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_VIDEO_SUPPORT);
        // Frames are copied on capture threads while the encoder works on the writer thread.
        ((ID3D10Multithread)_device).SetMultithreadProtected(true);
        _device.GetImmediateContext(out _context);
        _winrtDevice = CaptureInterop.Wrap(_device);
        _writer = Mp4Writer.Create(_path, _device, _width, _height, options.Fps);
        _sound = SoundRecorder.Open(options.AppSound, options.Mic);
        _sound.MicFailed += state => MicFailed?.Invoke(state);

        _poolSize = _item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
        _session = _pool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = options.Cursor;
        if (options.Borderless) _session.IsBorderRequired = false;
        _item.Closed += OnItemClosed;
        _pool.FrameArrived += OnFrameArrived;

        // Capture first: if it can't start, no writer thread is left waiting. Early frames wait in the queue.
        _session.StartCapture();
        _thread = new Thread(WriteLoop) { IsBackground = true, Name = "Orbit recording writer" };
        _thread.Start();
        Log.Info($"Recording started: {_width}x{_height} at {options.Fps} fps, {(_writer.Hardware ? "hardware" : "software")} encoder");
    }

    /// <summary>Stops and saves. Null when no frame was recorded (the file is removed).</summary>
    public Task<RecordingResult?> StopAsync() => End(discard: false);

    /// <summary>Stops and throws the file away.</summary>
    public Task DiscardAsync() => End(discard: true);

    private async Task<RecordingResult?> End(bool discard)
    {
        bool first = false;
        lock (_gate)
        {
            if (!_stopping)
            {
                first = true;
                _stopping = true;
                _discard = discard;
                _stopAt = SampleClock.Now();
            }
        }
        // Only the first call tears down; a second (the window closed as Stop was pressed) just waits.
        if (first)
        {
            try
            {
                _pool.FrameArrived -= OnFrameArrived;
                _item.Closed -= OnItemClosed;
                _session.Dispose();
            }
            catch (Exception e) when (e is COMException or ObjectDisposedException)
            {
                Log.Warn($"The capture session did not close cleanly ({e.GetType().Name})");
            }
            finally
            {
                _queue.CompleteAdding();
            }
        }
        try
        {
            return await _done.Task.WaitAsync(FinishTimeout);
        }
        catch (TimeoutException e)
        {
            Log.Warn("The recording writer did not finish in time");
            throw new RecordingException(RecordingException.Reason.WriteFailed, "The recording writer did not finish in time", e);
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args) => SourceClosed?.Invoke();

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            Take(sender);
        }
        catch (Exception e) when (e is COMException or ObjectDisposedException or InvalidOperationException)
        {
            // The pool closing under a late callback at stop; nothing is lost.
            Log.Warn($"A recording frame was skipped ({e.GetType().Name})");
        }
    }

    private void Take(Direct3D11CaptureFramePool sender)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame is null) return;
        lock (_gate)
        {
            if (_stopping) return;
            var size = frame.ContentSize;
            try
            {
                long time = frame.SystemRelativeTime.Ticks;
                if (_pacer.Keep(time))
                {
                    var copy = Copy(frame, size);
                    if (!_queue.TryAdd(new Pending(copy, time)))
                    {
                        Marshal.ReleaseComObject(copy);
                        _dropped++;
                    }
                }
            }
            catch (Exception e) when (e is COMException or InvalidCastException)
            {
                Log.Warn($"A recording frame could not be copied (0x{e.HResult:X8})");
                _dropped++;
            }
            // A window that changed size: frames of the new size from now on; the video keeps its own.
            if (size.Width > 0 && size.Height > 0 && (size.Width != _poolSize.Width || size.Height != _poolSize.Height))
            {
                _poolSize = size;
                sender.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            }
        }
    }

    /// <summary>The frame cut to the area (or the window's start size) in a texture of its own.</summary>
    private unsafe ID3D11Texture2D Copy(Direct3D11CaptureFrame frame, SizeInt32 content)
    {
        var source = Texture(frame.Surface);
        ID3D11Texture2D? output = null;
        try
        {
            output = NewTexture(default, 0);
            int x = _crop?.X ?? 0, y = _crop?.Y ?? 0;
            int width = Math.Min(_width, content.Width - x), height = Math.Min(_height, content.Height - y);
            if (width < _width || height < _height) _context.CopyResource(output, _black ??= Black());
            if (width > 0 && height > 0)
            {
                var box = new D3D11_BOX { left = (uint)x, top = (uint)y, front = 0, right = (uint)(x + width), bottom = (uint)(y + height), back = 1 };
                _context.CopySubresourceRegion(output, 0, 0, 0, 0, source, 0, &box);
            }
            var done = output;
            output = null;
            return done;
        }
        finally
        {
            if (output is not null) Marshal.ReleaseComObject(output);
            Marshal.ReleaseComObject(source);
        }
    }

    private unsafe ID3D11Texture2D NewTexture(D3D11_USAGE usage, D3D11_CPU_ACCESS_FLAG cpu, D3D11_SUBRESOURCE_DATA* initial = null)
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)_width, Height = (uint)_height, MipLevels = 1, ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = usage, CPUAccessFlags = cpu,
            BindFlags = usage == D3D11_USAGE.D3D11_USAGE_STAGING ? 0 : D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET,
        };
        ID3D11Texture2D_unmanaged* texture;
        _device.CreateTexture2D(&desc, initial, &texture);
        return Own<ID3D11Texture2D>(texture);
    }

    /// <summary>A managed reference to a COM pointer we own; ours is dropped.</summary>
    private static unsafe T Own<T>(void* pointer)
    {
        try
        {
            return (T)Marshal.GetObjectForIUnknown((nint)pointer);
        }
        finally
        {
            Marshal.Release((nint)pointer);
        }
    }

    private unsafe ID3D11Texture2D Black()
    {
        var zeros = new byte[_width * 4 * _height];
        fixed (byte* bytes = zeros)
        {
            var data = new D3D11_SUBRESOURCE_DATA { pSysMem = bytes, SysMemPitch = (uint)(_width * 4) };
            return NewTexture(default, 0, &data);
        }
    }

    private static unsafe ID3D11Texture2D Texture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var iid = typeof(ID3D11Texture2D).GUID;
        access.GetInterface(&iid, out var texture);
        ComObjects.Release(access);
        return Own<ID3D11Texture2D>(texture);
    }

    private void WriteLoop()
    {
        var clock = new SampleClock();
        Pending? held = null;
        int frames = 0;
        RecordingResult? result = null;
        Exception? failure = null;
        try
        {
            // The loop never throws: it must keep taking (and releasing) frames until Stop
            // completes the queue, even after the writer failed. Between frames it writes the sound
            // that has come in, and it writes a still screen's frame again every second so the video
            // keeps up with the sound in the file.
            while (true)
            {
                if (_queue.TryTake(out var next, QueueWait))
                {
                    if (failure is not null || !clock.Accept(next.Time, out var write))
                    {
                        ComObjects.Release(next.Texture);
                        continue;
                    }
                    if (write is { } w && held is { } h)
                    {
                        failure = Try(() => _writer.Write(h.Texture, w.Start, w.Duration));
                        if (failure is null) frames++;
                        ComObjects.Release(h.Texture);
                    }
                    held = next;
                }
                else if (_queue.IsCompleted) break;
                else if (failure is null && held is { } h && clock.Origin is { } origin)
                {
                    long now = SampleClock.Now() - RepeatLag;
                    if (now - origin - clock.Held >= RepeatAfter && clock.Accept(now, out var again) && again is { } a)
                    {
                        failure = Try(() => _writer.Write(h.Texture, a.Start, a.Duration));
                        if (failure is null) frames++;
                    }
                }
                if (failure is null) failure = Try(() => WriteSound(clock, null));
                else while (_sound.Chunks.TryDequeue(out _)) { } // nothing is written any more
            }

            _sound.Stop(_stopAt);
            if (_discard || failure is not null)
            {
                if (failure is not null) Log.Error("Recording failed while writing", failure);
            }
            else if (clock.End(_stopAt) is { } last && held is { } h)
            {
                _writer.Write(h.Texture, last.Start, last.Duration);
                frames++;
                long end = last.Start + last.Duration;
                WriteSound(clock, end);
                WriteSilence(AudioTimeline.Frames(end));
                var picture = ReadBack(h.Texture);
                _writer.Finish();
                result = new RecordingResult(_path, SampleClock.Length(last), frames, _dropped, _writer.Hardware, picture);
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            failure = e;
            Log.Error("Recording could not be finished", e);
        }
        finally
        {
            if (held is { } h) Marshal.ReleaseComObject(h.Texture);
            Close();
        }

        if (result is null) AppPaths.TryDelete(_path);
        if (failure is not null && !_discard)
            _done.TrySetException(new RecordingException(RecordingException.Reason.WriteFailed, "The recording could not be written", failure));
        else if (result is null && !_discard)
            _done.TrySetException(new RecordingException(RecordingException.Reason.NothingRecorded, "No frame was recorded"));
        else
            _done.TrySetResult(result);
        if (result is not null)
            Log.Info($"Recording saved: {result.Length.TotalSeconds:0.0} s, {result.Frames} frames, {result.Dropped} dropped");
    }

    private static Exception? Try(Action write)
    {
        try
        {
            write();
            return null;
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            return e;
        }
    }

    /// <summary>
    /// The sound chunks that have come in, cut to the video (from its first frame, up to
    /// <paramref name="end"/> ticks from it when stopping). Chunks follow each other without gaps.
    /// </summary>
    private void WriteSound(SampleClock clock, long? end)
    {
        if (clock.Origin is not { } origin) return;
        long zero = Math.Max(0, AudioTimeline.Frames(origin - _sound.Start));
        long stop = end is { } e ? zero + AudioTimeline.Frames(e) : long.MaxValue;
        while (_sound.Chunks.TryDequeue(out var chunk))
        {
            int frames = chunk.Pcm.Length / AudioTimeline.Channels;
            if (!AudioCut.Fit(chunk.Position, frames, zero, stop, out int skip, out int count, out long start, out long duration)) continue;
            _writer.WriteAudio(chunk.Pcm, skip, count, start, duration);
            _soundWritten = Math.Max(_soundWritten, chunk.Position + skip + count - zero);
        }
        // The sound thread died mid-recording: silence keeps the track going, as far behind now as the sound was.
        if (end is null && !_sound.Running && _sound.Chunks.IsEmpty)
            WriteSilence(AudioTimeline.Frames(SampleClock.Now() - origin - SoundLag));
    }

    /// <summary>How far behind now the sound thread hands on its mix, with some slack (100 ns ticks).</summary>
    private const long SoundLag = TimeSpan.TicksPerMillisecond * 200;

    /// <summary>Silence from where the sound ended to <paramref name="end"/> frames (at most a few seconds: the sound thread hands on silence too).</summary>
    private void WriteSilence(long end)
    {
        const int piece = AudioTimeline.Rate;
        var zeros = new short[piece * AudioTimeline.Channels];
        end = Math.Min(end, _soundWritten + piece * 5L);
        while (_soundWritten < end)
        {
            int count = (int)Math.Min(piece, end - _soundWritten);
            long start = AudioTimeline.Ticks(_soundWritten);
            _writer.WriteAudio(zeros, 0, count, start, AudioTimeline.Ticks(_soundWritten + count) - start);
            _soundWritten += count;
        }
    }

    /// <summary>The texture's pixels, for the preview card.</summary>
    private unsafe ScreenFrame? ReadBack(ID3D11Texture2D texture)
    {
        ID3D11Texture2D? staging = null;
        try
        {
            staging = NewTexture(D3D11_USAGE.D3D11_USAGE_STAGING, D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ);
            _context.CopyResource(staging, texture);
            D3D11_MAPPED_SUBRESOURCE mapped;
            _context.Map(staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped);
            try
            {
                int stride = _width * 4;
                var pixels = new byte[stride * _height];
                for (int row = 0; row < _height; row++)
                    new ReadOnlySpan<byte>((byte*)mapped.pData + (long)row * mapped.RowPitch, stride).CopyTo(pixels.AsSpan(row * stride));
                return new ScreenFrame(pixels, _width, _height, stride, _bounds with { Width = _width, Height = _height }, "WGC");
            }
            finally
            {
                _context.Unmap(staging, 0);
            }
        }
        catch (COMException e)
        {
            Log.Warn($"The recording's last frame could not be read back (0x{e.HResult:X8})");
            return null;
        }
        finally
        {
            if (staging is not null) Marshal.ReleaseComObject(staging);
        }
    }

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        try { _pool?.Dispose(); } catch (Exception e) when (e is COMException or ObjectDisposedException) { }
        try { _session?.Dispose(); } catch (Exception e) when (e is COMException or ObjectDisposedException) { }
        _sound?.Dispose();
        _writer?.Dispose();
        _writer = null!;
        if (_black is not null) Marshal.ReleaseComObject(_black);
        (_winrtDevice as IDisposable)?.Dispose();
        if (_context is not null) Marshal.ReleaseComObject(_context);
        if (_device is not null) Marshal.ReleaseComObject(_device);
        _black = null;
        _context = null!;
        _device = null!;
    }

    private readonly record struct Pending(ID3D11Texture2D Texture, long Time);
}
