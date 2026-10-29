using Windows.Win32;
using Windows.Win32.System.Threading;

namespace Orbit.Core;

/// <summary>Other programs, by process id.</summary>
public static class Processes
{
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
}
