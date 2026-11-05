using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Orbit.App.Design;

namespace Orbit.App.Settings;

/// <summary>
/// The settings window's left side (Orbit: App/MainWindow.swift <c>Sidebar</c>): the brand, then the
/// pages in groups, with a card that slides to the selected one. Still a list box underneath, so the
/// arrow keys and screen readers work as they do on any Windows list.
/// </summary>
internal sealed class SettingsSidebar : DockPanel
{
    /// <summary>Orbit's own width.</summary>
    public const double SideWidth = 228;

    private const double ItemHeight = 34, CardRadius = 9;
    private static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(320);

    /// <summary>The groups, as Orbit's <c>Page.sections</c> where Windows has the same page. The first has no heading.</summary>
    private static readonly (Func<string>? Title, string[] Pages)[] Sections =
    [
        (null, ["ring", "launcher"]),
        (() => Strings.SettingsSectionEssentials, ["capture", "clipboard", "translate", "snippets"]),
        (() => Strings.SettingsSectionUtilities, ["sound", "input", "keepAwake"]),
        (() => Strings.SettingsSectionWindows, ["system", "ports", "displays"]),
        (() => Strings.SettingsSectionSettings, ["overlaps", "shortcuts", "general", "about"]),
    ];

    private static readonly Dictionary<string, string> PageGlyphs = new()
    {
        ["ring"] = "",
        ["launcher"] = Glyphs.Search,
        ["capture"] = "",
        ["clipboard"] = "",
        ["translate"] = "",
        ["snippets"] = "",
        ["sound"] = Glyphs.Volume,
        ["input"] = Glyphs.Keyboard,
        ["keepAwake"] = "",
        ["system"] = "",
        ["ports"] = Glyphs.Network,
        ["displays"] = "",
        ["overlaps"] = "",
        ["shortcuts"] = "",
        ["general"] = Glyphs.Settings,
        ["about"] = "",
    };

    private readonly Palette _palette = Palette.Light;
    private readonly ListBox _list;
    private readonly Dictionary<string, Item> _items = [];
    private readonly TranslateTransform _cardPosition = new();
    private Item? _selected;

