using Orbit.Core.Capture;
using Orbit.Core.Display;

namespace Orbit.Tests;

public class CardLayoutTests
{
    [Theory]
    [InlineData("bottomLeft", true, false)]
    [InlineData("bottomRight", false, false)]
    [InlineData("topLeft", true, true)]
    [InlineData("topRight", false, true)]
    [InlineData("", false, false)]
    [InlineData(null, false, false)]
    [InlineData("TopLeft", false, false)]
    public void CornerParsesTheFourNamesAndFallsBackToBottomRight(string? name, bool left, bool top) =>
        Assert.Equal(new CardCorner(left, top), CardCorner.Parse(name));

    [Theory]
    [InlineData(1920, 1080, 135)]   // 16:9 is 240 × 135
    [InlineData(2000, 100, 96)]     // a thin strip still gets the minimum
    [InlineData(300, 1000, 180)]    // a tall one stops at the maximum
    [InlineData(0, 100, 180)]
    public void HeightFollowsTheRatioBetweenTheLimits(double w, double h, double expected) =>
        Assert.Equal(expected, CardLayout.Height(w, h), 6);

    [Fact]
    public void PictureFitsWholeAndIsNeverBlownUp()
    {
        // 16:9 card is 135 tall, so the height limits: 119 tall, 211.6 wide.
        var (w169, h169) = CardLayout.Fit(1920, 1080);
        Assert.Equal(119, h169, 6);
        Assert.Equal(1920 * 119 / 1080.0, w169, 6);
        Assert.Equal((50, 40), CardLayout.Fit(50, 40));
        var (w, h) = CardLayout.Fit(300, 1000);
        Assert.Equal(164, h, 6);
        Assert.Equal(49.2, w, 6);
        Assert.Equal((0, 0), CardLayout.Fit(0, 10));
    }

    [Fact]
    public void SmallMeansUnderEightyPercentBothWays()
    {
        Assert.True(CardLayout.IsSmall(100, 50));
        Assert.False(CardLayout.IsSmall(200, 50));
        Assert.False(CardLayout.IsSmall(1920, 1080));
    }

    [Fact]
    public void FittingKeepsAtLeastOneAndStopsBeforeOverflowing()
    {
        Assert.Equal(3, CardLayout.Fitting([135, 135, 135], 1000));
        // 1000 - 40 = 960 room; 180 + 12 + 180 = 372 fits; a third makes 564.
        Assert.Equal(2, CardLayout.Fitting([180, 180, 180], 412));
        Assert.Equal(1, CardLayout.Fitting([180, 180], 100));
    }

    [Fact]
    public void PanelSizeAddsPaddingAndGaps() =>
        Assert.Equal((272, 135 + 96 + 12 + 32), CardLayout.PanelSize([135, 96]));

    [Fact]
    public void PlaceSitsTheCardsTwentyInFromTheCornerInPixels()
    {
        var work = new PixelRect(0, 0, 1920, 1040);
        // At 100 % the panel's padding (16) eats into the 20 offset: the panel is 4 px in.
        Assert.Equal(new PixelRect(1920 - 4 - 272, 1040 - 4 - 199, 272, 199), CardLayout.Place(work, 1, new CardCorner(false, false), 272, 199, false));
        Assert.Equal(new PixelRect(4, 4, 272, 199), CardLayout.Place(work, 1, new CardCorner(true, true), 272, 199, false));
        // 150 % on a monitor left of the primary: sizes and offsets scale.
        var left = new PixelRect(-2560, 0, 2560, 1400);
        Assert.Equal(new PixelRect(-2560 + 6, 1400 - 6 - 299, 408, 299), CardLayout.Place(left, 1.5, new CardCorner(true, false), 272, 199, false));
    }

    [Fact]
    public void TuckedTabIsFlushWithTheSideEdge()
    {
        var work = new PixelRect(0, 0, 1920, 1040);
        Assert.Equal(new PixelRect(1890, 1040 - 20 - 64, 30, 64), CardLayout.Place(work, 1, new CardCorner(false, false), 30, 64, true));
        Assert.Equal(new PixelRect(0, 20, 30, 64), CardLayout.Place(work, 1, new CardCorner(true, true), 30, 64, true));
    }
}

public class CardStackTests
{
    private sealed class Card(string name)
    {
        public override string ToString() => name;
    }

