using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Display;
using Orbit.Core.Input;
using Orbit.Core.Settings;
using Orbit.Core.Translate;

namespace Orbit.App.Features.Translate;

/// <summary>
/// The translation card (Orbit: TranslatePanel.swift, <c>TranslateCard</c>): the detected source
/// language, the target (any language, from a menu), swap, the engine, the original and the
/// result. Enter copies, Ctrl+Enter replaces the selection it came from, Ctrl+Shift+S swaps, Esc
/// closes. A new translation reuses the open card where it is. Drag the header to move it.
/// </summary>
internal sealed class TranslateCard : OverlayWindow
{
    // Shadow: the transparent margin the drop shadow draws in. Its blur (30) and offset (8) need
    // about 23 px below the card; a smaller margin cuts the shadow off in a hard line.
    private const double CardWidth = 440, Shadow = 24;

    private readonly TranslateFeature _feature;
    private readonly AppServices _services;
    private readonly Preferences _preferences;
    private readonly Palette _palette;
    private readonly TextBlock _sourceName, _targetName, _engineName, _source, _count;
    private readonly ContentControl _result = new();
    private readonly Button _copy, _replace;
    private readonly StackPanel _root = new();
    private CancellationTokenSource? _job;
    private int _request;
    private string _text = "";
    private string _target = "ko";
    private TranslationEngine _engine;
    private TranslationResult? _output;
    private nint _replaceInto;

