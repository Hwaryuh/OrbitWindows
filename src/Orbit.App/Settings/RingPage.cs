using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Orbit.App.Design;
using Orbit.App.Features.Ring;
using Orbit.Core.Actions;
using Orbit.Core.Ring;
using Orbit.Core.Settings;

namespace Orbit.App.Settings;

/// <summary>
/// The ring page (Orbit: App/Pages/RingPage.swift): the ring drawn as it is, its slots chosen by a
/// click and filled from the action list (a click, or a drag onto a slot; slots dragged onto each
/// other swap), how many slots, presets, how it opens and how resting chooses. Not ported yet:
/// Orbit's "Try it" mode and a shortcut per slot (the shortcut recorder comes later).
/// </summary>
internal sealed class RingPage : StackPanel
{
    private const double EditorSize = 320;
    private const double EditorCenter = EditorSize / 2;
    private const double EditorRadius = EditorSize * 0.335;
    private const double MiniSize = 92;
    private const string DragFormat = "OrbitRingItem";
    private static readonly TimeSpan UndoTime = TimeSpan.FromSeconds(6);
    private static readonly ActionGroup[] Groups = [ActionGroup.Capture, ActionGroup.Clipboard, ActionGroup.Orbit, ActionGroup.System, ActionGroup.Media];

    private readonly Preferences _preferences;
    private readonly RingPresets _presets;
    private readonly Palette _palette = Palette.Light;
    private readonly TextBlock _count = new() { FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button _fewer;
    private readonly Button _more;
    private readonly Canvas _editor = new() { Width = EditorSize, Height = EditorSize };
    private readonly TextBlock _libraryTitle = new() { FontSize = 13, FontWeight = FontWeights.SemiBold };
    private readonly TextBox _search;
    private readonly StackPanel _library = new();
    private readonly WrapPanel _presetCards = new();
    private readonly StackPanel _undoBar = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(10, 0, 0, 0) };
    private readonly StackPanel _naming = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBox _name;
    private readonly DispatcherTimer _undoTimer = new() { Interval = UndoTime };

    private IReadOnlyList<OrbitAction> _slots;
    private int _selected;
    /// <summary>The ring before a preset was chosen, and the preset: undo only while the ring is still that preset.</summary>
    private (IReadOnlyList<OrbitAction> Before, IReadOnlyList<OrbitAction> Applied)? _undo;
    /// <summary>The saved preset being renamed in the name box; null while saving a new one.</summary>
    private string? _renaming;
    private int _dropTarget = -1;

    public RingPage(Preferences preferences)
    {
        _preferences = preferences;
        _presets = new RingPresets(preferences);
        _slots = _presets.Current;

        _fewer = StepButton("−", Strings.RingFewer, () => Resize(_slots.Count - 1));
        _more = StepButton("+", Strings.RingMore, () => Resize(_slots.Count + 1));
        _search = _palette.PlainField(12);
        _search.TextChanged += (_, _) => FillLibrary();
        // Transparent, so the hint behind it shows while it is empty.
        _name = new TextBox { Width = 220, FontSize = 13, Padding = new Thickness(6, 3, 6, 3), VerticalContentAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
        _name.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) CommitName();
            else if (e.Key == Key.Escape) _naming.Visibility = Visibility.Collapsed;
        };
        _undoTimer.Tick += (_, _) => HideUndo();
        Unloaded += (_, _) => _undoTimer.Stop();

