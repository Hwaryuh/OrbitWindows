using System.Windows;
using System.Windows.Controls;
using Orbit.App.Design;
using Orbit.App.Features;
using Orbit.Core.Actions;
using Orbit.Core.Input;

namespace Orbit.App.Settings;

/// <summary>
/// Every action's keys. Change presses the new keys; Esc cancels and Delete removes the shortcut.
/// The keys are read in the hook, not by this window: a Win or Alt combination would otherwise open
/// the Start menu or a menu bar. Leaving the window or the page ends the recording.
/// </summary>
internal sealed class ShortcutsPage : StackPanel
{
    private const uint VkEscape = 0x1B, VkBack = 0x08, VkDelete = 0x2E;

    private readonly AppServices _services;
    private readonly Palette _palette = Palette.Light;
    private readonly PageParts _parts = new(Palette.Light);
    private IDisposable? _recording;
    private string? _recordingKey;
    private string? _messageKey;
    private string? _message;
    private Window? _window;

    public ShortcutsPage(AppServices services)
    {
        _services = services;
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this);
            if (_window is not null) _window.Deactivated += OnLeave;
        };
        Unloaded += (_, _) =>
        {
            if (_window is not null) _window.Deactivated -= OnLeave;
            _recording?.Dispose();
        };
        Rebuild();
    }

    private void OnLeave(object? sender, EventArgs e) => _recording?.Dispose();

    private void Rebuild()
    {
        Children.Clear();
        var header = _parts.PageTitles(Strings.PageShortcuts, Strings.ShortcutsSubtitle, bottom: 20);
        Children.Add(header);

        var keys = _services.Shortcuts.Keys();
        Children.Add(_parts.Group(null, null, Row(Orbit.Core.Input.ShortcutRegistry.Ring, Strings.ShortcutRing)));
        foreach (var group in keys.Select(OrbitActions.FromId).OfType<OrbitAction>().GroupBy(a => a.GroupTitle()))
            Children.Add(_parts.Group(group.Key, null, group.Select(a => Row(a.Id(), a.Title())).ToArray<UIElement>()));
    }

    private FrameworkElement Row(string key, string title)
    {
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (_recordingKey == key)
        {
            controls.Children.Add(new TextBlock { Text = Strings.ShortcutPress, FontSize = 12, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center });
            return _parts.Row(title, null, controls);
        }
        if (_services.Shortcuts.For(key) is { } hotkey) controls.Children.Add(KeyCap.Row(hotkey.Labels(), _palette));
        else controls.Children.Add(new TextBlock { Text = Strings.ShortcutNone, FontSize = 12, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center });
        var change = _parts.PillButton(Strings.ShortcutChange, prominent: false, () => Record(key));
        change.Margin = new Thickness(10, 0, 0, 0);
        controls.Children.Add(change);
        if (_services.Shortcuts.IsChanged(key))
        {
            var reset = _parts.PillButton(Strings.ShortcutReset, prominent: false, () => Change(key, () => _services.Shortcuts.Reset(key)));
            reset.Margin = new Thickness(6, 0, 0, 0);
            controls.Children.Add(reset);
        }
        return _parts.Row(title, _messageKey == key ? _message : null, controls);
    }

    private void Record(string key)
    {
        _recording?.Dispose();
        _recordingKey = key;
        _messageKey = _message = null;
        _recording = _services.ShortcutRecorder!.BeginRecording(hotkey => Got(key, hotkey), () =>
        {
            _recording = null;
            _recordingKey = null;
            Rebuild();
        });
        Rebuild();
    }

    private void Got(string key, Hotkey hotkey)
    {
        if (hotkey.Modifiers == Modifiers.None)
        {
            if (hotkey.Key == VkEscape) return;
            if (hotkey.Key is VkDelete or VkBack)
            {
                _services.Shortcuts.Set(key, null);
                return;
            }
        }
        var problem = ShortcutRules.Check(hotkey);
        if (problem != ShortcutProblem.None)
        {
            Say(key, problem switch
            {
                ShortcutProblem.InputKey => Strings.ShortcutInputKey,
                ShortcutProblem.Reserved => Strings.ShortcutReserved,
                _ => Strings.ShortcutNeedsModifier,
            });
            return;
        }
        if (_services.Shortcuts.Holder(hotkey, key) is { } holder)
        {
            Say(key, Strings.ShortcutInUse(PlaceTitle(holder)));
            return;
        }
        _services.Shortcuts.Set(key, hotkey);
        if (ShortcutRules.KnownOwners(hotkey) is { Count: > 0 } owners) Say(key, Strings.ShortcutKnownOwner(string.Join(", ", owners)));
    }

    private void Change(string key, Action change)
    {
        change();
        if (_messageKey == key) _messageKey = _message = null;
        Rebuild();
    }

    private void Say(string key, string message)
    {
        _messageKey = key;
        _message = message;
    }

    /// <summary>What a shortcut's place is called on screen.</summary>
    public static string PlaceTitle(string key) =>
        key == Orbit.Core.Input.ShortcutRegistry.Ring ? Strings.ShortcutRing : OrbitActions.FromId(key)?.Title() ?? key;
}
