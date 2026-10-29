using System.Diagnostics;
using System.Net.NetworkInformation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.SystemInformation;
using Orbit.Core.Power;

namespace Orbit.Core.SystemInfo;

/// <summary>One reading of the PC (Orbit: Core/SystemInfo.swift). Percentages are 0–100; rates are bytes a second.</summary>
public sealed record SystemReading(
    double Cpu, double? Gpu, ulong MemoryUsed, ulong MemoryTotal, long DiskFree, long DiskTotal,
    double Download, double Upload, BatteryState Battery, TimeSpan Uptime);

/// <summary>A program by its share of the processors and its memory.</summary>
public sealed record ProcessReading(int Pid, string Name, double Cpu, long Memory);

/// <summary>The rolling numbers behind the System page: one sample each call, histories of the last 60.</summary>
public sealed class SystemSampler : IDisposable
{
    public const int HistoryLength = 60;

    private (ulong Idle, ulong Kernel, ulong User)? _lastCpu;
    private (long Rx, long Tx, long At)? _lastNet;
    private readonly Dictionary<int, (TimeSpan Cpu, long At)> _lastProcess = [];
    private GpuCounter? _gpu = GpuCounter.Open();

    public Queue<double> CpuHistory { get; } = new();
    public Queue<double> MemoryHistory { get; } = new();
    public Queue<double> DownloadHistory { get; } = new();

    public SystemReading Sample()
    {
        double cpu = SampleCpu();
        var (used, total) = Memory();
        var (free, size) = Disk();
        var (down, up) = Network();
        double? gpu = _gpu?.Read();
        Push(CpuHistory, cpu);
        Push(MemoryHistory, total == 0 ? 0 : 100.0 * used / total);
        Push(DownloadHistory, down);
        return new SystemReading(cpu, gpu, used, total, free, size, down, up, PowerRequest.Battery(), TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    private static void Push(Queue<double> history, double value)
    {
        history.Enqueue(value);
        while (history.Count > HistoryLength) history.Dequeue();
    }

    /// <summary>Busy share of all processors since the last call, from GetSystemTimes (kernel time includes idle).</summary>
    public static double CpuPercent((ulong Idle, ulong Kernel, ulong User) before, (ulong Idle, ulong Kernel, ulong User) now)
    {
        ulong idle = now.Idle - before.Idle, total = now.Kernel - before.Kernel + now.User - before.User;
        return total == 0 ? 0 : Math.Clamp(100.0 * (total - idle) / total, 0, 100);
    }

    private double SampleCpu()
    {
        if (!PInvoke.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var now = (Ticks(idle), Ticks(kernel), Ticks(user));
        double percent = _lastCpu is { } before ? CpuPercent(before, now) : 0;
        _lastCpu = now;
        return percent;
    }

    private static ulong Ticks(System.Runtime.InteropServices.ComTypes.FILETIME t) => ((ulong)(uint)t.dwHighDateTime << 32) | (uint)t.dwLowDateTime;

    private static unsafe (ulong Used, ulong Total) Memory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)sizeof(MEMORYSTATUSEX) };
        if (!PInvoke.GlobalMemoryStatusEx(ref status)) return (0, 0);
        return (status.ullTotalPhys - status.ullAvailPhys, status.ullTotalPhys);
    }

