using Orbit.Core.Capture;
using Orbit.Core.Display;
using Orbit.Core.Settings;

namespace Orbit.Tests;

public class SelectionGeometryTests
{
    private static readonly Area Screen = new(0, 0, 1000, 800);

    [Fact]
    public void DragSpansAnchorAndPointerInAnyDirection()
    {
        var g = new SelectionGeometry(500, 400);
        g.Move(600, 450, carrying: false, aspect: null, fromCenter: false, Screen);
        Assert.Equal(new Area(500, 400, 100, 50), g.Rect(null, false, Screen));
        g.Move(450, 300, carrying: false, aspect: null, fromCenter: false, Screen);
        Assert.Equal(new Area(450, 300, 50, 100), g.Rect(null, false, Screen));
    }

    [Fact]
    public void AspectGrowsToHoldTheTipAndFromCenterDoubles()
    {
        var g = new SelectionGeometry(100, 100);
        g.Move(200, 120, false, 1, false, Screen);
        Assert.Equal(new Area(100, 100, 100, 100), g.Rect(1, false, Screen));
        Assert.Equal(new Area(0, 80, 200, 40), g.Rect(null, true, Screen));
    }

    [Fact]
    public void SelectionStaysOnTheMonitorAndKeepsItsRatioAtAnEdge()
    {
        var g = new SelectionGeometry(900, 700);
        g.Move(2000, 2000, false, null, false, Screen);
        Assert.Equal(new Area(900, 700, 100, 100), g.Rect(null, false, Screen));
        // 16:9 from (900, 700): 100 px of room to the right, 100 down: the width limits it.
        var r = g.Rect(16.0 / 9, false, Screen);
        Assert.Equal(100, r.Width, 6);
        Assert.Equal(56.25, r.Height, 6);
    }

    [Fact]
    public void CarryingMovesTheWholeSelectionUntilAnEdge()
    {
        var g = new SelectionGeometry(100, 100);
        g.Move(200, 200, false, null, false, Screen);
        g.Move(150, 150, carrying: true, null, false, Screen);
        Assert.Equal(new Area(50, 50, 100, 100), g.Rect(null, false, Screen));
        g.Move(-500, -500, carrying: true, null, false, Screen);
        Assert.Equal(new Area(0, 0, 100, 100), g.Rect(null, false, Screen));
    }

    [Fact]
    public void NudgeMovesTheTipWithinTheMonitor()
    {
        var g = new SelectionGeometry(10, 10);
        g.Move(20, 20, false, null, false, Screen);
        g.Nudge(-1, 10, Screen);
        Assert.Equal((19.0, 30.0), g.Tip);
        g.Nudge(5000, 0, Screen);
        Assert.Equal(1000, g.Tip.X);
    }

    [Fact]
    public void PresetsCycleBothWays()
    {
        Assert.Equal(AspectPreset.Square, AspectPreset.Free.Next());
        Assert.Equal(AspectPreset.NineSixteen, AspectPreset.Free.Next(backward: true));
        Assert.Null(AspectPreset.Free.Ratio());
        Assert.Equal("16:9", AspectPreset.SixteenNine.Label());
        Assert.Equal("#0A0BFF", SelectionGeometry.Hex(10, 11, 255));
    }
}

public sealed class CaptureStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "orbit-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Preferences _prefs;

    public CaptureStoreTests() => _prefs = new Preferences(Path.Combine(_folder, "settings.json"), Pref.Defaults());

    public void Dispose()
    {
        _prefs.Dispose();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void FilesAreNamedAsOrbitAndNeverOverwritten()
    {
        var when = new DateTime(2026, 10, 30, 14, 5, 9);
        Assert.Equal("Orbit 2026-10-30 at 14.05.09.png", CaptureStore.FileName(when));
        var store = new CaptureStore(_prefs, Path.Combine(_folder, "Captures"));
        var first = store.Write([1, 2, 3], when);
        var second = store.Write([4], when);
        Assert.EndsWith("at 14.05.09.png", first);
        Assert.EndsWith("at 14.05.09 2.png", second);
        Assert.Equal([second, first], store.Recent);
    }

    [Fact]
    public void SavingIntoTheLibraryItselfMakesNoCopy()
    {
        var library = Path.Combine(_folder, "Captures");
        _prefs.Set(Pref.CaptureSaveFolder, library + "\\");
        var store = new CaptureStore(_prefs, library);
        var file = store.Write([1], new DateTime(2026, 10, 30, 1, 2, 3));
        Assert.Equal(file, store.SaveCopy(file));
        Assert.Single(Directory.GetFiles(library));
    }

    [Fact]
    public void ForgetTakesAFileOffTheRecentList()
    {
        var store = new CaptureStore(_prefs, Path.Combine(_folder, "Captures"));
        var when = new DateTime(2026, 10, 30, 14, 5, 9);
        var first = store.Write([1], when);
        var second = store.Write([2], when);
        store.Forget(first.ToUpperInvariant());
        Assert.Equal([second], store.Recent);
    }

    [Fact]
    public void LastAreaRoundTripsAndOnlyCountsOnItsMonitor()
    {
        var store = new CaptureStore(_prefs, Path.Combine(_folder, "Captures"));
        Assert.Null(store.LastArea);
        store.LastArea = new LastArea(new PixelRect(-1000, 20, 300, 200), @"\\.\DISPLAY2");
        Assert.Equal(new LastArea(new PixelRect(-1000, 20, 300, 200), @"\\.\DISPLAY2"), store.LastArea);

        MonitorInfo Monitor(string name, PixelRect bounds) => new(0, name, bounds, bounds, 96, false);
        var left = Monitor(@"\\.\DISPLAY2", new PixelRect(-1080, 0, 1080, 1920));
        var main = Monitor(@"\\.\DISPLAY1", new PixelRect(0, 0, 1920, 1080));
        Assert.Equal(new PixelRect(-1000, 20, 300, 200), CaptureStore.Usable(store.LastArea, [main, left]));
        Assert.Null(CaptureStore.Usable(store.LastArea, [main]));
        Assert.Null(CaptureStore.Usable(store.LastArea, [main, Monitor(@"\\.\DISPLAY2", new PixelRect(-500, 0, 500, 800))]));
    }

    [Fact]
    public void HalvingAveragesFourPixels()
    {
        byte[] pixels = [0, 0, 0, 0, 40, 40, 40, 40, 80, 80, 80, 80, 120, 120, 120, 120];
        var frame = new ScreenFrame(pixels, 2, 2, 8, new PixelRect(0, 0, 2, 2), "GDI");
        var half = TextRecognition.Halve(frame);
        Assert.Equal((1, 1), (half.Width, half.Height));
        Assert.Equal([60, 60, 60, 60], half.Pixels);
    }
}

