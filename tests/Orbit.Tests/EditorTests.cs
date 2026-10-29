using Orbit.Core.Capture;
using Orbit.Core.Capture.Editor;

namespace Orbit.Tests;

public class EditorModelTests
{
    /// <summary>A 400 × 300 point picture; text is 10 points per letter, 30 tall; every pixel gray unless set.</summary>
    private static EditorModel Model(Func<PointD, uint?>? pixels = null) =>
        new(400, 300, 1, a => (a.Text.Length * 10, 30), pixels ?? (_ => 0xFF808080));

    private static void Drag(EditorModel m, double x0, double y0, double x1, double y1, bool shift = false, int clicks = 1)
    {
        m.DragChanged(new(x0, y0), new(x1, y1), shift);
        m.DragEnded(new(x1, y1), new(x0, y0), clicks);
    }

    [Fact]
    public void ADragPlacesAMarkWithTheCurrentStyleAndAClickPlacesNothing()
    {
        var m = Model();
        m.Tool = EditorTool.Rect;
        m.Color = EditorTools.Palette[4];
        m.StrokeWidth = 7;
        Drag(m, 10, 10, 60, 40);
        var rect = Assert.Single(m.Annotations);
        Assert.Equal(new Area(10, 10, 50, 30), rect.Rect);
        Assert.Equal((EditorTools.Palette[4], 7.0), (rect.Color, rect.Width));
        Drag(m, 100, 100, 101, 101);
        Assert.Single(m.Annotations);
        Assert.Null(m.Draft);
    }

    [Fact]
    public void APenClickStillLeavesADot()
    {
        var m = Model();
        m.Tool = EditorTool.Pen;
        m.DragChanged(new(5, 5), new(5, 5), false);
        m.DragEnded(new(5, 5), new(5, 5));
        Assert.Equal(EditorTool.Pen, Assert.Single(m.Annotations).Tool);
    }

    [Fact]
    public void ShiftSquaresBoxesAndSnapsLinesTo45Degrees()
    {
        var m = Model();
        m.Tool = EditorTool.Rect;
        Drag(m, 0, 0, 50, -20, shift: true);
        Assert.Equal(new Area(0, -50, 50, 50), m.Annotations[^1].Rect);
        var snapped = AnnotationGeometry.Constrained(EditorTool.Arrow, new(0, 0), new(100, 10));
        Assert.Equal(100.4988, snapped.X, 3);
        Assert.Equal(0, snapped.Y, 6);
    }

    [Fact]
    public void UndoAndRedoWalkTheHistoryAndANewChangeClearsRedo()
    {
        var m = Model();
        m.Tool = EditorTool.Line;
        Drag(m, 0, 0, 50, 50);
        Drag(m, 0, 0, 60, 60);
        m.Undo();
        Assert.Single(m.Annotations);
        Assert.True(m.CanRedo);
        m.Redo();
        Assert.Equal(2, m.Annotations.Count);
        m.Undo();
        Drag(m, 0, 0, 70, 70);
        Assert.False(m.CanRedo);
    }

    [Fact]
    public void UndoKeepsAtMostAHundredSteps()
    {
        var m = Model();
        m.Tool = EditorTool.Line;
        for (int i = 0; i < 120; i++) Drag(m, 0, 0, 10 + i, 10);
        Assert.Equal(EditorModel.UndoLimit, m.UndoDepth);
        for (int i = 0; i < 200; i++) m.Undo();
        // The 20 oldest steps are gone, so 20 lines remain.
        Assert.Equal(20, m.Annotations.Count);
    }

    [Fact]
    public void ChangingTheStyleChangesTheSelectedMarkAsOneStep()
    {
        var m = Model();
        m.Tool = EditorTool.Rect;
        Drag(m, 10, 10, 60, 60);
        m.Tool = EditorTool.Select;
        Drag(m, 30, 30, 30, 30);
        Assert.NotNull(m.Selected);
        int before = m.UndoDepth;
        m.StrokeWidth = 12;
        Assert.Equal(12, m.Selected!.Width);
        Assert.Equal(before + 1, m.UndoDepth);
        // Setting the same value again records nothing.
        m.StrokeWidth = 12;
        Assert.Equal(before + 1, m.UndoDepth);
    }

    [Fact]
    public void SelectingAMarkAdoptsItsStyleWithoutRecording()
    {
        var m = Model();
        m.Tool = EditorTool.Ellipse;
        m.Color = EditorTools.Palette[2];
        Drag(m, 10, 10, 60, 60);
        m.Color = EditorTools.Palette[0];
        m.Tool = EditorTool.Select;
        int depth = m.UndoDepth;
        Drag(m, 20, 20, 20, 20);
        Assert.Equal(EditorTools.Palette[2], m.Color);
        Assert.Equal(depth, m.UndoDepth);
    }

