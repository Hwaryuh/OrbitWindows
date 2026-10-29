using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.Power;
using Windows.Win32.System.Threading;

namespace Orbit.Core.Power;

public enum AwakeKind { Indefinite, Until, WhileApp }

/// <summary>
/// A keep-awake session (Orbit: KeepAwake.swift <c>Session</c>). A duration and a time of day are
/// both an instant in UTC, so a clock or time zone change doesn't move the end.
/// </summary>
public sealed record AwakeSession(AwakeKind Kind, DateTime? UntilUtc = null, string? App = null, string? AppName = null)
{
    public static readonly AwakeSession Indefinite = new(AwakeKind.Indefinite);

    public static AwakeSession For(TimeSpan span, DateTime nowUtc) => new(AwakeKind.Until, nowUtc + span);

    /// <summary>The next time the clock shows <paramref name="time"/> (today, or tomorrow when it has passed), in UTC.</summary>
    public static AwakeSession UntilTime(TimeOnly time, DateTime nowLocal, TimeZoneInfo zone)
    {
        var day = DateOnly.FromDateTime(nowLocal);
        var at = day.ToDateTime(time);
        if (at <= nowLocal) at = at.AddDays(1);
        // A time skipped by daylight saving moves on by the gap (02:45 in a 02:00 to 03:00 jump is
        // 03:45); a repeated one reads as standard time, as TimeZoneInfo does.
        if (zone.IsInvalidTime(at)) at += zone.GetUtcOffset(at.AddHours(6)) - zone.GetUtcOffset(at.AddHours(-6));
        return new AwakeSession(AwakeKind.Until, TimeZoneInfo.ConvertTimeToUtc(at, zone));
    }

    /// <summary>While a program runs: <paramref name="exe"/> is its file name, lowercase.</summary>
    public static AwakeSession While(string exe, string name) => new(AwakeKind.WhileApp, App: exe.ToLowerInvariant(), AppName: name);

    /// <summary>As stored in awake.resume.</summary>
    public string Save() => Kind switch
    {
        AwakeKind.Until => $"until|{UntilUtc!.Value.Ticks.ToString(CultureInfo.InvariantCulture)}",
        AwakeKind.WhileApp => $"app|{App}|{AppName}",
        _ => "indefinite",
    };

    /// <summary>A stored session that should still run now; null for one that is over or unreadable.</summary>
    public static AwakeSession? Resume(string? stored, DateTime nowUtc, Func<string, bool> running)
    {
        var parts = (stored ?? "").Split('|');
        switch (parts[0])
        {
            case "indefinite":
                return Indefinite;
            case "until" when parts.Length == 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)
                && ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks:
                var until = new DateTime(ticks, DateTimeKind.Utc);
                // A few seconds left is not worth starting for.
                return until > nowUtc.AddSeconds(5) ? new AwakeSession(AwakeKind.Until, until) : null;
            case "app" when parts.Length == 3 && running(parts[1]):
                return new AwakeSession(AwakeKind.WhileApp, App: parts[1], AppName: parts[2]);
            default:
                return null;
        }
    }
}

public enum AwakeStop { None, Finished, AppQuit, BatteryLow }

/// <summary>The battery as Windows reports it; Percent is null when unknown or there is no battery.</summary>
public readonly record struct BatteryState(bool OnAc, int? Percent);

public static class AwakeRules
{
    /// <summary>Why a running session should end now, if it should (Orbit: <c>check</c>; the thermal stop has no Windows counterpart).</summary>
    public static AwakeStop Check(AwakeSession session, DateTime nowUtc, Func<string, bool> running, BatteryState battery, int batteryFloor)
    {
        if (session.UntilUtc is { } until && until <= nowUtc) return AwakeStop.Finished;
        if (session.Kind == AwakeKind.WhileApp && session.App is { } app && !running(app)) return AwakeStop.AppQuit;
        if (batteryFloor > 0 && !battery.OnAc && battery.Percent is { } percent && percent <= batteryFloor) return AwakeStop.BatteryLow;
        return AwakeStop.None;
    }

    /// <summary>"2h", "1h 05m", "15m", "40s", as Orbit writes a length.</summary>
    public static string Format(TimeSpan span)
    {
        int s = (int)Math.Round(span.TotalSeconds);
        if (s >= 3600) return s % 3600 / 60 == 0 ? $"{s / 3600}h" : $"{s / 3600}h {s % 3600 / 60:00}m";
        if (s >= 60) return $"{s / 60}m";
        return $"{s}s";
    }
}

/// <summary>
/// One power request held for a session (PowerCreateRequest): the system stays awake, and the
/// display too when asked. Windows drops it with the process, so a crash never leaves it behind.
/// Lid close and the power button still sleep.
/// </summary>
public sealed class PowerRequest : IDisposable
{
    private readonly SafeFileHandle _handle;
    private bool _display;

    private PowerRequest(SafeFileHandle handle) => _handle = handle;

    /// <summary>A held request, or null when Windows refused it.</summary>
    public static unsafe PowerRequest? Hold(string reason, bool display)
    {
        fixed (char* text = reason)
        {
            var context = new REASON_CONTEXT { Version = 0 /* POWER_REQUEST_CONTEXT_VERSION */, Flags = POWER_REQUEST_CONTEXT_FLAGS.POWER_REQUEST_CONTEXT_SIMPLE_STRING };
            context.Reason.SimpleReasonString = text;
            var handle = PInvoke.PowerCreateRequest(context);
            if (handle.IsInvalid)
            {
                Log.Warn($"PowerCreateRequest failed ({Marshal.GetLastWin32Error()})");
                return null;
            }
            var request = new PowerRequest(handle);
            if (!PInvoke.PowerSetRequest(handle, POWER_REQUEST_TYPE.PowerRequestSystemRequired) || !request.SetDisplay(display))
            {
                request.Dispose();
                return null;
            }
            return request;
        }
    }

    /// <summary>Keeps the display on, or lets it sleep, without letting go of the system.</summary>
    public bool SetDisplay(bool on)
    {
        if (on == _display) return true;
        bool ok = on
            ? PInvoke.PowerSetRequest(_handle, POWER_REQUEST_TYPE.PowerRequestDisplayRequired)
            : PInvoke.PowerClearRequest(_handle, POWER_REQUEST_TYPE.PowerRequestDisplayRequired);
        if (ok) _display = on;
        else Log.Warn($"Changing the display request failed ({Marshal.GetLastWin32Error()})");
        return ok;
    }

    public void Dispose()
    {
        if (_handle.IsClosed) return;
        PInvoke.PowerClearRequest(_handle, POWER_REQUEST_TYPE.PowerRequestSystemRequired);
        if (_display) PInvoke.PowerClearRequest(_handle, POWER_REQUEST_TYPE.PowerRequestDisplayRequired);
        _handle.Dispose();
    }

    public static BatteryState Battery()
    {
        if (!PInvoke.GetSystemPowerStatus(out var status)) return new BatteryState(true, null);
        bool onAc = status.ACLineStatus != 0;
        // 255: unknown; flag 128: no system battery.
        int? percent = status.BatteryLifePercent == 255 || (status.BatteryFlag & 128) != 0 ? null : status.BatteryLifePercent;
        return new BatteryState(onAc, percent);
    }
}
