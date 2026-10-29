using System.Management;
using System.Runtime.InteropServices;
using Orbit.Core.Display;

namespace Orbit.Core.Displays;

public enum DisplayKind
{
    /// <summary>The laptop's own panel, set through Windows (WMI).</summary>
    BuiltIn,
    /// <summary>A monitor that answered over DDC/CI.</summary>
    Ddc,
    /// <summary>Nothing Orbit can set: the monitor didn't answer, or the panel has no brightness control.</summary>
    NoControl,
    /// <summary>One screen shown on several monitors: which one a setting reaches can't be told, so none is set.</summary>
    Duplicated,
}

/// <summary>One screen and its levels (0–1), as the UI sees them. Changed only on the UI thread.</summary>
public sealed class ManagedDisplay
{
    internal ManagedDisplay(string key, string name, nint monitor, bool isPrimary, bool isBuiltIn, DisplayKind kind, double brightness, double? contrast, double? volume, bool? muted)
    {
        Key = key;
        Name = name;
        Monitor = monitor;
        IsPrimary = isPrimary;
        IsBuiltIn = isBuiltIn;
        Kind = kind;
        Brightness = brightness;
        Contrast = contrast;
        Volume = volume;
        Muted = muted;
    }

    /// <summary>The monitor's device path: the same across restarts and reconnects to the same port.</summary>
    public string Key { get; }

    /// <summary>The name Windows' Settings shows; empty when Windows has none.</summary>
    public string Name { get; }

    public nint Monitor { get; }
    public bool IsPrimary { get; }
    public bool IsBuiltIn { get; }
    public DisplayKind Kind { get; }
    public double Brightness { get; internal set; }

    /// <summary>Null when the monitor didn't say.</summary>
    public double? Contrast { get; internal set; }

    public double? Volume { get; internal set; }
    public bool? Muted { get; internal set; }

    /// <summary>Three writes in a row failed: unplugged, asleep or no longer listening. Ask again to retry.</summary>
    public bool Unreachable { get; internal set; }

    public bool CanSetBrightness => Kind is DisplayKind.BuiltIn or DisplayKind.Ddc && !Unreachable;

    /// <summary>The probe this came from; a change made through an older one is dropped.</summary>
    internal int Generation { get; init; }
}

/// <summary>
/// Brightness, contrast and volume for every screen (Orbit: Modules/Displays/Displays.swift,
/// MonitorControl in Orbit): the laptop's panel through WMI, other monitors over DDC/CI. Software
/// dimming, input switching and standby are not in this version.
/// </summary>
/// <remarks>
/// One thread owns every monitor handle and makes every DDC and WMI call: they are slow (tens of
/// milliseconds each) and a monitor must not be talked to from two threads. A slider sends a
/// value per frame; only the latest per screen and setting waits, and each screen gets at most one
/// write every 40 ms. Asking again drops what still waits and the old handles, then probes.
/// </remarks>
public sealed class DisplayManager : IDisposable
{
    private const byte Brightness = 0x10, Contrast = 0x12, Volume = 0x62, Mute = 0x8D;
    private const int WriteSpacingMs = 40, FailuresBeforeUnreachable = 3;

    private readonly SynchronizationContext? _ui;
    private readonly object _gate = new();
    private readonly Dictionary<(string Key, byte Code), double> _pending = [];
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _worker;
    private TaskCompletionSource? _refresh;
    private int _generation;
    private bool _stopping;

    // The worker's own.
    private readonly Dictionary<string, Target> _targets = [];
    private int _probeGeneration;
    private bool _stopped;

    /// <param name="ui">Where results and <see cref="Changed"/> arrive; the creating thread's context by default.</param>
    public DisplayManager(SynchronizationContext? ui = null)
    {
        _ui = ui ?? SynchronizationContext.Current;
        _worker = new Thread(Work) { IsBackground = true, Name = "Orbit displays" };
        _worker.Start();
    }