    public TranslateCard(TranslateFeature feature, AppServices services) : base(activating: true, transparent: true, clickThrough: false)
    {
        _feature = feature;
        _services = services;
        _preferences = services.Preferences;
        _palette = Palette.Current();
        _sourceName = Label(12, FontWeights.SemiBold, _palette.InkSecondary);
        _targetName = Label(12, FontWeights.SemiBold, _palette.Ink);
        _engineName = Label(11, FontWeights.SemiBold, _palette.InkSecondary);
        _source = new TextBlock { FontSize = 12, Foreground = _palette.InkSecondary, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 51, Margin = new Thickness(18, 0, 18, 10) };
        _count = Label(11, FontWeights.Normal, _palette.InkSecondary);
        _copy = Pill(Strings.ActionCopy, "Enter", prominent: true, CopyResult);
        _replace = Pill(Strings.TranslateReplace, "Ctrl Enter", prominent: false, ReplaceSelection);
        _replace.Margin = new Thickness(0, 0, 8, 0);

        _root.Children.Add(Header());
        _root.Children.Add(_source);
        _root.Children.Add(new Border { Height = 1, Background = _palette.Hairline, Margin = new Thickness(18, 0, 18, 0) });
        _result.Margin = new Thickness(18, 14, 18, 14);
        _root.Children.Add(_result);
        _root.Children.Add(Footer());
        Content = new Border
        {
            Margin = new Thickness(Shadow), Width = CardWidth, Background = _palette.Canvas, BorderBrush = _palette.Hairline, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18), Child = _root, VerticalAlignment = VerticalAlignment.Top,
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.22 },
        };
        PreviewKeyDown += KeyPressed;
        Deactivated += (_, _) =>
        {
            if (_preferences.Get<bool>(Pref.TranslateCloseOnBlur)) Dismiss();
        };
        Closed += (_, _) =>
        {
            _job?.Cancel();
            _request++;
        };
    }

    /// <summary>Translates <paramref name="text"/>; <paramref name="replaceInto"/> is the window Replace pastes into.</summary>
    public void Translate(string text, nint replaceInto)
    {
        _text = text.Length > Translator.MaxLength ? text[..Translator.MaxLength] : text;
        _replaceInto = replaceInto;
        _engine = Enum.TryParse<TranslationEngine>(_preferences.Get<string>(Pref.TranslateEngine), ignoreCase: true, out var engine) ? engine : TranslationEngine.Google;
        _target = Languages.Direction(_text, Primary, Secondary);
        _source.Text = _text;
        _source.Visibility = _preferences.Get<bool>(Pref.TranslateShowSource) ? Visibility.Visible : Visibility.Collapsed;
        bool fresh = !IsVisible;
        Run();
        Present(fresh);
    }

    private string Primary => _preferences.Get<string>(Pref.TranslatePrimary);
    private string Secondary => _preferences.Get<string>(Pref.TranslateSecondary);
    private double FontSizePref => _preferences.Get<double>(Pref.TranslateFontSize);

    // MARK: Translating

    private async void Run()
    {
        _job?.Cancel();
        var job = _job = new CancellationTokenSource();
        int request = ++_request;
        _output = null;
        ShowWorking();
        var text = _text;
        var target = _target;
        // Named up front when the script gives it away: Google calls short Chinese text English.
        var script = Languages.Script(text) is { } s && !Languages.Same(s, target) ? s : null;
        var formality = Enum.TryParse<Formality>(_preferences.Get<string>(Pref.TranslateFormality), ignoreCase: true, out var f) ? f : Formality.Auto;
        var key = Secrets.Unprotect(_preferences.Get<string>(Pref.TranslateDeepLKey));
        try
        {
            var result = await _feature.Translator.TranslateAsync(text, target, script, _engine, formality, key, job.Token);
            // A newer translation (or a closed card) replaced this one meanwhile.
            if (request != _request) return;
            _output = script is null ? result : result with { DetectedSource = script };
            ShowResult(_output);
        }
        catch (TranslationException e)
        {
            if (request != _request) return;
            ShowFailure(e.Failure);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ShowWorking()
    {
        _sourceName.Text = Languages.Guess(_text) is { } guess ? Languages.Named(guess) : Strings.TranslateAuto;
        _targetName.Text = Languages.Named(_target);
        _engineName.Text = _engine == TranslationEngine.DeepL ? "DeepL" : "Google";
        var bars = new StackPanel();
        int lines = Math.Min(3, Math.Max(1, _text.Length / 40 + 1));
        for (int i = 0; i < lines; i++)
            bars.Children.Add(new Border
            {
                Height = FontSizePref * 0.9, CornerRadius = new CornerRadius(4), Background = _palette.Raised, Margin = new Thickness(0, i == 0 ? 0 : 8, i == 2 ? 200 : 0, 0),
            });
        bars.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 1, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        _result.Content = bars;
        SetButtons(false);
        _count.Text = "";
        Fit();
    }

    private void ShowResult(TranslationResult result)
    {
        if (result.DetectedSource is { } detected) _sourceName.Text = Languages.Named(detected);
        var box = new TextBox
        {
            Text = result.Text, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = _palette.Ink,
            FontSize = FontSizePref, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0), Margin = new Thickness(-2, 0, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CaretBrush = _palette.Ink,
        };
        _result.Content = box;
        _count.Text = Strings.TranslateCharacters(result.Text.Length);
        SetButtons(true);
        Fit();
    }

    private void ShowFailure(TranslationFailure failure)
    {
        var panel = new DockPanel();
        var icon = new TextBlock { Text = "", FontFamily = Glyphs.Font, FontSize = 14, Foreground = _palette.Accent, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(icon, Dock.Left);
        panel.Children.Add(icon);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = Strings.TranslateFailure(failure), FontSize = 13, Foreground = _palette.Ink, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Pill(Strings.TranslateTryAgain, null, prominent: false, Run));
        if (_engine != TranslationEngine.Google)
        {
            var google = Pill(Strings.TranslateUseGoogle, null, prominent: false, () => Use(TranslationEngine.Google));
            google.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(google);
        }
        body.Children.Add(buttons);
        panel.Children.Add(body);
        _result.Content = panel;
        SetButtons(false);
        Fit();
    }

    private void SetButtons(bool on)
    {
        _copy.IsEnabled = _replace.IsEnabled = on;
        _copy.Opacity = _replace.Opacity = on ? 1 : 0.4;
    }

    private void Swap()
    {
        _target = Languages.Same(_target, Primary) ? Secondary : Primary;
        if (_output is { } done) _text = done.Text;
        _source.Text = _text;
        Run();
    }

    private void TranslateInto(string code)
    {
        if (code == _target) return;
        _target = code;
        Run();
    }

    private void Use(TranslationEngine engine)
    {
        _engine = engine;
        _preferences.Set(Pref.TranslateEngine, engine == TranslationEngine.DeepL ? "deepl" : "google");
        Run();
    }

    private void CopyResult()
    {
        if (_output is not { } result) return;
        var data = new DataObject();
        data.SetText(result.Text);
        bool copied = Clipboards.Set(data);
        Dismiss();
        Toast.Show(copied ? Strings.TranslateCopied : Strings.CopyFailed);
    }

    /// <summary>Pastes over the selection the text was copied from (it is still selected in that window).</summary>
    private async void ReplaceSelection()
    {
        if (_output is not { } result) return;
        var data = new DataObject();
        data.SetText(result.Text);
        if (!Clipboards.Set(data, out uint written))
        {
            Toast.Show(Strings.CopyFailed);
            return;
        }
        Dismiss();
        var outcome = await Paster.PasteAsync(_replaceInto, () => Clipboards.Sequence() == written);
        if (outcome != PasteResult.Pasted)
        {
            Log.Info($"Translate replace did not happen: {outcome}");
            Toast.Show(outcome == PasteResult.ClipboardChanged ? Strings.ClipChanged : Strings.TranslateCopied);
        }
    }

    public void Dismiss()
    {
        _job?.Cancel();
        _request++;
        if (IsVisible) Hide();
    }

    // MARK: Layout

    private FrameworkElement Header()
    {
        var bar = new DockPanel { Margin = new Thickness(14, 14, 14, 10), Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = Strings.TranslateDrag };
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not Button) DragMove();
        };
        var close = Round(Glyphs.Close, Strings.TranslateClose, Dismiss);
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);
        var engine = new Button
        {
            Content = _engineName, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Template = Controls.RoundTemplate(0),
            Cursor = Cursors.Hand, Focusable = false, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        engine.Click += (_, _) => OpenMenu(engine, [("Google", () => Use(TranslationEngine.Google)), ("DeepL", () => Use(TranslationEngine.DeepL))]);
        DockPanel.SetDock(engine, Dock.Right);
        bar.Children.Add(engine);

        var pill = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _sourceName.Margin = new Thickness(9, 0, 0, 0);
        pill.Children.Add(_sourceName);
        var swap = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = Glyphs.Font, FontSize = 9, Foreground = _palette.InkSecondary },
            Width = 22, Height = 22, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Template = Controls.RoundTemplate(11),
            Cursor = Cursors.Hand, Focusable = false, ToolTip = Strings.TranslateSwap,
        };
        swap.Click += (_, _) => Swap();
        pill.Children.Add(swap);
        var target = new Button
        {
            Content = _targetName, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Template = Controls.RoundTemplate(0),
            Cursor = Cursors.Hand, Focusable = false, ToolTip = Strings.TranslateInto, Margin = new Thickness(0, 0, 9, 0),
        };
        target.Click += (_, _) => OpenMenu(target, Languages.All.Select(l => (l.Name, (Action)(() => TranslateInto(l.Code)))).ToList());
        pill.Children.Add(target);
        bar.Children.Add(new Border { Background = _palette.Raised, CornerRadius = new CornerRadius(12), Height = 24, Child = pill, HorizontalAlignment = HorizontalAlignment.Left });
        return bar;
    }

    private FrameworkElement Footer()
    {
        var bar = new DockPanel { Height = 44, LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(_replace);
        buttons.Children.Add(_copy);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        _count.Margin = new Thickness(14, 0, 0, 0);
        _count.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(_count);
        // The tint is the border's own rounded background: ClipToBounds clips to a rectangle and
        // would square off the card's bottom corners.
        return new Border { Child = bar, Background = Palette.Fade(_palette.Surface, 0.6), CornerRadius = new CornerRadius(0, 0, 17, 17) };
    }

    private void OpenMenu(FrameworkElement under, IReadOnlyList<(string Title, Action Run)> items)
    {
        var menu = new ContextMenu { PlacementTarget = under, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (title, run) in items) menu.Items.Add(Controls.MenuItem(title, run));
        menu.IsOpen = true;
    }

    private static TextBlock Label(double size, FontWeight weight, Brush brush) =>
        new() { FontSize = size, FontWeight = weight, Foreground = brush, VerticalAlignment = VerticalAlignment.Center };

    private Button Round(string glyph, string help, Action click)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = Glyphs.Font, FontSize = 9, Foreground = _palette.InkSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Width = 22, Height = 22, Background = _palette.Raised, BorderThickness = new Thickness(0), Template = Controls.RoundTemplate(11),
            Cursor = Cursors.Hand, Focusable = false, ToolTip = help, VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => click();
        return button;
    }

    private Button Pill(string title, string? keys, bool prominent, Action click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = prominent ? _palette.Canvas : _palette.Ink });
        if (keys is not null)
            content.Children.Add(new TextBlock { Text = keys, FontSize = 11, Foreground = prominent ? _palette.Canvas : _palette.Ink, Opacity = 0.6, Margin = new Thickness(6, 0, 0, 0) });
        var button = new Button
        {
            Content = content, Height = 28, Padding = new Thickness(12, 0, 12, 0), Background = prominent ? _palette.Ink : _palette.Raised,
            BorderThickness = new Thickness(0), Template = Controls.RoundTemplate(14), Cursor = Cursors.Hand, Focusable = false,
        };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Opens by the pointer (below it, or above when there's no room) or, when open, keeps its top where it is.</summary>
    private void Present(bool fresh)
    {
        if (fresh)
        {
            var (x, y) = Monitors.Cursor();
            _monitor = Monitors.FromPoint(x, y);
            if (_monitor is null) return;
            double s = _monitor.Scale;
            _left = x - (int)Math.Round((40 + Shadow) * s);
            _top = y + (int)Math.Round((18 - Shadow) * s);
            Fit();
            Show();
            Fit();
        }
        FrontWindow.Take(Handle);
        Activate();
        Focus();
    }

    private MonitorInfo? _monitor;
    private int _left, _top;

    /// <summary>Sizes the window to the card and keeps it on the work area; a tall result scrolls.</summary>
    private void Fit()
    {
        if (IsVisible)
        {
            // Moved to another monitor meanwhile: its work area and scale now.
            var at = CurrentRect();
            _monitor = Monitors.FromPoint(at.X + at.Width / 2, at.Y + at.Height / 2) ?? _monitor;
        }
        if (_monitor is not { } monitor) return;
        var work = monitor.WorkArea;
        double s = monitor.Scale;
        var card = (FrameworkElement)Content;
        var space = new Size(CardWidth + Shadow * 2, double.PositiveInfinity);
        if (_result.Content is TextBox box)
        {
            // The result gets what the work area leaves after the rest of the card (header, source,
            // footer, shadow), measured rather than guessed, so a long result scrolls inside the
            // card instead of pushing the footer off the screen.
            box.MaxHeight = double.PositiveInfinity;
            card.Measure(space);
            double rest = card.DesiredSize.Height - box.DesiredSize.Height;
            // At least two lines show even on a very short work area.
            box.MaxHeight = Math.Max(FontSizePref * 3, work.Height / s - rest);
        }
        card.Measure(space);
        int w = (int)Math.Ceiling((CardWidth + Shadow * 2) * s), h = (int)Math.Ceiling(card.DesiredSize.Height * s);
        h = Math.Min(h, work.Height);
        var (_, py) = Monitors.Cursor();
        if (!IsVisible && _top + h > work.Bottom && py - h - (int)(18 * s) >= work.Y)
        {
            _top = py - h - (int)Math.Round((18 - Shadow) * s);
        }
        int left = Math.Clamp(_left, work.X, Math.Max(work.X, work.Right - w));
        int top = Math.Clamp(_top, work.Y, Math.Max(work.Y, work.Bottom - h));
        if (IsVisible)
        {
            // Keep where the user put it: read it back, then grow downward.
            var placed = CurrentRect();
            left = Math.Clamp(placed.X, work.X, Math.Max(work.X, work.Right - w));
            top = Math.Clamp(placed.Y, work.Y, Math.Max(work.Y, work.Bottom - h));
        }
        Place(new PixelRect(left, top, w, h));
    }

    private PixelRect CurrentRect()
    {
        Windows.Win32.PInvoke.GetWindowRect(Handle, out var r);
        return new PixelRect(r.left, r.top, r.right - r.left, r.bottom - r.top);
    }

    protected override bool HoldsPlacement => false;

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        switch (e.Key)
        {
            case Key.Escape:
                Dismiss();
                break;
            case Key.Enter when mods == ModifierKeys.Control:
                ReplaceSelection();
                break;
            case Key.Enter when mods == ModifierKeys.None:
                CopyResult();
                break;
            case Key.S when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                Swap();
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
