using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Orbit.App.Design;
using Orbit.App.Features;
using Orbit.Core.Actions;
using Orbit.Core.SystemInfo;

namespace Orbit.App.Settings;

/// <summary>
/// The System page (Orbit: the System page over SystemInfo and ProcessMonitor): processor, memory,
/// GPU, disk, network, battery and uptime, the busiest programs, and quick actions. Sampled every
/// 2 seconds while it shows. Temperatures and fans are not in the Windows version.
/// </summary>
internal sealed class SystemPage : StackPanel
{
    private static readonly OrbitAction[] QuickActions =
        [OrbitAction.LockScreen, OrbitAction.SleepDisplay, OrbitAction.DarkMode, OrbitAction.DesktopIcons, OrbitAction.HiddenFiles, OrbitAction.EmptyTrash, OrbitAction.EjectAll];

    private readonly AppServices _services;
    private readonly SystemSampler _sampler = new();
    private readonly Palette _palette = Palette.Light;
    private readonly PageParts _parts = new(Palette.Light);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, (TextBlock Value, TextBlock Detail, Polyline? Line)> _tiles = [];
    private readonly StackPanel _processes = new();

    public SystemPage(AppServices services)
    {
        _services = services;
        var header = _parts.PageTitles(Strings.PageSystem, Strings.SysSubtitle, bottom: 20);
        Children.Add(header);

        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 28) };
        foreach (var (key, title, line) in new[] { ("cpu", "CPU", true), ("memory", Strings.SysMemory, true), ("gpu", "GPU", false), ("disk", Strings.SysDisk, false), ("network", Strings.SysNetwork, true), ("battery", Strings.SysBattery, false) })
            grid.Children.Add(Tile(key, title, line, grid.Children.Count));
        Children.Add(grid);

        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 28) };
        section.Children.Add(_parts.SectionTitle(Strings.SysTopPrograms, bottom: 10));
        section.Children.Add(_parts.Card(_processes));
        Children.Add(section);

        var actions = new WrapPanel();
        foreach (var action in QuickActions)
        {
            var body = new StackPanel { Orientation = Orientation.Horizontal };
            body.Children.Add(new TextBlock { Text = Glyphs.Of(action), FontFamily = Glyphs.Font, FontSize = 13, Foreground = _palette.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            body.Children.Add(new TextBlock { Text = action.Title(), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button
            {
                Content = body, Height = 34, Padding = new Thickness(12, 0, 14, 0), Margin = new Thickness(0, 0, 8, 8), Background = _palette.Raised,
                BorderThickness = new Thickness(0), Template = Design.Controls.RoundTemplate(17), Cursor = Cursors.Hand,
            };
            var run = action;
            button.Click += (_, _) => _services.Actions.Perform(run);
            actions.Children.Add(button);
        }
        var actionSection = new StackPanel();
        actionSection.Children.Add(_parts.SectionTitle(Strings.SysActions, bottom: 10));
        actionSection.Children.Add(actions);
        Children.Add(actionSection);

        _timer.Tick += (_, _) => Sample();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _sampler.Dispose();
        };
        Sample();
    }

    private Border Tile(string key, string title, bool line, int index)
    {
        var body = new Grid { Height = 92 };
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = _palette.InkSecondary });
        var value = new TextBlock { FontSize = 20, FontWeight = FontWeights.Bold, Foreground = _palette.Ink, Margin = new Thickness(0, 4, 0, 0) };
        var detail = new TextBlock { FontSize = 11, Foreground = _palette.InkSecondary, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Children.Add(value);
        text.Children.Add(detail);
        body.Children.Add(text);
        Polyline? polyline = null;
        if (line)
        {
            polyline = new Polyline { Stroke = _palette.Accent, StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.8 };
            body.Children.Add(polyline);
        }
        _tiles[key] = (value, detail, polyline);
        var card = _parts.Card(body, 14);
        card.Margin = new Thickness(index % 3 == 0 ? 0 : 6, 0, index % 3 == 2 ? 0 : 6, 12);
        return card;
    }

    private void Sample()
    {
        var r = _sampler.Sample();
        Set("cpu", $"{r.Cpu:0}%", Strings.SysThreads(Environment.ProcessorCount), _sampler.CpuHistory, 100);
        Set("memory", r.MemoryTotal == 0 ? "—" : $"{100.0 * r.MemoryUsed / r.MemoryTotal:0}%", $"{Bytes(r.MemoryUsed)} / {Bytes(r.MemoryTotal)}", _sampler.MemoryHistory, 100);
        Set("gpu", r.Gpu is { } gpu ? $"{gpu:0}%" : "—", r.Gpu is null ? Strings.SysNotAvailable : "", null, 0);
        Set("disk", r.DiskTotal == 0 ? "—" : $"{100.0 * (r.DiskTotal - r.DiskFree) / r.DiskTotal:0}%", Strings.SysDiskFree(Bytes(r.DiskFree)), null, 0);
        Set("network", $"↓ {Bytes(r.Download)}/s", $"↑ {Bytes(r.Upload)}/s", _sampler.DownloadHistory, Math.Max(1, _sampler.DownloadHistory.Max()));
        Set("battery", r.Battery.Percent is { } percent ? $"{percent}%" : "—",
            r.Battery.Percent is null ? Strings.SysNoBattery : (r.Battery.OnAc ? Strings.SysCharging : $"{Strings.SysUptime} {Strings.SysUptimeValue(r.Uptime)}"), null, 0);
        if (r.Battery.Percent is null) _tiles["battery"].Value.Text = Strings.SysUptimeValue(r.Uptime);

        _processes.Children.Clear();
        foreach (var p in _sampler.TopProcesses(8))
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var numbers = new TextBlock { Text = $"{p.Cpu,5:0.0}%   {Bytes((ulong)p.Memory),9}", FontSize = 12, FontFamily = Glyphs.Mono, Foreground = _palette.InkSecondary };
            DockPanel.SetDock(numbers, Dock.Right);
            row.Children.Add(numbers);
            row.Children.Add(new TextBlock { Text = p.Name, FontSize = 13, Foreground = _palette.Ink, TextTrimming = TextTrimming.CharacterEllipsis });
            _processes.Children.Add(row);
        }
    }

    private void Set(string key, string value, string detail, IEnumerable<double>? history, double max)
    {
        var (valueBlock, detailBlock, line) = _tiles[key];
        valueBlock.Text = value;
        detailBlock.Text = detail;
        if (line is null || history is null) return;
        var points = new PointCollection();
        var values = history.ToList();
        double width = 180, height = 30;
        for (int i = 0; i < values.Count; i++)
            points.Add(new Point(width * i / Math.Max(1, SystemSampler.HistoryLength - 1), height - height * Math.Clamp(values[i] / max, 0, 1)));
        line.Points = points;
    }

    /// <summary>"512 MB", "1.4 GB", as File Explorer writes sizes.</summary>
    public static string Bytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes:0} {units[unit]}" : $"{bytes:0.#} {units[unit]}";
    }
}
