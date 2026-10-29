using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.System.Com;

namespace Orbit.Core.Audio;

/// <summary>
/// One program's sound on the default output: every audio session of the same program file as
/// one row (a browser opens several). Volume is 0–1 (Windows has no boost; Orbit's 0–200 % is not
/// ported). <see cref="Peak"/> is the loudest of its sessions right now.
/// </summary>
public sealed record AudioApp(string Key, string Name, string? ExePath, float Volume, bool Muted, float Peak, bool Active, bool IsSystemSounds);

/// <summary>
/// Per-app volume and mute (Orbit: AudioMixer.swift, the volume and mute parts) through Windows'
/// audio sessions (IAudioSessionManager2, ISimpleAudioVolume, IAudioMeterInformation), and the
/// output's own volume (IAudioEndpointVolume). Windows keeps each program's level itself, so
/// nothing is stored. Use from one thread.
/// </summary>
public sealed class AudioMixer
{
    private static readonly Guid SessionManagerId = typeof(IAudioSessionManager2).GUID;
    private static readonly Guid EndpointVolumeId = typeof(IAudioEndpointVolume).GUID;

    /// <summary>Program keys by process id, so the 10-a-second level reads skip the path lookups.</summary>
    private readonly Dictionary<uint, string> _keys = [];

    /// <summary>A program is its full path, lowercase (two programs with one file name stay apart); one Windows won't name is its own.</summary>
    private string KeyOf(uint pid, bool system)
    {
        if (system) return "";
        if (!_keys.TryGetValue(pid, out var key)) _keys[pid] = key = Processes.ImagePath(pid)?.ToLowerInvariant() ?? $"pid:{pid}";
        return key;
    }

    /// <summary>Each program's loudest session right now.</summary>
    public IReadOnlyDictionary<string, float> Peaks()
    {
        var peaks = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var sessions = Sessions(out var owned);
        try
        {
            foreach (var (control, pid, system, _) in sessions)
            {
                if (control is not IAudioMeterInformation meter) continue;
                meter.GetPeakValue(out float p);
                var key = KeyOf(pid, system);
                peaks[key] = Math.Max(peaks.GetValueOrDefault(key), p);
            }
        }
        finally
        {
            Release(owned);
        }
        return peaks;
    }

