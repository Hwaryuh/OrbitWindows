using Orbit.Core.Input;
using static Orbit.Core.Input.HotkeyMatcher;

namespace Orbit.Tests;

public class HotkeyMatcherTests
{
    [Fact]
    public void LabelsReadAsOnTheKeyboard()
    {
        Assert.Equal(["Alt", "Space"], new Hotkey(Modifiers.Alt, HotkeyMatcher.VkSpace).Labels());
        Assert.Equal(["Ctrl", "Alt", "4"], new Hotkey(Modifiers.Alt | Modifiers.Ctrl, 0x34).Labels());
        Assert.Equal(["Win", "Alt", "V"], new Hotkey(Modifiers.Win | Modifiers.Alt, 0x56).Labels());
        Assert.Equal(["PrtScn"], new Hotkey(Modifiers.None, 0x2C).Labels());
        Assert.Equal(["Ctrl", "F12"], new Hotkey(Modifiers.Ctrl, 0x7B).Labels());
    }

    private const uint Space = 0x20, Q = 0x51;
    private const int Ring = 1, Launcher = 2;


    private static HotkeyMatcher Matcher()
    {
        var matcher = new HotkeyMatcher();
        matcher.Register(Ring, new Hotkey(Modifiers.Ctrl | Modifiers.Alt, Space));
        matcher.Register(Launcher, new Hotkey(Modifiers.Alt, Space));
        return matcher;
    }

    [Fact]
    public void LeftCtrlAltSpaceMatchesAndMasksTheAlt()
    {
        var decision = Matcher().KeyDown(Space, new Keys(VkLControl, VkLMenu));
        Assert.Equal(new HotkeyDecision(true, Ring, 0, true), decision);
    }

