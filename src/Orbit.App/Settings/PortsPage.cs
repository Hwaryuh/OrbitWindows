using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Ports;

namespace Orbit.App.Settings;

/// <summary>
/// The Ports page (behavior after Vorssaint's Port Manager; Orbit has none): the ports programs
/// listen on, found by port, program or PID, and a careful way to close the program holding one.
/// Read when the page opens and on Refresh; nothing runs while it is closed.
/// </summary>
internal sealed class PortsPage : StackPanel
{
    private readonly Palette _palette = Palette.Light;
    private readonly PageParts _parts = new(Palette.Light);
    private readonly TextBox _search;
    private readonly TextBlock _status;
    private readonly TextBlock _problem;
    private readonly StackPanel _rows = new();
    private readonly HashSet<int> _closing = [];
    private PortSnapshot? _snapshot;
    private bool _udp;
    private int _generation;

    public PortsPage()
    {
        var header = _parts.PageTitles(Strings.PagePorts, Strings.PortsSubtitle, bottom: 20);
        Children.Add(header);

        _search = new TextBox { FontSize = 13, Padding = new Thickness(8, 5, 8, 5), VerticalContentAlignment = VerticalAlignment.Center };
        var hint = new TextBlock
        {
            Text = Strings.PortsSearch, FontSize = 13, Foreground = _palette.InkSecondary, IsHitTestVisible = false,
            Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        _search.TextChanged += (_, _) =>
        {
            hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Show();
        };
        var searchBox = new Grid();
        searchBox.Children.Add(_search);
        searchBox.Children.Add(hint);

        var refresh = _parts.PillButton(Strings.PortsRefresh, false, () => _ = Load());
        refresh.Margin = new Thickness(10, 0, 0, 0);
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(refresh, Dock.Right);
        bar.Children.Add(refresh);
        bar.Children.Add(searchBox);
        Children.Add(bar);

        var udp = new CheckBox { Content = Strings.PortsShowUdp, FontSize = 12, Foreground = _palette.Ink, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        udp.Click += (_, _) =>
        {
            _udp = udp.IsChecked == true;
            Show();
        };
        _status = new TextBlock { FontSize = 11.5, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center };
        var line = new DockPanel { Margin = new Thickness(2, 0, 2, 10) };
        DockPanel.SetDock(udp, Dock.Right);
        line.Children.Add(udp);
        line.Children.Add(_status);
        Children.Add(line);

        _problem = new TextBlock { FontSize = 12, Foreground = _palette.Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 10), Visibility = Visibility.Collapsed };
        Children.Add(_problem);
        Children.Add(_parts.Card(_rows, 6));

        Loaded += (_, _) => _ = Load();
        // A result that arrives after the page closed has nowhere to go.
        Unloaded += (_, _) => _generation++;
    }

    private async Task Load()
    {
        int generation = ++_generation;
        if (_snapshot is null) _status.Text = Strings.PortsLoading;
        PortSnapshot snapshot;
        try
        {
            snapshot = await Task.Run(PortTable.Snapshot);
        }
        catch (Exception e)
        {
            Log.Error("Port listing failed", e);
            snapshot = new PortSnapshot([], false);
        }
        // Only the latest read shows: an older one finishing late is dropped.
        if (generation != _generation) return;
        _snapshot = snapshot;
        Show();
    }

    private void Show()
    {
        if (_snapshot is not { } snapshot) return;
        _problem.Text = Strings.PortsPartial;
        _problem.Visibility = snapshot.Complete ? Visibility.Collapsed : Visibility.Visible;
        _status.Text = Strings.PortsCount(snapshot.Entries.Count(e => e.Protocol == PortProtocol.Tcp), snapshot.Entries.Count(e => e.Protocol == PortProtocol.Udp));
        _rows.Children.Clear();
        var shown = PortTable.Filter(snapshot.Entries, _search.Text, _udp).ToList();
        if (shown.Count == 0)
        {
            var any = PortTable.Filter(snapshot.Entries, "", _udp).Any();
            _rows.Children.Add(new TextBlock
            {
                Text = any ? Strings.PortsNoMatch : Strings.PortsEmpty, FontSize = 12.5, Foreground = _palette.InkSecondary,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 18),
            });
            return;
        }
        for (int i = 0; i < shown.Count; i++)
        {
            if (i > 0) _rows.Children.Add(new Border { Height = 1, Background = _palette.Hairline, Margin = new Thickness(10, 0, 10, 0) });
            _rows.Children.Add(Row(shown[i]));
        }
    }

