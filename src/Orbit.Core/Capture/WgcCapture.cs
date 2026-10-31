using System.Runtime.InteropServices;
using Orbit.Core.Display;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace Orbit.Core.Capture;

/// <summary>
/// One still frame of a monitor through Windows.Graphics.Capture: no cursor, and no yellow border
/// once <see cref="RequestBorderlessAsync"/> was granted.
/// </summary>
public sealed class WgcCapture : IDisposable
{
    private readonly IDirect3DDevice _device;
    private bool _borderless;

    public WgcCapture()
    {
        _device = CaptureInterop.Wrap(CaptureInterop.CreateDevice(default));
    }

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    /// <summary>Whether capture without the yellow border was granted (recordings use it too).</summary>
    public bool Borderless => _borderless;

    /// <summary>Asks once for capture without the border. Unpackaged apps are normally allowed.</summary>
    public async Task<bool> RequestBorderlessAsync()
    {
        try
        {
            var status = await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
            _borderless = status == Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus.Allowed;
        }
        catch (Exception e)
        {
            Log.Error("Borderless capture request failed", e);
            _borderless = false;
        }
        return _borderless;
    }

    public Task<ScreenFrame> CaptureMonitorAsync(MonitorInfo monitor, TimeSpan timeout) =>
        CaptureAsync(CaptureInterop.CreateItem(monitor.Handle, forWindow: false), monitor.Bounds, timeout);

    /// <summary>
    /// One window on its own: its pixels without what overlaps it (Orbit: <c>captureWindow</c>).
    /// <paramref name="bounds"/> is only recorded on the frame; the frame has the window's own size.
    /// </summary>
    public Task<ScreenFrame> CaptureWindowAsync(nint window, PixelRect bounds, TimeSpan timeout) =>
        CaptureAsync(CaptureInterop.CreateItem(window, forWindow: true), bounds, timeout);

    private async Task<ScreenFrame> CaptureAsync(GraphicsCaptureItem item, PixelRect bounds, TimeSpan timeout)
    {
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        if (_borderless) session.IsBorderRequired = false;

        // A frame belongs to whoever takes it out of `arrived`: this method, or the cleanup below
        // once the wait timed out. After `closed`, the handler disposes whatever still comes in.
        var arrived = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object();
        bool closed = false;
        TypedEventHandler<Direct3D11CaptureFramePool, object> handler = (sender, _) =>
        {
            var frame = sender.TryGetNextFrame();
            if (frame is null) return;
            lock (gate)
            {
                if (!closed && arrived.TrySetResult(frame)) return;
            }
            frame.Dispose();
        };
        pool.FrameArrived += handler;
        session.StartCapture();

        try
        {
            using var first = await arrived.Task.WaitAsync(timeout);
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(first.Surface, BitmapAlphaMode.Premultiplied);
            return ToFrame(bitmap, bounds);
        }
        catch (TimeoutException)
        {
            lock (gate)
            {
                closed = true;
                // A frame that won the race with the timeout has no one else to dispose it.
                if (arrived.Task.IsCompletedSuccessfully) arrived.Task.Result.Dispose();
                else arrived.TrySetCanceled();
            }
            throw;
        }
        finally
        {
            lock (gate) closed = true;
            pool.FrameArrived -= handler;
        }
    }

    public void Dispose() => (_device as IDisposable)?.Dispose();

    private static unsafe ScreenFrame ToFrame(SoftwareBitmap bitmap, PixelRect bounds)
    {
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        var plane = buffer.GetPlaneDescription(0);
        using var reference = buffer.CreateReference();
        reference.As<IMemoryBufferByteAccess>().GetBuffer(out byte* data, out uint capacity);

        int width = plane.Width, height = plane.Height, stride = width * 4;
        var pixels = new byte[stride * height];
        for (int row = 0; row < height; row++)
        {
            long offset = plane.StartIndex + (long)row * plane.Stride;
            if (offset + stride > capacity) break;
            new ReadOnlySpan<byte>(data + offset, stride).CopyTo(pixels.AsSpan(row * stride));
        }
        return new ScreenFrame(pixels, width, height, stride, bounds with { Width = width, Height = height }, "WGC");
    }

    [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