    [Fact]
    public void ExactModifiersOnly()
    {
        var matcher = Matcher();
        Assert.Equal(Launcher, matcher.KeyDown(Space, new Keys(VkLMenu)).Pressed);
        matcher.KeyUp(Space);
        // Shift is not part of either shortcut.
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyDown(Space, new Keys(VkLMenu, VkLShift)));
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyDown(Space, new Keys()));
    }

    [Fact]
    public void AltGrNeverMatches()
    {
        // AltGr arrives as left Ctrl + right Alt.
        Assert.Equal(HotkeyDecision.Pass, Matcher().KeyDown(Space, new Keys(VkLControl, VkRMenu)));
    }

    [Theory]
    [InlineData(VkRMenu)]     // 한/영 on Korean keyboards
    [InlineData(VkRControl)]  // 한자 on Korean keyboards
    [InlineData(VkRShift)]
    [InlineData(VkRWin)]
    public void ARightModifierRulesOutAMatch(uint right)
    {
        Assert.Equal(HotkeyDecision.Pass, Matcher().KeyDown(Space, new Keys(VkLControl, VkLMenu, right)));
    }

    [Fact]
    public void ModifiersAreNeverSwallowed()
    {
        var matcher = Matcher();
        foreach (var vk in new[] { VkLControl, VkLMenu, VkRMenu, VkRControl, VkLShift, VkLWin, VkControl, VkMenu })
        {
            Assert.Equal(HotkeyDecision.Pass, matcher.KeyDown(vk, new Keys(VkLControl, VkLMenu)));
            Assert.Equal(HotkeyDecision.Pass, matcher.KeyUp(vk));
        }
    }

    [Fact]
    public void OwnedKeyKeepsItsRepeatsAndUpEvenAfterModifiersGo()
    {
        var matcher = Matcher();
        matcher.KeyDown(Space, new Keys(VkLControl, VkLMenu));
        // Repeat with the modifiers already released: still Orbit's, no second press.
        Assert.Equal(new HotkeyDecision(true, 0, 0, false), matcher.KeyDown(Space, new Keys()));
        Assert.Equal(new HotkeyDecision(true, 0, Ring, false), matcher.KeyUp(Space));
        // After the up, Space is the app's again.
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyUp(Space));
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyDown(Space, new Keys()));
    }

    [Fact]
    public void OtherKeysPassWhileAShortcutIsHeld()
    {
        var matcher = Matcher();
        matcher.KeyDown(Space, new Keys(VkLControl, VkLMenu));
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyDown(Q, new Keys(VkLControl, VkLMenu)));
    }

    [Fact]
    public void ResetReturnsTheHeldShortcut()
    {
        var matcher = Matcher();
        matcher.KeyDown(Space, new Keys(VkLControl, VkLMenu));
        Assert.Equal(Ring, matcher.Reset());
        Assert.Equal(0, matcher.Reset());
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyUp(Space));
    }

    [Fact]
    public void CtrlOnlyShortcutDoesNotAskForAMask()
    {
        var matcher = new HotkeyMatcher();
        matcher.Register(3, new Hotkey(Modifiers.Ctrl | Modifiers.Shift, Q));
        Assert.False(matcher.KeyDown(Q, new Keys(VkLControl, VkLShift)).SendMenuMask);
    }

    [Fact]
    public void AKeyTheAppGotIsNotTakenByItsRepeats()
    {
        // Space goes down plain, then Ctrl+Alt, then Space repeats and goes up: all the app's.
        var matcher = Matcher();
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyDown(Space, new Keys()));
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyDown(Space, new Keys(VkLControl, VkLMenu)));
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyUp(Space));
        // A fresh press matches again.
        Assert.Equal(Ring, matcher.KeyDown(Space, new Keys(VkLControl, VkLMenu)).Pressed);
    }

    [Fact]
    public void RecoverForgetsAnUpTheHookMissed()
    {
        var matcher = Matcher();
        matcher.KeyDown(Space, new Keys());              // the app got it; its up went elsewhere
        Assert.Equal(0, matcher.Recover(new Keys()));    // Windows says Space is up
        Assert.Equal(Ring, matcher.KeyDown(Space, new Keys(VkLControl, VkLMenu)).Pressed);
    }

    [Fact]
    public void RecoverReleasesAShortcutOnceItsModifiersAreUp()
    {
        var matcher = Matcher();
        matcher.KeyDown(Space, new Keys(VkLControl, VkLMenu));
        Assert.Equal(0, matcher.Recover(new Keys(VkLControl)));      // one modifier still down: held
        Assert.Equal(Ring, matcher.Recover(new Keys()));             // none: let go
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyUp(Space));     // a late up is the app's
        Assert.Equal(Ring, matcher.KeyDown(Space, new Keys(VkLControl, VkLMenu)).Pressed);
    }

    [Fact]
    public void ResetAlsoForgetsKeysDown()
    {
        var matcher = Matcher();
        matcher.KeyDown(Space, new Keys());
        matcher.Reset();
        Assert.Equal(Ring, matcher.KeyDown(Space, new Keys(VkLControl, VkLMenu)).Pressed);
    }

    [Fact]
    public void ZeroIdIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HotkeyMatcher().Register(0, new Hotkey(Modifiers.Alt, Q)));
    }

    [Fact]
    public void APassedThroughShortcutGoesToTheAppAndOthersStillMatch()
    {
        var matcher = Matcher();
        const int Area = 3;
        matcher.Register(Area, new Hotkey(Modifiers.Ctrl | Modifiers.Alt, 0x34));
        matcher.UpdatePassThrough(h => PassThroughRule.Passes(PassThroughRule.Defaults, "winword.exe", h));
        var ctrlAlt = new Keys(VkLControl, VkLMenu);
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyDown(0x34, ctrlAlt));
        Assert.Equal(HotkeyDecision.Pass, matcher.KeyUp(0x34));
        Assert.Equal(Ring, matcher.KeyDown(Space, ctrlAlt).Pressed);
        matcher.KeyUp(Space);
        // Back in another app, Orbit takes it again.
        matcher.UpdatePassThrough(h => PassThroughRule.Passes(PassThroughRule.Defaults, "notepad.exe", h));
        Assert.Equal(Area, matcher.KeyDown(0x34, ctrlAlt).Pressed);
    }

    [Fact]
    public void DefaultRulesCoverCtrlAltDigitsInWordAndOneNoteOnly()
    {
        var rules = PassThroughRule.Defaults;
        Assert.True(PassThroughRule.Passes(rules, "WINWORD.EXE", new Hotkey(Modifiers.Ctrl | Modifiers.Alt, 0x32)));
        Assert.True(PassThroughRule.Passes(rules, "onenote.exe", new Hotkey(Modifiers.Ctrl | Modifiers.Alt, 0x35)));
        Assert.False(PassThroughRule.Passes(rules, "winword.exe", new Hotkey(Modifiers.Ctrl | Modifiers.Alt, Space)));
        Assert.False(PassThroughRule.Passes(rules, "winword.exe", new Hotkey(Modifiers.Ctrl | Modifiers.Alt | Modifiers.Shift, 0x32)));
        Assert.False(PassThroughRule.Passes(rules, "excel.exe", new Hotkey(Modifiers.Ctrl | Modifiers.Alt, 0x32)));
        Assert.False(PassThroughRule.Passes(rules, "", new Hotkey(Modifiers.Ctrl | Modifiers.Alt, 0x32)));
    }
}