    /// <summary>The Windows drive.</summary>
    private static (long Free, long Total) Disk()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            return (drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (0, 0);
        }
    }

    /// <summary>Bytes a second over every connected interface but loopback, since the last call.</summary>
    private (double Down, double Up) Network()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var stats = nic.GetIPStatistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }
        }
        catch (NetworkInformationException)
        {
            return (0, 0);
        }
        long at = Environment.TickCount64;
        var rates = _lastNet is { } last && at > last.At ? RatesPerSecond(last, (rx, tx, at)) : (0, 0);
        _lastNet = (rx, tx, at);
        return rates;
    }

    /// <summary>Byte counters into rates; a counter that went back (an adapter reset) reads as 0.</summary>
    public static (double Down, double Up) RatesPerSecond((long Rx, long Tx, long At) before, (long Rx, long Tx, long At) now)
    {
        double seconds = (now.At - before.At) / 1000.0;
        if (seconds <= 0) return (0, 0);
        return (Math.Max(0, now.Rx - before.Rx) / seconds, Math.Max(0, now.Tx - before.Tx) / seconds);
    }

    /// <summary>The programs using the most processor time since the last call, then memory.</summary>
    public IReadOnlyList<ProcessReading> TopProcesses(int count)
    {
        long at = Environment.TickCount64;
        var readings = new List<ProcessReading>();
        var seen = new HashSet<int>();
        int cores = Environment.ProcessorCount;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == 0) continue;
                    var cpu = p.TotalProcessorTime;
                    seen.Add(p.Id);
                    double percent = 0;
                    if (_lastProcess.TryGetValue(p.Id, out var last) && at > last.At)
                        percent = Math.Clamp((cpu - last.Cpu).TotalMilliseconds / (at - last.At) / cores * 100, 0, 100);
                    _lastProcess[p.Id] = (cpu, at);
                    readings.Add(new ProcessReading(p.Id, p.ProcessName, percent, p.WorkingSet64));
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Protected processes don't tell their times.
                }
            }
        }
        foreach (var gone in _lastProcess.Keys.Where(k => !seen.Contains(k)).ToList()) _lastProcess.Remove(gone);
        return readings.OrderByDescending(r => r.Cpu).ThenByDescending(r => r.Memory).Take(count).ToList();
    }

    public void Dispose()
    {
        _gpu?.Dispose();
        _gpu = null;
    }

    /// <summary>
    /// GPU busy through the "GPU Engine" performance counters (as Task Manager reads it): the
    /// busiest engine type summed over processes, capped at 100. Null where the counters aren't there.
    /// </summary>
    private sealed class GpuCounter : IDisposable
    {
        private readonly nint _query;
        private readonly nint _counter;
        private bool _primed;

        private GpuCounter(nint query, nint counter)
        {
            _query = query;
            _counter = counter;
        }

        public static GpuCounter? Open()
        {
            if (PdhOpenQueryW(null, 0, out var query) != 0) return null;
            if (PdhAddEnglishCounterW(query, @"\GPU Engine(*)\Utilization Percentage", 0, out var counter) != 0)
            {
                PdhCloseQuery(query);
                return null;
            }
            return new GpuCounter(query, counter);
        }

        public unsafe double? Read()
        {
            if (PdhCollectQueryData(_query) != 0) return null;
            // A rate needs two collections: the first only primes it.
            if (!_primed)
            {
                _primed = true;
                return null;
            }
            uint size = 0;
            PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble, ref size, out _, null);
            if (size == 0) return null;
            var buffer = new byte[size];
            fixed (byte* items = buffer)
            {
                if (PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble, ref size, out uint count, items) != 0) return null;
                var byEngine = new Dictionary<string, double>();
                for (int i = 0; i < count; i++)
                {
                    // PDH_FMT_COUNTERVALUE_ITEM_W: the name pointer, then CStatus and padding, then the double.
                    byte* item = items + i * ItemSize;
                    var name = new string(*(char**)item);
                    // CStatus: PDH_CSTATUS_VALID_DATA (0) or NEW_DATA (1); anything else isn't a reading.
                    uint status = *(uint*)(item + sizeof(nint));
                    double value = *(double*)(item + 2 * sizeof(nint));
                    if (status > 1 || !double.IsFinite(value)) continue;
                    int at = name.IndexOf("engtype_", StringComparison.Ordinal);
                    if (at < 0) continue;
                    var engine = name[(at + 8)..];
                    byEngine[engine] = byEngine.GetValueOrDefault(engine) + value;
                }
                return byEngine.Count == 0 ? null : Math.Min(100, byEngine.Values.Max());
            }
        }

        private const uint PdhFmtDouble = 0x200;
        private static readonly int ItemSize = 2 * IntPtr.Size + sizeof(double);

        // Declared here: CsWin32's PDH wrappers want SafeHandles that PDH's handles are not.
        [System.Runtime.InteropServices.DllImport("pdh.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern uint PdhOpenQueryW(string? source, nint user, out nint query);

        [System.Runtime.InteropServices.DllImport("pdh.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounterW(nint query, string path, nint user, out nint counter);

        [System.Runtime.InteropServices.DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(nint query);

        [System.Runtime.InteropServices.DllImport("pdh.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern unsafe uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint size, out uint count, byte* items);

        [System.Runtime.InteropServices.DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(nint query);

        public void Dispose() => PdhCloseQuery(_query);
    }
}
