using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Orbit.App.Design;
using Orbit.App.Features;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Araso;
using Orbit.Core.Settings;

namespace Orbit.App.Settings;

/// <summary>
/// Araso: whether it is installed, known to Windows, on the list, in use and running, each on its own
/// row, one button for what comes next, and the user's words and replacements once it is installed.
/// </summary>
internal sealed class InputPage : StackPanel
{
    private readonly ArasoFeature _araso;
    private readonly Palette _palette = Palette.Light;
    private readonly PageParts _parts = new(Palette.Light);
    private readonly StackPanel _status = new();
    private readonly StackPanel _lists = new();
    private readonly Action _changed;
    private string _language = "korean";
    private string? _note;

    /// <param name="services">Null in the self-test, which only looks at Araso's rows.</param>
    public InputPage(ArasoFeature araso, AppServices? services = null)
    {
        _araso = araso;
        Children.Add(_parts.PageTitles(Strings.PageInput, Strings.InputSubtitle, bottom: 20));
        if (services is not null) Children.Add(TypingGroup(services));
        Children.Add(_status);
        Children.Add(_lists);
        _changed = RebuildStatus;
        Loaded += async (_, _) =>
        {
            _araso.Changed += _changed;
            await _araso.RefreshAsync();
            RebuildLists();
        };
        Unloaded += (_, _) => _araso.Changed -= _changed;
        RebuildStatus();
    }

    private UIElement TypingGroup(AppServices services)
    {
        var typing = services.Typing!;
        var preferences = services.Preferences;
        var excluded = new StackPanel();
        _parts.ProgramList(excluded, preferences, Pref.TypingExcludedApps);
        var group = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        var toggle = _parts.Toggle(preferences.Get<bool>(Pref.TypingEnabled), on => preferences.Set(Pref.TypingEnabled, on));
        toggle.IsEnabled = typing.Bundled;
        string? problem = !typing.Bundled ? Strings.TypingNotBundled : typing.Failed ? Strings.TypingFailed : null;
        group.Children.Add(_parts.Group(null, problem ?? Strings.TypingFooter,
            _parts.Row(Strings.TypingTitle, Strings.TypingDetail, toggle),
            _parts.Row(Strings.TypingInTerminals, Strings.TypingInTerminalsDetail, _parts.Toggle(preferences.Get<bool>(Pref.TypingInTerminals), on => preferences.Set(Pref.TypingInTerminals, on)))));
        group.Children.Add(_parts.Group(Strings.TypingExcluded, null, excluded));
        return group;
    }