    [Fact]
    public void NewestFirstAndTheOldestLeavesPastThree()
    {
        var stack = new CardStack<Card>();
        Card a = new("a"), b = new("b"), c = new("c"), d = new("d");
        stack.Add(a);
        stack.Add(b);
        stack.Add(c);
        Assert.Equal([a], stack.Add(d));
        Assert.Equal([d, c, b], stack.Items);
    }

    [Fact]
    public void AddingAgainMovesToTheTopWithoutDuplicates()
    {
        var stack = new CardStack<Card>();
        Card a = new("a"), b = new("b");
        stack.Add(a);
        stack.Add(b);
        Assert.Empty(stack.Add(a));
        Assert.Equal([a, b], stack.Items);
    }

    [Fact]
    public void AHeldCardIsNotPushedOutAndAnUnheldOneGoesInstead()
    {
        var stack = new CardStack<Card>();
        Card a = new("a"), b = new("b"), c = new("c"), d = new("d");
        stack.Add(a);
        stack.Add(b);
        stack.Add(c);
        stack.Hold(a);
        Assert.Equal([b], stack.Add(d));
        Assert.Equal([d, c, a], stack.Items);
    }

    [Fact]
    public void DismissWhileHeldWaitsForTheRelease()
    {
        var stack = new CardStack<Card>();
        var a = new Card("a");
        stack.Add(a);
        stack.Hold(a);
        stack.Hold(a);
        Assert.False(stack.Dismiss(a));
        Assert.Single(stack.Items);
        Assert.False(stack.Release(a));
        Assert.True(stack.Release(a));
        Assert.Empty(stack.Items);
        Assert.False(stack.Release(a));
    }

    [Fact]
    public void ReleaseWithoutADismissKeepsTheCard()
    {
        var stack = new CardStack<Card>();
        var a = new Card("a");
        stack.Add(a);
        stack.Hold(a);
        Assert.False(stack.Release(a));
        Assert.Equal([a], stack.Items);
    }

    [Fact]
    public void AutoDismissOnlyForAPlainCardWithATime()
    {
        var stack = new CardStack<Card>();
        Card a = new("a"), kept = new("kept");
        stack.Add(a);
        stack.Add(kept, keep: true);
        Assert.True(stack.AutoDismissDue(a, 8));
        Assert.False(stack.AutoDismissDue(a, 0));
        Assert.False(stack.AutoDismissDue(kept, 8));
        Assert.False(stack.ShouldSchedule(kept, 8));

        stack.SetHover(a, true);
        Assert.False(stack.AutoDismissDue(a, 8));
        Assert.True(stack.ShouldSchedule(a, 8));
        stack.SetHover(a, false);

        stack.Hold(a);
        Assert.False(stack.AutoDismissDue(a, 8));
        stack.Release(a);

        stack.SetTucked(true);
        Assert.False(stack.AutoDismissDue(a, 8));
        Assert.False(stack.ShouldSchedule(a, 8));
    }

    [Fact]
    public void TuckingEndsWithTheLastCardAndNeedsACard()
    {
        var stack = new CardStack<Card>();
        stack.SetTucked(true);
        Assert.False(stack.Tucked);
        var a = new Card("a");
        stack.Add(a);
        stack.SetTucked(true);
        Assert.True(stack.Tucked);
        stack.Dismiss(a);
        Assert.False(stack.Tucked);
    }

    [Fact]
    public void ADismissedCardForgetsItWasKept()
    {
        var stack = new CardStack<Card>();
        var a = new Card("a");
        stack.Add(a, keep: true);
        stack.Dismiss(a);
        stack.Add(a);
        Assert.False(stack.IsKept(a));
        Assert.False(stack.Dismiss(new Card("never added")));
    }
}

public class CardGestureTests
{
    [Fact]
    public void SmallMovesStayAClick()
    {
        var g = new CardGesture(1);
        Assert.Equal(CardGestureKind.Pending, g.Move(3, 3));
        Assert.False(g.Dismisses(3));
    }

    [Fact]
    public void TowardTheEdgeIsASwipeAndOtherWaysADrag()
    {
        Assert.Equal(CardGestureKind.Swipe, new CardGesture(1).Move(10, 2));
        Assert.Equal(CardGestureKind.Drag, new CardGesture(1).Move(-10, 2));
        Assert.Equal(CardGestureKind.Drag, new CardGesture(1).Move(6, 10));
        Assert.Equal(CardGestureKind.Swipe, new CardGesture(-1).Move(-10, 0));
    }