    [Fact]
    public void SelectDragMovesTheTopmostMarkAsOneStep()
    {
        var m = Model();
        m.Tool = EditorTool.Rect;
        Drag(m, 10, 10, 60, 60);
        Drag(m, 20, 20, 70, 70);
        m.Tool = EditorTool.Select;
        int depth = m.UndoDepth;
        m.DragChanged(new(30, 30), new(35, 30), false);
        m.DragChanged(new(30, 30), new(40, 30), false);
        m.DragEnded(new(40, 30), new(30, 30));
        Assert.Equal(depth + 1, m.UndoDepth);
        Assert.Equal(new Area(30, 20, 50, 50), m.Annotations[1].Rect);
        Assert.Equal(new Area(10, 10, 50, 50), m.Annotations[0].Rect);
    }

    [Fact]
    public void DeleteRemovesTheSelection()
    {
        var m = Model();
        m.Tool = EditorTool.Rect;
        Drag(m, 10, 10, 60, 60);
        m.Tool = EditorTool.Select;
        Drag(m, 9, 9, 9, 9);
        m.DeleteSelected();
        Assert.Empty(m.Annotations);
        m.Undo();
        Assert.Single(m.Annotations);
    }

    [Fact]
    public void HitTestAllowsEightPointsOfSlop()
    {
        var m = Model();
        m.Tool = EditorTool.Rect;
        Drag(m, 100, 100, 150, 150);
        Assert.NotNull(m.HitTest(new(93, 100)));
        Assert.Null(m.HitTest(new(91, 100)));
    }

    [Fact]
    public void CountersNumberOnFromTheHighest()
    {
        var m = Model();
        m.Tool = EditorTool.Counter;
        Drag(m, 10, 10, 10, 10);
        Drag(m, 50, 10, 50, 10);
        Assert.Equal([1, 2], m.Annotations.Select(a => a.Number));
        m.Tool = EditorTool.Select;
        Drag(m, 10, 10, 10, 10);
        m.DeleteSelected();
        m.Tool = EditorTool.Counter;
        Drag(m, 90, 10, 90, 10);
        Assert.Equal([2, 3], m.Annotations.Select(a => a.Number));
    }

    [Fact]
    public void TextIsTypedThenCommittedAndCanBeReopened()
    {
        var m = Model();
        m.Tool = EditorTool.Text;
        Drag(m, 20, 20, 20, 20);
        Assert.NotNull(m.EditingText);
        m.EditingText!.Text = "  hello ";
        m.CommitText();
        var text = Assert.Single(m.Annotations);
        Assert.Equal("hello", text.Text);
        // A click on it with the text tool opens it again with its style.
        m.FontSize = 40;
        Drag(m, 25, 25, 25, 25);
        Assert.Empty(m.Annotations);
        Assert.Equal("hello", m.EditingText!.Text);
        Assert.Equal(24, m.FontSize);
        m.EditingText.Text = " ";
        m.CommitText();
        Assert.Empty(m.Annotations);
        m.Undo();
        Assert.Single(m.Annotations);
    }

    [Fact]
    public void ChangingAPlacedTextIsOneUndoStep()
    {
        var m = Model();
        m.Tool = EditorTool.Text;
        Drag(m, 20, 20, 20, 20);
        m.EditingText!.Text = "before";
        m.CommitText();
        Drag(m, 25, 25, 25, 25);
        m.EditingText!.Text = "after";
        m.CommitText();
        Assert.Equal("after", Assert.Single(m.Annotations).Text);
        m.Undo();
        Assert.Equal("before", Assert.Single(m.Annotations).Text);
    }

    [Fact]
    public void UndoWhileTypingDropsTheTextAndRestoresAReopenedOne()
    {
        var m = Model();
        m.Tool = EditorTool.Text;
        Drag(m, 20, 20, 20, 20);
        m.EditingText!.Text = "keep";
        m.CommitText();
        Drag(m, 200, 200, 200, 200);
        m.EditingText!.Text = "new";
        m.Undo();
        Assert.Null(m.EditingText);
        Assert.Equal("keep", Assert.Single(m.Annotations).Text);
        Drag(m, 25, 25, 25, 25);
        m.EditingText!.Text = "changed";
        m.Undo();
        Assert.Null(m.EditingText);
        Assert.Equal("keep", Assert.Single(m.Annotations).Text);
        m.CommitText();
        Assert.Single(m.Annotations);
    }