public sealed class CaptureLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orbit-library-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string File(string folder, string name, DateTime created)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        System.IO.File.WriteAllBytes(path, [1]);
        System.IO.File.SetCreationTime(path, created);
        return path;
    }

    [Fact]
    public void ListsTheLibraryAndOrbitFilesFromTheSaveFolderNewestFirst()
    {
        var a = File("lib", "Orbit 1.png", new DateTime(2026, 10, 1));
        var b = File("lib", "Other.jpg", new DateTime(2026, 10, 3));
        File("lib", "notes.txt", new DateTime(2026, 10, 4));
        var c = File("save", "Orbit 2.png", new DateTime(2026, 10, 2));
        File("save", "Holiday.png", new DateTime(2026, 10, 5));
        // A saved copy of a library file is the same picture: listed once, from the library.
        File("save", "Orbit 1.png", new DateTime(2026, 10, 6));
        var list = CaptureLibrary.List(Path.Combine(_root, "lib"), Path.Combine(_root, "save"));
        Assert.Equal([b, c, a], list.Select(e => e.Path));
    }

    [Fact]
    public void TheSameFolderTwiceAndMissingFoldersAreFine()
    {
        File("lib", "Orbit 1.png", new DateTime(2026, 10, 1));
        File("lib", "Orbit 2.png", new DateTime(2026, 10, 2));
        var lib = Path.Combine(_root, "lib");
        Assert.Equal(2, CaptureLibrary.List(lib, lib.ToUpperInvariant() + Path.DirectorySeparatorChar).Count);
        Assert.Equal(2, CaptureLibrary.List(lib, Path.Combine(_root, "missing")).Count);
        Assert.Empty(CaptureLibrary.List(Path.Combine(_root, "missing"), null));
        Assert.Single(CaptureLibrary.List(lib, null, limit: 1));
        Assert.True(CaptureLibrary.SameFolder(lib, lib + "\\"));
        Assert.False(CaptureLibrary.SameFolder(lib, Path.Combine(_root, "save")));
    }
}

public class BarcodeAndSoundTests
{
    [Fact]
    public void AQrCodeInACaptureIsRead()
    {
        var writer = new ZXing.BarcodeWriterPixelData
        {
            Format = ZXing.BarcodeFormat.QR_CODE,
            Options = new ZXing.QrCode.QrCodeEncodingOptions { Width = 200, Height = 200, Margin = 2, CharacterSet = "UTF-8" },
        };
        var data = writer.Write("https://example.com/오르빗");
        var frame = new ScreenFrame(data.Pixels, data.Width, data.Height, data.Width * 4, new PixelRect(0, 0, data.Width, data.Height), "GDI");
        Assert.Equal(["https://example.com/오르빗"], Barcodes.Read(frame));
    }

    [Fact]
    public void APlainPictureHasNoCode()
    {
        var pixels = Enumerable.Repeat((byte)200, 64 * 64 * 4).ToArray();
        Assert.Empty(Barcodes.Read(new ScreenFrame(pixels, 64, 64, 256, new PixelRect(0, 0, 64, 64), "GDI")));
    }

    [Fact]
    public void ShutterIsAShortPcmWave()
    {
        var wave = ShutterSound.Wave();
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(wave.Length - 8, BitConverter.ToInt32(wave, 4));
        int samples = (wave.Length - 44) / 2;
        Assert.InRange(samples / (double)ShutterSound.SampleRate, 0.1, 0.3);
        Assert.Contains(Enumerable.Range(0, samples), i => Math.Abs(BitConverter.ToInt16(wave, 44 + i * 2)) > 3000);
        Assert.Same(wave, ShutterSound.Wave());
    }
}
