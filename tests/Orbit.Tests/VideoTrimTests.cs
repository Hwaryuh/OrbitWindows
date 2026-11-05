using Orbit.Core.Capture.Recording;

namespace Orbit.Tests;

public class VideoTrimTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void StartsWithTheWholeVideoUnedited()
    {
        var trim = new VideoTrim(S(10));
        Assert.Equal((S(0), S(10)), (trim.Start, trim.End));
        Assert.False(trim.IsEdited(muted: false));
        Assert.True(trim.IsEdited(muted: true));
    }

    [Fact]
    public void TheEndsStayInsideAndApart()
    {
        var trim = new VideoTrim(S(10));
        trim.SetStart(S(-5));
        Assert.Equal(S(0), trim.Start);
        trim.SetEnd(S(50));
        Assert.Equal(S(10), trim.End);
        trim.SetEnd(S(4));
        trim.SetStart(S(9));
        Assert.Equal(S(4) - VideoTrim.Shortest, trim.Start);
        trim.SetEnd(S(0));
        Assert.Equal(trim.Start + VideoTrim.Shortest, trim.End);
    }

    [Fact]
    public void AVideoShorterThanTheGapStaysValid()
    {
        var trim = new VideoTrim(S(0.05));
        trim.SetStart(S(1));
        trim.SetEnd(S(0));
        Assert.Equal(S(0), trim.Start);
        Assert.Equal(S(0.05), trim.End);
    }

    [Theory]
    [InlineData(0.04, 10, false)]
    [InlineData(0.06, 10, true)]
    [InlineData(0, 9.96, false)]
    [InlineData(0, 9.94, true)]
    public void AHairAtEitherEndIsNotAnEdit(double start, double end, bool edited)
    {
        var trim = new VideoTrim(S(10));
        trim.SetStart(S(start));
        trim.SetEnd(S(end));
        Assert.Equal(edited, trim.IsEdited(muted: false));
    }

    [Fact]
    public void ResetBringsItAllBack()
    {
        var trim = new VideoTrim(S(10));
        trim.SetStart(S(2));
        trim.SetEnd(S(3));
        trim.Reset();
        Assert.False(trim.IsEdited(false));
    }

    [Fact]
    public void AMovedTimeStaysInTheKeptPart()
    {
        var trim = new VideoTrim(S(10));
        trim.SetStart(S(2));
        trim.SetEnd(S(3));
        Assert.Equal(S(2), trim.Clamped(S(0)));
        Assert.Equal(S(3), trim.Clamped(S(9)));
        Assert.Equal(S(2.5), trim.Clamped(S(2.5)));
    }

    [Theory]
    [InlineData(0, "0:00.0")]
    [InlineData(62.5, "1:02.5")]
    [InlineData(-3, "0:00.0")]
    [InlineData(3599.9, "59:59.9")]
    public void TheClock(double seconds, string text) => Assert.Equal(text, VideoTrim.Clock(S(seconds)));
}