    [Fact]
    public void MovingBackToTheStartPutsTheMarkBack()
    {
        var m = Model();
        m.Tool = EditorTool.Rect;
        Drag(m, 10, 10, 60, 60);
        m.Tool = EditorTool.Select;
        m.DragChanged(new(30, 30), new(50, 30), false);
        m.DragChanged(new(30, 30), new(30, 30), false);
        m.DragEnded(new(30, 30), new(30, 30));
        Assert.Equal(new Area(10, 10, 50, 50), m.Annotations[0].Rect);
    }

    [Fact]
    public void DoubleClickWithSelectReopensText()
    {
        var m = Model();
        m.Tool = EditorTool.Text;
        Drag(m, 20, 20, 20, 20);
        m.EditingText!.Text = "hi";
        m.CommitText();
        m.Tool = EditorTool.Select;
        Drag(m, 22, 22, 22, 22, clicks: 2);
        Assert.Equal("hi", m.EditingText?.Text);
    }

    [Fact]
    public void ChangingToolCommitsTextAndClearsSelection()
    {
        var m = Model();
        m.Tool = EditorTool.Text;
        Drag(m, 20, 20, 20, 20);
        m.EditingText!.Text = "x";
        m.Tool = EditorTool.Arrow;
        Assert.Null(m.EditingText);
        Assert.Single(m.Annotations);
        Assert.Equal(EditorTool.Arrow, m.LastTool(ToolGroup.Shapes));
        m.Tool = EditorTool.Ellipse;
        Assert.Equal(EditorTool.Ellipse, m.LastTool(ToolGroup.Shapes));
        Assert.Equal(EditorTool.Pen, m.LastTool(ToolGroup.Draw));
    }

    [Fact]
    public void CropNeedsMoreThanEightPointsAndStaysInsideThePicture()
    {
        var m = Model();
        m.Tool = EditorTool.Crop;
        Drag(m, 10, 10, 15, 100);
        Assert.Null(m.Crop);
        Drag(m, 300, 200, 500, 400);
        Assert.Equal(new Area(300, 200, 100, 100), m.Crop);
        Assert.Empty(m.Annotations);
        m.ResetCrop();
        Assert.Null(m.Crop);
        m.Undo();
        Assert.NotNull(m.Crop);
    }

    [Fact]
    public void EraseFillsWithTheColorAroundIt()
    {
        var m = Model(p => p.X < 200 ? 0xFF102030 : 0xFFFFFFFF);
        m.Tool = EditorTool.Erase;
        Drag(m, 50, 50, 100, 100);
        Assert.Equal(0xFF102030u, m.Annotations[0].Fill);
        Assert.Equal(0xFFFFFFFFu, Model(_ => null).SurroundingColor(new Area(0, 0, 10, 10)));
    }

    [Fact]
    public void PickerTakesTheColorAndReportsItsHex()
    {
        var m = Model(_ => 0x00ABCDEF);
        string? picked = null;
        m.ColorPicked += hex => picked = hex;
        m.Tool = EditorTool.Picker;
        Drag(m, 5, 5, 5, 5);
        Assert.Equal("#ABCDEF", picked);
        Assert.Equal(0xFFABCDEFu, m.Color);
        Assert.Empty(m.Annotations);
    }

    [Fact]
    public void LoupeAClickPutsTheLensUpAndRight()
    {
        var m = Model();
        m.Tool = EditorTool.Loupe;
        m.StrokeWidth = 4;
        Drag(m, 100, 200, 101, 200);
        var loupe = Assert.Single(m.Annotations);
        Assert.Equal(64, loupe.LoupeRadius);
        Assert.Equal(new PointD(100 + 64 * 1.6, 200 - 64 * 1.6), loupe.LoupeCenter);
    }
}

public class EditorToolsTests
{
    [Fact]
    public void EveryToolHasItsOwnKeyAndGroup()
    {
        var tools = Enum.GetValues<EditorTool>();
        Assert.Equal(tools.Length, tools.Select(t => t.Key()).Distinct().Count());
        foreach (var tool in tools)
        {
            Assert.Equal(tool, EditorTools.ForKey(char.ToLowerInvariant(tool.Key())));
            Assert.Contains(tool, tool.Group().Tools());
        }
        Assert.Null(EditorTools.ForKey('Z'));
    }

    [Fact]
    public void ContrastAndHex()
    {
        Assert.Equal(0xFF000000u, EditorTools.Contrast(0xFFFFD61A));
        Assert.Equal(0xFFFFFFFFu, EditorTools.Contrast(0xFF3880FF));
        Assert.Equal("#FF4554", EditorTools.Hex(EditorTools.Palette[0]));
    }

