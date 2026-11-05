using Orbit.Core.Input;

namespace Orbit.Tests;

public class ShortcutMapTests
{
    private const Modifiers CtrlAlt = Modifiers.Ctrl | Modifiers.Alt;
    private static readonly Hotkey A = new(CtrlAlt, 'A'), B = new(CtrlAlt, 'B'), C = new(CtrlAlt, 'C');

    [Fact]
    public void ADifferenceSurvivesASave()
    {
        var saved = ShortcutMap.Serialize(new Dictionary<string, Hotkey?> { ["one"] = A, ["two"] = null });
        var back = ShortcutMap.Parse(saved);
        Assert.Equal(A, back["one"]);
        Assert.True(back.ContainsKey("two"));
        Assert.Null(back["two"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void UnreadableSettingsMeanNoChanges(string? json) => Assert.Empty(ShortcutMap.Parse(json));

    [Theory]
    [InlineData("""{"one":"x"}""")]
    [InlineData("""{"one":"3"}""")]
    [InlineData("""{"one":"3:0"}""")]
    [InlineData("""{"one":"3:999"}""")]
    [InlineData("""{"one":"99:65"}""")]
    [InlineData("""{"one":"3:162"}""")] // a modifier as the key
    [InlineData("""{"one":"0:65"}""")] // a bare letter
    [InlineData("""{"one":"3:46"}""")] // Ctrl+Alt+Del
    public void ABadEntryIsDroppedAndTheDefaultStays(string json)
    {
        var overrides = ShortcutMap.Parse(json);
        Assert.Empty(overrides);
        Assert.Equal(A, ShortcutMap.Resolve([new ShortcutSlot("one", A)], overrides)["one"]);
    }

    [Fact]
    public void DefaultsApplyUntilChanged()
    {
        var map = ShortcutMap.Resolve([new ShortcutSlot("one", A), new ShortcutSlot("two", null)], new Dictionary<string, Hotkey?>());
        Assert.Equal(A, map["one"]);
        Assert.False(map.ContainsKey("two"));
    }

    [Fact]
    public void RemovingLeavesNoKeysEvenWithADefault()
    {
        var map = ShortcutMap.Resolve([new ShortcutSlot("one", A)], new Dictionary<string, Hotkey?> { ["one"] = null });
        Assert.Empty(map);
    }

    [Fact]
    public void AChoiceBeatsADefaultThatCollidesWithIt()
    {
        var map = ShortcutMap.Resolve([new ShortcutSlot("one", A), new ShortcutSlot("two", B)], new Dictionary<string, Hotkey?> { ["two"] = A });
        Assert.Equal(A, map["two"]);
        Assert.False(map.ContainsKey("one"));
    }

    [Fact]
    public void OfTwoEqualChoicesTheEarlierSlotKeepsIt()
    {
        var map = ShortcutMap.Resolve([new ShortcutSlot("one", null), new ShortcutSlot("two", null)], new Dictionary<string, Hotkey?> { ["two"] = C, ["one"] = C });
        Assert.Equal(C, map["one"]);
        Assert.False(map.ContainsKey("two"));
    }

    [Fact]
    public void AnOverrideForAPlaceThatIsGoneIsIgnored()
    {
        var map = ShortcutMap.Resolve([new ShortcutSlot("one", A)], new Dictionary<string, Hotkey?> { ["gone"] = B });
        Assert.Equal(A, Assert.Single(map).Value);
    }

    [Theory]
    [InlineData(Modifiers.None, 'A', ShortcutProblem.NeedsModifier)]
    [InlineData(Modifiers.Shift, 'A', ShortcutProblem.NeedsModifier)]
    [InlineData(Modifiers.Shift, 0x1B, ShortcutProblem.NeedsModifier)]
    [InlineData(CtrlAlt, 0x15, ShortcutProblem.InputKey)]
    [InlineData(Modifiers.None, 0x19, ShortcutProblem.InputKey)]
    [InlineData(CtrlAlt, 0x2E, ShortcutProblem.Reserved)]
    [InlineData(Modifiers.Ctrl, 0x1B, ShortcutProblem.Reserved)]
    [InlineData(Modifiers.Alt, 0x09, ShortcutProblem.Reserved)]
    [InlineData(Modifiers.Alt, 0x73, ShortcutProblem.Reserved)]
    [InlineData(Modifiers.Win, 'L', ShortcutProblem.Reserved)]
    [InlineData(CtrlAlt, 'A', ShortcutProblem.None)]
    [InlineData(Modifiers.Alt, 0x20, ShortcutProblem.None)]
    [InlineData(Modifiers.Win | Modifiers.Alt, 'V', ShortcutProblem.None)]
    public void TheRules(Modifiers modifiers, int key, ShortcutProblem problem) =>
        Assert.Equal(problem, ShortcutRules.Check(new Hotkey(modifiers, (uint)key)));

    [Fact]
    public void KnownOwnersAreNamed()
    {
        Assert.Contains("PowerToys Run", ShortcutRules.KnownOwners(new Hotkey(Modifiers.Alt, 0x20)));
        Assert.Contains("Word", ShortcutRules.KnownOwners(new Hotkey(CtrlAlt, '2')));
        Assert.Contains("OneNote", ShortcutRules.KnownOwners(new Hotkey(CtrlAlt, '5')));
        Assert.DoesNotContain("Word", ShortcutRules.KnownOwners(new Hotkey(CtrlAlt, '5')));
        Assert.Empty(ShortcutRules.KnownOwners(new Hotkey(Modifiers.Win | Modifiers.Alt, 'V')));
        Assert.Empty(ShortcutRules.KnownOwners(new Hotkey(CtrlAlt, 'X')));
    }
}
