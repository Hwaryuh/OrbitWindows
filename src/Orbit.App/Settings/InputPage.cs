using System.Windows;
using System.Windows.Controls;
using Orbit.App.Design;
using Orbit.App.Features;
using Orbit.Core.Settings;

namespace Orbit.App.Settings;

/// <summary>Automatic Korean/English typing: the switch, terminals, and programs to leave alone.</summary>
internal sealed class InputPage : StackPanel
{
    private readonly PageParts _parts = new(Palette.Light);

    public InputPage(AppServices services)
    {
        var typing = services.Typing!;
        var preferences = services.Preferences;
        Children.Add(_parts.PageTitles(Strings.PageInput, Strings.InputSubtitle, bottom: 20));

        var toggle = _parts.Toggle(preferences.Get<bool>(Pref.TypingEnabled), on => preferences.Set(Pref.TypingEnabled, on));
        toggle.IsEnabled = typing.Bundled;
        string? problem = !typing.Bundled ? Strings.TypingNotBundled : typing.Failed ? Strings.TypingFailed : null;
        Children.Add(_parts.Group(null, problem ?? Strings.TypingFooter,
            _parts.Row(Strings.TypingTitle, Strings.TypingDetail, toggle),
            _parts.Row(Strings.TypingInTerminals, Strings.TypingInTerminalsDetail, _parts.Toggle(preferences.Get<bool>(Pref.TypingInTerminals), on => preferences.Set(Pref.TypingInTerminals, on)))));

        var excluded = new StackPanel();
        _parts.ProgramList(excluded, preferences, Pref.TypingExcludedApps);
        Children.Add(_parts.Group(Strings.TypingExcluded, null, excluded));
    }
}
