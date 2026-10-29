using Orbit.Core.Clipboard;

namespace Orbit.Tests;

public class ClipItemTests
{
    [Theory]
    [InlineData("#fff", ClipKind.Color)]
    [InlineData(" #1A2B3C ", ClipKind.Color)]
    [InlineData("1A2B3C", ClipKind.Text)]
    [InlineData("#12345", ClipKind.Text)]
    [InlineData("https://example.com/a?b=1", ClipKind.Link)]
    [InlineData("http://localhost:3000", ClipKind.Link)]
    [InlineData("ftp://example.com", ClipKind.Text)]
    [InlineData("see https://example.com", ClipKind.Text)]
    [InlineData("https://a.com\nhttps://b.com", ClipKind.Text)]
    [InlineData("hello", ClipKind.Text)]
    public void ClassifyAsOrbit(string text, ClipKind kind) => Assert.Equal(kind, ClipItem.Classify(text));

    [Fact]
    public void ColorValuesAndSignatures()
    {
        Assert.Equal(0xFFFFAA00u, ClipItem.ColorValue("#fa0"));
        Assert.Equal(0xFF102030u, ClipItem.ColorValue("#102030"));
        Assert.Null(ClipItem.ColorValue("red"));
        Assert.Equal(24, ClipItem.SignatureOf("x").Length);
        Assert.Equal(ClipItem.SignatureOf("same"), ClipItem.SignatureOf("same"));
        Assert.NotEqual(ClipItem.SignatureOf("a"), ClipItem.SignatureOf("b"));
    }

    [Fact]
    public void CodeLooksLikeCode()
    {
        Assert.True(Text("const x = () => { return 1; }").LooksLikeCode);
        Assert.False(Text("Dear team; thanks").LooksLikeCode);
    }

    internal static ClipItem Text(string text, bool pinned = false) =>
        new() { Kind = ClipItem.Classify(text), Text = text, Signature = ClipItem.SignatureOf(text), Pinned = pinned };
}

