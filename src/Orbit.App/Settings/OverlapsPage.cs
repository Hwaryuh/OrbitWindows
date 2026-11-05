using System.Windows;
using System.Windows.Controls;
using Orbit.App.Design;
using Orbit.App.Features;
using Orbit.Core.Actions;
using Orbit.Core.Input;
using Orbit.Core.Overlaps;

namespace Orbit.App.Settings;

/// <summary>
/// Other programs sharing keys or jobs with Orbit (Orbit's Overlaps, small). Each clash says which
/// program, which keys and which Orbit action, and offers other keys. It looks when the page opens
/// and on the refresh button; only the newest look counts, and none after the page is gone.
/// </summary>
internal sealed class OverlapsPage : StackPanel
{
    private readonly AppServices _services;
    private readonly Palette _palette = Palette.Light;
    private readonly PageParts _parts = new(Palette.Light);
    private OverlapInput? _input;
    private int _look;

    public OverlapsPage(AppServices services)
    {
        _services = services;
        Unloaded += (_, _) => _look++;
        Loaded += (_, _) => _ = LookAsync();
        Rebuild();
    }

    private async Task LookAsync()
    {
        int look = ++_look;
        var input = await OverlapDetector.DetectAsync();
        if (look != _look) return;
        _input = input;
        Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        var header = _parts.PageTitles(Strings.PageOverlaps, Strings.OverlapsSubtitle, bottom: 20);
        var again = _parts.PillButton(Strings.OverlapsRefresh, prominent: false, () => _ = LookAsync());
        again.HorizontalAlignment = HorizontalAlignment.Left;
        again.Margin = new Thickness(0, 12, 0, 0);
        header.Children.Add(again);
        Children.Add(header);

        if (_input is null)
        {
            Children.Add(_parts.Caption(Strings.OverlapsLooking));
            return;
        }
        var shortcuts = _services.Shortcuts.Keys().Where(k => _services.Shortcuts.For(k) is not null).ToDictionary(k => k, k => _services.Shortcuts.For(k)!.Value);
        var found = OverlapScan.Find(_input, shortcuts);
        var clashes = found.Where(o => o.Kind == OverlapKind.KeyClash).ToList();
        var jobs = found.Where(o => o.Kind == OverlapKind.SameJob).ToList();
        if (found.Count == 0)
        {
            Children.Add(_parts.Caption(Strings.OverlapsNone));
            return;
        }
        if (clashes.Count > 0)
            Children.Add(_parts.Group(Strings.OverlapsKeys, Strings.OverlapsKeysFooter, clashes.Select(c => ClashRow(c, shortcuts.Values)).ToArray<UIElement>()));
        if (jobs.Count > 0)
            Children.Add(_parts.Group(Strings.OverlapsJobs, null, jobs.Select(JobRow).ToArray<UIElement>()));
    }

    private FrameworkElement ClashRow(Overlap clash, IEnumerable<Hotkey> taken)
    {
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(KeyCap.Row(clash.Keys!.Value.Labels(), _palette));
        if (OverlapScan.Suggest(clash.SlotKey!, taken) is { } other)
        {
            var move = _parts.PillButton(Strings.OverlapsMoveTo(string.Join("+", other.Labels())), prominent: false, () =>
            {
                _services.Shortcuts.Set(clash.SlotKey!, other);
                Rebuild();
            });
            move.Margin = new Thickness(10, 0, 0, 0);
            controls.Children.Add(move);
        }
        return _parts.Row(ShortcutsPage.PlaceTitle(clash.SlotKey!), Strings.OverlapsAlsoUsedBy(clash.Program), controls);
    }

    private FrameworkElement JobRow(Overlap job) =>
        _parts.Row(job.Program, Strings.OverlapsSameJob(job.Job!.Value.Title()), new StackPanel());
}
