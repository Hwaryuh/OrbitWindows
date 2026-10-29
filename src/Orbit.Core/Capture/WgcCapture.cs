using System.Runtime.InteropServices;
using Orbit.Core.Display;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using WinRT;

namespace Orbit.Core.Capture;

/// <summary>
/// One still frame of a monitor through Windows.Graphics.Capture: no cursor, and no yellow border
/// once <see cref="RequestBorderlessAsync"/> was granted.
/// </summary>
public sealed class WgcCapture : IDisposable
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private readonly IDirect3DDevice _device;
    private bool _borderless;

    public WgcCapture()
    {
        _device = CreateDevice();
    }

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

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

    public async Task<ScreenFrame> CaptureMonitorAsync(MonitorInfo monitor, TimeSpan timeout)
    {
        var item = CreateItemForMonitor(monitor.Handle);
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
            return ToFrame(bitmap, monitor.Bounds);
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

    private static GraphicsCaptureItem CreateItemForMonitor(nint monitor)
    {
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        var iid = GraphicsCaptureItemIid;
        return Adopt(interop.CreateForMonitor(monitor, ref iid), GraphicsCaptureItem.FromAbi);
    }

    private static IDirect3DDevice CreateDevice()
    {
        PInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, default,
            D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT, ReadOnlySpan<D3D_FEATURE_LEVEL>.Empty,
            PInvoke.D3D11_SDK_VERSION, out ID3D11Device device, out _).ThrowOnFailure();
        var dxgi = (IDXGIDevice)device;
        PInvoke.CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable).ThrowOnFailure();
        return Adopt(Marshal.GetIUnknownForObject(inspectable), MarshalInterface<IDirect3DDevice>.FromAbi);
    }

    /// <summary>Wraps a COM pointer we own in a projected object, which takes its own reference, then drops ours.</summary>
    private static T Adopt<T>(nint pointer, Func<nint, T> wrap)
    {
        try
        {
            return wrap(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, ref Guid iid);
        nint CreateForMonitor(nint monitor, ref Guid iid);
    }

    [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