    public IReadOnlyList<ManagedDisplay> Displays { get; private set; } = [];

    /// <summary>On the UI thread, after a refresh or when a screen stops answering.</summary>
    public event Action? Changed;

    /// <summary>Finds the screens and asks each what it supports; reading only. Completes on the UI thread after <see cref="Changed"/>.</summary>
    public Task Refresh()
    {
        Task task;
        lock (_gate)
        {
            if (_stopping) return Task.CompletedTask;
            // What waits was meant for the screens as they were; the probe decides anew.
            _generation++;
            _pending.Clear();
            _refresh ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            task = _refresh.Task;
        }
        _wake.Set();
        return task;
    }

    public ManagedDisplay? Find(nint monitor) => Displays.FirstOrDefault(d => d.Monitor == monitor);

    /// <summary>A screen's brightness changed through Orbit (a slider, an action, a preset). On the UI thread.</summary>
    public event Action<ManagedDisplay>? BrightnessChanged;

    public void SetBrightness(ManagedDisplay display, double level)
    {
        if (!display.CanSetBrightness || !Queue(display, Brightness, Math.Clamp(level, 0, 1))) return;
        display.Brightness = Math.Clamp(level, 0, 1);
        BrightnessChanged?.Invoke(display);
    }

    public void SetContrast(ManagedDisplay display, double level)
    {
        if (display.Contrast is null || display.Kind != DisplayKind.Ddc || display.Unreachable) return;
        if (Queue(display, Contrast, Math.Clamp(level, 0, 1))) display.Contrast = Math.Clamp(level, 0, 1);
    }

    public void SetVolume(ManagedDisplay display, double level)
    {
        if (display.Volume is null || display.Kind != DisplayKind.Ddc || display.Unreachable) return;
        if (!Queue(display, Volume, Math.Clamp(level, 0, 1))) return;
        display.Volume = Math.Clamp(level, 0, 1);
        // As Orbit (and Windows' own volume): turning it up unmutes.
        if (display.Muted == true && level > 0) SetMuted(display, false);
    }

    public void SetMuted(ManagedDisplay display, bool muted)
    {
        if (display.Muted is null || display.Kind != DisplayKind.Ddc || display.Unreachable) return;
        if (Queue(display, Mute, muted ? 1 : 0)) display.Muted = muted;
    }

    /// <summary>False when the display is from before the last refresh, or Orbit is closing.</summary>
    private bool Queue(ManagedDisplay display, byte code, double value)
    {
        lock (_gate)
        {
            if (_stopping || display.Generation != _generation) return false;
            _pending[(display.Key, code)] = value;
        }
        _wake.Set();
        return true;
    }

    private void Work()
    {
        while (true)
        {
            _wake.WaitOne();
            while (true)
            {
                TaskCompletionSource? refresh;
                (string Key, byte Code, double Value)? job = null;
                lock (_gate)
                {
                    if (_stopping)
                    {
                        _refresh?.TrySetResult();
                        _refresh = null;
                    }
                    refresh = _refresh;
                    _refresh = null;
                    _probeGeneration = _generation;
                    if (refresh is null && _pending.Count > 0)
                    {
                        var (key, value) = _pending.First();
                        _pending.Remove(key);
                        job = (key.Key, key.Code, value);
                    }
                }
                if (Volatile.Read(ref _stopped))
                {
                    Release();
                    return;
                }
                if (refresh is not null)
                {
                    List<ManagedDisplay> found;
                    try
                    {
                        found = Probe();
                    }
                    catch (Exception e)
                    {
                        Log.Error("Display probe failed", e);
                        found = [];
                    }
                    OnUi(() =>
                    {
                        Displays = found;
                        Changed?.Invoke();
                        refresh.TrySetResult();
                    });
                    continue;
                }
                if (job is not { } j) break;
                Write(j.Key, j.Code, j.Value);
            }
        }
    }

    private void OnUi(Action action)
    {
        if (_ui is null) action();
        else _ui.Post(_ => action(), null);
    }

