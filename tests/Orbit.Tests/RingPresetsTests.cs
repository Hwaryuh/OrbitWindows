using Orbit.Core.Actions;
using Orbit.Core.Ring;
using Orbit.Core.Settings;

namespace Orbit.Tests;

public sealed class RingPresetsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "orbit-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Preferences _prefs;

    public RingPresetsTests() => _prefs = new Preferences(Path.Combine(_folder, "settings.json"), Pref.Defaults());

    public void Dispose()
    {
        _prefs.Dispose();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly OrbitAction[] Four = [OrbitAction.Clipboard, OrbitAction.Launcher, OrbitAction.Translate, OrbitAction.KeepAwake];

    [Fact]
    public void ResizeDropsTheLastOrAddsActionsNotOnTheRing()
    {
        Assert.Equal(Four[..3], RingPresets.Resize(Four, 3));
        var five = RingPresets.Resize(Four, 5);
        Assert.Equal(5, five.Count);
        Assert.Equal(Four, five.Take(4));
        // The first default not on the ring yet.
        Assert.Equal(OrbitAction.CaptureArea, five[4]);
        var twelve = RingPresets.Resize(Four, 99);
        Assert.Equal(RingSlots.Max, twelve.Count);
        Assert.Equal(twelve.Count, twelve.Distinct().Count());
        Assert.DoesNotContain(OrbitAction.Settings, twelve);
        Assert.Equal(RingSlots.Min, RingPresets.Resize(Four, 0).Count);
    }

    [Fact]
    public void AssignAndSwapIgnoreSlotsThatAreNotThere()
    {
        Assert.Equal(OrbitAction.Apps, RingPresets.Assign(Four, 2, OrbitAction.Apps)[2]);
        Assert.Same(Four, RingPresets.Assign(Four, 4, OrbitAction.Apps));
        Assert.Equal([OrbitAction.Translate, OrbitAction.Launcher, OrbitAction.Clipboard, OrbitAction.KeepAwake], RingPresets.Swap(Four, 0, 2));
        Assert.Same(Four, RingPresets.Swap(Four, 1, 1));
        Assert.Same(Four, RingPresets.Swap(Four, -1, 2));
    }

    [Theory]
    [InlineData(0, 8, 0)]
    [InlineData(1, 8, 1)]
    [InlineData(2, 4, 4)]
    [InlineData(3, 4, 6)]
    [InlineData(3, 12, 2)]
    [InlineData(1, 12, null)]
    [InlineData(1, 5, null)]
    public void CompassPointsOnlyOnTheEighths(int index, int count, int? compass)
    {
        Assert.Equal(compass, RingPresets.Compass(index, count));
    }

    [Fact]
    public void EditingChangesMyRingButAPresetDoesNot()
    {
        var presets = new RingPresets(_prefs);
        Assert.Equal(RingSlots.Default, presets.Current);
        Assert.Equal(RingSlots.Default, presets.Custom);

        presets.Edit(Four);
        Assert.Equal(Four, presets.Current);
        Assert.Equal(Four, presets.Custom);

        var screenshots = RingPresets.BuiltIn[1].Slots;
        presets.Apply(screenshots);
        Assert.Equal(screenshots, presets.Current);
        Assert.Equal(Four, presets.Custom);
    }

    [Fact]
    public void TheFirstPresetKeepsTheRingInUseAsMyRing()
    {
        _prefs.Set(Pref.RingSlots, RingSlots.Ids(Four));
        var presets = new RingPresets(_prefs);
        presets.Apply(RingPresets.BuiltIn[1].Slots);
        Assert.Equal(Four, presets.Custom);
    }

    [Fact]
    public void RingsOutsideThreeToTwelveAreNotStored()
    {
        var presets = new RingPresets(_prefs);
        presets.Edit([OrbitAction.Clipboard]);
        presets.Apply([]);
        presets.Save("x", [OrbitAction.Clipboard, OrbitAction.Apps], "fallback");
        Assert.Equal(RingSlots.Default, presets.Current);
        Assert.Empty(presets.Saved);
    }

    [Fact]
    public void SavedPresetsRenameUpdateDelete()
    {
        var presets = new RingPresets(_prefs);
        presets.Save("  ", Four, "My preset");
        presets.Save("Work", RingSlots.Default, "My preset");
        var saved = presets.Saved;
        Assert.Equal(["My preset", "Work"], saved.Select(s => s.Name));

        presets.Rename(saved[0].Id, "Home");
        presets.Rename(saved[1].Id, "   ");
        presets.Update(saved[1].Id, Four);
        saved = presets.Saved;
        Assert.Equal(["Home", "Work"], saved.Select(s => s.Name));
        Assert.Equal(Four, saved[1].Slots);

        presets.Delete(saved[0].Id);
        Assert.Equal(["Work"], presets.Saved.Select(s => s.Name));
    }

    [Fact]
    public void UnreadableStoredRingsAreLeftOut()
    {
        _prefs.Set(RingPresets.SavedKey, new[]
        {
            new RingPresets.Stored("a", "Gone", ["clipboard", "noSuchAction", "launcher", "apps"]),
            new RingPresets.Stored("b", "Fine", ["clipboard", "launcher", "apps"]),
        });
        _prefs.Set(RingPresets.CustomKey, new[] { "clipboard" });
        var presets = new RingPresets(_prefs);
        Assert.Equal(["Fine"], presets.Saved.Select(s => s.Name));
        Assert.Equal(presets.Current, presets.Custom);
        Assert.Null(RingSlots.TryParse(["clipboard", "noSuchAction", "launcher", "apps"]));
        Assert.Equal(3, RingSlots.Parse(["clipboard", "noSuchAction", "launcher", "apps"]).Count);
    }
}
