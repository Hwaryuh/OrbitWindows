using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Orbit.Core.SystemInfo;

/// <summary>
/// One-shot Windows utilities (Orbit: Modules/System/SystemActions.swift, with Windows' own
/// switches): lock, display off, dark mode, desktop icons, hidden files, the Recycle Bin, eject.
/// </summary>
public static class SystemActions
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private static readonly HWND AllWindows = new(0xFFFF);
    private const uint ShErbNoConfirmation = 0x1, ShErbNoProgressUi = 0x2, ShErbNoSound = 0x4;
    private const uint WmCommand = 0x0111, WmSysCommand = 0x0112, WmSettingChange = 0x001A;
    private const nuint ScMonitorPower = 0xF170;
    private const nuint DesktopIconsCommand = 0x7402, RefreshCommand = 0xA220;

    public static bool Lock() => PInvoke.LockWorkStation();

    /// <summary>Every display to sleep; the next input wakes them.</summary>
    public static unsafe void DisplayOff() =>
        PInvoke.SendMessageTimeout(AllWindows, WmSysCommand, ScMonitorPower, 2, SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, 1000, null);

    /// <summary>Apps and Windows itself to dark, or back to light, following the apps' setting. True when dark now.</summary>
    public static bool ToggleDarkMode()
    {
        using var key = Registry.CurrentUser.CreateSubKey(Personalize);
        bool light = key.GetValue("AppsUseLightTheme") is not int v || v != 0;
        int next = light ? 0 : 1;
        key.SetValue("AppsUseLightTheme", next, RegistryValueKind.DWord);
        key.SetValue("SystemUsesLightTheme", next, RegistryValueKind.DWord);
        Broadcast("ImmersiveColorSet");
        return light;
    }

    /// <summary>Hides or shows the desktop's icons, as the desktop's own View menu does. True when shown now; null without a desktop.</summary>
    public static bool? ToggleDesktopIcons()
    {
        var view = DesktopView();
        if (view.IsNull) return null;
        var list = PInvoke.FindWindowEx(view, HWND.Null, "SysListView32", null);
        bool shown = !list.IsNull && PInvoke.IsWindowVisible(list);
        PInvoke.SendMessage(view, WmCommand, DesktopIconsCommand, 0);
        return !shown;
    }

    /// <summary>Shows or hides hidden files in File Explorer and refreshes its open windows. True when shown now.</summary>
    public static unsafe bool ToggleHiddenFiles()
    {
        using var key = Registry.CurrentUser.CreateSubKey(Advanced);
        bool shown = key.GetValue("Hidden") is int v && v == 1;
        key.SetValue("Hidden", shown ? 2 : 1, RegistryValueKind.DWord);
        PInvoke.SHChangeNotify(SHCNE_ID.SHCNE_ASSOCCHANGED, SHCNF_FLAGS.SHCNF_IDLIST);
        // Each open folder window redraws with the new setting.
        var window = HWND.Null;
        while (!(window = PInvoke.FindWindowEx(HWND.Null, window, "CabinetWClass", null)).IsNull)
            PostToView(window);
        PostToView(DesktopView());
        return !shown;
    }

    /// <summary>How many items the Recycle Bin holds on every drive; null when Windows won't say.</summary>
    public static unsafe long? RecycleBinCount()
    {
        var info = new SHQUERYRBINFO { cbSize = (uint)sizeof(SHQUERYRBINFO) };
        return PInvoke.SHQueryRecycleBin(null, ref info).Succeeded ? info.i64NumItems : null;
    }

    /// <summary>Empties it without Windows' own question (Orbit asks first) or sound.</summary>
    public static bool EmptyRecycleBin() =>
        PInvoke.SHEmptyRecycleBin(HWND.Null, null, ShErbNoConfirmation | ShErbNoProgressUi | ShErbNoSound).Succeeded;

    /// <summary>
    /// Ejects every removable drive (USB sticks, cards) through the shell's own Eject, as File
    /// Explorer does. How many went, and how many were still there after a few seconds (in use,
    /// or Windows still at it). Call on an STA thread.
    /// </summary>
    public static (int Ejected, int Failed) EjectAll()
    {
        var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady).Select(d => d.Name).ToList();
        if (drives.Count == 0) return (0, 0);
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return (0, drives.Count);
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic? computer = null;
        try
        {
            // 17: "This PC".
            computer = shell.NameSpace(17);
            foreach (var drive in drives)
            {
                dynamic? item = null;
                try
                {
                    item = computer.ParseName(drive);
                    item.InvokeVerb("Eject");
                }
                catch (Exception e) when (e is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    Log.Warn($"Eject failed for one drive ({e.GetType().Name})");
                }
                finally
                {
                    if (item is not null) Marshal.FinalReleaseComObject(item);
                }
            }
        }
        finally
        {
            if (computer is not null) Marshal.FinalReleaseComObject(computer);
            Marshal.FinalReleaseComObject(shell);
        }
        // The verb returns before Windows lets go: wait for the drives to leave, up to 4 seconds.
        int still = drives.Count;
        for (int i = 0; i < 20 && still > 0; i++)
        {
            Thread.Sleep(200);
            still = DriveInfo.GetDrives().Count(d => drives.Contains(d.Name) && d.IsReady);
        }
        return (drives.Count - still, still);
    }

    private static unsafe void Broadcast(string area)
    {
        fixed (char* text = area)
            PInvoke.SendMessageTimeout(AllWindows, WmSettingChange, 0, (nint)text, SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, 1000, null);
    }

    /// <summary>The desktop's folder view: under Progman, or under a WorkerW once the wallpaper slideshow made one.</summary>
    private static HWND DesktopView()
    {
        var view = PInvoke.FindWindowEx(PInvoke.FindWindow("Progman", null), HWND.Null, "SHELLDLL_DefView", null);
        var worker = HWND.Null;
        while (view.IsNull && !(worker = PInvoke.FindWindowEx(HWND.Null, worker, "WorkerW", null)).IsNull)
            view = PInvoke.FindWindowEx(worker, HWND.Null, "SHELLDLL_DefView", null);
        return view;
    }

    /// <summary>The window's Refresh command (41504), as F5: folder windows and the desktop take it.</summary>
    private static void PostToView(HWND window)
    {
        if (!window.IsNull) PInvoke.PostMessage(window, WmCommand, RefreshCommand, 0);
    }
}