    private sealed class Target(nint physical, ManagementObject? panel, IReadOnlyList<byte> levels)
    {
        public nint Physical { get; } = physical;
        public ManagementObject? Panel { get; } = panel;
        public IReadOnlyList<byte> Levels { get; } = levels;
        public uint BrightnessMax { get; set; }
        public uint ContrastMax { get; set; }
        public uint VolumeMax { get; set; }
        public long LastWrite { get; set; }
        public int Failures { get; set; }
    }

    private void Write(string key, byte code, double value)
    {
        if (!_targets.TryGetValue(key, out var target)) return;
        long wait = target.LastWrite + WriteSpacingMs - Environment.TickCount64;
        if (wait > 0) Thread.Sleep((int)wait);
        bool ok;
        try
        {
            ok = target.Panel is { } panel
                ? code == Brightness && SetPanel(panel, DisplayLevels.Nearest(value, target.Levels))
                : SetVCPFeature(target.Physical, code, code switch
                {
                    Brightness => DisplayLevels.ToRaw(value, target.BrightnessMax),
                    Contrast => DisplayLevels.ToRaw(value, target.ContrastMax),
                    Volume => DisplayLevels.ToRaw(value, target.VolumeMax),
                    // MCCS: 1 mutes, 2 unmutes.
                    _ => value > 0.5 ? 1u : 2u,
                });
        }
        catch (Exception e) when (e is ManagementException or COMException)
        {
            Log.Warn($"Display write failed ({e.GetType().Name})");
            ok = false;
        }
        target.LastWrite = Environment.TickCount64;
        if (ok)
        {
            target.Failures = 0;
            return;
        }
        if (++target.Failures != FailuresBeforeUnreachable) return;
        Log.Warn($"Display stopped answering (code 0x{code:X2})");
        OnUi(() =>
        {
            if (Displays.FirstOrDefault(d => d.Key == key) is not { } display) return;
            display.Unreachable = true;
            Changed?.Invoke();
        });
    }

    private static bool SetPanel(ManagementObject panel, byte percent)
    {
        // WmiSetBrightness returns nothing: a failure comes back as an exception.
        panel.InvokeMethod("WmiSetBrightness", [1u, percent]);
        return true;
    }

    private List<ManagedDisplay> Probe()
    {
        Release();
        var paths = DisplayPaths.Read();
        var panels = Panels();
        var found = new List<ManagedDisplay>();
        foreach (var monitor in Monitors.All())
        {
            var targets = paths.GetValueOrDefault(monitor.DeviceName) ?? [];
            var first = targets.FirstOrDefault();
            string key = first?.DevicePath is { Length: > 0 } path ? path : monitor.DeviceName;
            string name = first?.FriendlyName ?? "";
            bool builtIn = first?.IsBuiltIn ?? false;
            ManagedDisplay Make(DisplayKind kind, double brightness = 1, double? contrast = null, double? volume = null, bool? muted = null) =>
                new(key, name, monitor.Handle, monitor.IsPrimary, builtIn, kind, brightness, contrast, volume, muted) { Generation = _probeGeneration };

            if (targets.Count > 1)
            {
                found.Add(Make(DisplayKind.Duplicated));
                continue;
            }
            if (builtIn)
            {
                // Only a panel whose WMI instance is this monitor's own device: never a guess.
                var matches = panels.Where(p => DisplayLevels.HardwareKey(p.Instance) == DisplayLevels.HardwareKey(key)).ToList();
                if (matches.Count == 1 && MethodsFor(matches[0].Instance) is { } methods)
                {
                    _targets[key] = new Target(0, methods, matches[0].Levels);
                    found.Add(Make(DisplayKind.BuiltIn, matches[0].Current / 100.0));
                }
                else found.Add(Make(DisplayKind.NoControl));
                continue;
            }
            found.Add(ProbeDdc(monitor.Handle, key, Make));
        }
        Log.Info($"Displays: {string.Join(", ", found.Select(d => $"{d.Name} {d.Kind}"))}");
        return found;
    }

