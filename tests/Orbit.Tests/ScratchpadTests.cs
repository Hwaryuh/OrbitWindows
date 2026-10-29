using Orbit.Core.Scratchpad;

namespace Orbit.Tests;

public class ScratchpadTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static string Name(int n) => $"Note {n}";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "orbit-scratch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public void ClosingKeepsTextToReopen()
    {
        var doc = ScratchpadDocument.New(Name(1), Now);
        var first = doc.Current.Id;
        doc.Update(first, p => p with { Text = "keep me" });
        var second = doc.Add(doc.NextName(Name), Now);
        Assert.Equal("Note 2", second.Name);
        Assert.False(doc.Close(Guid.NewGuid()));
        Assert.True(doc.Close(first));
        Assert.Equal(second.Id, doc.Selected);
        Assert.False(doc.Close(second.Id)); // the last tab stays
        var back = doc.Reopen();
        Assert.Equal("keep me", back!.Text);
        Assert.Equal(back.Id, doc.Selected);
        Assert.Null(doc.Reopen());
    }

    [Fact]
    public void EmptyTabsCloseWithoutATrace()
    {
        var doc = ScratchpadDocument.New(Name(1), Now);
        var empty = doc.Add(Name(2), Now);
        doc.Close(empty.Id);
        Assert.Empty(doc.Closed);
    }

    [Fact]
    public void ClosedListIsCapped()
    {
        var doc = ScratchpadDocument.New(Name(1), Now);
        for (int i = 0; i < ScratchpadDocument.ClosedLimit + 5; i++)
        {
            var pad = doc.Add(Name(i + 2), Now);
            doc.Update(pad.Id, p => p with { Text = $"t{i}" });
            doc.Close(pad.Id);
        }
        Assert.Equal(ScratchpadDocument.ClosedLimit, doc.Closed.Count);
        Assert.Equal($"t{ScratchpadDocument.ClosedLimit + 4}", doc.Closed[0].Text);
    }

    [Fact]
    public void RetentionClosesOnlyOldUnselectedTabs()
    {
        var doc = ScratchpadDocument.New(Name(1), Now.AddDays(-30));
        var old = doc.Add(Name(2), Now.AddDays(-10));
        doc.Update(old.Id, p => p with { Text = "old" });
        var future = doc.Add(Name(3), Now.AddDays(5));
        var fresh = doc.Add(Name(4), Now.AddHours(-1));
        Assert.Equal(0, doc.ApplyRetention(0, Now));
        // The first tab (empty, 30 days) and the old one go; only the one with text is kept to reopen.
        Assert.Equal(2, doc.ApplyRetention(7, Now));
        Assert.DoesNotContain(doc.Pads, p => p.Id == old.Id);
        Assert.Equal("old", Assert.Single(doc.Closed).Text);
        Assert.Contains(doc.Pads, p => p.Id == future.Id);
        Assert.Contains(doc.Pads, p => p.Id == fresh.Id);
    }

    [Fact]
    public void RetentionNeverPushesNotesOutOfTheClosedList()
    {
        var doc = ScratchpadDocument.New(Name(1), Now);
        for (int i = 0; i < ScratchpadDocument.ClosedLimit; i++)
            doc.Closed.Add(new Pad(Guid.NewGuid(), $"c{i}", "closed", Now));
        var old = doc.Add(Name(2), Now.AddDays(-40));
        doc.Update(old.Id, p => p with { Text = "still here" });
        doc.Selected = doc.Pads[0].Id;
        Assert.Equal(0, doc.ApplyRetention(7, Now));
        Assert.Contains(doc.Pads, p => p.Id == old.Id);
        Assert.Equal(ScratchpadDocument.ClosedLimit, doc.Closed.Count);
        Assert.Contains("still here", doc.AllText());
    }

    [Fact]
    public void SanitizeRepairsBadDocuments()
    {
        var id = Guid.NewGuid();
        var bad = new ScratchpadDocument { Pads = [new Pad(id, " ", null!, Now), new Pad(id, "B", "x", Now)], Selected = Guid.NewGuid() };
        var clean = bad.Sanitized(Name, Now);
        Assert.Equal(2, clean.Pads.Select(p => p.Id).Distinct().Count());
        Assert.Equal("Note 1", clean.Pads[0].Name);
        Assert.Equal("", clean.Pads[0].Text);
        Assert.Equal(clean.Pads[0].Id, clean.Selected);
        Assert.Single(new ScratchpadDocument().Sanitized(Name, Now).Pads);
    }

    [Theory]
    // "abc ABC abc": matches at 0, 4, 8.
    [InlineData(0, 0, true, 0)]
    [InlineData(0, 3, true, 4)]
    [InlineData(8, 3, true, 0)]
    [InlineData(4, 3, false, 0)]
    [InlineData(0, 3, false, 8)]
    [InlineData(11, 0, false, 8)]
    public void SearchMovesFromTheSelectionAndWraps(int start, int length, bool forward, int expected) =>
        Assert.Equal(expected, ScratchpadSearch.Next("abc ABC abc", "abc", start, length, forward));

    [Fact]
    public void SearchWithoutMatches()
    {
        Assert.Equal(-1, ScratchpadSearch.Next("abc", "x", 0, 0, true));
        Assert.Equal(-1, ScratchpadSearch.Next("abc", "", 0, 0, true));
        Assert.Equal(-1, ScratchpadSearch.Next("", "a", 0, 0, false));
    }

    [Fact]
    public void StoreRoundTrips()
    {
        var store = new ScratchpadStore(Path.Combine(_folder, "scratchpad.json"));
        var doc = store.Load(Name, Now);
        Assert.True(store.CanSave);
        doc.Update(doc.Selected, p => p with { Text = "안녕\nline" });
        Assert.True(store.Save(doc));
        var again = new ScratchpadStore(store.Path).Load(Name, Now);
        Assert.Equal("안녕\nline", again.Current.Text);
    }

    [Fact]
    public void UnreadableFileIsMovedAsideNotOverwritten()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "scratchpad.json");
        File.WriteAllText(path, "{ not json");
        var store = new ScratchpadStore(path);
        var doc = store.Load(Name, Now);
        Assert.True(store.CanSave);
        Assert.Single(doc.Pads);
        var aside = Assert.Single(Directory.GetFiles(_folder, "scratchpad.corrupt-*.json"));
        Assert.Equal("{ not json", File.ReadAllText(aside));
    }

    [Fact]
    public void LockedFileIsNeverSavedOver()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "scratchpad.json");
        File.WriteAllText(path, "{}");
        var store = new ScratchpadStore(path);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var doc = store.Load(Name, Now);
            Assert.False(store.CanSave);
            Assert.False(store.Save(doc));
        }
        Assert.Equal("{}", File.ReadAllText(path));
    }
}
