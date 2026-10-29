using Orbit.Core.Display;

namespace Orbit.Tests;

public class PixelRectTests
{
    [Fact]
    public void FromCornersAcceptsAnyDragDirection()
    {
        Assert.Equal(new PixelRect(-20, 5, 30, 15), PixelRect.FromCorners(10, 20, -20, 5));
    }

    [Fact]
    public void IntersectAcrossMonitors()
    {
        var left = new PixelRect(-1080, 0, 1080, 1920);
        var drag = new PixelRect(-100, 100, 300, 50);
        Assert.Equal(new PixelRect(-100, 100, 100, 50), drag.Intersect(left));
        Assert.True(new PixelRect(5000, 0, 1, 1).Intersect(left).IsEmpty);
    }

    [Fact]
    public void ContainsIsHalfOpen()
    {
        var r = new PixelRect(0, 0, 10, 10);
        Assert.True(r.Contains(0, 0));
        Assert.False(r.Contains(10, 0));
    }

    [Fact]
    public void MonitorConvertsPixelsToDips()
    {
        var monitor = new MonitorInfo(0, "TEST", new PixelRect(0, 0, 2880, 1620), new PixelRect(0, 0, 2880, 1580), 144, true);
        Assert.Equal(1.5, monitor.Scale);
        Assert.Equal(1920, monitor.ToDip(2880));
    }
}
