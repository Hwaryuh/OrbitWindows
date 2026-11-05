using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace Orbit.Core;

/// <summary>Other programs, by process id.</summary>
public static class Processes
{
    /// <summary>Terminals, whose prompts no accessibility can see into. VS Code counts as an editor.</summary>
    /// <summary>xterm.js's input element: a terminal inside VS Code, Orca or another Electron app, which the program's name doesn't show.</summary>
    public static bool IsTerminalElement(string? className) => className?.Contains("xterm-helper-textarea", StringComparison.Ordinal) == true;

    public static readonly string[] Terminals = ["windowsterminal.exe", "openconsole.exe", "conhost.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "wezterm-gui.exe", "alacritty.exe", "mintty.exe"];

    /// <summary>The program's full path; null for a process Orbit may not open (elevated, another user's).</summary>
    public static unsafe string? ImagePath(uint process)
    {
        var handle = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, process);
        if (handle.IsNull) return null;
        try
        {
            Span<char> buffer = stackalloc char[1024];
            uint size = (uint)buffer.Length;
            fixed (char* p = buffer)
                if (!PInvoke.QueryFullProcessImageName(handle, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, p, &size)) return null;
            return new string(buffer[..(int)size]);
        }
        finally
        {
            PInvoke.CloseHandle(handle);
        }
    }

    /// <summary>A program's name as File Explorer shows it: its file description, else its file name.</summary>
    public static string DisplayName(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription;
        }
        catch (FileNotFoundException)
        {
        }
        return Path.GetFileNameWithoutExtension(path);
    }
}
