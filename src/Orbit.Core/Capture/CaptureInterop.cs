using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using WinRT;

namespace Orbit.Core.Capture;

/// <summary>What still capture (<see cref="WgcCapture"/>) and recording share: a D3D11 device and capture items.</summary>
internal static class CaptureInterop
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static ID3D11Device CreateDevice(D3D11_CREATE_DEVICE_FLAG flags)
    {
        PInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, default,
            flags | D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT, ReadOnlySpan<D3D_FEATURE_LEVEL>.Empty,
            PInvoke.D3D11_SDK_VERSION, out ID3D11Device device, out _).ThrowOnFailure();
        return device;
    }

    /// <summary>The WinRT face of a D3D11 device, which capture frame pools take.</summary>
    public static IDirect3DDevice Wrap(ID3D11Device device)
    {
        PInvoke.CreateDirect3D11DeviceFromDXGIDevice((IDXGIDevice)device, out var inspectable).ThrowOnFailure();
        return Adopt(Marshal.GetIUnknownForObject(inspectable), MarshalInterface<IDirect3DDevice>.FromAbi);
    }

    public static GraphicsCaptureItem CreateItem(nint handle, bool forWindow)
    {
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        var iid = GraphicsCaptureItemIid;
        var pointer = forWindow ? interop.CreateForWindow(handle, ref iid) : interop.CreateForMonitor(handle, ref iid);
        return Adopt(pointer, GraphicsCaptureItem.FromAbi);
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
}