    [Fact]
    public void ArrowHeadSitsAtTheTipAndCurvesOnlyWhenAsked()
    {
        var straight = AnnotationGeometry.Arrow(new(0, 0), new(100, 0), 4, false);
        Assert.Null(straight.Control);
        Assert.Equal(new PointD(100, 0), straight.Tip);
        // Head 4.2 × 4 = 16.8 long, its base 0.8 of that back from the tip.
        Assert.Equal(100 - 16.8 * 0.8, straight.Base.X, 6);
        Assert.Equal(16.8 * 0.85 / 2, straight.Left.Y, 6);
        Assert.NotNull(AnnotationGeometry.Arrow(new(0, 0), new(100, 0), 4, true).Control);
        // A short arrow's head is at most 0.6 of its length.
        Assert.Equal(20 - 12 * 0.8, AnnotationGeometry.Arrow(new(0, 0), new(20, 0), 4, false).Base.X, 6);
    }

    [Fact]
    public void SmoothGoesThroughMidpointsAndEndsAtTheLastPoint()
    {
        var segments = AnnotationGeometry.Smooth([new(0, 0), new(10, 0), new(10, 10), new(20, 10)]);
        Assert.Equal(3, segments.Count);
        Assert.Equal(new PointD(10, 5), segments[0].End);
        Assert.Equal(new PointD(10, 0), segments[0].Control);
        Assert.Equal(new PointD(20, 10), segments[^1].End);
        Assert.Single(AnnotationGeometry.Smooth([new(0, 0), new(1, 1)]));
        Assert.Empty(AnnotationGeometry.Smooth([new(0, 0)]));
    }

    [Fact]
    public void RulerLabelInPicturePixels()
    {
        Assert.Equal("150 px", AnnotationGeometry.RulerLabel(new(0, 0), new(100, 1), 1.5));
        Assert.Equal("50 px  (30 × 40)", AnnotationGeometry.RulerLabel(new(0, 0), new(30, 40), 1));
    }

    [Fact]
    public void PixelCropCoversTheAreaAndStaysInside()
    {
        Assert.Equal((15, 15, 76, 76), AnnotationGeometry.PixelCrop(new Area(10, 10, 50.3, 50.3), 1.5, 1000, 1000));
        Assert.Equal((90, 0, 10, 50), AnnotationGeometry.PixelCrop(new Area(60, -5, 100, 55), 1.5, 100, 50));
    }
}

public class RedactionTests
{
    private static byte[] Image(int w, int h, Func<int, int, byte> value)
    {
        var p = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                for (int c = 0; c < 4; c++) p[(y * w + x) * 4 + c] = value(x, y);
        return p;
    }

    [Fact]
    public void StrengthMatchesOrbit()
    {
        Assert.Equal(10, Redaction.BlurRadius(5, 800));
        Assert.Equal(48, Redaction.BlurRadius(5, 3840), 6);
        Assert.Equal(15, Redaction.PixelCell(5, 900));
        Assert.Equal(43, Redaction.PixelCell(5, 3840));
    }

    [Fact]
    public void BlurKeepsAFlatImageFlatAndSpreadsAnEdge()
    {
        var flat = Image(20, 10, (_, _) => 77);
        Assert.All(Redaction.Blur(flat, 20, 10, 80, 3), b => Assert.Equal(77, b));
        var edge = Image(40, 4, (x, _) => (byte)(x < 20 ? 0 : 200));
        var blurred = Redaction.Blur(edge, 40, 4, 160, 3);
        Assert.Equal(0, blurred[0]);
        Assert.Equal(200, blurred[39 * 4]);
        Assert.InRange(blurred[19 * 4], 60, 140);
        Assert.InRange(blurred[16 * 4], 1, 99);
        // The source is untouched.
        Assert.Equal(0, edge[19 * 4]);
    }

    [Fact]
    public void BoxSizesAreOddAndGrowWithSigma()
    {
        var sizes = Redaction.BoxSizes(10, 3);
        Assert.All(sizes, s => Assert.Equal(1, s % 2));
        Assert.True(Redaction.BoxSizes(20, 3).Sum() > sizes.Sum());
    }

    [Fact]
    public void PixelateAveragesEachCellIncludingPartialOnes()
    {
        var p = Image(3, 2, (x, y) => (byte)(x * 10 + y * 100));
        var cells = Redaction.Pixelate(p, 3, 2, 12, 2);
        // Cell (0..1, 0..1): 0, 10, 100, 110 → 55. Partial column 2: 20, 120 → 70.
        Assert.Equal(55, cells[0]);
        Assert.Equal(55, cells[(1 * 3 + 1) * 4]);
        Assert.Equal(70, cells[2 * 4]);
        Assert.Equal(70, cells[(1 * 3 + 2) * 4]);
    }
}
