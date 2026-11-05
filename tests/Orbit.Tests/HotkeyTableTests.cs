using Orbit.Core.Input;
using static Orbit.Core.Input.HotkeyMatcher;

namespace Orbit.Tests;

public class HotkeyTableTests
{
    private const uint K = 0x4B, J = 0x4A;
    private const Modifiers CtrlAlt = Modifiers.Ctrl | Modifiers.Alt;


    private static readonly Keys Chord = new(VkLControl, VkLMenu);

    [Fact]
    public void ReplacingChangesWhatMatches()
    {
        var matcher = new HotkeyMatcher();
        matcher.Replace([(5, new Hotkey(CtrlAlt, K))]);
        Assert.Equal(5, matcher.KeyDown(K, Chord).Pressed);
        matcher.KeyUp(K);
        matcher.Replace([(5, new Hotkey(CtrlAlt, J))]);
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyDown(K, Chord));
        matcher.KeyUp(K);
        Assert.Equal(5, matcher.KeyDown(J, Chord).Pressed);
    }

    [Fact]
    public void AKeyHeldAcrossAReplacementStaysOwnedUntilItGoesUp()
    {
        var matcher = new HotkeyMatcher();
        matcher.Replace([(5, new Hotkey(CtrlAlt, K))]);
        Assert.Equal(5, matcher.KeyDown(K, Chord).Pressed);
        matcher.Replace([]);
        Assert.True(matcher.KeyDown(K, Chord).Swallow);
        var up = matcher.KeyUp(K);
        Assert.True(up.Swallow);
        Assert.Equal(5, up.Released);
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyDown(K, Chord));
    }

    [Fact]
    public void AHeldKeyIsRecoveredWithTheModifiersItWasMatchedWith()
    {
        var matcher = new HotkeyMatcher();
        matcher.Replace([(5, new Hotkey(CtrlAlt, K))]);
        matcher.KeyDown(K, Chord);
        // The table no longer has it, and the same id now has other modifiers.
        matcher.Replace([(5, new Hotkey(Modifiers.Win, K))]);
        Assert.Equal(0, matcher.Recover(new Keys(VkLControl)));
        Assert.Equal(5, matcher.Recover(new Keys()));
    }

    [Fact]
    public void WhatTheFrontAppKeepsComesWithTheTable()
    {
        var matcher = new HotkeyMatcher();
        var first = new Hotkey(CtrlAlt, K);
        matcher.Replace([(1, first), (2, new Hotkey(CtrlAlt, J))], h => h == first);
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyDown(K, Chord));
        matcher.KeyUp(K);
        Assert.Equal(2, matcher.KeyDown(J, Chord).Pressed);
        matcher.KeyUp(J);
        // The same slots with other keys: the old answer doesn't carry over.
        matcher.Replace([(1, new Hotkey(CtrlAlt, J)), (2, first)]);
        Assert.Equal(2, matcher.KeyDown(K, Chord).Pressed);
    }

    [Fact]
    public void ATableHoldsSixtyFourAtMost()
    {
        var matcher = new HotkeyMatcher();
        var full = Enumerable.Range(1, MaxShortcuts).Select(i => (i, new Hotkey(CtrlAlt, (uint)(i + 0x30)))).ToList();
        matcher.Replace(full, _ => false);
        Assert.Equal(MaxShortcuts, matcher.KeyDown(MaxShortcuts + 0x30, Chord).Pressed);
        full.Add((65, new Hotkey(CtrlAlt, 0xF0)));
        Assert.Throws<ArgumentException>(() => matcher.Replace(full));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(RecordedFlag)]
    public void AnIdMustNotLookLikeARecordedKey(int id) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new HotkeyMatcher().Replace([(id, new Hotkey(CtrlAlt, K))]));

    [Fact]
    public void RecordingTakesTheNextKeyInsteadOfAnyShortcut()
    {
        var matcher = new HotkeyMatcher();
        matcher.Replace([(5, new Hotkey(CtrlAlt, K))]);
        matcher.Recording = true;
        var press = matcher.KeyDown(K, Chord);
        Assert.True(press.Swallow);
        Assert.True(press.SendMenuMask);
        Assert.Equal(new Hotkey(CtrlAlt, K), Recorded(press.Pressed));
        Assert.True(matcher.KeyDown(K, Chord).Swallow);
        Assert.True(matcher.KeyUp(K).Swallow);
        matcher.Recording = false;
        Assert.Equal(5, matcher.KeyDown(K, Chord).Pressed);
    }

    [Fact]
    public void RecordingLeavesModifiersAndRightSideKeysAlone()
    {
        var matcher = new HotkeyMatcher { Recording = true };
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyDown(VkLMenu, new Keys()));
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyDown(K, new Keys(VkRMenu)));
    }

    [Fact]
    public void RecordingABareKeySendsNoMenuMask()
    {
        var matcher = new HotkeyMatcher { Recording = true };
        var press = matcher.KeyDown(0x1B, new Keys());
        Assert.False(press.SendMenuMask);
        Assert.Equal(new Hotkey(Modifiers.None, 0x1B), Recorded(press.Pressed));
    }

    [Fact]
    public void ABareRecordedKeyHeldPastARecoveryStaysFromTheApp()
    {
        var matcher = new HotkeyMatcher { Recording = true };
        matcher.KeyDown(0x1B, new Keys());
        matcher.Recording = false;
        Assert.NotEqual(0, matcher.Recover(new Keys()));
        Assert.True(matcher.KeyDown(0x1B, new Keys()).Swallow);
        Assert.True(matcher.KeyUp(0x1B).Swallow);
        // Over: the next press is the app's.
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyDown(0x1B, new Keys()));
    }

    [Fact]
    public void AShortcutRecoveredAfterALostUpWorksOnTheNextPress()
    {
        var matcher = new HotkeyMatcher();
        matcher.Replace([(5, new Hotkey(CtrlAlt, K))]);
        matcher.KeyDown(K, Chord);
        Assert.Equal(5, matcher.Recover(new Keys()));
        Assert.Equal(5, matcher.KeyDown(K, Chord).Pressed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public void AShortcutsIdIsNotARecordedKey(int id) => Assert.Null(Recorded(id));
}
