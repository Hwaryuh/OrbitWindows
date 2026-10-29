using System.Text.Json;

namespace Orbit.Core.Scratchpad;

/// <summary>
/// The pad's file. A file that can't be read never lets a save replace it: an unparsable one is
/// moved aside first (and saving waits until that worked), and one Windows won't open (locked,
/// no access) keeps the pad in memory only. Saves go through a temporary file and a replace.
/// </summary>
public sealed class ScratchpadStore(string path)
{
    private string? _lastSaved;

    public string Path { get; } = path;

    /// <summary>False after a load that couldn't make sure the file was safe to replace.</summary>
    public bool CanSave { get; private set; }

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Orbit", "scratchpad.json");

    public ScratchpadDocument Load(Func<int, string> defaultName, DateTimeOffset now)
    {
        CanSave = false;
        _lastSaved = null;
        string json;
        try
        {
            if (!File.Exists(Path))
            {
                CanSave = true;
                return ScratchpadDocument.New(defaultName(1), now);
            }
            json = File.ReadAllText(Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Scratchpad file could not be read ({e.GetType().Name}); not saving over it");
            return ScratchpadDocument.New(defaultName(1), now);
        }
        try
        {
            var document = JsonSerializer.Deserialize<ScratchpadDocument>(json, ScratchpadDocument.Json)
                ?? throw new JsonException("empty");
            _lastSaved = json;
            CanSave = true;
            return document.Sanitized(defaultName, now);
        }
        catch (JsonException)
        {
            // Kept for the user to recover by hand; the pad starts over.
            var aside = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, $"scratchpad.corrupt-{now.UtcDateTime:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            try
            {
                File.Move(Path, aside);
                Log.Warn("Scratchpad file was unreadable; moved aside");
                CanSave = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Scratchpad file was unreadable and could not be moved aside ({e.GetType().Name}); not saving over it");
            }
            return ScratchpadDocument.New(defaultName(1), now);
        }
    }

    /// <summary>Writes the document unless it is what the file already holds. False when it couldn't (the text stays in memory).</summary>
    public bool Save(ScratchpadDocument document)
    {
        if (!CanSave) return false;
        var json = document.Serialize();
        if (json == _lastSaved) return true;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            AtomicFile.WriteAllText(Path, json);
            _lastSaved = json;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Scratchpad save failed ({e.GetType().Name})");
            return false;
        }
    }

    /// <summary>
    /// The last way out when Orbit quits with a save that failed: the same text beside the file,
    /// under a new name. Never touches the file itself.
    /// </summary>
    public string? SaveCopy(ScratchpadDocument document, DateTimeOffset now)
    {
        var copy = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, $"scratchpad.unsaved-{now.UtcDateTime:yyyyMMdd-HHmmss}.json");
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(copy, document.Serialize());
            return copy;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Scratchpad copy failed ({e.GetType().Name})");
            return null;
        }
    }
}
