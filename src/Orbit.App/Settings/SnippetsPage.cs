using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Orbit.App.Design;
using Orbit.App.Features;
using Orbit.App.Overlays;
using Orbit.Core.Settings;
using Orbit.Core.Snippets;

namespace Orbit.App.Settings;

/// <summary>
/// The Snippets page (Orbit: the Snippets page): expanding as you type and when, terminals, the
/// snippets themselves with an editor, import and export, and programs where nothing expands.
/// </summary>
internal sealed class SnippetsPage : StackPanel
{
    private readonly AppServices _services;
    private readonly Preferences _preferences;
    private readonly SnippetStore _store;
    private readonly Palette _palette = Palette.Light;
    private readonly PageParts _parts = new(Palette.Light);
    private readonly StackPanel _list = new();
    private readonly StackPanel _editor = new() { Margin = new Thickness(16, 0, 0, 0) };
    private readonly StackPanel _excluded = new();
    private Guid? _selected;

    public SnippetsPage(AppServices services)
    {
        _services = services;
        _preferences = services.Preferences;
        _store = services.Snippets!.Store;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        tools.Children.Add(_parts.PillButton(Strings.SnippetImport, prominent: false, Import));
        var export = _parts.PillButton(Strings.SnippetExport, prominent: false, Export);
        export.Margin = new Thickness(6, 0, 0, 0);
        tools.Children.Add(export);
        DockPanel.SetDock(tools, Dock.Right);
        header.Children.Add(tools);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = Strings.PageSnippets, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink });
        titles.Children.Add(_parts.Caption(Strings.SnippetPageSubtitle));
        header.Children.Add(titles);
        Children.Add(header);

        Children.Add(_parts.Group(null, null,
            _parts.Row(Strings.SnippetEnabled, Strings.SnippetEnabledDetail, _parts.Toggle(_preferences.Get<bool>(Pref.SnippetsEnabled), on => _preferences.Set(Pref.SnippetsEnabled, on))),
            _parts.Row(Strings.SnippetTrigger, null, _parts.Chips<string>([("immediate", Strings.SnippetTriggerImmediate), ("delimiter", Strings.SnippetTriggerDelimiter)],
                () => _preferences.Get<string>(Pref.SnippetsTrigger), v => _preferences.Set(Pref.SnippetsTrigger, v))),
            _parts.Row(Strings.SnippetInTerminals, Strings.SnippetInTerminalsDetail, _parts.Toggle(_preferences.Get<bool>(Pref.SnippetsInTerminals), on => _preferences.Set(Pref.SnippetsInTerminals, on)))));

        var split = new DockPanel();
        var left = new StackPanel { Width = 220 };
        var add = _parts.PillButton(Strings.SnippetNew, prominent: true, () =>
        {
            _selected = _store.Add(new Snippet { Name = Strings.SnippetUntitled }).Id;
            Fill();
        });
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 0, 0, 10);
        left.Children.Add(add);
        left.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        DockPanel.SetDock(left, Dock.Left);
        split.Children.Add(left);
        split.Children.Add(_editor);
        Children.Add(_parts.Group(Strings.SnippetList, Strings.SnippetPlaceholders, split));

        _parts.ProgramList(_excluded, _preferences, Pref.SnippetsExcludedApps);
        Children.Add(_parts.Group(Strings.SnippetExcluded, null, _excluded));
        _selected = _store.All.FirstOrDefault()?.Id;
        Fill();
    }

    private void Fill()
    {
        _list.Children.Clear();
        foreach (var snippet in _store.All)
        {
            bool on = snippet.Id == _selected;
            var row = new DockPanel();
            if (snippet.Keyword.Length > 0)
            {
                var keyword = new TextBlock { Text = snippet.Keyword, FontSize = 11, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Foreground = on ? _palette.Canvas : _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(keyword, Dock.Right);
                row.Children.Add(keyword);
            }
            row.Children.Add(new TextBlock
            {
                Text = snippet.Name.Length > 0 ? snippet.Name : Strings.SnippetUntitled, FontSize = 13, Foreground = on ? _palette.Canvas : _palette.Ink,
                TextTrimming = TextTrimming.CharacterEllipsis, Opacity = snippet.Enabled ? 1 : 0.5, Margin = new Thickness(0, 0, 6, 0),
            });
            var frame = new Border { Background = on ? _palette.Ink : Brushes.Transparent, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Child = row, Cursor = Cursors.Hand };
            var id = snippet.Id;
            frame.MouseLeftButtonUp += (_, _) =>
            {
                _selected = id;
                Fill();
            };
            _list.Children.Add(frame);
        }
        FillEditor();
    }

    private void FillEditor()
    {
        _editor.Children.Clear();
        if (_store.All.FirstOrDefault(s => s.Id == _selected) is not { } snippet) return;
        var clash = new TextBlock { FontSize = 11, Foreground = _palette.Accent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        void Save(Snippet changed)
        {
            _store.Update(changed);
            var trigger = _preferences.Get<string>(Pref.SnippetsTrigger) == "delimiter" ? SnippetTrigger.Delimiter : SnippetTrigger.Immediate;
            var other = _store.Clash(changed, trigger);
            clash.Text = other is null ? "" : Strings.SnippetClash(other.Keyword);
            clash.Visibility = other is null ? Visibility.Collapsed : Visibility.Visible;
        }
        TextBox Field(string label, string value, bool mono = false, bool multiline = false)
        {
            _editor.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = _palette.InkSecondary, Margin = new Thickness(0, 0, 0, 4) });
            var box = new TextBox
            {
                Text = value, FontSize = 13, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 10),
                FontFamily = mono ? new FontFamily("Cascadia Mono, Consolas") : TextElement.GetFontFamily(this), AcceptsReturn = multiline, AcceptsTab = multiline,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 140 : 0, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
            };
            _editor.Children.Add(box);
            return box;
        }
        var name = Field(Strings.SnippetName, snippet.Name);
        var keyword = Field(Strings.SnippetKeyword, snippet.Keyword, mono: true);
        _editor.Children.Add(clash);
        var content = Field(Strings.SnippetContent, snippet.Content, mono: true, multiline: true);
        var folder = Field(Strings.SnippetFolder, snippet.Folder);
        Snippet Current() => (_store.All.FirstOrDefault(s => s.Id == snippet.Id) ?? snippet) with
        {
            Name = name.Text, Keyword = keyword.Text.Trim(), Content = content.Text, Folder = folder.Text.Trim(),
        };
        // Saved when a field is left, so the list doesn't reorder under the typing.
        foreach (var box in new[] { name, keyword, content, folder }) box.LostKeyboardFocus += (_, _) =>
        {
            Save(Current());
            RefreshList();
        };
        var bottom = new DockPanel();
        var delete = _parts.PillButton(Strings.Delete, prominent: false, () =>
        {
            _store.Delete(snippet.Id);
            _selected = _store.All.FirstOrDefault()?.Id;
            Fill();
        });
        DockPanel.SetDock(delete, Dock.Right);
        bottom.Children.Add(delete);
        var enabled = new CheckBox { Content = Strings.SnippetOn, IsChecked = snippet.Enabled, VerticalAlignment = VerticalAlignment.Center };
        enabled.Click += (_, _) =>
        {
            Save(Current() with { Enabled = enabled.IsChecked == true });
            RefreshList();
        };
        bottom.Children.Add(enabled);
        _editor.Children.Add(bottom);
        Save(snippet);
    }

    /// <summary>The list again without rebuilding the editor (its fields have the caret).</summary>
    private void RefreshList()
    {
        var editor = _editor.Children.Cast<UIElement>().ToList();
        Fill();
        _editor.Children.Clear();
        foreach (var e in editor) _editor.Children.Add(e);
    }

    private void Import()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = $"{Strings.SnippetFiles}|*.json;*.csv;*.txt" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            int count = _store.Import(File.ReadAllText(dialog.FileName), Strings.SnippetImported);
            Toast.Show(Strings.SnippetImportedCount(count));
            Fill();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Toast.Show(Strings.SnippetImportFailed);
        }
    }

    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "Orbit snippets.json", Filter = $"{Strings.SnippetFiles}|*.json", DefaultExt = ".json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, _store.Export());
            Toast.Show(Strings.SnippetExported);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Toast.Show(Strings.CaptureSaveFailed);
        }
    }
}