    [Fact]
    public void TheKindIsDecidedOnce()
    {
        var g = new CardGesture(1);
        g.Move(10, 0);
        Assert.Equal(CardGestureKind.Swipe, g.Move(-200, 300));
    }

    [Fact]
    public void SwipeDismissesPastSeventyAndFollowsAFifthBackward()
    {
        var g = new CardGesture(1);
        g.Move(10, 0);
        Assert.False(g.Dismisses(70));
        Assert.True(g.Dismisses(71));
        Assert.Equal(50, g.Offset(50));
        Assert.Equal(-4, g.Offset(-20), 6);
        var left = new CardGesture(-1);
        left.Move(-10, 0);
        Assert.True(left.Dismisses(-80));
    }
}

public class PinLayoutTests
{
    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void ASmallPictureIsCenteredAtItsOwnSize() =>
        Assert.Equal(new PixelRect(860, 420, 200, 200), PinLayout.Initial(200, 200, Work));

    [Fact]
    public void ALargePictureShrinksToSixTenthsOfTheShorterSide()
    {
        var r = PinLayout.Initial(1920, 1080, Work);
        Assert.Equal(624, r.Width);
        Assert.Equal(351, r.Height);
        Assert.Equal((1920 - 624) / 2, r.X);
    }

    [Fact]
    public void OpacityMovesAStepANotchBetweenTheLimits()
    {
        Assert.Equal(0.95, PinLayout.Opacity(1, -1), 6);
        Assert.Equal(1, PinLayout.Opacity(1, 3));
        Assert.Equal(0.2, PinLayout.Opacity(0.25, -5), 6);
    }

    [Fact]
    public void ZoomKeepsTheCenterAndRatioAndItsLimits()
    {
        var r = new PixelRect(100, 100, 400, 200);
        var grown = PinLayout.Zoom(r, 1.5, 60, new PixelRect(0, 0, 3840, 1080));
        Assert.Equal(new PixelRect(0, 50, 600, 300), grown);
        var tiny = PinLayout.Zoom(r, 0.01, 60, new PixelRect(0, 0, 3840, 1080));
        Assert.Equal(60, tiny.Height);
        Assert.Equal(120, tiny.Width);
        // Never past the screen, and zooming in never shrinks one that is already larger.
        var capped = PinLayout.Zoom(r, 10, 60, new PixelRect(0, 0, 1000, 1000));
        Assert.Equal(1000, capped.Width);
        Assert.Equal(500, capped.Height);
    }

    [Fact]
    public void ResizingByACornerKeepsTheRatioAndTheOppositeCorner()
    {
        // 2:1 picture; dragging the bottom-right corner to 500 × 100 follows the width.
        var r = PinLayout.KeepRatio(SizingEdge.BottomRight, new PixelRect(10, 20, 500, 100), 2, 60);
        Assert.Equal(new PixelRect(10, 20, 500, 250), r);
        // Dragging the top-left corner mostly down follows the height, anchored at the bottom right.
        var t = PinLayout.KeepRatio(SizingEdge.TopLeft, new PixelRect(100, 0, 300, 300), 2, 60);
        Assert.Equal(new PixelRect(400 - 600, 0, 600, 300), t);
    }

    [Fact]
    public void ResizingBySidesFollowsThatSideAndCentersTheOther()
    {
        var r = PinLayout.KeepRatio(SizingEdge.Right, new PixelRect(0, 100, 400, 100), 2, 60);
        Assert.Equal(new PixelRect(0, 50, 400, 200), r);
        var b = PinLayout.KeepRatio(SizingEdge.Bottom, new PixelRect(100, 0, 200, 300), 2, 60);
        Assert.Equal(new PixelRect(-100, 0, 600, 300), b);
    }

    [Fact]
    public void ResizingStopsAtTheMinimum()
    {
        var r = PinLayout.KeepRatio(SizingEdge.BottomRight, new PixelRect(0, 0, 20, 10), 2, 60);
        Assert.Equal(new PixelRect(0, 0, 120, 60), r);
    }
}

public class RecycleBinTests
{
    [Fact]
    public void AMissingFileIsReportedAsMissing() =>
        Assert.Equal(RecycleBin.Outcome.Missing, RecycleBin.Send(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png")));

    [Fact]
    public void AFileGoesToTheRecycleBin()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Orbit test {Guid.NewGuid()}.png");
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Equal(RecycleBin.Outcome.Moved, RecycleBin.Send(path));
        Assert.False(File.Exists(path));
    }
}
