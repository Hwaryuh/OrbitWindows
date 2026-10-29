using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Orbit.Core.Ports;

public enum PortProtocol { Tcp, Udp }

/// <summary>Why a row offers no way to end its program.</summary>
public enum CloseBlock
{
    None,
    /// <summary>Windows itself: System, critical processes, the session's core programs.</summary>
    System,
    /// <summary>Orbit.</summary>
    Self,
    /// <summary>Windows won't let Orbit end it: elevated, another user's, a service.</summary>
    Denied,
    /// <summary>The process could not be told apart from one that reused its id: refresh first.</summary>
    Unverified,
}

public enum CloseResult { Closed, StillRunning, NoWindow, Gone, Changed, Denied, Failed }

/// <summary>One listening TCP socket or bound UDP socket and the program that owns it.</summary>
public sealed record PortEntry(PortProtocol Protocol, string Host, int Port, int Pid, string Name, long? StartedAt, bool HasWindow, CloseBlock Block)
{
    /// <summary>Bound to every interface (0.0.0.0 or ::), so other devices may reach it unless a firewall stops them.</summary>
    public bool AllInterfaces => PortTable.IsWildcard(Host);

    /// <summary>"127.0.0.1:3000", "[::1]:3000".</summary>
    public string Endpoint => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
}

/// <summary>The listing; <see cref="Complete"/> is false when one of Windows' four tables could not be read.</summary>
public sealed record PortSnapshot(IReadOnlyList<PortEntry> Entries, bool Complete);

/// <summary>
/// The ports programs listen on (behavior after Vorssaint's Port Manager; no Orbit original):
/// Windows' TCP listener and UDP tables for IPv4 and IPv6, each row's program, and a careful way
/// to end it. A program may be ended only when the same process was seen before and after the
/// tables were read, and is checked once more on the handle that ends it.
/// </summary>
public static class PortTable
{
    public readonly record struct Socket(PortProtocol Protocol, string Host, int Port, int Pid);

    private const int AfInet = 2, AfInet6 = 23;
    private const int TcpTableOwnerPidListener = 3, UdpTableOwnerPid = 1;
    private const uint ErrorInsufficientBuffer = 122, ErrorInvalidParameter = 87;

