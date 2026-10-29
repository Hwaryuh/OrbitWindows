using Orbit.Core.Clipboard;

namespace Orbit.Tests;

public class UrlCleanerTests
{
    private static readonly UrlCleaner Cleaner = new();

    [Fact]
    public void TrackersGoAndEverythingElseStaysAsWritten()
    {
        var r = Cleaner.Clean("https://shop.example.com/p?id=42&utm_source=news&Color=Red%20Blue&fbclid=abc#reviews")!;
        Assert.Equal("https://shop.example.com/p?id=42&Color=Red%20Blue#reviews", r.Url);
        Assert.Equal(["utm_source", "fbclid"], r.Removed);
    }

    [Fact]
    public void OnlyTrackersLeavesNoQuestionMark()
    {
        Assert.Equal("https://example.com/a#top", Cleaner.Clean("https://example.com/a?gclid=1&utm_medium=x#top")!.Url);
    }

    [Fact]
    public void NamesAreDecodedAndBareNamesCount()
    {
        var r = Cleaner.Clean("https://example.com/?%75tm_source=x&fbclid&q=1")!;
        Assert.Equal("https://example.com/?q=1", r.Url);
        Assert.Equal(["utm_source", "fbclid"], r.Removed);
        Assert.NotNull(Cleaner.Clean("https://example.com/?UTM_Campaign=a"));
    }

    [Fact]
    public void SiteRulesApplyOnTheirSitesAndSubdomainsOnly()
    {
        Assert.Equal("https://youtu.be/abc?t=42", Cleaner.Clean("https://youtu.be/abc?si=XYZ&t=42")!.Url);
        Assert.Equal("https://www.youtube.com/watch?v=1", Cleaner.Clean("https://www.youtube.com/watch?v=1&si=a&feature=share")!.Url);
        Assert.Equal("https://x.com/a/status/1", Cleaner.Clean("https://x.com/a/status/1?s=20&t=abc")!.Url);
        // t is the playback time on YouTube and si is data elsewhere: kept.
        Assert.Null(Cleaner.Clean("https://example.com/?si=1&t=2"));
        Assert.Null(Cleaner.Clean("https://notyoutube.com/?si=1"));
    }

    [Theory]
    [InlineData("see https://example.com/?utm_source=x")]
    [InlineData("https://example.com/?utm_source=x https://b.com")]
    [InlineData("ftp://example.com/?utm_source=x")]
    [InlineData("example.com/?utm_source=x")]
    [InlineData("https://example.com/?id=1")]
    [InlineData("https://example.com/")]
    [InlineData("")]
    [InlineData("https://example.com/#section?utm_source=x")]
    public void OnlyALoneLinkWithTrackersChanges(string text) => Assert.Null(Cleaner.Clean(text));

    [Fact]
    public void SurroundingWhitespaceIsTrimmed() =>
        Assert.Equal("https://example.com/", Cleaner.Clean("  https://example.com/?utm_source=x\r\n")!.Url);

    [Fact]
    public void UserEditsAddAndSwitchOffNames()
    {
        var mine = new UrlCleaner(added: ["ref"], disabled: ["fbclid", "si", "utm_*"]);
        Assert.Equal("https://example.com/?fbclid=1&utm_source=2", mine.Clean("https://example.com/?fbclid=1&utm_source=2&ref=hn")!.Url);
        Assert.Null(mine.Clean("https://youtu.be/a?si=1"));
        Assert.Null(mine.Clean("https://example.com/?fbclid=1"));
    }

    [Fact]
    public void HostMatching()
    {
        Assert.True(UrlCleaner.OnHost("m.youtube.com", "youtube.com"));
        Assert.True(UrlCleaner.OnHost("youtube.com", "youtube.com"));
        Assert.False(UrlCleaner.OnHost("fakeyoutube.com", "youtube.com"));
    }
}