    /// <summary>The programs with sound sessions now, the system sounds first, then by name.</summary>
    public IReadOnlyList<AudioApp> Apps()
    {
        _keys.Clear();
        var groups = new Dictionary<string, List<(IAudioSessionControl2 Control, uint Pid, bool System, bool Active)>>(StringComparer.OrdinalIgnoreCase);
        var paths = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<AudioApp>();
        var sessionList = Sessions(out var owned);
        try
        {
            foreach (var session in sessionList)
            {
                var key = KeyOf(session.Pid, session.System);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
                list.Add(session);
                if (!session.System) paths[key] = key.StartsWith("pid:", StringComparison.Ordinal) ? null : Processes.ImagePath(session.Pid);
            }
            foreach (var (key, sessions) in groups)
            {
                float volume = 0, peak = 0;
                bool muted = true, active = false;
                foreach (var (control, _, _, isActive) in sessions)
                {
                    var simple = (ISimpleAudioVolume)control;
                    simple.GetMasterVolume(out float v);
                    simple.GetMute(out BOOL m);
                    volume = Math.Max(volume, v);
                    muted &= m;
                    active |= isActive;
                    if (control is IAudioMeterInformation meter)
                    {
                        meter.GetPeakValue(out float p);
                        peak = Math.Max(peak, p);
                    }
                }
                var path = paths.GetValueOrDefault(key);
                apps.Add(new AudioApp(key, key.Length == 0 ? "" : path is null ? key : Processes.DisplayName(path), path, volume, muted, peak, active, key.Length == 0));
            }
        }
        finally
        {
            Release(owned);
        }
        return apps.OrderByDescending(a => a.IsSystemSounds).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Sets every session of the program; false when it has none now.</summary>
    public bool SetVolume(string key, float volume) => ForEach(key, simple =>
    {
        var none = Guid.Empty;
        unsafe { simple.SetMasterVolume(Math.Clamp(volume, 0, 1), &none); }
    });

    public bool SetMuted(string key, bool muted) => ForEach(key, simple =>
    {
        var none = Guid.Empty;
        unsafe { simple.SetMute(muted, &none); }
    });

    /// <summary>The default output's volume (0–1) and mute; null without an output device.</summary>
    public (float Volume, bool Muted)? Master()
    {
        if (Endpoint() is not { } endpoint) return null;
        try
        {
            endpoint.GetMasterVolumeLevelScalar(out float v);
            endpoint.GetMute(out BOOL m);
            return (v, m);
        }
        finally
        {
            Release([endpoint]);
        }
    }

    public void SetMaster(float volume)
    {
        WithEndpoint(endpoint =>
        {
            var none = Guid.Empty;
            unsafe { endpoint.SetMasterVolumeLevelScalar(Math.Clamp(volume, 0, 1), &none); }
        });
    }

    public void SetMasterMuted(bool muted)
    {
        WithEndpoint(endpoint =>
        {
            var none = Guid.Empty;
            unsafe { endpoint.SetMute(muted, &none); }
        });
    }

    private bool ForEach(string key, Action<ISimpleAudioVolume> change)
    {
        bool any = false;
        var sessions = Sessions(out var owned);
        try
        {
            foreach (var session in sessions)
            {
                if (!string.Equals(KeyOf(session.Pid, session.System), key, StringComparison.OrdinalIgnoreCase)) continue;
                change((ISimpleAudioVolume)session.Control);
                any = true;
            }
        }
        finally
        {
            Release(owned);
        }
        return any;
    }

    private static IMMDevice? Output()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
            return device;
        }
        catch (COMException)
        {
            // No output device (all unplugged or disabled).
            return null;
        }
    }

    private static void WithEndpoint(Action<IAudioEndpointVolume> change)
    {
        if (Endpoint() is not { } endpoint) return;
        try
        {
            change(endpoint);
        }
        finally
        {
            Release([endpoint]);
        }
    }

    private static unsafe IAudioEndpointVolume? Endpoint()
    {
        if (Output() is not { } device) return null;
        try
        {
            device.Activate(EndpointVolumeId, CLSCTX.CLSCTX_ALL, null, out var obj);
            return obj as IAudioEndpointVolume;
        }
        finally
        {
            Release([device]);
        }
    }

    /// <summary>
    /// Every live session (expired ones left out) on the default output. <paramref name="owned"/>
    /// holds every COM object made on the way, sessions included, for <see cref="Release"/> once
    /// the caller is done: read 10 times a second, they mustn't wait for the garbage collector.
    /// </summary>
    private static unsafe List<(IAudioSessionControl2 Control, uint Pid, bool System, bool Active)> Sessions(out List<object> owned)
    {
        owned = [];
        var found = new List<(IAudioSessionControl2, uint, bool, bool)>();
        if (Output() is not { } device) return found;
        owned.Add(device);
        device.Activate(SessionManagerId, CLSCTX.CLSCTX_ALL, null, out var obj);
        if (obj is not IAudioSessionManager2 manager) return found;
        owned.Add(manager);
        var sessions = manager.GetSessionEnumerator();
        owned.Add(sessions);
        sessions.GetCount(out int count);
        for (int i = 0; i < count; i++)
        {
            sessions.GetSession(i, out var session);
            owned.Add(session);
            if (session is not IAudioSessionControl2 control) continue;
            control.GetState(out var state);
            if (state == AudioSessionState.AudioSessionStateExpired) continue;
            control.GetProcessId(out uint pid);
            bool system = control.IsSystemSoundsSession() == HRESULT.S_OK;
            found.Add((control, pid, system, state == AudioSessionState.AudioSessionStateActive));
        }
        return found;
    }

    /// <summary>Lets go of COM objects at once; each runtime wrapper once, whichever interface it was seen as.</summary>
    private static void Release(IEnumerable<object> objects)
    {
        foreach (var o in objects.Distinct(ReferenceEqualityComparer.Instance))
            if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
    }
}
