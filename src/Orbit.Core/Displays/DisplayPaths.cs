using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace Orbit.Core.Displays;

/// <summary>A monitor behind one of Windows' screens: the name Settings shows, its device path and whether it is the laptop's own panel.</summary>
public sealed record DisplayTarget(string FriendlyName, string DevicePath, bool IsBuiltIn);

/// <summary>Windows' active display paths, by the screen's GDI name (<c>\\.\DISPLAY1</c>).</summary>
public static class DisplayPaths
{
    // DISPLAYCONFIG_OUTPUT_TECHNOLOGY: INTERNAL, DISPLAYPORT_EMBEDDED, UDI_EMBEDDED.
    private static readonly uint[] Internal = [0x80000000, 11, 13];

    /// <summary>Every active target, grouped by its screen; a duplicated screen has several.</summary>
    public static unsafe Dictionary<string, List<DisplayTarget>> Read()
    {
        var byScreen = new Dictionary<string, List<DisplayTarget>>(StringComparer.OrdinalIgnoreCase);
        DISPLAYCONFIG_PATH_INFO[] paths = [];
        DISPLAYCONFIG_MODE_INFO[] modes = [];
        // The paths can change between the size query and the read: try again then.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (PInvoke.GetDisplayConfigBufferSizes(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != WIN32_ERROR.ERROR_SUCCESS) return byScreen;
            paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            WIN32_ERROR result;
            // The pointer form: the topology must be null for active paths.
            fixed (DISPLAYCONFIG_PATH_INFO* p = paths)
            fixed (DISPLAYCONFIG_MODE_INFO* m = modes)
                result = PInvoke.QueryDisplayConfig(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, &pathCount, p, &modeCount, m, null);
            if (result == WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER) continue;
            if (result != WIN32_ERROR.ERROR_SUCCESS) return byScreen;
            Array.Resize(ref paths, (int)pathCount);
            break;
        }
        foreach (var path in paths)
        {
            var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
            source.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
            source.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
            source.header.adapterId = path.sourceInfo.adapterId;
            source.header.id = path.sourceInfo.id;
            if (PInvoke.DisplayConfigGetDeviceInfo(&source.header) != 0) continue;
            var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
            target.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
            target.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
            target.header.adapterId = path.targetInfo.adapterId;
            target.header.id = path.targetInfo.id;
            if (PInvoke.DisplayConfigGetDeviceInfo(&target.header) != 0) continue;
            string screen = source.viewGdiDeviceName.ToString();
            if (!byScreen.TryGetValue(screen, out var list)) byScreen[screen] = list = [];
            list.Add(new DisplayTarget(
                target.monitorFriendlyDeviceName.ToString(),
                target.monitorDevicePath.ToString(),
                Internal.Contains((uint)path.targetInfo.outputTechnology)));
        }
        return byScreen;
    }
}