    /// <param name="titles">Every page's id and title. A page left out of the groups would be unreachable, so it is added at the end.</param>
    public SettingsSidebar(IReadOnlyList<(string Id, string Title)> titles)
    {
        Width = SideWidth;
        Background = _palette.Sidebar;

        var brand = Brand();
        SetDock(brand, Dock.Top);
        Children.Add(brand);

        _list = new ListBox
        {
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, Template = ListTemplate(),
            ItemContainerStyle = ItemStyle(), Padding = new Thickness(0),
        };
        VirtualizingPanel.SetIsVirtualizing(_list, false);

        var names = titles.ToDictionary(t => t.Id, t => t.Title);
        var left = titles.Select(t => t.Id).ToList();
        foreach (var (title, pages) in Sections)
        {
            if (title is not null) _list.Items.Add(Heading(title()));
            foreach (string id in pages)
            {
                if (names.TryGetValue(id, out string? name) && left.Remove(id)) AddPage(id, name);
            }
        }
        foreach (string id in left) AddPage(id, names[id]);
        Children.Add(_list);

        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is ListBoxItem { Tag: Item item }) Show(item, animate: true);
        };
        _list.Loaded += (_, _) =>
        {
            if (_selected is not null) MoveCard(_selected, false);
        };
    }

    /// <summary>A page was picked, by mouse, keyboard or <see cref="Select"/>.</summary>
    public event Action<string>? PageSelected;

    /// <summary>The ids in the order shown.</summary>
    public IEnumerable<string> PageIds => _list.Items.OfType<ListBoxItem>().Select(i => i.Tag).OfType<Item>().Select(i => i.Id);

    public string? SelectedId => _selected?.Id;

    /// <summary>The selection card's position, and the selected row's, for the self-test.</summary>
    public (double Card, double Row) CardAndRow()
    {
        if (_selected is null || Card() is not { Parent: FrameworkElement host }) return (-1, -1);
        return (_cardPosition.Y, _selected.Row.TranslatePoint(default, host).Y);
    }

    public void Select(string id)
    {
        if (_items.TryGetValue(id, out var item)) _list.SelectedItem = item.Row;
    }

    /// <summary>A small dot beside a page, for something on right now (Keep Awake).</summary>
    public void SetDot(string id, bool on)
    {
        if (_items.TryGetValue(id, out var item)) item.Dot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddPage(string id, string title)
    {
        var glyph = new TextBlock
        {
            Text = PageGlyphs.GetValueOrDefault(id, Glyphs.Document), FontFamily = Glyphs.Font, FontSize = 14, Width = 20,
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
        };
        var label = new TextBlock { Text = title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var dot = new Ellipse { Width = 7, Height = 7, Fill = _palette.Accent, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        var content = new DockPanel { LastChildFill = true };
        SetDock(glyph, Dock.Left);
        SetDock(dot, Dock.Right);
        content.Children.Add(glyph);
        content.Children.Add(dot);
        content.Children.Add(label);

        var row = new ListBoxItem { Content = content, Height = ItemHeight, Margin = new Thickness(10, 1, 10, 1), Padding = new Thickness(12, 0, 12, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        System.Windows.Automation.AutomationProperties.SetName(row, title);
        var item = new Item(id, row, glyph, label, dot);
        row.Tag = item;
        _items[id] = item;
        _list.Items.Add(row);
        Paint(item, false);
    }

    private void Show(Item item, bool animate)
    {
        if (_selected is not null) Paint(_selected, false);
        Paint(item, true);
        _selected = item;
        MoveCard(item, animate);
        PageSelected?.Invoke(item.Id);
    }

    private void Paint(Item item, bool selected)
    {
        var color = selected ? _palette.Ink : _palette.InkSecondary;
        item.Glyph.Foreground = color;
        item.Label.Foreground = color;
        item.Label.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private Border? Card() => _list.Template.FindName("Card", _list) as Border;

    private void MoveCard(Item item, bool animate)
    {
        if (Card() is not { Parent: FrameworkElement host } card) return;
        double y = item.Row.TranslatePoint(default, host).Y;
        // Set here: a template freezes what it is given, and this one has to animate.
        card.RenderTransform = _cardPosition;
        card.Visibility = Visibility.Visible;
        if (!animate || !SystemParameters.ClientAreaAnimation || !IsVisible)
        {
            _cardPosition.BeginAnimation(TranslateTransform.YProperty, null);
            _cardPosition.Y = y;
            return;
        }
        // Orbit's spring: 0.32 s, with a slight overshoot.
        _cardPosition.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, SlideTime)
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 },
        });
    }

    private UIElement Brand()
    {
        var logo = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(7), Background = Brushes.Black,
            Child = new OrbitMark { Size = 24, Ink = Brushes.White, Accent = _palette.Accent, Dot = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Margin = new Thickness(0, 0, 10, 0),
        };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = Strings.AppName, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = _palette.Ink });
        words.Children.Add(new TextBlock { Text = Strings.SettingsTagline, FontSize = 10, Foreground = _palette.InkSecondary, TextTrimming = TextTrimming.CharacterEllipsis });
        var brand = new DockPanel { Margin = new Thickness(18, 20, 18, 12) };
        SetDock(logo, Dock.Left);
        brand.Children.Add(logo);
        brand.Children.Add(words);
        return brand;
    }

    /// <summary>A group's heading. Disabled, so the arrow keys and clicks pass over it.</summary>
    private ListBoxItem Heading(string title) => new()
    {
        IsEnabled = false, Focusable = false, Margin = new Thickness(22, 16, 10, 5), Padding = new Thickness(0),
        Content = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Palette.Fade(_palette.InkSecondary, 0.8) },
    };

    /// <summary>The list's content scrolling over the card, with no bar: the mouse wheel and the keys still scroll, as in Orbit.</summary>
    private ControlTemplate ListTemplate()
    {
        var card = new FrameworkElementFactory(typeof(Border), "Card");
        card.SetValue(FrameworkElement.HeightProperty, ItemHeight);
        card.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        card.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 10, 0));
        card.SetValue(Border.CornerRadiusProperty, new CornerRadius(CardRadius));
        card.SetValue(Border.BackgroundProperty, _palette.Canvas);
        card.SetValue(Border.BorderBrushProperty, _palette.Hairline);
        card.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        card.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);

        var host = new FrameworkElementFactory(typeof(Grid));
        host.AppendChild(card);
        host.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));

        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, false);
        scroll.SetValue(UIElement.FocusableProperty, false);
        scroll.AppendChild(host);
        return new ControlTemplate(typeof(ListBox)) { VisualTree = scroll };
    }

    private Style ItemStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(CardRadius));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border };
        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty, Value = true,
            Setters = { new Setter(Control.BackgroundProperty, Palette.Fade(_palette.Ink, 0.04)) },
        });

        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(FocusVisualStyleProperty, FocusStyle()));
        return style;
    }

    /// <summary>Only for the keyboard: a mouse click draws no outline.</summary>
    private Style FocusStyle()
    {
        var outline = new FrameworkElementFactory(typeof(Rectangle));
        outline.SetValue(Rectangle.RadiusXProperty, CardRadius);
        outline.SetValue(Rectangle.RadiusYProperty, CardRadius);
        outline.SetValue(Shape.StrokeProperty, _palette.Ink);
        outline.SetValue(Shape.StrokeThicknessProperty, 1.5);
        var style = new Style(typeof(Control));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control)) { VisualTree = outline }));
        return style;
    }

    private sealed record Item(string Id, ListBoxItem Row, TextBlock Glyph, TextBlock Label, Ellipse Dot);
}
