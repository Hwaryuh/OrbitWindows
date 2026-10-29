using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orbit.Core.Launcher;

namespace Orbit.Core.Snippets;

/// <summary>
/// The user's snippets in %APPDATA%\Orbit\snippets.json (Orbit: SnippetStore.swift). A first run
/// gets a few examples. Not thread-safe: the UI thread owns it.
/// </summary>
public sealed class SnippetStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _file;
    private readonly List<Snippet> _snippets;

    /// <param name="starters">The examples a first run gets (their text is the app's, in the user's language).</param>
    public SnippetStore(string file, IEnumerable<Snippet> starters)
    {
        _file = file;
        var loaded = Load(file);
        _snippets = loaded ?? starters.ToList();
        if (loaded is null) Save();
    }

    public IReadOnlyList<Snippet> All => _snippets;

    /// <summary>Snippets that expand as they're typed.</summary>
    public IReadOnlyList<Snippet> Active => _snippets.Where(s => s.Enabled && s.Keyword.Length > 0).ToList();

    public event Action? Changed;

    /// <summary>{snippet:…} refers to another snippet by keyword or name.</summary>
    public string? ContentNamed(string name)
    {
        var n = name.Trim();
        return _snippets.FirstOrDefault(s => s.Keyword == n || string.Equals(s.Name, n, StringComparison.CurrentCultureIgnoreCase))?.Content;
    }

    public Snippet Add(Snippet snippet)
    {
        _snippets.Insert(0, snippet);
        Save();
        return snippet;
    }

    public void Update(Snippet snippet)
    {
        int i = _snippets.FindIndex(s => s.Id == snippet.Id);
        if (i < 0) return;
        _snippets[i] = snippet;
        Save();
    }

    public void Delete(Guid id)
    {
        if (_snippets.RemoveAll(s => s.Id == id) > 0) Save();
    }

    public void MarkUsed(Guid id)
    {
        int i = _snippets.FindIndex(s => s.Id == id);
        if (i < 0) return;
        _snippets[i] = _snippets[i] with { Used = _snippets[i].Used + 1 };
        Save();
    }

    /// <summary>
    /// Another snippet whose keyword is the same, or (expanding at once) starts this one's or is
    /// started by it: typing the longer one would expand the shorter one first.
    /// </summary>
    public Snippet? Clash(Snippet snippet, SnippetTrigger trigger)
    {
        if (snippet.Keyword.Length == 0) return null;
        return _snippets.FirstOrDefault(s => s.Id != snippet.Id && s.Keyword.Length > 0 && (s.Keyword == snippet.Keyword
            || (trigger == SnippetTrigger.Immediate && (snippet.Keyword.StartsWith(s.Keyword, StringComparison.Ordinal) || s.Keyword.StartsWith(snippet.Keyword, StringComparison.Ordinal)))));
    }

    /// <summary>Most used first with no query; else by the best of name, keyword and content.</summary>
    public IReadOnlyList<Snippet> Search(string query)
    {
        var q = query.Trim();
        if (q.Length == 0) return _snippets.OrderByDescending(s => s.Used).ToList();
        return _snippets
            .Select(s => (Snippet: s, Score: new[] { Fuzzy.Score(q, s.Name), Fuzzy.Score(q, s.Keyword), s.Content.Contains(q, StringComparison.CurrentCultureIgnoreCase) ? 300 : (int?)null }.Max()))
            .Where(x => x.Score is not null)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Snippet)
            .ToList();
    }

    // MARK: Import and export

    /// <summary>Raycast or Alfred JSON, TextExpander or plain CSV. Snippets whose keyword is taken are skipped. How many came in.</summary>
    public int Import(string text, string folder)
    {
        var found = ParseJson(text) ?? ParseCsv(text);
        var taken = _snippets.Select(s => s.Keyword).Where(k => k.Length > 0).ToHashSet();
        var fresh = found.Where(s => s.Keyword.Length == 0 || taken.Add(s.Keyword)).Select(s => s with { Folder = folder }).ToList();
        _snippets.InsertRange(0, fresh);
        if (fresh.Count > 0) Save();
        return fresh.Count;
    }

    /// <summary>Raycast's shape: [{name, keyword, text}].</summary>
    public string Export() =>
        JsonSerializer.Serialize(_snippets.Select(s => new { name = s.Name, keyword = s.Keyword, text = s.Content }), Json);

    internal static List<Snippet>? ParseJson(string text)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
        var found = new List<Snippet>();
        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonArray list:
                    foreach (var item in list) Walk(item);
                    break;
                case JsonObject obj when obj["alfredsnippet"] is { } inner:
                    Walk(inner);
                    break;
                case JsonObject obj when obj["snippets"] is JsonArray list:
                    Walk(list);
                    break;
                case JsonObject obj:
                    var content = Text(obj, "text", "snippet", "content", "plainText");
                    if (string.IsNullOrEmpty(content)) break;
                    var name = Text(obj, "name", "label", "title");
                    found.Add(new Snippet
                    {
                        Name = string.IsNullOrEmpty(name) ? Head(content) : name,
                        Keyword = Text(obj, "keyword", "abbreviation") ?? "",
                        Content = Placeholders(content),
                    });
                    break;
            }
        }
        Walk(root);
        return found;
    }

    private static string? Text(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
            if (obj[key] is JsonValue value && value.TryGetValue<string>(out var s)) return s;
        return null;
    }

    internal static List<Snippet> ParseCsv(string text) =>
        Rows(text)
            .Where(c => c.Count >= 2 && c[1].Length > 0 && c[0].ToLowerInvariant() is not "abbreviation" and not "keyword")
            .Select(c => new Snippet { Keyword = c[0], Content = Placeholders(c[1]), Name = c.Count >= 3 && c[2].Length > 0 ? c[2] : Head(c[1]) })
            .ToList();

    private static string Head(string content) => content.Length > 30 ? content[..30] : content;

    /// <summary>Raycast's {clipboard} and {cursor} already match; TextExpander's %clipboard, %| and %Y-%m-%d don't.</summary>
    internal static string Placeholders(string s) =>
        s.Replace("%clipboard", "{clipboard}").Replace("%|", "{cursor}").Replace("%Y-%m-%d", "{date:yyyy-MM-dd}");

    /// <summary>A small CSV reader: quoted fields, doubled quotes, newlines inside quotes.</summary>
    internal static List<List<string>> Rows(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else quoted = false;
            }
            else if (c == '"') quoted = true;
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else if (c != '\r') field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }

    // MARK: Saving

    private void Save()
    {
        Changed?.Invoke();
        try
        {
            AtomicFile.WriteAllText(_file, JsonSerializer.Serialize(_snippets, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("Snippets could not be saved", e);
        }
    }

    private static List<Snippet>? Load(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<List<Snippet>>(File.ReadAllText(file), Json) ?? [] : null;
        }
        catch (JsonException)
        {
            Log.Error("The snippets file is damaged; it was renamed and the examples come back");
            try { File.Move(file, file + ".damaged", overwrite: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("The snippets file could not be read", e);
            return [];
        }
    }
}
