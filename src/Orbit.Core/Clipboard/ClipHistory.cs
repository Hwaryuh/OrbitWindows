using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orbit.Core.Clipboard;

/// <summary>Which items the panel shows: all, the pinned ones, or one kind.</summary>
public readonly record struct ClipFilter(bool PinnedOnly = false, ClipKind? Kind = null)
{
    public static readonly ClipFilter All = new();
    public static readonly ClipFilter Pinned = new(PinnedOnly: true);

    /// <summary>All, Pinned, then each kind, as the panel's ↑ and ↓ go through them.</summary>
    public static IReadOnlyList<ClipFilter> Cycle { get; } = [All, Pinned, .. Enum.GetValues<ClipKind>().Select(k => new ClipFilter(Kind: k))];

    public ClipFilter Next(bool backward = false)
    {
        int i = Math.Max(0, Cycle.ToList().IndexOf(this));
        return Cycle[(i + (backward ? Cycle.Count - 1 : 1)) % Cycle.Count];
    }
}

/// <summary>
/// The clipboard history (Orbit: ClipboardStore.swift): newest first; copying the same thing
/// again moves it to the top and keeps its pin. Unpinned items beyond the limit (at least 20) go,
/// with their side files. Pictures together may take at most <see cref="FolderBudget"/>; past it
/// the oldest unpinned pictures go first. Not thread-safe: the UI thread owns it.
/// </summary>
public sealed class ClipHistory
{
    public const int MinimumKept = 20;
    public const long FolderBudget = 1L << 30;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, Converters = { new JsonStringEnumConverter() } };

    private readonly List<ClipItem> _items;
    private readonly string _historyFile;
    private readonly Func<int> _limit;
    private readonly object _saveGate = new();
    private long _version;
    private long _written;

    public ClipHistory(string historyFile, string clipsFolder, Func<int> limit)
    {
        _historyFile = historyFile;
        ClipsFolder = clipsFolder;
        _limit = limit;
        _items = Load(historyFile);
    }

    public string ClipsFolder { get; }
    public IReadOnlyList<ClipItem> Items => _items;

    /// <summary>Anything changed; the caller saves soon after.</summary>
    public event Action? Changed;

    public string PathOf(string sideFile) => Path.Combine(ClipsFolder, sideFile);

    /// <summary>Adds a copy on top, or moves the same copy there (keeping its id, pin, recognized text and files).</summary>
    public ClipItem Insert(ClipItem item)
    {
        int i = _items.FindIndex(x => x.Signature == item.Signature);
        if (i >= 0)
        {
            var old = _items[i];
            _items.RemoveAt(i);
            item = item with
            {
                Id = old.Id,
                Pinned = old.Pinned,
                OcrText = item.OcrText ?? old.OcrText,
                ImageFile = item.ImageFile ?? old.ImageFile,
                RtfFile = item.RtfFile ?? old.RtfFile,
                HtmlFile = item.HtmlFile ?? old.HtmlFile,
            };
            // The new copy's own files replace the old ones.
            DeleteFiles(old.Files.Except(item.Files));
        }
        item = item with { Date = DateTime.Now };
        _items.Insert(0, item);
        Trim();
        Changed?.Invoke();
        return item;
    }

    /// <summary>Unpinned items beyond the limit go, oldest first.</summary>
    public void Trim()
    {
        int limit = Math.Max(MinimumKept, _limit());
        int unpinned = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Pinned) continue;
            if (++unpinned <= limit) continue;
            DeleteFiles(_items[i].Files);
            _items.RemoveAt(i--);
        }
    }

    public IReadOnlyList<ClipItem> Search(string query, ClipFilter filter)
    {
        var q = query.Trim();
        return _items.Where(item =>
            (!filter.PinnedOnly || item.Pinned)
            && (filter.Kind is not { } kind || item.Kind == kind)
            && (q.Length == 0 || item.Searchable.Contains(q, StringComparison.CurrentCultureIgnoreCase))).ToList();
    }

    public ClipItem? Find(Guid id) => _items.FirstOrDefault(i => i.Id == id);

    public void TogglePin(Guid id) => Update(id, item => item with { Pinned = !item.Pinned });

    /// <summary>Text recognized in a picture after it was added, for search.</summary>
    public void SetOcrText(Guid id, string text) => Update(id, item => item with { OcrText = text });

    /// <summary>Copied back from the history: it is what's on the clipboard now, so it goes to the top.</summary>
    public void MarkUsed(Guid id)
    {
        int i = _items.FindIndex(x => x.Id == id);
        if (i <= 0) return;
        var item = _items[i] with { Date = DateTime.Now };
        _items.RemoveAt(i);
        _items.Insert(0, item);
        Changed?.Invoke();
    }

    public void Delete(Guid id)
    {
        int i = _items.FindIndex(x => x.Id == id);
        if (i < 0) return;
        DeleteFiles(_items[i].Files);
        _items.RemoveAt(i);
        Changed?.Invoke();
    }

    public void Clear(bool keepPinned = true)
    {
        foreach (var item in _items.Where(i => !(keepPinned && i.Pinned))) DeleteFiles(item.Files);
        _items.RemoveAll(i => !(keepPinned && i.Pinned));
        Changed?.Invoke();
    }

    private void Update(Guid id, Func<ClipItem, ClipItem> change)
    {
        int i = _items.FindIndex(x => x.Id == id);
        if (i < 0) return;
        _items[i] = change(_items[i]);
        Changed?.Invoke();
    }

    // MARK: Files

    /// <summary>
    /// Keeps the pictures within <paramref name="budget"/> bytes: the oldest unpinned ones go
    /// first. True when the folder fits afterwards (false only when pinned pictures alone are over).
    /// </summary>
    public bool EnforceBudget(long budget = FolderBudget)
    {
        long used = _items.Sum(i => i.Files.Sum(SizeOf));
        for (int i = _items.Count - 1; i >= 0 && used > budget; i--)
        {
            if (_items[i].Pinned || _items[i].Kind != ClipKind.Image) continue;
            used -= _items[i].Files.Sum(SizeOf);
            DeleteFiles(_items[i].Files);
            _items.RemoveAt(i);
            Changed?.Invoke();
        }
        return used <= budget;
    }

    /// <summary>Side files no item refers to (left by a crash between writing a file and saving the history).</summary>
    public int RemoveOrphans()
    {
        if (!Directory.Exists(ClipsFolder)) return 0;
        var used = _items.SelectMany(i => i.Files).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int removed = 0;
        foreach (var file in Directory.EnumerateFiles(ClipsFolder))
        {
            if (used.Contains(Path.GetFileName(file))) continue;
            if (AppPaths.TryDelete(file)) removed++;
        }
        return removed;
    }

    private long SizeOf(string sideFile)
    {
        try
        {
            var info = new FileInfo(PathOf(sideFile));
            return info.Exists ? info.Length : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private void DeleteFiles(IEnumerable<string> files)
    {
        foreach (var file in files.ToList()) AppPaths.TryDelete(PathOf(file));
    }

    // MARK: Saving

    /// <summary>The history as JSON now, numbered, to be written with <see cref="Write"/> off the UI thread.</summary>
    public (string Json, long Version) Snapshot() => (JsonSerializer.Serialize(_items, Json), Interlocked.Increment(ref _version));

    /// <summary>
    /// Writes a snapshot atomically, one write at a time. An older snapshot than the last one
    /// written is dropped: saves started in one order may run in another.
    /// </summary>
    public void Write((string Json, long Version) snapshot)
    {
        lock (_saveGate)
        {
            if (snapshot.Version <= _written) return;
            _written = snapshot.Version;
            try
            {
                AtomicFile.WriteAllText(_historyFile, snapshot.Json);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("The clipboard history could not be saved", e);
            }
        }
    }

    private static List<ClipItem> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<ClipItem>>(File.ReadAllText(path), Json) ?? [];
        }
        catch (JsonException)
        {
            // Kept aside rather than overwritten by the next save.
            Log.Error("The clipboard history file is damaged; it was renamed and the history starts empty");
            try { File.Move(path, path + ".damaged", overwrite: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("The clipboard history could not be read; starting empty", e);
            return [];
        }
    }
}
