using Orbit.Core.Power;

namespace Orbit.Tests;

public class KeepAwakeTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void UntilTimeIsTodayOrTomorrow()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");
        var later = AwakeSession.UntilTime(new TimeOnly(23, 0), new DateTime(2026, 10, 30, 21, 0, 0), zone);
        Assert.Equal(new DateTime(2026, 10, 30, 14, 0, 0, DateTimeKind.Utc), later.UntilUtc);
        var tomorrow = AwakeSession.UntilTime(new TimeOnly(8, 0), new DateTime(2026, 10, 30, 21, 0, 0), zone);
        Assert.Equal(new DateTime(2026, 10, 30, 23, 0, 0, DateTimeKind.Utc), tomorrow.UntilUtc);
    }

    [Fact]
    public void ASkippedDaylightSavingTimeMovesOn()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 29),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 25));
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.Zero, "Test", "Test", "Test DST", [rule]);
        var s = AwakeSession.UntilTime(new TimeOnly(2, 30), new DateTime(2026, 3, 29, 1, 0, 0), zone);
        Assert.Equal(new DateTime(2026, 3, 29, 2, 30, 0, DateTimeKind.Utc), s.UntilUtc);
        Assert.Equal(new DateTime(2026, 3, 29, 3, 30, 0), TimeZoneInfo.ConvertTimeFromUtc(s.UntilUtc!.Value, zone));
    }

    [Fact]
    public void SessionsRoundTripAndStaleOnesDontResume()
    {
        var until = AwakeSession.For(TimeSpan.FromHours(2), Now);
        Assert.Equal(until, AwakeSession.Resume(until.Save(), Now, _ => true));
        Assert.Null(AwakeSession.Resume(until.Save(), Now.AddHours(3), _ => true));
        Assert.Equal(AwakeSession.Indefinite, AwakeSession.Resume("indefinite", Now, _ => false));
        var app = AwakeSession.While("Blender.exe", "Blender");
        Assert.Equal("blender.exe", app.App);
        Assert.Equal(app, AwakeSession.Resume(app.Save(), Now, exe => exe == "blender.exe"));
        Assert.Null(AwakeSession.Resume(app.Save(), Now, _ => false));
        Assert.Null(AwakeSession.Resume(null, Now, _ => true));
        Assert.Null(AwakeSession.Resume("until|x", Now, _ => true));
        Assert.Null(AwakeSession.Resume("until|9223372036854775807", Now, _ => true));
    }

    [Fact]
    public void StopsForTheEndTheAppAndALowBattery()
    {
        var full = new BatteryState(false, 80);
        Assert.Equal(AwakeStop.None, AwakeRules.Check(AwakeSession.Indefinite, Now, _ => true, full, 20));
        Assert.Equal(AwakeStop.Finished, AwakeRules.Check(AwakeSession.For(TimeSpan.FromMinutes(5), Now), Now.AddMinutes(5), _ => true, full, 20));
        Assert.Equal(AwakeStop.AppQuit, AwakeRules.Check(AwakeSession.While("x.exe", "X"), Now, _ => false, full, 20));
        Assert.Equal(AwakeStop.BatteryLow, AwakeRules.Check(AwakeSession.Indefinite, Now, _ => true, new BatteryState(false, 20), 20));
        // On power, with the floor off, or with no battery: never for the battery.
        Assert.Equal(AwakeStop.None, AwakeRules.Check(AwakeSession.Indefinite, Now, _ => true, new BatteryState(true, 5), 20));
        Assert.Equal(AwakeStop.None, AwakeRules.Check(AwakeSession.Indefinite, Now, _ => true, new BatteryState(false, 5), 0));
        Assert.Equal(AwakeStop.None, AwakeRules.Check(AwakeSession.Indefinite, Now, _ => true, new BatteryState(false, null), 20));
    }

    [Theory]
    [InlineData(7200, "2h")]
    [InlineData(3900, "1h 05m")]
    [InlineData(900, "15m")]
    [InlineData(40, "40s")]
    public void FormatsLengthsAsOrbit(int seconds, string expected) => Assert.Equal(expected, AwakeRules.Format(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void APowerRequestIsHeldAndLetGo()
    {
        using var request = PowerRequest.Hold("Orbit test", display: false);
        Assert.NotNull(request);
        Assert.True(request!.SetDisplay(true));
        Assert.True(request.SetDisplay(false));
        var battery = PowerRequest.Battery();
        Assert.True(battery.Percent is null or >= 0 and <= 100);
    }
}
