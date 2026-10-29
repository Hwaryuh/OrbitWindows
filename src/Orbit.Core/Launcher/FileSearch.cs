using System.Data.OleDb;

namespace Orbit.Core.Launcher;

/// <summary>
/// Files by name under the user's folder, newest first, from the Windows Search index (Orbit:
/// SmartAnswers.swift, <c>FileSearch</c> over Spotlight). A query waits <see cref="Settle"/> for
/// typing to pause and runs one at a time on the thread pool; results for anything but the latest
/// query are thrown away. Without the Windows Search service there are simply no file results.
/// </summary>
public sealed class FileSearch
{
    private const string Connection = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows';";
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly string _scope = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _term = "";
    private IReadOnlyList<string> _results = [];
    private CancellationTokenSource? _cancel;
    private DateTime _unavailableUntil = DateTime.MinValue;

    /// <summary>Raised on a background thread when results for the latest query arrived.</summary>
    public event Action? Changed;

    /// <summary>Paths found for <paramref name="query"/> so far; a new query starts a search and has none yet.</summary>
    public IReadOnlyList<string> Results(string query)
    {
        var term = query.Trim();
        lock (_gate)
        {
            if (term == _term) return _results;
            _results = [];
            _cancel?.Cancel();
            _cancel = null;
            // Not remembered while Windows Search is out, so the same term searches once it may be back.
            _term = DateTime.UtcNow < _unavailableUntil ? "" : term;
            if (term.Length < 2 || _term.Length == 0) return [];
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            _ = Task.Run(() => Search(term, token));
            return [];
        }
    }

    private async Task Search(string term, CancellationToken token)
    {
        try
        {
            await Task.Delay(Settle, token).ConfigureAwait(false);
            await _one.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        List<string> found;
        try
        {
            if (token.IsCancellationRequested) return;
            found = Query(term);
        }
        catch (Exception e) when (e is OleDbException or InvalidOperationException)
        {
            lock (_gate)
            {
                _unavailableUntil = DateTime.UtcNow + RetryAfter;
                if (_term == term) _term = "";
            }
            Log.Warn($"Windows Search is not available: {e.GetType().Name} 0x{e.HResult:X8}");
            return;
        }
        finally
        {
            _one.Release();
        }
        lock (_gate)
        {
            if (token.IsCancellationRequested || term != _term) return;
            _results = found;
        }
        Changed?.Invoke();
    }

    private List<string> Query(string term)
    {
        using var connection = new OleDbConnection(Connection);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = Sql(term, _scope);
        using var reader = command.ExecuteReader();
        var paths = new List<string>();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0) && reader.GetString(0) is { } path && !Hidden(path)) paths.Add(path);
        }
        return paths;
    }

    /// <summary>
    /// The newest 40 items under <paramref name="scope"/> whose name contains <paramref name="term"/>.
    /// Quotes are doubled and LIKE's wildcards bracketed, so the term is matched as typed.
    /// </summary>
    internal static string Sql(string term, string scope)
    {
        var pattern = term.Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
        return "SELECT TOP 40 System.ItemPathDisplay FROM SystemIndex " +
               $"WHERE SCOPE='file:{scope.Replace("'", "''")}' AND System.FileName LIKE '%{pattern}%' " +
               "ORDER BY System.DateModified DESC";
    }

    /// <summary>App data and dot folders are noise in a launcher, as Orbit leaves out ~/Library and dot folders.</summary>
    internal static bool Hidden(string path) =>
        path.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\.", StringComparison.Ordinal);
}
