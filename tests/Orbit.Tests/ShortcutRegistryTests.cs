using Orbit.Core.Actions;
using Orbit.Core.Input;
using Orbit.Core.Settings;
using static Orbit.Core.Input.HotkeyMatcher;

namespace Orbit.Tests;

public sealed class ShortcutRegistryTests : IDisposable
{
    private const uint K = 0x4B, J = 0x4A, T = 0x54;
    private const Modifiers CtrlAlt = Modifiers.Ctrl | Modifiers.Alt;
    private static readonly Keys Chord = new(VkLControl, VkLMenu);
    private static readonly Hotkey Translate = new(CtrlAlt, T);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "orbit-shortcuts-" + Guid.NewGuid().ToString("N"));
    private readonly Preferences _preferences;
    private readonly ActionRunner _actions = new();
    private readonly HotkeyMatcher _matcher = new();
    private readonly ShortcutRegistry _shortcuts;

    public ShortcutRegistryTests()
    {
        Directory.CreateDirectory(_folder);
        _preferences = new Preferences(Path.Combine(_folder, "settings.json"), Pref.Defaults());
        _shortcuts = new ShortcutRegistry(_preferences, _actions, _matcher);
    }

    public void Dispose()
    {
        _preferences.Dispose();
        Directory.Delete(_folder, true);
    }

    private int Press(uint key)
    {
        int pressed = _matcher.KeyDown(key, Chord).Pressed;
        _matcher.KeyUp(key);
        return pressed;
    }

    [Fact]
    public void ADefaultIsInPlaceOnceApplied()
    {
        int id = _shortcuts.Declare(Translate, OrbitAction.Translate);
        Assert.Equal(0, Press(T));
        _shortcuts.Apply();
        Assert.Equal(id, Press(T));
        Assert.Equal(Translate, _shortcuts.For(OrbitAction.Translate));
    }

    [Fact]
    public void ADeclarationNeedsAPlace() =>
        Assert.Throws<ArgumentException>(() => _shortcuts.Declare(Translate));

    [Fact]
    public void AnAvailableActionGetsAPlaceAndAnUnavailableOneDoesNot()
    {
        _actions.Register(OrbitAction.Scratchpad, () => { });
        _shortcuts.Apply();
        Assert.Contains(OrbitAction.Scratchpad.Id(), _shortcuts.Keys());
        Assert.DoesNotContain(OrbitAction.DarkMode.Id(), _shortcuts.Keys());
        Assert.Null(_shortcuts.For(OrbitAction.Scratchpad));
    }

    [Fact]
    public void KeysGivenToAnActionRunItAndChangeAtOnce()
    {
        _actions.Register(OrbitAction.Scratchpad, () => { });
        _shortcuts.Apply();
        _shortcuts.Set(OrbitAction.Scratchpad.Id(), new Hotkey(CtrlAlt, K));
        int id = Press(K);
        Assert.Equal(OrbitAction.Scratchpad, _shortcuts.ActionFor(id));
        _shortcuts.Set(OrbitAction.Scratchpad.Id(), new Hotkey(CtrlAlt, J));
        Assert.Equal(0, Press(K));
        Assert.Equal(id, Press(J));
    }

    [Fact]
    public void ADeclaredShortcutIsLeftToItsFeature()
    {
        int id = _shortcuts.Declare(Translate, OrbitAction.Translate);
        _actions.Register(OrbitAction.Translate, () => { });
        _shortcuts.Apply();
        Assert.Null(_shortcuts.ActionFor(id));
        Assert.Single(_shortcuts.Keys(), OrbitAction.Translate.Id());
    }

    [Fact]
    public void AFeatureKeepsItsIdWhateverKeysItHas()
    {
        int id = _shortcuts.Declare(Translate, OrbitAction.Translate);
        _shortcuts.Apply();
        _shortcuts.Set(OrbitAction.Translate.Id(), new Hotkey(CtrlAlt, K));
        Assert.Equal(id, Press(K));
    }

    [Fact]
    public void ChoosingTheDefaultStoresNoDifference()
    {
        _shortcuts.Declare(Translate, OrbitAction.Translate);
        _shortcuts.Apply();
        _shortcuts.Set(OrbitAction.Translate.Id(), new Hotkey(CtrlAlt, K));
        Assert.True(_shortcuts.IsChanged(OrbitAction.Translate.Id()));
        _shortcuts.Set(OrbitAction.Translate.Id(), Translate);
        Assert.False(_shortcuts.IsChanged(OrbitAction.Translate.Id()));
        Assert.Empty(ShortcutMap.Parse(_preferences.Get<string>(Pref.Shortcuts)));
    }

    [Fact]
    public void RemovingAndResetting()
    {
        int id = _shortcuts.Declare(Translate, OrbitAction.Translate);
        _shortcuts.Apply();
        _shortcuts.Set(OrbitAction.Translate.Id(), null);
        Assert.Null(_shortcuts.For(OrbitAction.Translate));
        Assert.True(_shortcuts.IsChanged(OrbitAction.Translate.Id()));
        Assert.Equal(0, Press(T));
        _shortcuts.Reset(OrbitAction.Translate.Id());
        Assert.Equal(Translate, _shortcuts.For(OrbitAction.Translate));
        Assert.Equal(id, Press(T));
    }

    [Fact]
    public void TheHolderIsNamedExceptItself()
    {
        _shortcuts.Declare(Translate, OrbitAction.Translate);
        _shortcuts.Apply();
        Assert.Equal(OrbitAction.Translate.Id(), _shortcuts.Holder(Translate, "other"));
        Assert.Null(_shortcuts.Holder(Translate, OrbitAction.Translate.Id()));
        Assert.Null(_shortcuts.Holder(new Hotkey(CtrlAlt, K), "other"));
    }

    [Fact]
    public void ChangesSurviveARestart()
    {
        _shortcuts.Declare(Translate, OrbitAction.Translate);
        _shortcuts.Apply();
        _shortcuts.Set(OrbitAction.Translate.Id(), new Hotkey(CtrlAlt, K));
        var again = new ShortcutRegistry(_preferences, _actions, new HotkeyMatcher());
        again.Declare(Translate, OrbitAction.Translate);
        again.Apply();
        Assert.Equal(new Hotkey(CtrlAlt, K), again.For(OrbitAction.Translate));
    }

    [Fact]
    public void WhatTheFrontAppKeepsAppliesToANewTable()
    {
        int id = _shortcuts.Declare(Translate, OrbitAction.Translate);
        _shortcuts.PassesToFront = h => h == Translate;
        _shortcuts.Apply();
        Assert.Equal(0, Press(T));
        _shortcuts.PassesToFront = _ => false;
        _shortcuts.Apply();
        Assert.Equal(id, Press(T));
    }
}
