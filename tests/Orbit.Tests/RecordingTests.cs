using Orbit.Core.Capture.Recording;

namespace Orbit.Tests;

public class RecordingSizeTests
{
    [Theory]
    [InlineData(1921, 1081, 1920, 1080)]
    [InlineData(3840, 2160, 3840, 2160)]
    [InlineData(17, 17, 16, 16)]
    public void SidesAreRoundedDownToEven(int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(RecordingSize.Verdict.Ok, RecordingSize.Fit(width, height, out int w, out int h));
        Assert.Equal((expectedWidth, expectedHeight), (w, h));
    }

    [Theory]
    [InlineData(15, 400)]
    [InlineData(400, 1)]
    public void TinyAreasAreRefused(int width, int height) =>
        Assert.Equal(RecordingSize.Verdict.TooSmall, RecordingSize.Fit(width, height, out _, out _));

    [Theory]
    [InlineData(5120, 1440)]
    [InlineData(4096, 2400)]
    public void AreasBeyondTheEncoderAreRefused(int width, int height) =>
        Assert.Equal(RecordingSize.Verdict.TooBig, RecordingSize.Fit(width, height, out _, out _));

    [Fact]
    public void BitrateIsClamped()
    {
        Assert.Equal(4_000_000u, RecordingSize.Bitrate(320, 240, 30));
        Assert.Equal(12_441_600u, RecordingSize.Bitrate(1920, 1080, 60));
        Assert.Equal(40_000_000u, RecordingSize.Bitrate(3840, 2160, 60));
    }
}

public class FramePacerTests
{
    private const long Ms = TimeSpan.TicksPerMillisecond;

    [Fact]
    public void SixtyHertzAtThirtyFpsKeepsEveryOtherFrame()
    {
        var pacer = new FramePacer(30);
        var kept = Enumerable.Range(0, 10).Where(i => pacer.Keep(i * 16_667 * 10)).ToList();
        Assert.Equal([0, 2, 4, 6, 8], kept);
    }

    [Fact]
    public void JitterAtSixtyFpsKeepsEveryFrame()
    {
        var pacer = new FramePacer(60);
        long[] times = [0, 16 * Ms, 33 * Ms, 49 * Ms, 67 * Ms];
        Assert.All(times, t => Assert.True(pacer.Keep(t)));
    }

    [Fact]
    public void BurstsAreThinned()
    {
        var pacer = new FramePacer(60);
        Assert.True(pacer.Keep(0));
        Assert.False(pacer.Keep(2 * Ms));
        Assert.False(pacer.Keep(10 * Ms));
        Assert.True(pacer.Keep(13 * Ms));
    }
}

public class SampleClockTests
{
    private const long Ms = TimeSpan.TicksPerMillisecond;

    [Fact]
    public void EachFrameIsWrittenWhenTheNextComes()
    {
        var clock = new SampleClock();
        Assert.True(clock.Accept(1000 * Ms, out var first));
        Assert.Null(first);
        Assert.True(clock.Accept(1040 * Ms, out var second));
        Assert.Equal((0L, 40 * Ms), second);
        Assert.True(clock.Accept(3040 * Ms, out var third));
        Assert.Equal((40 * Ms, 2000 * Ms), third);
        var last = clock.End(3540 * Ms);
        Assert.Equal((2040 * Ms, 500 * Ms), last);
        Assert.Equal(TimeSpan.FromMilliseconds(2540), SampleClock.Length(last!.Value));
    }

    [Fact]
    public void FramesNotLaterThanTheHeldOneAreDropped()
    {
        var clock = new SampleClock();
        clock.Accept(100 * Ms, out _);
        clock.Accept(140 * Ms, out _);
        Assert.False(clock.Accept(140 * Ms, out var same));
        Assert.Null(same);
        Assert.False(clock.Accept(120 * Ms, out _));
    }

    [Fact]
    public void NoFramesNoEnd()
    {
        var clock = new SampleClock();
        Assert.False(clock.HasFrame);
        Assert.Null(clock.End(5 * Ms));
    }

    [Fact]
    public void StopRightAfterTheFrameStillGivesItALength()
    {
        var clock = new SampleClock();
        clock.Accept(100 * Ms, out _);
        Assert.Equal((0L, Ms), clock.End(100 * Ms));
    }
}