public sealed class ClipHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orbit-clips-" + Guid.NewGuid().ToString("N"));
    private int _limit = 500;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ClipHistory History()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Clips"));
        return new ClipHistory(Path.Combine(_root, "clipboard.json"), Path.Combine(_root, "Clips"), () => _limit);
    }

    private string SideFile(string name, int bytes = 10)
    {
        System.IO.File.WriteAllBytes(Path.Combine(_root, "Clips", name), new byte[bytes]);
        return name;
    }

    [Fact]
    public void CopyingAgainMovesToTopAndKeepsThePin()
    {
        var h = History();
        var first = h.Insert(ClipItemTests.Text("one"));
        h.Insert(ClipItemTests.Text("two"));
        h.TogglePin(first.Id);
        var again = h.Insert(ClipItemTests.Text("one"));
        Assert.Equal(first.Id, again.Id);
        Assert.True(again.Pinned);
        Assert.Equal(["one", "two"], h.Items.Select(i => i.Text));
    }

    [Fact]
    public void TrimKeepsPinnedAndAtLeastTwentyUnpinned()
    {
        _limit = 5;
        var h = History();
        var pinned = h.Insert(ClipItemTests.Text("pinned"));
        h.TogglePin(pinned.Id);
        for (int i = 0; i < 30; i++) h.Insert(ClipItemTests.Text($"item {i}"));
        Assert.Equal(21, h.Items.Count);
        Assert.Contains(h.Items, i => i.Text == "pinned");
        Assert.Equal("item 29", h.Items[0].Text);
    }

    [Fact]
    public void TrimmedAndDeletedItemsTakeTheirFiles()
    {
        _limit = 0;
        var h = History();
        var image = h.Insert(new ClipItem { Kind = ClipKind.Image, ImageFile = SideFile("a.png"), Signature = "img" });
        for (int i = 0; i < 20; i++) h.Insert(ClipItemTests.Text($"t{i}"));
        Assert.False(System.IO.File.Exists(h.PathOf("a.png")));
        Assert.Null(h.Find(image.Id));
        var rich = h.Insert(ClipItemTests.Text("rich") with { RtfFile = SideFile("r.rtf") });
        h.Delete(rich.Id);
        Assert.False(System.IO.File.Exists(h.PathOf("r.rtf")));
    }

    [Fact]
    public void ACopyAgainWithNewRichTextReplacesTheOldFile()
    {
        var h = History();
        h.Insert(ClipItemTests.Text("x") with { RtfFile = SideFile("old.rtf") });
        var item = h.Insert(ClipItemTests.Text("x") with { RtfFile = SideFile("new.rtf") });
        Assert.Equal("new.rtf", item.RtfFile);
        Assert.False(System.IO.File.Exists(h.PathOf("old.rtf")));
        // Copied again without rich text: the stored one stays.
        Assert.Equal("new.rtf", h.Insert(ClipItemTests.Text("x")).RtfFile);
    }

    [Fact]
    public void SearchFiltersByKindPinAndWords()
    {
        var h = History();
        h.Insert(ClipItemTests.Text("Hello world"));
        var link = h.Insert(ClipItemTests.Text("https://example.com"));
        h.Insert(new ClipItem { Kind = ClipKind.Image, OcrText = "invoice total", Signature = "i" });
        h.TogglePin(link.Id);
        Assert.Single(h.Search("WORLD", ClipFilter.All));
        Assert.Single(h.Search("invoice", ClipFilter.All));
        Assert.Equal(link.Id, Assert.Single(h.Search("", ClipFilter.Pinned)).Id);
        Assert.Single(h.Search("", new ClipFilter(Kind: ClipKind.Image)));
        Assert.Equal(3, h.Search("  ", ClipFilter.All).Count);
    }

    [Fact]
    public void FiltersCycleBothWays()
    {
        Assert.Equal(ClipFilter.Pinned, ClipFilter.All.Next());
        Assert.Equal(new ClipFilter(Kind: ClipKind.Color), ClipFilter.All.Next(backward: true));
        Assert.Equal(ClipFilter.All, new ClipFilter(Kind: ClipKind.Color).Next());
    }

    [Fact]
    public void ClearKeepsPinnedAndUsedGoesToTop()
    {
        var h = History();
        var a = h.Insert(ClipItemTests.Text("a"));
        h.Insert(ClipItemTests.Text("b"));
        h.MarkUsed(a.Id);
        Assert.Equal("a", h.Items[0].Text);
        h.TogglePin(a.Id);
        h.Clear();
        Assert.Equal(["a"], h.Items.Select(i => i.Text));
    }

    [Fact]
    public void SavedHistoryLoadsBackAndADamagedFileIsSetAside()
    {
        var h = History();
        var item = h.Insert(ClipItemTests.Text("keep me") with { AppName = "Notepad", AppProcess = "notepad.exe" });
        h.TogglePin(item.Id);
        var older = h.Snapshot();
        h.Write(h.Snapshot());
        // A save that started earlier and runs later changes nothing.
        h.Delete(item.Id);
        h.Write(older);
        var loaded = History();
        var back = Assert.Single(loaded.Items);
        Assert.Equal(("keep me", true, "notepad.exe", ClipKind.Text), (back.Text, back.Pinned, back.AppProcess, back.Kind));

        System.IO.File.WriteAllText(Path.Combine(_root, "clipboard.json"), "{ not json");
        Assert.Empty(History().Items);
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "clipboard.json.damaged")));
    }

    [Fact]
    public void BudgetDropsTheOldestUnpinnedPicturesAndOrphansGo()
    {
        var h = History();
        var old = h.Insert(new ClipItem { Kind = ClipKind.Image, ImageFile = SideFile("old.png", 600), Signature = "o" });
        var pinned = h.Insert(new ClipItem { Kind = ClipKind.Image, ImageFile = SideFile("pin.png", 600), Signature = "p" });
        h.TogglePin(pinned.Id);
        h.Insert(new ClipItem { Kind = ClipKind.Image, ImageFile = SideFile("new.png", 300), Signature = "n" });
        Assert.True(h.EnforceBudget(1000));
        Assert.Null(h.Find(old.Id));
        Assert.NotNull(h.Find(pinned.Id));
        Assert.False(h.EnforceBudget(100));
        Assert.NotNull(h.Find(pinned.Id));

        SideFile("stray.png");
        Assert.Equal(1, h.RemoveOrphans());
        Assert.True(System.IO.File.Exists(h.PathOf("pin.png")));
    }
}

public class ClipboardRulesTests
{
    [Fact]
    public void PasswordManagerMarkersAndOwnWrites()
    {
        Assert.True(ClipboardRules.IsPrivate([ClipboardRules.ExcludeFormat, "UnicodeText"], null));
        Assert.True(ClipboardRules.IsPrivate(["UnicodeText"], 0));
        Assert.False(ClipboardRules.IsPrivate(["UnicodeText"], 1));
        Assert.False(ClipboardRules.IsPrivate(["UnicodeText"], null));
        Assert.True(ClipboardRules.IsOwn([ClipboardRules.OwnFormat]));
    }

    [Fact]
    public void IgnoredAppsPauseAndImageSize()
    {
        Assert.True(ClipboardRules.IsIgnored("KeePass.exe", ["keepass.exe"]));
        Assert.False(ClipboardRules.IsIgnored(null, ["keepass.exe"]));
        var now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);
        Assert.True(ClipboardRules.IsPaused(1_000_100, now));
        Assert.False(ClipboardRules.IsPaused(0, now));
        Assert.True(ClipboardRules.ImageFits(3840, 2160));
        Assert.False(ClipboardRules.ImageFits(8000, 8000));
        Assert.False(ClipboardRules.ImageFits(0, 10));
    }
}