    private ManagedDisplay ProbeDdc(nint monitor, string key, Func<DisplayKind, double, double?, double?, bool?, ManagedDisplay> make)
    {
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count) || count != 1)
            return make(count > 1 ? DisplayKind.Duplicated : DisplayKind.NoControl, 1, null, null, null);
        var physical = new PhysicalMonitor[1];
        if (!GetPhysicalMonitorsFromHMONITOR(monitor, 1, physical)) return make(DisplayKind.NoControl, 1, null, null, null);
        nint handle = physical[0].Handle;
        var brightness = Read(handle, Brightness);
        var contrast = Read(handle, Contrast);
        // A monitor that says neither won't say anything else.
        if (brightness is null || brightness.Value.Max == 0)
        {
            DestroyPhysicalMonitor(handle);
            return make(DisplayKind.NoControl, 1, null, null, null);
        }
        var volume = Read(handle, Volume);
        var mute = volume is { Max: > 0 } ? Read(handle, Mute) : null;
        var target = new Target(handle, null, [])
        {
            BrightnessMax = brightness.Value.Max,
            ContrastMax = contrast?.Max ?? 0,
            VolumeMax = volume?.Max ?? 0,
        };
        _targets[key] = target;
        return make(DisplayKind.Ddc,
            DisplayLevels.ToLevel(brightness.Value.Current, brightness.Value.Max) ?? 1,
            contrast is { } c ? DisplayLevels.ToLevel(c.Current, c.Max) : null,
            volume is { } v ? DisplayLevels.ToLevel(v.Current, v.Max) : null,
            mute is { } m ? m.Current == 1 : null);
    }

    private static (uint Current, uint Max)? Read(nint handle, byte code) =>
        GetVCPFeatureAndVCPFeatureReply(handle, code, out _, out uint current, out uint max) ? (current, max) : null;

    /// <summary>The laptop panels Windows can brighten, with their current percentage and the steps they take.</summary>
    private static List<(string Instance, byte Current, byte[] Levels)> Panels()
    {
        var list = new List<(string, byte, byte[])>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, CurrentBrightness, Level FROM WmiMonitorBrightness WHERE Active = TRUE");
            using var results = searcher.Get();
            foreach (ManagementObject panel in results)
                using (panel)
                    list.Add(((string)panel["InstanceName"], (byte)panel["CurrentBrightness"], panel["Level"] as byte[] ?? []));
        }
        catch (Exception e) when (e is ManagementException or COMException or InvalidCastException)
        {
            // Desktops have no such panel: WMI says "not supported".
        }
        return list;
    }

    private static ManagementObject? MethodsFor(string instance)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE");
            using var results = searcher.Get();
            ManagementObject? match = null;
            foreach (ManagementObject methods in results)
            {
                if (match is null && string.Equals(methods["InstanceName"] as string, instance, StringComparison.OrdinalIgnoreCase)) match = methods;
                else methods.Dispose();
            }
            return match;
        }
        catch (Exception e) when (e is ManagementException or COMException)
        {
            return null;
        }
    }

    /// <summary>Lets go of every monitor handle and WMI object. On the worker only.</summary>
    private void Release()
    {
        foreach (var target in _targets.Values)
        {
            if (target.Physical != 0) DestroyPhysicalMonitor(target.Physical);
            target.Panel?.Dispose();
        }
        _targets.Clear();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopping = true;
            _pending.Clear();
            _refresh?.TrySetResult();
            _refresh = null;
        }
        Volatile.Write(ref _stopped, true);
        _wake.Set();
        // A DDC call in progress can't be cut short; the worker releases the handles when it ends.
        _worker.Join(TimeSpan.FromSeconds(2));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public nint Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, [Out] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(nint monitor);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(nint monitor, byte code, out uint type, out uint current, out uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetVCPFeature(nint monitor, byte code, uint value);
}
