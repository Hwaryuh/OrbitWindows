using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Effects;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.Core.Display;

namespace Orbit.App.Features.Snippets;

/// <summary>
/// A small card asking for a snippet's {input:…} fields before it goes in (Orbit:
/// <c>SnippetInputPrompt</c>). Enter moves to the next field and inserts on the last; Esc or a
/// click elsewhere cancels. One at a time.
/// </summary>
internal sealed class InputPrompt : OverlayWindow
{
    private const double CardWidth = 360, Shadow = 12;

    private static InputPrompt? _open;

    private readonly TaskCompletionSource<Dictionary<string, string>?> _done = new();
    private readonly List<(string Name, TextBox Box)> _fields = [];

    private InputPrompt(string title, IReadOnlyList<string> fields) : base(activating: true, transparent: true, clickThrough: false)
    {
        var palette = Palette.Current();
        var parts = new StackPanel { Margin = new Thickness(18) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(new TextBlock { Text = "", FontFamily = Glyphs.Font, FontSize = 14, Foreground = palette.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        head.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = palette.Ink, TextTrimming = TextTrimming.CharacterEllipsis });
        parts.Children.Add(head);
        foreach (var name in fields)
        {
            parts.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = palette.InkSecondary, Margin = new Thickness(0, 0, 0, 4) });
            var box = palette.PlainField(13);
            box.Height = 30;
            box.Padding = new Thickness(8, 0, 8, 0);
            var frame = new Border { Background = palette.Raised, CornerRadius = new CornerRadius(8), Child = box, Margin = new Thickness(0, 0, 0, 12) };
            parts.Children.Add(frame);
            _fields.Add((name, box));
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Pill(Strings.SnippetCancel, false, palette, () => Finish(null)));
        var insert = Pill(Strings.SnippetInsert, true, palette, () => Finish(Values()));
        insert.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(insert);
        parts.Children.Add(buttons);
        Content = new Border
        {
            Margin = new Thickness(Shadow), Width = CardWidth, Background = palette.Canvas, BorderBrush = palette.Hairline, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Child = parts, VerticalAlignment = VerticalAlignment.Top,
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.22 },
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Finish(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                int i = _fields.FindIndex(f => f.Box.IsKeyboardFocusWithin);
                if (i >= 0 && i + 1 < _fields.Count) _fields[i + 1].Box.Focus();
                else Finish(Values());
                e.Handled = true;
            }
        };
        Deactivated += (_, _) => Finish(null);
    }

    /// <summary>The values by field name; null when cancelled.</summary>
    public static Task<Dictionary<string, string>?> AskAsync(string title, IReadOnlyList<string> fields)
    {
        _open?.Finish(null);
        var prompt = _open = new InputPrompt(title, fields);
        var (x, y) = Monitors.Cursor();
        if (Monitors.FromPoint(x, y) is { } monitor)
        {
            var card = (FrameworkElement)prompt.Content;
            card.Measure(new Size(CardWidth + Shadow * 2, double.PositiveInfinity));
            double s = monitor.Scale;
            int w = (int)Math.Ceiling((CardWidth + Shadow * 2) * s), h = (int)Math.Ceiling(card.DesiredSize.Height * s);
            var work = monitor.WorkArea;
            prompt.ShowAt(new PixelRect(work.X + (work.Width - w) / 2, work.Y + (int)(work.Height * 0.38) - h / 2, w, h));
        }
        else prompt.Show();
        FrontWindow.Take(prompt.Handle);
        prompt.Activate();
        prompt._fields[0].Box.Focus();
        return prompt._done.Task;
    }

    private Dictionary<string, string> Values() => _fields.ToDictionary(f => f.Name, f => f.Box.Text);

    private void Finish(Dictionary<string, string>? values)
    {
        if (!_done.TrySetResult(values)) return;
        if (_open == this) _open = null;
        Close();
    }

    private static Button Pill(string title, bool prominent, Palette palette, Action click)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = prominent ? palette.Canvas : palette.Ink },
            Height = 28, Padding = new Thickness(14, 0, 14, 0), Background = prominent ? palette.Ink : palette.Raised, BorderThickness = new Thickness(0),
            Template = Controls.RoundTemplate(14), Cursor = Cursors.Hand,
        };
        button.Click += (_, _) => click();
        return button;
    }
}
