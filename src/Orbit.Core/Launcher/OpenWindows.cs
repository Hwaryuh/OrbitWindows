using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Orbit.Core.Launcher;

public sealed record OpenWindow(nint Handle, string Title, string AppName, string? ExePath, bool Minimized);

/// <summary>
/// The windows Alt+Tab would show (Orbit: Launcher/OpenWindows.swift reads them through
/// Accessibility). A window counts when it is visible, not cloaked (another virtual desktop, a
/// suspended Store app), has a title, isn't Orbit's, and is either marked as an app window or is
/// an unowned window that isn't a tool window.
/// </summary>
public static class OpenWindows
{
    public static unsafe IReadOnlyList<OpenWindow> List()
    {
        var found = new List<OpenWindow>();
        var names = new Dictionary<uint, (string Name, string? Path)>();
        uint own = (uint)Environment.ProcessId;
        PInvoke.EnumWindows((window, _) =>
        {
            if (Counts(window, own, out uint process))
            {
                if (!names.TryGetValue(process, out var app)) names[process] = app = AppOf(process);
                found.Add(new OpenWindow(window, Title(window), app.Name, app.Path, PInvoke.IsIconic(window)));
            }
            return true;
        }, default);
        return found;
    }

    /// <summary>Brings the window forward, restored if minimized. Call while Orbit is in front (the launcher).</summary>
    public static bool Focus(nint handle)
    {
        var window = new HWND(handle);
        if (!PInvoke.IsWindow(window)) return false;
        if (PInvoke.IsIconic(window)) PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_RESTORE);
        return PInvoke.SetForegroundWindow(window);
    }

    /// <summary>Asks the window to close, as its close button would.</summary>
    public static bool Close(nint handle)
    {
        var window = new HWND(handle);
        return PInvoke.IsWindow(window) && PInvoke.PostMessage(window, PInvoke.WM_CLOSE, default, default);
    }

    private static unsafe bool Counts(HWND window, uint own, out uint process)
    {
        process = 0;
        if (!PInvoke.IsWindowVisible(window) || PInvoke.GetWindowTextLength(window) == 0) return false;
        int cloaked = 0;
        if (PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(int)).Succeeded && cloaked != 0) return false;
        var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        bool appWindow = (style & WINDOW_EX_STYLE.WS_EX_APPWINDOW) != 0;
        bool toolWindow = (style & WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0;
        bool owned = !PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_OWNER).IsNull;
        if (!appWindow && (owned || toolWindow)) return false;
        uint pid = 0;
        PInvoke.GetWindowThreadProcessId(window, &pid);
        process = pid;
        if (pid == own) return false;
        // The desktop itself.
        Span<char> name = stackalloc char[32];
        int length = PInvoke.GetClassName(window, name);
        return !name[..length].SequenceEqual("Progman");
    }

    private static string Title(HWND window)
    {
        Span<char> text = stackalloc char[512];
        int length = PInvoke.GetWindowText(window, text);
        return new string(text[..length]);
    }

    /// <summary>The program's name (its file description) and path; a process Orbit may not open gets neither.</summary>
    private static unsafe (string Name, string? Path) AppOf(uint process)
    {
        var handle = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, process);
        if (handle.IsNull) return ("", null);
        try
        {
            Span<char> buffer = stackalloc char[1024];
            uint size = (uint)buffer.Length;
            fixed (char* p = buffer)
                if (!PInvoke.QueryFullProcessImageName(handle, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, p, &size)) return ("", null);
            var path = new string(buffer[..(int)size]);
            string name;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription : System.IO.Path.GetFileNameWithoutExtension(path);
            }
            catch (FileNotFoundException)
            {
                name = System.IO.Path.GetFileNameWithoutExtension(path);
            }
            return (name, path);
        }
        finally
        {
            PInvoke.CloseHandle(handle);
        }
    }
}