    private FrameworkElement Row(PortEntry entry)
    {
        var mono = new FontFamily("Cascadia Mono, Consolas");
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(new TextBlock { Text = entry.Port.ToString(), FontFamily = mono, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink, VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(Badge(entry.Protocol == PortProtocol.Tcp ? "TCP" : "UDP", null));
        if (entry.AllInterfaces) top.Children.Add(Badge(Strings.PortsAllInterfaces, Strings.PortsAllInterfacesHelp));
        top.Children.Add(new TextBlock
        {
            Text = entry.Name.Length == 0 ? Strings.PortsUnknown : entry.Name, FontSize = 13, Foreground = _palette.Ink,
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(top);
        text.Children.Add(new TextBlock { Text = $"PID {entry.Pid}  ·  {entry.Endpoint}", FontFamily = mono, FontSize = 11, Foreground = _palette.InkSecondary, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });

        var side = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        if (entry.Block != CloseBlock.None)
        {
            side.Children.Add(new TextBlock
            {
                Text = Reason(entry.Block), FontSize = 11, Foreground = _palette.InkSecondary, TextWrapping = TextWrapping.Wrap,
                MaxWidth = 220, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else
        {
            bool busy = _closing.Contains(entry.Pid);
            if (entry.HasWindow)
            {
                var close = _parts.PillButton(Strings.PortsClose, false, () => _ = End(entry, force: false));
                close.IsEnabled = !busy;
                side.Children.Add(close);
            }
            var force = _parts.PillButton(Strings.PortsForce, false, () => _ = End(entry, force: true));
            force.Margin = new Thickness(6, 0, 0, 0);
            force.IsEnabled = !busy;
            side.Children.Add(force);
        }

        var row = new DockPanel { Margin = new Thickness(10, 8, 10, 8), Background = Brushes.Transparent };
        DockPanel.SetDock(side, Dock.Right);
        row.Children.Add(side);
        row.Children.Add(text);
        row.ContextMenu = Menu(entry);
        return row;
    }

    private Border Badge(string text, string? tip) => new()
    {
        Background = _palette.Raised, CornerRadius = new CornerRadius(6), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(6, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center, ToolTip = tip,
        Child = new TextBlock { Text = text, FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = _palette.InkSecondary },
    };

    private static string Reason(CloseBlock block) => block switch
    {
        CloseBlock.System => Strings.PortsBlockSystem,
        CloseBlock.Self => Strings.PortsBlockSelf,
        CloseBlock.Denied => Strings.PortsBlockDenied,
        _ => Strings.PortsBlockUnverified,
    };

    private static ContextMenu Menu(PortEntry entry)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Design.Controls.MenuItem(Strings.PortsCopyPort, () => Clipboards.CopyText(entry.Port.ToString(), Strings.PortsCopied)));
        menu.Items.Add(Design.Controls.MenuItem(Strings.PortsCopyAddress, () => Clipboards.CopyText(entry.Endpoint, Strings.PortsCopied)));
        if (BrowserAddress(entry) is { } url)
            menu.Items.Add(Design.Controls.MenuItem(Strings.PortsOpenBrowser, () => Shell.Open(url)));
        return menu;
    }

    /// <summary>http://host:port for a TCP listener; a wildcard bind is reached through loopback of its family.</summary>
    private static string? BrowserAddress(PortEntry entry)
    {
        if (entry.Protocol != PortProtocol.Tcp || entry.Host.Contains('%')) return null;
        string host = entry.Host switch
        {
            "0.0.0.0" => "127.0.0.1",
            "::" => "[::1]",
            var h when h.Contains(':') => $"[{h}]",
            var h => h,
        };
        return $"http://{host}:{entry.Port}/";
    }

    private async Task End(PortEntry entry, bool force)
    {
        string name = entry.Name.Length == 0 ? Strings.PortsUnknown : entry.Name;
        var question = force ? Strings.PortsForceConfirm(name, entry.Port, entry.Pid) : Strings.PortsCloseConfirm(name, entry.Port, entry.Pid);
        var owner = Window.GetWindow(this);
        var answer = owner is null
            ? MessageBox.Show(question, Strings.AppName, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            : MessageBox.Show(owner, question, Strings.AppName, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK || !_closing.Add(entry.Pid)) return;
        Show();
        CloseResult result;
        try
        {
            result = await Task.Run(() => PortTable.Close(entry, force));
        }
        catch (Exception e)
        {
            Log.Error("Ending a port's program failed", e);
            result = CloseResult.Failed;
        }
        finally
        {
            _closing.Remove(entry.Pid);
        }
        Log.Info($"Port program end ({(force ? "force" : "close")}): {result}");
        Toast.Show(result switch
        {
            CloseResult.Closed => Strings.PortsClosed(name),
            CloseResult.StillRunning => Strings.PortsStillRunning,
            CloseResult.NoWindow => Strings.PortsNoWindow,
            CloseResult.Gone => Strings.PortsGone,
            CloseResult.Changed => Strings.PortsChanged,
            CloseResult.Denied => Strings.PortsDenied,
            _ => Strings.PortsFailed,
        });
        // The toast tells the result anywhere; the list only reloads while the page shows.
        if (IsLoaded) await Load();
    }
}
