using Orbit.Core.Actions;
using Orbit.Core.Ring;

namespace Orbit.Tests;

public class RingTrackerTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(10);
    private static TimeSpan At(double seconds) => T0 + TimeSpan.FromSeconds(seconds);

    private static RingTracker Open(bool hold = true, int count = 8)
    {
        var ring = new RingTracker(count, TimeSpan.FromSeconds(0.45));
        ring.Open(0, 0, hold, T0);
        return ring;
    }

    [Theory]
    [InlineData(0, -100, 0)]    // up
    [InlineData(100, 0, 2)]     // right
    [InlineData(0, 100, 4)]     // down
    [InlineData(-100, 0, 6)]    // left
    [InlineData(70, -70, 1)]    // up right
    public void AnglesStartUpAndGoClockwise(double x, double y, int slot)
    {
        var ring = Open();
        ring.Track(x, y, At(0.1));
        Assert.Equal(slot, ring.Selection);
    }

    [Fact]
    public void SectorBoundariesSplitHalfway()
    {
        // 8 slots: slot 0 covers -22.5..22.5 degrees.
        Assert.Equal(0, RingTracker.IndexAt(22.4 * Math.PI / 180, 8));
        Assert.Equal(1, RingTracker.IndexAt(22.6 * Math.PI / 180, 8));
        Assert.Equal(0, RingTracker.IndexAt(359 * Math.PI / 180, 8));
    }

    [Fact]
    public void NothingIsChosenUntilThePointerMoves()
    {
        var ring = new RingTracker(8, TimeSpan.FromSeconds(0.45));
        ring.Open(80, 0, hold: true, T0);   // opened off an edge: the pointer already sits on slot 2
        ring.Track(83, 0, At(0.05));
        Assert.Null(ring.Selection);
        ring.Track(90, 0, At(0.1));
        Assert.Equal(2, ring.Selection);
    }

    [Fact]
    public void DeadZoneClearsTheSelection()
    {
        var ring = Open();
        ring.Track(0, -100, At(0.1));
        ring.Track(0, -30, At(0.2));
        Assert.Null(ring.Selection);
        Assert.Null(ring.PointerAngle);
    }

    [Fact]
    public void FlickFiresAtOnceAndOnlyOnce()
    {
        var ring = Open();
        Assert.Equal(RingEvent.Fire(2), ring.Track(200, 0, At(0.1)));
        Assert.Equal(RingState.Closed, ring.State);
        Assert.Equal(RingEvent.None, ring.Release(At(0.2)));
        Assert.Equal(RingEvent.None, ring.Track(300, 0, At(0.3)));
    }

    [Fact]
    public void ReleaseRunsTheSelection()
    {
        var ring = Open();
        ring.Track(0, 100, At(0.1));
        Assert.Equal(RingEvent.Fire(4), ring.Release(At(0.5)));
    }

    [Fact]
    public void QuickReleaseWithoutSelectionStaysOpen()
    {
        var ring = Open();
        Assert.Equal(RingEvent.None, ring.Release(At(0.2)));
        Assert.Equal(RingState.Tapping, ring.State);
    }

    [Fact]
    public void SlowReleaseWithoutSelectionCloses()
    {
        var ring = Open();
        Assert.Equal(RingEvent.Close, ring.Release(At(0.5)));
    }

    [Fact]
    public void DwellRunsOnlyInTapModeAfterTheGrace()
    {
        var ring = Open(hold: false);
        ring.Track(100, 0, At(0.1));
        Assert.Equal(RingEvent.None, ring.Tick(At(0.15)));      // within the 0.08 s grace
        Assert.Equal(0, ring.DwellProgress);
        Assert.Equal(RingEvent.None, ring.Tick(At(0.4)));
        Assert.True(ring.DwellProgress is > 0.4 and < 0.6);
        Assert.Equal(RingEvent.Fire(2), ring.Tick(At(0.1 + 0.08 + 0.45)));
    }

    [Fact]
    public void DwellRestartsWhenTheSelectionChanges()
    {
        var ring = Open(hold: false);
        ring.Track(100, 0, At(0.1));
        ring.Tick(At(0.5));
        ring.Track(0, 100, At(0.5));
        Assert.Equal(0, ring.DwellProgress);
        Assert.Equal(RingEvent.None, ring.Tick(At(0.6)));
        Assert.Equal(4, ring.Selection);
    }

    [Fact]
    public void HoldingNeverDwells()
    {
        var ring = Open(hold: true);
        ring.Track(100, 0, At(0.1));
        Assert.Equal(RingEvent.None, ring.Tick(At(5)));
        Assert.Equal(0, ring.DwellProgress);
    }

    [Fact]
    public void ClicksAndCancel()
    {
        var ring = Open(hold: false);
        Assert.Equal(RingEvent.Fire(3), ring.Click(3));
        ring.Open(0, 0, hold: false, T0);
        Assert.Equal(RingEvent.Close, ring.Click(null));
        ring.Open(0, 0, hold: false, T0);
        Assert.Equal(RingEvent.Close, ring.Cancel());
        Assert.Equal(RingEvent.None, ring.Cancel());
    }

    [Fact]
    public void DwellHasAFloor()
    {
        Assert.Equal(TimeSpan.FromSeconds(0.2), new RingTracker(8, TimeSpan.FromSeconds(0.05)).Dwell);
    }

    [Fact]
    public void ItemsShrinkPastEight()
    {
        Assert.Equal(1, RingTracker.ItemScale(8));
        Assert.Equal(8.6 / 12, RingTracker.ItemScale(12), 6);
    }

    [Fact]
    public void SlotsParseOrbitIdsAndFallBack()
    {
        Assert.Equal([OrbitAction.CaptureArea, OrbitAction.Clipboard, OrbitAction.Launcher],
            RingSlots.Parse(["captureArea", "winLeft", "clipboard", "launcher"]));
        Assert.Equal(RingSlots.Default, RingSlots.Parse(["captureArea", "clipboard"]));   // too few
        Assert.Equal(RingSlots.Default, RingSlots.Parse(null));
        Assert.Equal(8, RingSlots.Default.Count);
    }
}
