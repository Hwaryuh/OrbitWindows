using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orbit.Core.Settings;

/// <summary>
/// User preferences: one JSON object of flat keys (Orbit's UserDefaults keys, see <see cref="Pref"/>),
/// in %APPDATA%\Orbit\settings.json. A key that isn't in the file reads its registered default.
/// Writes are saved shortly after, atomically (a temporary file, then a replace), so a crash
/// mid-write leaves the previous file. Thread-safe.
/// </summary>
public sealed class Preferences : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly string _path;
    private readonly Dictionary<string, JsonNode?> _defaults;
    private readonly JsonObject _values;
    private readonly Timer _saveTimer;
    private bool _dirty;
    private bool _disposed;

    public Preferences(string path, IReadOnlyDictionary<string, object?> defaults)
    {
        _path = path;
        _defaults = defaults.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToNode(p.Value));
        _values = Load(path);
        _saveTimer = new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>The app's preferences file, with Orbit's defaults.</summary>
    public static Preferences OpenDefault() =>
        new(Path.Combine(AppPaths.RoamingFolder(), "settings.json"), Pref.Defaults());

    /// <summary>Raised after a value changed, with its key, on the thread that set it.</summary>
    public event Action<string>? Changed;

    public T Get<T>(string key)
    {
        JsonNode? node;
        lock (_gate)
            node = _values.TryGetPropertyValue(key, out var stored) ? stored : _defaults.GetValueOrDefault(key);
        if (node is null) return default!;
        try
        {
            return node.Deserialize<T>() ?? default!;
        }
        catch (Exception e) when (JsonErrors.IsUnreadable(e))
        {
            // A value of the wrong type (an edited file, an older version): use the default.
            Log.Warn($"Preference {key} has an unreadable value; using the default");
            var fallback = _defaults.GetValueOrDefault(key);
            return fallback is null ? default! : fallback.Deserialize<T>() ?? default!;
        }
    }

    public void Set<T>(string key, T value)
    {
        var node = JsonSerializer.SerializeToNode(value);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_values.TryGetPropertyValue(key, out var old) && JsonNode.DeepEquals(old, node)) return;
            _values[key] = node;
            MarkDirty();
        }
        Changed?.Invoke(key);
    }

    /// <summary>Back to the default: the key leaves the file.</summary>
    public void Reset(string key)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_values.Remove(key)) return;
            MarkDirty();
        }
        Changed?.Invoke(key);
    }

    /// <summary>Under the lock: a save follows shortly.</summary>
    private void MarkDirty()
    {
        _dirty = true;
        _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Writes pending changes now.</summary>
    public void Save()
    {
        // One save at a time, and the snapshot taken inside it: a later save always writes
        // a later snapshot, never one that an earlier, slower save then overwrites.
        lock (_fileGate)
        {
            string json;
            lock (_gate)
            {
                if (!_dirty) return;
                json = _values.ToJsonString(WriteOptions);
                _dirty = false;
            }
            try
            {
                AtomicFile.WriteAllText(_path, json);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("Preferences could not be saved", e);
                lock (_gate) _dirty = true;
            }
        }
    }

    /// <summary>Waits for a save the timer started, then writes what is left. Set and Reset throw after this.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        using (var stopped = new ManualResetEvent(false))
        {
            if (_saveTimer.Dispose(stopped)) stopped.WaitOne();
        }
        Save();
    }

    private static JsonObject Load(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // Keep the broken file for a look, and start from the defaults.
            var aside = path + ".broken";
            Log.Error($"Preferences unreadable; kept as {Path.GetFileName(aside)} and starting from defaults", e);
            try { File.Copy(path, aside, overwrite: true); } catch (IOException) { }
            return [];
        }
    }
}
