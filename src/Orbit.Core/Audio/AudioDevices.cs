using System.Runtime.InteropServices;
using Windows.Win32.Media.Audio;

namespace Orbit.Core.Audio;

/// <summary>Windows' default sound devices.</summary>
internal static class AudioDevices
{
    /// <summary>The default output or input for <paramref name="role"/>; null when there is none (all unplugged or disabled).</summary>
    public static IMMDevice? Default(EDataFlow flow, ERole role)
    {
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(flow, role, out var device);
            return device;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            ComObjects.Release(enumerator);
        }
    }
}