    // Core programs of a Windows session. Ending one logs the user off or crashes Windows; most
    // are critical processes anyway, and this list holds even where that flag can't be read.
    private static readonly HashSet<string> CoreNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "svchost", "fontdrvhost", "dwm", "memory compression",
    };

    public static bool IsWildcard(string host) => host is "0.0.0.0" or "::";

    /// <summary>A table's port field: network byte order in the low 16 bits.</summary>
    public static int PortOf(uint raw) => (int)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));

    /// <summary>
    /// The rows of a MIB_*_OWNER_PID table as Windows wrote it (<paramref name="length"/> bytes):
    /// a count, then fixed-size rows. Null when the count doesn't fit the length.
    /// </summary>
    public static List<Socket>? Parse(ReadOnlySpan<byte> table, PortProtocol protocol, bool v6)
    {
        if (table.Length < 4) return null;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(table);
        // Row sizes and field offsets of MIB_TCPROW_OWNER_PID, MIB_TCP6ROW_OWNER_PID,
        // MIB_UDPROW_OWNER_PID and MIB_UDP6ROW_OWNER_PID: every field is 4-byte aligned.
        var (size, address, scope, port, pid) = (protocol, v6) switch
        {
            (PortProtocol.Tcp, false) => (24, 4, -1, 8, 20),
            (PortProtocol.Tcp, true) => (56, 0, 16, 20, 52),
            (PortProtocol.Udp, false) => (12, 0, -1, 4, 8),
            _ => (28, 0, 16, 20, 24),
        };
        if (count > (uint)((table.Length - 4) / size)) return null;
        var rows = new List<Socket>((int)count);
        for (int i = 0; i < count; i++)
        {
            var row = table.Slice(4 + i * size, size);
            IPAddress host = v6
                ? new IPAddress(row.Slice(address, 16), BinaryPrimitives.ReadUInt32LittleEndian(row[scope..]))
                : new IPAddress(row.Slice(address, 4));
            rows.Add(new Socket(protocol, host.ToString(), PortOf(BinaryPrimitives.ReadUInt32LittleEndian(row[port..])), (int)BinaryPrimitives.ReadUInt32LittleEndian(row[pid..])));
        }
        return rows;
    }

    /// <summary>One row per protocol, address, port and program, by port then program.</summary>
    public static List<PortEntry> Arrange(IEnumerable<PortEntry> entries) =>
        entries.DistinctBy(e => (e.Protocol, e.Host, e.Port, e.Pid))
            .OrderBy(e => e.Port).ThenBy(e => e.Protocol).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Host, StringComparer.Ordinal)
            .ToList();

    /// <summary>The rows matching every word of the query in port, program, PID or address; UDP only when asked.</summary>
    public static IEnumerable<PortEntry> Filter(IEnumerable<PortEntry> entries, string query, bool udp)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return entries.Where(e => (udp || e.Protocol == PortProtocol.Tcp)
            && words.All(w => $"{e.Port} {e.Name} {e.Pid} {e.Endpoint}".Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Reads the tables now. Takes a moment (every process is opened once): call off the UI thread.</summary>
    public static PortSnapshot Snapshot()
    {
        // Start times of every process before the tables and of each listed one after: a row's
        // program may be ended only when both agree, so a reused id never inherits the row.
        var before = new Dictionary<int, long?>();
        foreach (var p in Process.GetProcesses())
        {
            using (p) before[p.Id] = StartTime((uint)p.Id);
        }
        var sockets = new List<Socket>();
        bool complete = true;
        foreach (var (protocol, v6) in new[] { (PortProtocol.Tcp, false), (PortProtocol.Tcp, true), (PortProtocol.Udp, false), (PortProtocol.Udp, true) })
        {
            var rows = Read(protocol, v6);
            if (rows is null) complete = false;
            else sockets.AddRange(rows);
        }
        var windows = WindowOwners();
        var programs = new Dictionary<int, (string Name, long? Started, CloseBlock Block)>();
        foreach (int pid in sockets.Select(s => s.Pid).Distinct())
        {
            long? after = StartTime((uint)pid);
            long? started = before.GetValueOrDefault(pid) is { } first && first == after ? first : null;
            programs[pid] = (NameOf(pid), started, BlockOf(pid, started, readable: after is not null));
        }
        var entries = sockets.Select(s =>
        {
            var program = programs[s.Pid];
            return new PortEntry(s.Protocol, s.Host, s.Port, s.Pid, program.Name, program.Started, windows.Contains((uint)s.Pid), program.Block);
        });
        return new PortSnapshot(Arrange(entries), complete);
    }

    /// <summary>
    /// Ends the row's program: asks each of its windows to close, or, with <paramref name="force"/>,
    /// terminates it. The process is opened once and checked to be the one listed before anything
    /// is sent; that handle keeps its id from being reused meanwhile. Waits up to 3 seconds.
    /// </summary>
    public static CloseResult Close(PortEntry entry, bool force)
    {
        if (entry.Block != CloseBlock.None || entry.StartedAt is null) return CloseResult.Denied;
        var handle = PInvoke.OpenProcess(
            PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE | PROCESS_ACCESS_RIGHTS.PROCESS_SYNCHRONIZE | PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false, (uint)entry.Pid);
        if (handle.IsNull) return Marshal.GetLastPInvokeError() == ErrorInvalidParameter ? CloseResult.Gone : CloseResult.Denied;
        try
        {
            if (StartTime(handle) != entry.StartedAt) return CloseResult.Changed;
            if (!IsProcessCritical(handle, out bool critical) || critical) return CloseResult.Denied;
            if (force)
            {
                if (!PInvoke.TerminateProcess(handle, 1)) return CloseResult.Failed;
            }
            else
            {
                int found = 0, asked = 0;
                foreach (var window in WindowsOf((uint)entry.Pid))
                {
                    // The window may have closed and its handle gone to another program since it was listed.
                    if (OwnerOf(window) != (uint)entry.Pid) continue;
                    found++;
                    if (PInvoke.PostMessage(window, PInvoke.WM_CLOSE, default, default)) asked++;
                }
                if (found == 0) return CloseResult.NoWindow;
                if (asked == 0) return CloseResult.Failed;
            }
            return PInvoke.WaitForSingleObject(handle, 3000) == WAIT_EVENT.WAIT_OBJECT_0 ? CloseResult.Closed : CloseResult.StillRunning;
        }
        finally
        {
            PInvoke.CloseHandle(handle);
        }
    }

    private static unsafe List<Socket>? Read(PortProtocol protocol, bool v6)
    {
        int family = v6 ? AfInet6 : AfInet;
        uint size = 16 * 1024;
        // The table can grow between the size query and the read: try a few times.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var buffer = new byte[size];
            uint result;
            fixed (byte* p = buffer)
                result = protocol == PortProtocol.Tcp
                    ? GetExtendedTcpTable(p, ref size, false, family, TcpTableOwnerPidListener, 0)
                    : GetExtendedUdpTable(p, ref size, false, family, UdpTableOwnerPid, 0);
            if (result == 0) return Parse(buffer.AsSpan(0, (int)Math.Min(size, (uint)buffer.Length)), protocol, v6);
            if (result != ErrorInsufficientBuffer) break;
            size += 4096;
        }
        Log.Warn($"Port table could not be read ({protocol}, {(v6 ? "IPv6" : "IPv4")})");
        return null;
    }

    private static long? StartTime(uint pid)
    {
        var handle = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle.IsNull) return null;
        try
        {
            return StartTime(handle);
        }
        finally
        {
            PInvoke.CloseHandle(handle);
        }
    }

    private static long? StartTime(HANDLE process) =>
        GetProcessTimes(process, out long created, out _, out _, out _) ? created : null;

    private static string NameOf(int pid)
    {
        if (pid == 4) return "System";
        if (Processes.ImagePath((uint)pid) is { } path) return Path.GetFileNameWithoutExtension(path);
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return "";
        }
    }

    private static CloseBlock BlockOf(int pid, long? started, bool readable)
    {
        if (pid is 0 or 4) return CloseBlock.System;
        if (pid == Environment.ProcessId) return CloseBlock.Self;
        // Not even its start time opens to Orbit: elevated, a service or another user's.
        if (!readable) return CloseBlock.Denied;
        if (started is null) return CloseBlock.Unverified;
        if (CoreNames.Contains(NameOf(pid))) return CloseBlock.System;
        // The rights the close itself asks for, so the button and the close agree.
        var handle = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE | PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle.IsNull) return CloseBlock.Denied;
        try
        {
            return !IsProcessCritical(handle, out bool critical) || critical ? CloseBlock.System : CloseBlock.None;
        }
        finally
        {
            PInvoke.CloseHandle(handle);
        }
    }

    /// <summary>The processes with a visible top-level window, as the taskbar would show them.</summary>
    private static HashSet<uint> WindowOwners()
    {
        var owners = new HashSet<uint>();
        PInvoke.EnumWindows((window, _) =>
        {
            if (IsMainWindow(window)) owners.Add(OwnerOf(window));
            return true;
        }, default);
        return owners;
    }

    private static List<HWND> WindowsOf(uint pid)
    {
        var windows = new List<HWND>();
        PInvoke.EnumWindows((window, _) =>
        {
            if (IsMainWindow(window) && OwnerOf(window) == pid) windows.Add(window);
            return true;
        }, default);
        return windows;
    }

    private static bool IsMainWindow(HWND window) =>
        PInvoke.IsWindowVisible(window) && PInvoke.GetWindow(window, Windows.Win32.UI.WindowsAndMessaging.GET_WINDOW_CMD.GW_OWNER).IsNull;

    private static unsafe uint OwnerOf(HWND window)
    {
        uint pid = 0;
        PInvoke.GetWindowThreadProcessId(window, &pid);
        return pid;
    }

    // Declared here: CsWin32 offers these two only over SafeHandles.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(HANDLE process, out long created, out long exited, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessCritical(HANDLE process, out bool critical);

    [DllImport("iphlpapi.dll")]
    private static extern unsafe uint GetExtendedTcpTable(byte* table, ref uint size, bool order, int family, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll")]
    private static extern unsafe uint GetExtendedUdpTable(byte* table, ref uint size, bool order, int family, int tableClass, uint reserved);
}
