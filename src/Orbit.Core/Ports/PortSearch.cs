namespace Orbit.Core.Ports;

/// <summary>
/// Listening ports for the launcher (Orbit: Modules/Ports/PortLauncher.swift): "3000", ":3000", "localhost:3000",
/// "ports" or "포트", or a dev server's program. Searches the last reading and reads again when it is two seconds
/// old; <see cref="Changed"/> says a new one is in. TCP only: what a browser can open.
/// </summary>
public sealed class PortSearch
{
    private static readonly TimeSpan Stale = TimeSpan.FromSeconds(2);
    private const int Most = 8;

    /// <summary>
    /// Programs that serve while developing. Orbit tells them by their project folder; here the program is all the
    /// table has, and a name like "svchost" must not answer a query of "svc".
    /// </summary>
    private static readonly HashSet<string> DevServers = new(StringComparer.OrdinalIgnoreCase)
    {
        "node", "bun", "deno", "python", "python3", "pythonw", "uvicorn", "gunicorn", "java", "javaw", "dotnet",
        "ruby", "php", "php-cgi", "go", "air", "hugo", "caddy", "nginx", "httpd",
    };

    private readonly Lock _lock = new();
    private IReadOnlyList<PortEntry> _entries = [];
    private DateTime _readAt = DateTime.MinValue;
    private bool _reading;

    /// <summary>A new reading is in. Raised on a background thread.</summary>
    public event Action? Changed;

    /// <summary>What answers <paramref name="query"/> now, and a fresh reading started if this one is old.</summary>
    public IReadOnlyList<PortEntry> Results(string query)
    {
        if (!Asks(query)) return [];
        IReadOnlyList<PortEntry> entries;
        lock (_lock)
        {
            entries = _entries;
            if (!_reading && DateTime.UtcNow - _readAt > Stale)
            {
                _reading = true;
                Task.Run(Read);
            }
        }
        return Match(query, entries);
    }

    private void Read()
    {
        try
        {
            var snapshot = PortTable.Snapshot();
            lock (_lock)
            {
                _entries = snapshot.Entries;
                _readAt = DateTime.UtcNow;
            }
            Changed?.Invoke();
        }
        catch (Exception e) when (e is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Warn($"The launcher could not read the ports ({e.GetType().Name})");
        }
        finally
        {
            lock (_lock) _reading = false;
        }
    }

    /// <summary>Whether a query could be about ports at all, before any reading.</summary>
    public static bool Asks(string query)
    {
        var q = query.Trim().ToLowerInvariant();
        return Number(q) is not null || AllPorts(q) || q.Length >= 3;
    }

    /// <summary>The ports answering <paramref name="query"/>, at most eight, one row per program and port.</summary>
    public static IReadOnlyList<PortEntry> Match(string query, IEnumerable<PortEntry> entries)
    {
        var q = query.Trim().ToLowerInvariant();
        var tcp = entries.Where(e => e.Protocol == PortProtocol.Tcp).DistinctBy(e => (e.Port, e.Pid)).OrderBy(e => e.Port).ToList();
        IEnumerable<PortEntry> found;
        if (Number(q) is { } digits)
            found = tcp.Where(e => e.Port.ToString().StartsWith(digits, StringComparison.Ordinal));
        else if (AllPorts(q))
            // Dev servers first, then programs with a window; Windows' own services aren't what anyone looks for.
            found = tcp.Where(e => DevServers.Contains(e.Name)).Concat(tcp.Where(e => !DevServers.Contains(e.Name) && e.HasWindow));
        else if (q.Length >= 3)
            // A dev server by its program ("node"); other programs already show up as apps.
            found = tcp.Where(e => DevServers.Contains(e.Name) && e.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        else
            found = [];
        return found.Take(Most).ToList();
    }

    /// <summary>"3000", ":3000", "localhost:3000", "127.0.0.1:3000": the digits, two or more.</summary>
    private static string? Number(string q)
    {
        var digits = q.Replace("localhost", "").Replace("127.0.0.1", "").Trim(':', ' ');
        return digits.Length >= 2 && digits.All(char.IsAsciiDigit) ? digits : null;
    }

    private static bool AllPorts(string q) => q == "포트" || (q.Length >= 3 && new[] { "ports", "port", "localhost", "netstat" }.Any(w => w.StartsWith(q, StringComparison.Ordinal)));
}
