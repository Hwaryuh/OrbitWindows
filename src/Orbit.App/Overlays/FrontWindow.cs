using Orbit.Core;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Orbit.App.Overlays;

/// <summary>
/// Bringing an Orbit window to the front when the user is in another app. A shortcut seen only by
/// Orbit's hook doesn't make Orbit the process that "received the last input", so a plain
/// SetForegroundWindow is refused; attaching to the foreground thread's input for the call is the
/// usual way. It can still fail (a full-screen game, the secure desktop): the caller is told.
/// </summary>
internal static class FrontWindow
{
    public static unsafe bool Take(nint handle)
    {
        var window = new HWND(handle);
        var front = PInvoke.GetForegroundWindow();
        if (front == window) return true;

        uint own = PInvoke.GetCurrentThreadId();
        uint other = front.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(front, null);
        bool attached = other != 0 && other != own && PInvoke.AttachThreadInput(own, other, true);
        try
        {
            PInvoke.SetForegroundWindow(window);
            PInvoke.BringWindowToTop(window);
        }
        finally
        {
            if (attached) PInvoke.AttachThreadInput(own, other, false);
        }

        bool taken = PInvoke.GetForegroundWindow() == window;
        if (!taken) Log.Warn("Could not take the foreground; the window is shown without the keyboard");
        return taken;
    }
}
