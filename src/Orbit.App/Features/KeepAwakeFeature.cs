using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.Power;
using Orbit.Core.Settings;

namespace Orbit.App.Features;

/// <summary>
/// Keep Awake (Orbit: Core/KeepAwake.swift): sessions without end, for a while, until a time or
/// while a program runs, held by one power request. Checked every 5 seconds; a low battery ends it.
/// A session that should still run comes back after Orbit restarts. Closed-lid mode and the
/// thermal stop are not in the Windows version.
/// </summary>
internal sealed class KeepAwakeFeature : IFeature
{

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(5) };
    private AppServices? _services;
    private PowerRequest? _request;

    public AwakeSession? Session { get; private set; }

    /// <summary>Started or stopped; the settings page shows it.</summary>
    public event Action? Changed;

    public void Start(AppServices services)
    {
        _services = services;
        services.KeepAwake = this;
        services.Actions.Register(OrbitAction.KeepAwake, Toggle);
        _tick.Tick += (_, _) => Check();
        services.Preferences.Changed += key =>
        {
            if (key == Pref.AwakeDisplay) _request?.SetDisplay(services.Preferences.Get<bool>(Pref.AwakeDisplay));
        };
        // Orbit went away without stopping (a crash, an update): pick up a session that should still run.
        if (AwakeSession.Resume(services.Preferences.Get<string>(Pref.AwakeResume), DateTime.UtcNow, Running) is { } resume) Begin(resume, announce: false);
        else services.Preferences.Reset(Pref.AwakeResume);
    }

    public void Dispose()
    {
        _tick.Stop();
        _request?.Dispose();
        _request = null;
    }

    public void Toggle()
    {
        if (Session is null) Begin(AwakeSession.Indefinite);
        else End(Strings.AwakeStopped);
    }

    public void Begin(AwakeSession session, bool announce = true)
    {
        var prefs = _services!.Preferences;
        // The new request first: if Windows refuses it, the old session keeps running.
        var request = PowerRequest.Hold("Orbit: Keep Awake", prefs.Get<bool>(Pref.AwakeDisplay));
        if (request is null)
        {
            Toast.Show(Strings.AwakeFailed);
            return;
        }
        _request?.Dispose();
        _request = request;
        Session = session;
        prefs.Set(Pref.AwakeResume, session.Save());
        _tick.Start();
        Log.Info($"Keep awake started: {session.Kind}");
        if (announce) Toast.Show(Strings.AwakeStarted, Describe(session));
        Changed?.Invoke();
    }

    public void End(string message)
    {
        if (Session is null) return;
        _tick.Stop();
        _request?.Dispose();
        _request = null;
        Session = null;
        _services!.Preferences.Reset(Pref.AwakeResume);
        Log.Info("Keep awake stopped");
        Toast.Show(message);
        Changed?.Invoke();
    }

    private void Check()
    {
        if (Session is not { } session) return;
        var battery = PowerRequest.Battery();
        switch (AwakeRules.Check(session, DateTime.UtcNow, Running, battery, _services!.Preferences.Get<int>(Pref.AwakeBatteryFloor)))
        {
            case AwakeStop.Finished:
                End(Strings.AwakeFinished);
                break;
            case AwakeStop.AppQuit:
                End(Strings.AwakeAppQuit(session.AppName ?? session.App ?? ""));
                break;
            case AwakeStop.BatteryLow:
                End(Strings.AwakeBatteryLow(battery.Percent ?? 0));
                break;
        }
    }

    /// <summary>Whether a program with this file name runs.</summary>
    public static bool Running(string exe)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    public static string Describe(AwakeSession session) => session.Kind switch
    {
        AwakeKind.Until => Strings.AwakeUntil(session.UntilUtc!.Value.ToLocalTime().ToString("t")),
        AwakeKind.WhileApp => Strings.AwakeWhile(session.AppName ?? session.App ?? ""),
        _ => Strings.AwakeIndefinite,
    };
}
