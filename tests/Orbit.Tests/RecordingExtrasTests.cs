using Orbit.Core.Capture.Recording;
using Orbit.Core.Display;

namespace Orbit.Tests;

public class RecordingExtrasTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(-1, 3)]
    [InlineData(4, 3)]
    [InlineData(1000, 3)]
    public void OnlyTheOfferedCountdownsCount(int preference, int seconds) =>
        Assert.Equal(seconds, RecordingExtras.CountdownSeconds(preference));

    [Theory]
    [InlineData(500, 400, 1.0, 465, 365, 70)]
    [InlineData(500, 400, 1.5, 448, 348, 105)]
    [InlineData(-1000, -20, 1.25, -1044, -64, 88)]
    public void ARingCentersOnTheClick(int x, int y, double scale, int left, int top, int side) =>
        Assert.Equal(new PixelRect(left, top, side, side), RecordingExtras.RingPlace(x, y, scale));

    [Fact]
    public void TheCountdownCentersOnWhatIsRecorded()
    {
        var work = new PixelRect(0, 0, 1920, 1040);
        Assert.Equal(new PixelRect(860, 420, 200, 200), RecordingExtras.CountdownPlace(new PixelRect(0, 0, 1920, 1040), work, 1));
    }

    [Theory]
    [InlineData(-900, 100, 400, 300, 0, 150)]
    [InlineData(1900, 900, 400, 300, 1720, 840)]
    public void TheCountdownStaysInTheWorkArea(int x, int y, int width, int height, int left, int top)
    {
        var work = new PixelRect(0, 0, 1920, 1040);
        Assert.Equal(new PixelRect(left, top, 200, 200), RecordingExtras.CountdownPlace(new PixelRect(x, y, width, height), work, 1));
    }

    [Fact]
    public void ACountdownBiggerThanTheWorkAreaDoesNotThrow()
    {
        var place = RecordingExtras.CountdownPlace(new PixelRect(0, 0, 100, 100), new PixelRect(10, 10, 100, 100), 1);
        Assert.Equal(10, place.X);
        Assert.Equal(10, place.Y);
    }
}
