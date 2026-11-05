using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.Core.Scratchpad;
using Orbit.Core.Settings;

namespace Orbit.App.Features.Scratchpad;

/// <summary>
/// The pad (behavior after Vorssaint's Scratchpad; Orbit has none): tabs of text, saved as they
/// change. One text box per tab, kept while the pad lives, so each tab keeps its own undo. A tab
/// can show its Markdown instead; its box then stays Hidden, not Collapsed, because a Collapsed box
/// that never had a layout has no undo, and task boxes flip through it.
/// Hides when another app is clicked unless pinned. Keys: Ctrl+T new tab, Ctrl+W close it (the
/// last one hides the pad), Ctrl+Shift+T reopen, Ctrl+Tab and Ctrl+Shift+Tab, Ctrl+E preview,
/// Ctrl+F find, F3 and Shift+F3, Esc.
/// </summary>
internal sealed class ScratchpadWindow : Window
{
    private static readonly Brush Ink = Brushes.Black;
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(110, 110, 110)).Frozen();
    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(228, 228, 228)).Frozen();
    private static readonly Brush TabFill = new SolidColorBrush(Color.FromRgb(240, 240, 240)).Frozen();

    private readonly ScratchpadStore _store;
    private readonly Preferences _preferences;
    private readonly ScratchpadDocument _document;
    private readonly Dictionary<Guid, TextBox> _editors = [];
    private readonly Grid _editorHost = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly Border _findBar;
    private readonly TextBox _find;
    private readonly TextBlock _findStatus;
    private readonly TextBlock _status;
    private readonly Button _pin;
    private readonly Button _previewToggle;
    private readonly FlowDocumentScrollViewer _preview = new()
    {
        IsToolBarVisible = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Visibility = Visibility.Collapsed,
    };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _dirty;
    private bool _quitting;
    private bool _flipping;

    public ScratchpadWindow(ScratchpadStore store, ScratchpadDocument document, Preferences preferences)
    {
        _store = store;
        _document = document;
        _preferences = preferences;
        Title = Strings.ScratchpadTitle;
        Width = 560;
        Height = 440;
        MinWidth = 320;
        MinHeight = 200;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Background = Brushes.White;
        // Always light, whatever Windows uses, and so are its controls.
        Design.Palette.Light.ApplyTo(Resources);
        BorderBrush = Line;
        BorderThickness = new Thickness(1);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Malgun Gothic");
        Topmost = Pinned;

        var add = HeaderButton("", Strings.ScratchpadNewTab, () => NewTab());
        _pin = HeaderButton(Pinned ? "" : "", Strings.ScratchpadPin, TogglePin);
        var more = HeaderButton("", Strings.ScratchpadMore, () => { });
        more.Click += (_, _) =>
        {
            more.ContextMenu = Menu();
            more.ContextMenu.PlacementTarget = more;
            more.ContextMenu.IsOpen = true;
        };
        var close = HeaderButton("", Strings.ScratchpadHide, HidePad);
        _previewToggle = HeaderButton(Glyphs.Preview, Strings.ScratchpadShowPreview, TogglePreview);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 4, 0) };
        right.Children.Add(_previewToggle);
        right.Children.Add(_pin);
        right.Children.Add(more);
        right.Children.Add(close);
        var tabArea = new StackPanel { Orientation = Orientation.Horizontal };
        tabArea.Children.Add(new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        tabArea.Children.Add(add);
        var header = new DockPanel { Height = 40, Background = Brushes.White, LastChildFill = true };
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        header.Children.Add(new Border { Padding = new Thickness(8, 6, 0, 0), Child = tabArea, ClipToBounds = true });
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 1 && e.OriginalSource == header) DragMove();
        };

        _find = new TextBox { Width = 200, FontSize = 13, Padding = new Thickness(6, 2, 6, 2), VerticalContentAlignment = VerticalAlignment.Center };
        _find.TextChanged += (_, _) => FindAsTyped();
        _findStatus = new TextBlock { FontSize = 12, Foreground = Muted, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var findRow = new StackPanel { Orientation = Orientation.Horizontal };
        findRow.Children.Add(new TextBlock { Text = "", FontFamily = Glyphs.Font, FontSize = 13, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        findRow.Children.Add(_find);
        findRow.Children.Add(_findStatus);
        _findBar = new Border { BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 6, 12, 6), Child = findRow, Visibility = Visibility.Collapsed };
        _status = new TextBlock { FontSize = 12, Foreground = Brushes.Firebrick, Margin = new Thickness(14, 4, 14, 6), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        var rule = new Border { Height = 1, Background = Line };
        DockPanel.SetDock(rule, Dock.Top);
        layout.Children.Add(rule);
        DockPanel.SetDock(_status, Dock.Bottom);
        layout.Children.Add(_status);
        DockPanel.SetDock(_findBar, Dock.Bottom);
        layout.Children.Add(_findBar);
        _editorHost.Children.Add(_preview);
        layout.Children.Add(_editorHost);
        Content = layout;

        if (!_store.CanSave) ShowStatus(Strings.ScratchpadNotSaving);
        _saveTimer.Tick += (_, _) => Save();
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) =>
        {
            // A menu of the pad's own or another Orbit window taking focus isn't leaving.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (!IsActive && !Pinned && !Application.Current.Windows.OfType<Window>().Any(w => w.IsActive)) HidePad();
            });
        };
        Select(_document.Selected);
    }

    private bool Pinned => _preferences.Get<bool>(Pref.ScratchpadPinned);

    /// <summary>Brings the pad up with the cursor in the selected tab.</summary>
    public void Present()
    {
        if (_document.ApplyRetention(_preferences.Get<int>(Pref.ScratchpadClearDays), DateTimeOffset.Now) > 0)
        {
            Changed();
            Select(_document.Selected);
        }
        if (!IsVisible && WindowPlacement.PointerMonitor() is { } monitor)
            WindowPlacement.ShowAt(this, WindowPlacement.Centered(monitor, Width, Height));
        else Show();
        Activate();
        FocusEditor();
    }

    public void HidePad()
    {
        Save();
        Hide();
    }

    /// <summary>
    /// Orbit is quitting: writes what waits, then closes. A save that keeps failing leaves a copy
    /// beside the file; when even that fails, every note goes to the clipboard and the user is told.
    /// </summary>
    public void Quit()
    {
        if (!Save() && _dirty)
        {
            if (_store.SaveCopy(_document, DateTimeOffset.Now) is { } copy) Orbit.Core.Log.Warn($"Scratchpad kept in a copy: {System.IO.Path.GetFileName(copy)}");
            else
            {
                Orbit.Core.Log.Warn("Scratchpad could not be saved at quit; notes put on the clipboard");
                Clipboards.CopyText(_document.AllText(), Strings.ScratchpadCopied);
                MessageBox.Show(Strings.ScratchpadLostAtQuit, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        _quitting = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Alt+F4 only hides: the pad lives until Orbit quits.
        if (!_quitting)
        {
            e.Cancel = true;
            HidePad();
        }
        base.OnClosing(e);
    }

    private Button HeaderButton(string glyph, string tip, Action click)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = Glyphs.Font, FontSize = 12, Foreground = Muted },
            Width = 30, Height = 28, Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = tip,
            Cursor = Cursors.Hand, Template = Design.Controls.RoundTemplate(6), Focusable = false, VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => click();
        return button;
    }

    private ContextMenu Menu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Design.Controls.MenuItem(Strings.ScratchpadCopyAll, () =>
            Clipboards.CopyText(_document.Current.Text, Strings.ScratchpadCopied)));
        var reopen = Design.Controls.MenuItem(Strings.ScratchpadReopen, Reopen);
        reopen.IsEnabled = _document.Closed.Count > 0;
        reopen.InputGestureText = "Ctrl+Shift+T";
        menu.Items.Add(reopen);
        menu.Items.Add(new Separator());
        var clear = new MenuItem { Header = Strings.ScratchpadClearAfter };
        int days = _preferences.Get<int>(Pref.ScratchpadClearDays);
        foreach (var (value, title) in new[] { (0, Strings.ScratchpadClearNever), (1, Strings.ScratchpadClearDays(1)), (7, Strings.ScratchpadClearDays(7)), (30, Strings.ScratchpadClearDays(30)) })
        {
            var item = Design.Controls.MenuItem(title, () => _preferences.Set(Pref.ScratchpadClearDays, value));
            item.IsChecked = value == days;
            clear.Items.Add(item);
        }
        clear.Items.Add(new Separator());
        clear.Items.Add(new MenuItem { Header = Strings.ScratchpadClearHint, IsEnabled = false });
        menu.Items.Add(clear);
        return menu;
    }

    private void TogglePin()
    {
        bool pinned = !Pinned;
        _preferences.Set(Pref.ScratchpadPinned, pinned);
        Topmost = pinned;
        ((TextBlock)_pin.Content).Text = pinned ? "" : "";
    }

    private void FillTabs()
    {
        _tabs.Children.Clear();
        foreach (var pad in _document.Pads) _tabs.Children.Add(Tab(pad));
    }

    private Border Tab(Pad pad)
    {
        bool selected = pad.Id == _document.Selected;
        var name = new TextBlock { Text = pad.Name, FontSize = 12.5, Foreground = selected ? Ink : Muted, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis };
        var close = new TextBlock { Text = "", FontFamily = Glyphs.Font, FontSize = 9, Foreground = Muted, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = Strings.ScratchpadCloseTab, Visibility = selected ? Visibility.Visible : Visibility.Hidden };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(name);
        row.Children.Add(close);
        var tab = new Border { Child = row, Padding = new Thickness(10, 5, 8, 5), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(7), Background = selected ? TabFill : Brushes.Transparent, Cursor = Cursors.Hand };
        tab.MouseEnter += (_, _) => close.Visibility = Visibility.Visible;
        tab.MouseLeave += (_, _) => close.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
        close.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CloseTab(pad.Id);
        };
        tab.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) Rename(pad.Id, tab, name);
            else if (pad.Id != _document.Selected) Select(pad.Id);
            e.Handled = true;
        };
        tab.MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle) CloseTab(pad.Id);
        };
        return tab;
    }

    /// <summary>The tab's name becomes a box: Enter or leaving keeps it, Esc puts it back.</summary>
    private void Rename(Guid id, Border tab, TextBlock name)
    {
        var box = new TextBox { Text = name.Text, FontSize = 12.5, MinWidth = 80, Padding = new Thickness(2, 0, 2, 0), BorderThickness = new Thickness(0, 0, 0, 1) };
        bool done = false;
        void Finish(bool keep)
        {
            if (done) return;
            done = true;
            var text = box.Text.Trim();
            if (keep && text.Length > 0) _document.Update(id, p => p with { Name = text });
            Changed();
            FillTabs();
            FocusEditor();
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                Finish(e.Key == Key.Enter);
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => Finish(true);
        tab.Child = box;
        box.Focus();
        box.SelectAll();
    }

    private void Select(Guid id)
    {
        _document.Selected = id;
        if (!_editors.TryGetValue(id, out var editor))
        {
            editor = new TextBox
            {
                Text = _document.Pads.First(p => p.Id == id).Text, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0), Padding = new Thickness(14, 10, 14, 10), FontSize = 14, Foreground = Ink,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, UndoLimit = 500, IsInactiveSelectionHighlightEnabled = true,
            };
            editor.TextChanged += (_, _) =>
            {
                var text = editor.Text;
                _document.Update(id, p => p with { Text = text, Modified = DateTimeOffset.Now });
                Changed();
                // Undo and redo in the preview; a flip already shows its own change.
                if (!_flipping && id == _document.Selected && InPreview) RenderPreview(keepScroll: true);
            };
            _editors[id] = editor;
            _editorHost.Children.Add(editor);
        }
        ShowSelected();
        FillTabs();
        // An open find bar searches the tab now in front, from its own caret; a preview has none.
        if (_findBar.Visibility == Visibility.Visible)
        {
            if (InPreview) _findBar.Visibility = Visibility.Collapsed;
            else
            {
                _findAnchor = editor.SelectionStart;
                FindAsTyped();
            }
        }
        FocusEditor();
    }

    private void FocusEditor()
    {
        if (!IsVisible) return;
        if (InPreview) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _preview.Focus());
        else if (_editors.TryGetValue(_document.Selected, out var editor)) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => editor.Focus());
    }

    private bool InPreview => _document.Current.Preview;

    /// <summary>Shows the selected tab as text or as its preview, and the button for the other one.</summary>
    private void ShowSelected()
    {
        bool preview = InPreview;
        foreach (var (key, box) in _editors)
            box.Visibility = key != _document.Selected ? Visibility.Collapsed : preview ? Visibility.Hidden : Visibility.Visible;
        _preview.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        if (preview) RenderPreview();
        else _preview.Document = null;
        ((TextBlock)_previewToggle.Content).Text = preview ? Glyphs.Edit : Glyphs.Preview;
        _previewToggle.ToolTip = preview ? Strings.ScratchpadShowEditor : Strings.ScratchpadShowPreview;
    }

    private void RenderPreview(bool keepScroll = false)
    {
        double offset = keepScroll ? PreviewScroller()?.VerticalOffset ?? 0 : 0;
        var id = _document.Selected;
        _preview.Document = ScratchpadPreview.Build(_document.Current.Text, FontFamily, bracket => FlipTask(id, bracket));
        if (keepScroll) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => PreviewScroller()?.ScrollToVerticalOffset(offset));
    }

    private ScrollViewer? PreviewScroller() => _preview.Template?.FindName("PART_ContentHost", _preview) as ScrollViewer;

    private void SetPreview(bool preview)
    {
        if (InPreview == preview) return;
        _document.Update(_document.Selected, p => p with { Preview = preview });
        Changed();
        if (preview) _findBar.Visibility = Visibility.Collapsed;
        ShowSelected();
    }

    private void TogglePreview()
    {
        SetPreview(!InPreview);
        FocusEditor();
    }

    /// <summary>
    /// Flips the task box at a source position as one undo step, keeping the box's selection. The
    /// length stays the same, so the other boxes' positions hold and the preview needs no rebuild.
    /// Says the box's new state, or null after a rebuild when the box is from another tab or the
    /// text there isn't a box any more.
    /// </summary>
    private bool? FlipTask(Guid tab, int bracket)
    {
        if (!_editors.TryGetValue(tab, out var editor)) return null;
        if (tab != _document.Selected || ScratchpadMarkdown.ToggledMark(editor.Text, bracket) is not { } mark)
        {
            RenderPreview(keepScroll: true);
            return null;
        }
        int start = editor.SelectionStart, length = editor.SelectionLength;
        _flipping = true;
        try
        {
            editor.BeginChange();
            editor.Select(bracket + 1, 1);
            editor.SelectedText = mark.ToString();
            editor.Select(start, length);
            editor.EndChange();
        }
        finally
        {
            _flipping = false;
        }
        return mark != ' ';
    }

    private void NewTab()
    {
        var pad = _document.Add(_document.NextName(Strings.ScratchpadTabName), DateTimeOffset.Now);
        Changed();
        Select(pad.Id);
    }

    private void CloseTab(Guid id)
    {
        if (_document.Pads.Count == 1)
        {
            HidePad();
            return;
        }
        bool hadText = _document.Pads.First(p => p.Id == id).Text.Length > 0;
        if (!_document.Close(id)) return;
        if (_editors.Remove(id, out var editor)) _editorHost.Children.Remove(editor);
        Changed();
        Select(_document.Selected);
        if (hadText) Toast.Show(Strings.ScratchpadClosed, Strings.ScratchpadClosedHint);
    }

    private void Reopen()
    {
        if (_document.Reopen() is not { } pad) return;
        Changed();
        Select(pad.Id);
    }

    private void Cycle(int by)
    {
        int at = _document.Pads.FindIndex(p => p.Id == _document.Selected);
        Select(_document.Pads[(at + by + _document.Pads.Count) % _document.Pads.Count].Id);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        e.Handled = true;
        switch (key)
        {
            case Key.T when ctrl && shift: Reopen(); break;
            case Key.T when ctrl: NewTab(); break;
            case Key.W when ctrl: CloseTab(_document.Selected); break;
            case Key.Tab when ctrl: Cycle(shift ? -1 : 1); break;
            case Key.E when ctrl: TogglePreview(); break;
            // The preview has no keyboard edit of its own; a tab's name box keeps its undo.
            case Key.Z when ctrl && shift && InPreview && Keyboard.FocusedElement is not TextBox: _editors[_document.Selected].Redo(); break;
            case Key.Z when ctrl && InPreview && Keyboard.FocusedElement is not TextBox: _editors[_document.Selected].Undo(); break;
            case Key.Y when ctrl && InPreview && Keyboard.FocusedElement is not TextBox: _editors[_document.Selected].Redo(); break;
            case Key.F when ctrl:
                SetPreview(false);
                OpenFind();
                break;
            case Key.F3:
                if (InPreview)
                {
                    // The preview closed the find bar and holds the focus: both go back to the text.
                    SetPreview(false);
                    _findBar.Visibility = Visibility.Visible;
                    FocusEditor();
                }
                Find(forward: !shift);
                break;
            case Key.Enter when _find.IsKeyboardFocused: Find(forward: !shift); break;
            // The tab name box handles its own Esc (put the name back).
            case Key.Escape when Keyboard.FocusedElement is TextBox box && box != _find && !_editors.ContainsValue(box): e.Handled = false; break;
            case Key.Escape when _findBar.Visibility == Visibility.Visible:
                _findBar.Visibility = Visibility.Collapsed;
                FocusEditor();
                break;
            case Key.Escape: HidePad(); break;
            default: e.Handled = false; break;
        }
    }

    private void OpenFind()
    {
        _findBar.Visibility = Visibility.Visible;
        if (_editors.TryGetValue(_document.Selected, out var editor))
        {
            _findAnchor = editor.SelectionStart;
            if (editor.SelectionLength > 0 && !editor.SelectedText.Contains('\n')) _find.Text = editor.SelectedText;
        }
        _find.Focus();
        _find.SelectAll();
    }

    /// <summary>Where find-as-you-type looks from: the caret when the find bar opened, then the match Enter or F3 moved to.</summary>
    private int _findAnchor;

    /// <summary>Each typed letter selects the first match from the anchor, with the count beside the box.</summary>
    private void FindAsTyped()
    {
        if (_findStatus is null || !_editors.TryGetValue(_document.Selected, out var editor)) return;
        var query = _find.Text;
        if (query.Length == 0)
        {
            _findStatus.Text = "";
            editor.Select(Math.Min(_findAnchor, editor.Text.Length), 0);
            return;
        }
        ShowMatch(editor, ScratchpadSearch.From(editor.Text, query, _findAnchor), query);
    }

    /// <summary>Selects and scrolls to the match at <paramref name="at"/> with the count beside the box, or says there is none.</summary>
    private void ShowMatch(TextBox editor, int at, string query)
    {
        if (at < 0)
        {
            _findStatus.Text = Strings.ScratchpadNotFound;
            return;
        }
        _findStatus.Text = Strings.ScratchpadMatches(ScratchpadSearch.Count(editor.Text, query));
        editor.Select(at, query.Length);
        editor.ScrollToLine(editor.GetLineIndexFromCharacterIndex(at));
    }

    /// <summary>The next or previous match from the selection, around the end; the editor keeps showing it selected.</summary>
    private void Find(bool forward)
    {
        if (!_editors.TryGetValue(_document.Selected, out var editor)) return;
        if (_find.Text.Length == 0)
        {
            OpenFind();
            return;
        }
        int at = ScratchpadSearch.Next(editor.Text, _find.Text, editor.SelectionStart, editor.SelectionLength, forward);
        if (at >= 0) _findAnchor = at;
        ShowMatch(editor, at, _find.Text);
    }

    private void Changed()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private bool Save()
    {
        _saveTimer.Stop();
        if (!_dirty) return true;
        if (!_store.Save(_document))
        {
            // Kept in memory; the next edit tries again.
            if (_store.CanSave) ShowStatus(Strings.ScratchpadSaveFailed);
            return false;
        }
        _dirty = false;
        _status.Visibility = Visibility.Collapsed;
        return true;
    }

    private void ShowStatus(string text)
    {
        _status.Text = text;
        _status.Visibility = Visibility.Visible;
    }
}