    private void RebuildStatus()
    {
        var state = _araso.State;
        _status.Children.Clear();
        if (!_araso.Known)
        {
            _status.Children.Add(_parts.Caption(Strings.InputLooking));
            return;
        }
        _status.Children.Add(_parts.Group(null, Footer(state),
            Row(Strings.InputInstalled, state.IsInstalled, state.InstalledVersion is { } version ? Strings.InputVersion(version) : null),
            Row(Strings.InputRegistered, state.Registered, null),
            Row(Strings.InputOnList, state.Enabled, null),
            Row(Strings.InputInUse, state.Active, null),
            Row(Strings.InputHost, state.HostRunning, null)));

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 24) };
        if (_araso.Working is { } working)
        {
            buttons.Children.Add(new TextBlock { Text = working, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink });
            _status.Children.Add(buttons);
            return;
        }
        switch (state.Next)
        {
            case ArasoStep.NotPresent when _araso.Bundled:
                Add(buttons, Strings.InputEnable, true, () => _ = FirstEnableAsync());
                break;
            case ArasoStep.Enable:
                Add(buttons, Strings.InputEnable, true, () => _ = _araso.EnableAsync());
                break;
            case ArasoStep.StartHost:
                Add(buttons, Strings.InputStartHost, true, () => _ = _araso.StartHostAsync());
                Add(buttons, Strings.InputDisable, false, () => _ = _araso.DisableAsync());
                break;
            case ArasoStep.Ready:
                Add(buttons, Strings.InputDisable, false, () => _ = _araso.DisableAsync());
                break;
        }
        if (state.IsInstalled) Add(buttons, Strings.InputRemove, false, () => _ = RemoveAsync());
        _status.Children.Add(buttons);
    }

    private async Task FirstEnableAsync()
    {
        if (MessageBox.Show(Strings.InputEnableConfirm, Strings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _araso.EnableAsync();
        RebuildLists();
    }

    private async Task RemoveAsync()
    {
        if (MessageBox.Show(Strings.InputRemoveConfirm, Strings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _araso.RemoveAsync();
        RebuildLists();
    }

    private string? Footer(ArasoState state) =>
        state.RestartPending ? Strings.InputRestartPending
        : state.Active && !state.HostRunning ? Strings.InputHostDown
        : !state.IsInstalled ? (_araso.Bundled ? Strings.InputOffer : Strings.InputNotBundled)
        : null;

    private FrameworkElement Row(string title, bool yes, string? detail) =>
        _parts.Row(title, detail, new TextBlock
        {
            Text = yes ? Strings.InputYes : Strings.InputNo, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            Foreground = yes ? _palette.Ink : _palette.InkSecondary, FontWeight = yes ? FontWeights.SemiBold : FontWeights.Normal,
        });

    private void Add(WrapPanel into, string label, bool prominent, Action click)
    {
        var button = _parts.PillButton(label, prominent, click);
        button.Margin = new Thickness(0, 0, 8, 8);
        into.Children.Add(button);
    }

    private void RebuildLists()
    {
        _lists.Children.Clear();
        if (!_araso.State.IsInstalled) return;
        if (_note is not null) _lists.Children.Add(_parts.Caption(_note));
        try
        {
            _lists.Children.Add(WordsGroup(_araso.Files.Read(ArasoFileStore.Dictionary)));
            _lists.Children.Add(ReplacementsGroup(_araso.Files.Read(ArasoFileStore.Replacements)));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Araso's files could not be read ({e.GetType().Name})");
            _lists.Children.Add(_parts.Caption(Strings.InputFilesUnreadable));
        }
    }

    private UIElement WordsGroup(string? text)
    {
        var rows = ArasoFiles.Words(text).Select(word => (UIElement)_parts.Row(word.Keys, LanguageName(word.Language), RemoveButton(() =>
            Apply(_araso.Files.Edit(ArasoFileStore.Dictionary, json => ArasoFiles.WithoutWord(json, word.Keys)))))).ToList();
        rows.Add(NewWordRow());
        return _parts.Group(Strings.InputWords, Strings.InputWordsFooter, rows.ToArray());
    }

    private UIElement ReplacementsGroup(string? text)
    {
        var rows = ArasoFiles.Replacements(text).Select(pair => (UIElement)_parts.Row(pair.Key, pair.Value, RemoveButton(() =>
            Apply(_araso.Files.Edit(ArasoFileStore.Replacements, json => ArasoFiles.WithoutReplacement(json, pair.Key)))))).ToList();
        rows.Add(NewReplacementRow());
        return _parts.Group(Strings.InputReplacements, Strings.InputReplacementsFooter, rows.ToArray());
    }

    private static string LanguageName(string language) => language switch
    {
        "korean" => Strings.InputKorean,
        "english" => Strings.InputEnglish,
        "japanese" => Strings.InputJapanese,
        _ => language,
    };

    private Button RemoveButton(Action remove) => _parts.PillButton(Strings.InputDelete, prominent: false, remove);

    private TextBox Box(double width)
    {
        var box = _parts.TextField(width);
        box.Margin = new Thickness(0, 0, 8, 0);
        return box;
    }

    private FrameworkElement NewWordRow()
    {
        var keys = Box(120);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(keys);
        row.Children.Add(_parts.Chips<string>([("korean", Strings.InputKorean), ("english", Strings.InputEnglish)], () => _language, v => _language = v));
        var add = _parts.PillButton(Strings.InputAdd, prominent: false, () => AddWord(keys.Text));
        add.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(add);
        return _parts.Row(Strings.InputNewWord, null, row);
    }

    private FrameworkElement NewReplacementRow()
    {
        var shortcut = Box(90);
        var phrase = Box(170);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(shortcut);
        row.Children.Add(phrase);
        row.Children.Add(_parts.PillButton(Strings.InputAdd, prominent: false, () => AddReplacement(shortcut.Text, phrase.Text)));
        return _parts.Row(Strings.InputNewReplacement, null, row);
    }

    internal void AddWord(string typed)
    {
        var keys = typed.Trim();
        if (keys.Length == 0) return;
        if (keys.Any(char.IsWhiteSpace))
        {
            _note = Strings.InputNoSpaces;
            RebuildLists();
            return;
        }
        Apply(_araso.Files.Edit(ArasoFileStore.Dictionary, text => ArasoFiles.WithWord(text, new ArasoWord(keys, _language))));
    }

    internal void AddReplacement(string typedShortcut, string typedPhrase)
    {
        var shortcut = typedShortcut.Trim();
        if (shortcut.Length == 0 || typedPhrase.Length == 0) return;
        Apply(_araso.Files.Edit(ArasoFileStore.Replacements, text => ArasoFiles.WithReplacement(text, shortcut, typedPhrase)));
    }

    private void Apply(ArasoEdit result)
    {
        _note = result switch
        {
            ArasoEdit.Done => null,
            ArasoEdit.SettingsOpen => Strings.InputSettingsOpen,
            ArasoEdit.Unreadable => Strings.InputFilesUnreadable,
            ArasoEdit.KeptChanging => Strings.InputKeptChanging,
            _ => Strings.InputSaveFailed,
        };
        RebuildLists();
    }
}