        Children.Add(Header());
        Children.Add(EditorRow());
        Children.Add(PresetSection());
        Children.Add(TriggerSection());
        Children.Add(BehaviorSection());
        Refresh();
    }

    // MARK: Layout

    private UIElement Header()
    {
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = Strings.PageRing, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink });
        titles.Children.Add(Caption(Strings.RingSubtitle));

        var label = new StackPanel { Width = 38, VerticalAlignment = VerticalAlignment.Center };
        _count.Foreground = _palette.Ink;
        label.Children.Add(_count);
        label.Children.Add(new TextBlock { Text = Strings.RingSlotsLabel, FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = _palette.InkSecondary, HorizontalAlignment = HorizontalAlignment.Center });
        var stepper = new StackPanel { Orientation = Orientation.Horizontal };
        stepper.Children.Add(_fewer);
        stepper.Children.Add(label);
        stepper.Children.Add(_more);
        var pill = new Border { Background = _palette.Raised, CornerRadius = new CornerRadius(17), Padding = new Thickness(3), Child = stepper, VerticalAlignment = VerticalAlignment.Top };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        DockPanel.SetDock(pill, Dock.Right);
        header.Children.Add(pill);
        header.Children.Add(titles);
        return header;
    }

    private UIElement EditorRow()
    {
        var libraryHeader = new StackPanel { Margin = new Thickness(14, 12, 14, 8) };
        _libraryTitle.Foreground = _palette.Ink;
        libraryHeader.Children.Add(_libraryTitle);
        libraryHeader.Children.Add(new TextBlock { Text = Strings.RingLibraryHint, FontSize = 11, Foreground = _palette.InkSecondary });

        var searchRow = new DockPanel();
        var icon = new TextBlock { Text = Glyphs.Search, FontFamily = Glyphs.Font, FontSize = 11, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        DockPanel.SetDock(icon, Dock.Left);
        searchRow.Children.Add(icon);
        searchRow.Children.Add(_palette.WithHint(_search, Strings.RingSearchActions, 3));
        var search = new Border { Background = _palette.Raised, CornerRadius = new CornerRadius(8), Height = 28, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(12, 0, 12, 6), Child = searchRow };

        var list = new ScrollViewer { Content = _library, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6, 0, 6, 10) };
        var library = new DockPanel();
        DockPanel.SetDock(libraryHeader, Dock.Top);
        DockPanel.SetDock(search, Dock.Top);
        library.Children.Add(libraryHeader);
        library.Children.Add(search);
        library.Children.Add(list);
        var libraryCard = Card(library, padding: 0);
        libraryCard.Height = EditorSize;

        var row = new Grid { Margin = new Thickness(0, 0, 0, 28) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(EditorSize) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(_editor);
        Grid.SetColumn(libraryCard, 2);
        row.Children.Add(libraryCard);
        return row;
    }

    private UIElement PresetSection()
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 28) };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 10) };
        heading.Children.Add(SectionTitle(Strings.RingPresets));
        _undoBar.Children.Add(new TextBlock { Text = Strings.RingPresetApplied, FontSize = 11, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center });
        var undo = LinkButton(Strings.RingUndo, Undo);
        undo.Margin = new Thickness(8, 0, 0, 0);
        _undoBar.Children.Add(undo);
        heading.Children.Add(_undoBar);
        section.Children.Add(heading);
        section.Children.Add(_presetCards);

        _naming.Children.Add(_palette.WithHint(_name, Strings.RingPresetNamePlaceholder, 9));
        var save = PillButton(Strings.Save, prominent: true, CommitName);
        save.Margin = new Thickness(8, 0, 0, 0);
        _naming.Children.Add(save);
        var cancel = PillButton(Strings.Cancel, prominent: false, () => _naming.Visibility = Visibility.Collapsed);
        cancel.Margin = new Thickness(6, 0, 0, 0);
        _naming.Children.Add(cancel);
        section.Children.Add(_naming);
        return section;
    }

    private UIElement TriggerSection()
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 28) };
        section.Children.Add(SectionTitle(Strings.RingOpenTitle, bottom: 10));

        var keys = KeyCap.Row(Features.RingFeature.Shortcut.Labels(), _palette);
        keys.HorizontalAlignment = HorizontalAlignment.Left;
        keys.Margin = new Thickness(0, 12, 0, 0);
        var keyboard = new StackPanel();
        keyboard.Children.Add(CardTitle(Glyphs.Keyboard, Strings.RingKeyboard, Strings.RingKeyboardDetail));
        keyboard.Children.Add(keys);
        keyboard.Children.Add(Caption(Strings.RingKeyboardFixed));

        var mouseOn = _preferences.Get<bool>(Pref.RingMouse);
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var hint = Caption(mouseOn ? Strings.RingSideButtonsHint : Strings.RingMouseOffHint);
        void FillButtons()
        {
            buttons.Children.Clear();
            var current = _preferences.Get<string>(Pref.RingMouseButton);
            bool on = _preferences.Get<bool>(Pref.RingMouse);
            foreach (var (value, title) in new[] { ("middle", Strings.RingMiddleButton), ("x1", Strings.RingBackButton), ("x2", Strings.RingForwardButton) })
            {
                var pill = PillButton(title, prominent: current == value, () =>
                {
                    _preferences.Set(Pref.RingMouseButton, value);
                    FillButtons();
                });
                pill.Margin = new Thickness(0, 0, 6, 6);
                pill.IsEnabled = on;
                buttons.Children.Add(pill);
            }
            buttons.Opacity = on ? 1 : 0.4;
            hint.Text = on ? Strings.RingSideButtonsHint : Strings.RingMouseOffHint;
        }
        var toggle = Toggle(mouseOn, on =>
        {
            _preferences.Set(Pref.RingMouse, on);
            FillButtons();
        });
        var mouse = new StackPanel();
        var mouseTitle = new DockPanel();
        DockPanel.SetDock(toggle, Dock.Right);
        mouseTitle.Children.Add(toggle);
        mouseTitle.Children.Add(CardTitle("", Strings.RingMouse, Strings.RingMouseDetail));
        mouse.Children.Add(mouseTitle);
        mouse.Children.Add(buttons);
        mouse.Children.Add(hint);
        FillButtons();

        var cards = new UniformGrid { Columns = 2 };
        var left = Card(keyboard);
        left.Margin = new Thickness(0, 0, 6, 0);
        var right = Card(mouse);
        right.Margin = new Thickness(6, 0, 0, 0);
        cards.Children.Add(left);
        cards.Children.Add(right);
        section.Children.Add(cards);
        return section;
    }

    private UIElement BehaviorSection()
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        section.Children.Add(SectionTitle(Strings.RingChoosing, bottom: 10));

        double dwell = Math.Clamp(_preferences.Get<double>(Pref.RingDwell), 0.25, 1.0);
        var value = new TextBlock { Text = Strings.Seconds(dwell), FontSize = 12, Foreground = _palette.InkSecondary, Width = 52, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = 0.25, Maximum = 1.0, TickFrequency = 0.05, IsSnapToTickEnabled = true, Value = dwell, Width = 180, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) =>
        {
            double seconds = Math.Round(e.NewValue, 2);
            value.Text = Strings.Seconds(seconds);
            _preferences.Set(Pref.RingDwell, seconds);
        };
        var timeRow = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(slider);
        controls.Children.Add(value);
        DockPanel.SetDock(controls, Dock.Right);
        timeRow.Children.Add(controls);
        timeRow.Children.Add(new TextBlock { Text = Strings.RingRestTime, FontSize = 13, Foreground = _palette.Ink, VerticalAlignment = VerticalAlignment.Center });

        bool hover = _preferences.Get<bool>(Pref.RingHover);
        timeRow.Visibility = hover ? Visibility.Visible : Visibility.Collapsed;
        var toggle = Toggle(hover, on =>
        {
            _preferences.Set(Pref.RingHover, on);
            timeRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        });
        var restRow = new DockPanel();
        DockPanel.SetDock(toggle, Dock.Right);
        restRow.Children.Add(toggle);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = Strings.RingRest, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink });
        text.Children.Add(new TextBlock { Text = Strings.RingRestDetail, FontSize = 11, Foreground = _palette.InkSecondary });
        restRow.Children.Add(text);

        var body = new StackPanel();
        body.Children.Add(restRow);
        body.Children.Add(timeRow);
        section.Children.Add(Card(body));
        section.Children.Add(Caption(Strings.RingRestFooter));
        return section;
    }

    // MARK: State

    /// <summary>Draws everything that shows the slots again.</summary>
    private void Refresh()
    {
        _selected = Math.Clamp(_selected, 0, _slots.Count - 1);
        _count.Text = _slots.Count.ToString();
        _fewer.IsEnabled = _slots.Count > RingSlots.Min;
        _more.IsEnabled = _slots.Count < RingSlots.Max;
        _fewer.Opacity = _fewer.IsEnabled ? 1 : 0.35;
        _more.Opacity = _more.IsEnabled ? 1 : 0.35;
        _libraryTitle.Text = Strings.SlotTitle(SlotName(_selected));
        DrawEditor();
        FillLibrary();
        FillPresets();
    }

    private string SlotName(int index) => Strings.SlotName(RingPresets.Compass(index, _slots.Count), index);

    /// <summary>An edit: the ring and My ring change, and an offer to undo a preset is gone.</summary>
    private void Edit(IReadOnlyList<OrbitAction> slots)
    {
        if (ReferenceEquals(slots, _slots)) return;
        _slots = slots;
        _presets.Edit(slots);
        HideUndo();
        Refresh();
    }

    private void Resize(int count) => Edit(RingPresets.Resize(_slots, count));

    private void Assign(OrbitAction action)
    {
        int index = _selected;
        Edit(RingPresets.Assign(_slots, index, action));
        // On to the next slot, so filling the ring is a quick click, click, click.
        _selected = (index + 1) % _slots.Count;
        Refresh();
    }

    private void Choose(IReadOnlyList<OrbitAction> preset)
    {
        if (preset.SequenceEqual(_slots)) return;
        var before = _undo is { } pending && pending.Applied.SequenceEqual(_slots) ? pending.Before : _slots;
        _slots = preset;
        _presets.Apply(preset);
        _undo = (before, preset);
        _undoBar.Visibility = Visibility.Visible;
        _undoTimer.Stop();
        _undoTimer.Start();
        Refresh();
    }

    private void Undo()
    {
        if (_undo is { } undo && undo.Applied.SequenceEqual(_slots))
        {
            _slots = undo.Before;
            _presets.Apply(undo.Before);
        }
        HideUndo();
        Refresh();
    }

    private void HideUndo()
    {
        _undo = null;
        _undoTimer.Stop();
        _undoBar.Visibility = Visibility.Collapsed;
    }

    private void StartNaming(string? renaming, string text)
    {
        _renaming = renaming;
        _name.Text = text;
        _naming.Visibility = Visibility.Visible;
        _name.Focus();
        _name.SelectAll();
    }

    private void CommitName()
    {
        if (_renaming is { } id) _presets.Rename(id, _name.Text);
        else _presets.Save(_name.Text, _slots, Strings.RingDefaultPresetName);
        _naming.Visibility = Visibility.Collapsed;
        FillPresets();
    }

    // MARK: Editor

    private void DrawEditor()
    {
        _editor.Children.Clear();
        var disc = new Ellipse { Width = EditorSize - 2, Height = EditorSize - 2, Fill = _palette.Canvas, Stroke = _palette.Hairline, StrokeThickness = 1 };
        RingDrawing.Place(_editor, disc, EditorCenter, EditorCenter);
        _editor.Children.Add(RingDrawing.Spokes(_slots.Count, EditorCenter, 54, EditorSize / 2 - 10, RingDrawing.SpokeStroke(_palette)));
        var orbit = new Ellipse
        {
            Width = EditorRadius * 2, Height = EditorRadius * 2, Stroke = Palette.Fade(_palette.Ink, 0.14), StrokeThickness = 1,
            StrokeDashArray = [2, 4],
        };
        RingDrawing.Place(_editor, orbit, EditorCenter, EditorCenter);

        double scale = RingTracker.ItemScale(_slots.Count);
        for (int i = 0; i < _slots.Count; i++)
        {
            var (x, y) = RingDrawing.Offset(i * 2 * Math.PI / _slots.Count, EditorRadius);
            RingDrawing.Place(_editor, Slot(i, _slots[i], scale), EditorCenter + x, EditorCenter + y);
        }

        var hub = new Ellipse { Width = 100, Height = 100, Fill = _palette.Surface, Stroke = _palette.Hairline, StrokeThickness = 1 };
        RingDrawing.Place(_editor, hub, EditorCenter, EditorCenter);
        var inner = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        inner.Children.Add(new TextBlock { Text = _slots[_selected].ShortTitle(), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        inner.Children.Add(new TextBlock { Text = SlotName(_selected), FontSize = 10, Foreground = _palette.InkSecondary, TextAlignment = TextAlignment.Center });
        var center = new Grid { Width = 88, Height = 100 };
        center.Children.Add(inner);
        RingDrawing.Place(_editor, center, EditorCenter, EditorCenter);
    }

    private FrameworkElement Slot(int index, OrbitAction action, double scale)
    {
        bool lit = index == _selected;
        double size = 58 * scale;
        var root = new Grid { Width = 72 * scale, Height = 72 * scale, Background = Brushes.Transparent, Cursor = Cursors.Hand, AllowDrop = true, ToolTip = $"{SlotName(index)}: {action.Title()}" };
        root.Children.Add(new Ellipse
        {
            Width = size, Height = size, Fill = lit ? _palette.Accent : _palette.Surface, Stroke = lit ? Brushes.Transparent : _palette.Hairline,
            StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        root.Children.Add(new TextBlock
        {
            Text = Glyphs.Of(action), FontFamily = Glyphs.Font, FontSize = 20 * scale, Foreground = lit ? Brushes.White : _palette.Ink,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        if (index == _dropTarget)
        {
            root.Children.Add(new Ellipse
            {
                Width = size + 12, Height = size + 12, Stroke = _palette.Accent, StrokeThickness = 2, StrokeDashArray = [2, 1.5],
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
        }

        ClickOrDrag(root, $"slot:{index}", DragDropEffects.Move, () =>
        {
            _selected = index;
            Refresh();
        });
        // Only the slot itself takes the pointer: drag events from its parts would bubble up as leaving and entering.
        foreach (UIElement part in root.Children) part.IsHitTestVisible = false;
        root.DragEnter += (_, e) => SetDropTarget(e, index);
        root.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        root.DragLeave += (_, e) => SetDropTarget(e, -1);
        root.Drop += (_, e) =>
        {
            e.Handled = true;
            _dropTarget = -1;
            if (e.Data.GetData(DragFormat) is not string item) return;
            Dropped(item, index);
        };
        return root;
    }

    private void SetDropTarget(DragEventArgs e, int index)
    {
        if (!e.Data.GetDataPresent(DragFormat) || _dropTarget == index) return;
        _dropTarget = index;
        // Drawn on the next turn: redrawing removes the element this event is still routing through.
        Dispatcher.BeginInvoke(DrawEditor);
    }

    /// <summary>A slot onto a slot swaps them; an action onto a slot puts it there.</summary>
    private void Dropped(string item, int index)
    {
        if (index >= _slots.Count) return;
        if (item.StartsWith("slot:", StringComparison.Ordinal) && int.TryParse(item.AsSpan(5), out int from) && from < _slots.Count)
            Edit(RingPresets.Swap(_slots, from, index));
        else if (item.StartsWith("action:", StringComparison.Ordinal) && OrbitActions.FromId(item[7..]) is { } action)
            Edit(RingPresets.Assign(_slots, index, action));
        _selected = index;
        Refresh();
    }

    /// <summary>
    /// A press on <paramref name="element"/> that moves past the system's drag distance drags
    /// <paramref name="item"/>; one released over it without moving is a click. The element holds
    /// the mouse while pressed, so a press that wanders off and back never counts.
    /// </summary>
    private static void ClickOrDrag(FrameworkElement element, string item, DragDropEffects effects, Action click)
    {
        Point? pressAt = null;
        element.MouseLeftButtonDown += (_, e) =>
        {
            pressAt = e.GetPosition(element);
            element.CaptureMouse();
            e.Handled = true;
        };
        element.LostMouseCapture += (_, _) => pressAt = null;
        element.MouseMove += (_, e) =>
        {
            if (pressAt is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var now = e.GetPosition(element);
            if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            element.ReleaseMouseCapture();
            DragDrop.DoDragDrop(element, new DataObject(DragFormat, item), effects);
        };
        element.MouseLeftButtonUp += (_, e) =>
        {
            bool clicked = pressAt is not null && element.InputHitTest(e.GetPosition(element)) is not null;
            element.ReleaseMouseCapture();
            if (!clicked) return;
            e.Handled = true;
            click();
        };
    }

    // MARK: Library

    private void FillLibrary()
    {
        _library.Children.Clear();
        var query = _search.Text.Trim();
        foreach (var group in Groups)
        {
            var actions = OrbitActions.All.Where(a => a.Group() == group && a != OrbitAction.Settings
                && (query.Length == 0 || a.Title().Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || a.Keywords().Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
            if (actions.Count == 0) continue;
            _library.Children.Add(new TextBlock
            {
                Text = actions[0].GroupTitle(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = _palette.InkSecondary,
                Margin = new Thickness(8, _library.Children.Count == 0 ? 2 : 10, 0, 3),
            });
            foreach (var action in actions) _library.Children.Add(LibraryRow(action));
        }
    }

    private FrameworkElement LibraryRow(OrbitAction action)
    {
        bool current = _slots[_selected] == action;
        bool used = _slots.Contains(action);
        var tile = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = current ? _palette.Accent : _palette.Raised,
            Child = new TextBlock { Text = Glyphs.Of(action), FontFamily = Glyphs.Font, FontSize = 12, Foreground = current ? Brushes.White : _palette.Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var row = new DockPanel { Height = 34 };
        DockPanel.SetDock(tile, Dock.Left);
        row.Children.Add(tile);
        if (current || used)
        {
            var mark = current
                ? new TextBlock { Text = "", FontFamily = Glyphs.Font, FontSize = 11, Foreground = _palette.Accent, VerticalAlignment = VerticalAlignment.Center }
                : new TextBlock { Text = Strings.RingOnRing, FontSize = 10, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(mark, Dock.Right);
            row.Children.Add(mark);
        }
        row.Children.Add(new TextBlock { Text = action.Title(), FontSize = 12, FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, Foreground = _palette.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis });

        var cell = new Border { Child = row, Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        cell.MouseEnter += (_, _) => cell.Background = Palette.Fade(_palette.Ink, 0.05);
        cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
        ClickOrDrag(cell, $"action:{action.Id()}", DragDropEffects.Copy | DragDropEffects.Move, () => Assign(action));
        return cell;
    }

    // MARK: Presets

    private void FillPresets()
    {
        _presetCards.Children.Clear();
        _presetCards.Children.Add(PresetCard(Strings.RingMine, Strings.RingMineDetail, _presets.Custom, mine: true));
        foreach (var (kind, slots) in RingPresets.BuiltIn)
            _presetCards.Children.Add(PresetCard(Strings.RingPresetName(kind), Strings.RingPresetDetail(kind), slots));
        foreach (var saved in _presets.Saved)
        {
            var card = PresetCard(saved.Name, Strings.RingSavedDetail, saved.Slots);
            var menu = new ContextMenu();
            menu.Items.Add(Controls.MenuItem(Strings.Rename, () => StartNaming(saved.Id, saved.Name)));
            menu.Items.Add(Controls.MenuItem(Strings.RingUpdatePreset, () => { _presets.Update(saved.Id, _slots); FillPresets(); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Controls.MenuItem(Strings.Delete, () => { _presets.Delete(saved.Id); FillPresets(); }));
            card.ContextMenu = menu;
            _presetCards.Children.Add(card);
        }
        _presetCards.Children.Add(SaveCard());
    }

    private FrameworkElement PresetCard(string name, string detail, IReadOnlyList<OrbitAction> slots, bool mine = false)
    {
        bool active = slots.SequenceEqual(_slots);
        var body = new StackPanel();
        body.Children.Add(MiniRing(slots));
        var title = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 1) };
        if (mine) title.Children.Add(new TextBlock { Text = "", FontFamily = Glyphs.Font, FontSize = 10, Foreground = _palette.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        title.Children.Add(new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 100 });
        body.Children.Add(title);
        body.Children.Add(new TextBlock { Text = detail, FontSize = 10, Foreground = _palette.InkSecondary, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 8, 0) });
        var card = PresetFrame(body, active);
        card.MouseLeftButtonUp += (_, _) => Choose(slots);
        return card;
    }

    private FrameworkElement SaveCard()
    {
        var body = new StackPanel();
        var plus = new Grid { Width = MiniSize, Height = MiniSize, HorizontalAlignment = HorizontalAlignment.Center };
        plus.Children.Add(new Ellipse { Stroke = _palette.Hairline, StrokeThickness = 1.5, StrokeDashArray = [3, 3] });
        plus.Children.Add(new TextBlock { Text = "", FontFamily = Glyphs.Font, FontSize = 20, Foreground = _palette.InkSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(plus);
        body.Children.Add(new TextBlock { Text = Strings.RingSaveAsPreset, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 1) });
        body.Children.Add(new TextBlock { Text = Strings.RingSaveDetail, FontSize = 10, Foreground = _palette.InkSecondary, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 8, 0) });
        var card = PresetFrame(body, active: false);
        card.MouseLeftButtonUp += (_, _) => StartNaming(null, "");
        return card;
    }

    private Border PresetFrame(UIElement body, bool active)
    {
        var card = new Border
        {
            Width = 124, MinHeight = 170, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(0, 14, 0, 14), CornerRadius = new CornerRadius(14),
            Background = _palette.Surface, BorderBrush = active ? _palette.Accent : _palette.Hairline, BorderThickness = new Thickness(active ? 2 : 1),
            Cursor = Cursors.Hand, Child = body,
        };
        if (!active)
        {
            card.MouseEnter += (_, _) => card.BorderBrush = Palette.Fade(_palette.Ink, 0.2);
            card.MouseLeave += (_, _) => card.BorderBrush = _palette.Hairline;
        }
        return card;
    }

    /// <summary>A small still picture of a ring, for the presets.</summary>
    private FrameworkElement MiniRing(IReadOnlyList<OrbitAction> slots)
    {
        const double c = MiniSize / 2;
        var canvas = new Canvas { Width = MiniSize, Height = MiniSize, HorizontalAlignment = HorizontalAlignment.Center };
        RingDrawing.Place(canvas, new Ellipse { Width = MiniSize, Height = MiniSize, Fill = _palette.Canvas, Stroke = _palette.Hairline, StrokeThickness = 1 }, c, c);
        canvas.Children.Add(RingDrawing.Spokes(slots.Count, c, MiniSize * 0.16, MiniSize * 0.47, Palette.Fade(_palette.Ink, 0.06)));
        double item = MiniSize * 0.2 * RingTracker.ItemScale(slots.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            var (x, y) = RingDrawing.Offset(i * 2 * Math.PI / slots.Count, MiniSize * 0.34);
            var dot = new Grid { Width = item, Height = item };
            dot.Children.Add(new Ellipse { Fill = _palette.Surface });
            dot.Children.Add(new TextBlock { Text = Glyphs.Of(slots[i]), FontFamily = Glyphs.Font, FontSize = MiniSize * 0.09, Foreground = _palette.Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            RingDrawing.Place(canvas, dot, c + x, c + y);
        }
        RingDrawing.Place(canvas, new OrbitMark { Size = MiniSize * 0.2, Ink = _palette.Ink, Accent = _palette.Accent, Dot = true, Width = MiniSize * 0.2, Height = MiniSize * 0.2 }, c, c);
        return canvas;
    }

    // MARK: Pieces

    private Border Card(UIElement child, double padding = 16) => new()
    {
        Background = _palette.Surface, BorderBrush = _palette.Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
        Padding = new Thickness(padding), Child = child,
    };

    private UIElement CardTitle(string glyph, string title, string detail)
    {
        var icon = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Background = _palette.Ink, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = glyph, FontFamily = Glyphs.Font, FontSize = 15, Foreground = _palette.Canvas, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = _palette.InkSecondary, TextWrapping = TextWrapping.Wrap });
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(text);
        return row;
    }

    private TextBlock SectionTitle(string text, double bottom = 0) => new()
    {
        Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = _palette.InkSecondary,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, bottom),
    };

    private TextBlock Caption(string text) =>
        new() { Text = text, FontSize = 11.5, Foreground = _palette.InkSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    private Button StepButton(string label, string help, Action click)
    {
        var button = new Button
        {
            Content = label, Width = 28, Height = 28, FontSize = 14, FontWeight = FontWeights.Bold, ToolTip = help,
            Foreground = _palette.Ink, Background = _palette.Surface, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            Template = Controls.RoundTemplate(14),
        };
        button.Click += (_, _) => click();
        return button;
    }

    private Button PillButton(string label, bool prominent, Action click)
    {
        var button = new Button
        {
            Content = label, Height = 28, Padding = new Thickness(12, 0, 12, 0), FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = prominent ? _palette.Canvas : _palette.Ink, Background = prominent ? _palette.Ink : _palette.Raised,
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Template = Controls.RoundTemplate(14),
        };
        button.Click += (_, _) => click();
        return button;
    }

    private Button LinkButton(string label, Action click)
    {
        var button = new Button
        {
            Content = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = _palette.Accent, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Template = Controls.RoundTemplate(0),
        };
        button.Click += (_, _) => click();
        return button;
    }

    private CheckBox Toggle(bool on, Action<bool> changed)
    {
        var box = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 2, 0, 0), Cursor = Cursors.Hand };
        box.Click += (_, _) => changed(box.IsChecked == true);
        return box;
    }
}
