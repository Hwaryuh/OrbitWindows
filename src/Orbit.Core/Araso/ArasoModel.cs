using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orbit.Core.Araso;

/// <summary>What is true of Araso on this PC, each part apart, so the page can say which one fails.</summary>
/// <param name="InstalledVersion">From the uninstall key; null when not installed.</param>
/// <param name="InstallFolder">Where ArasoHost.exe is; null when it isn't there.</param>
/// <param name="Registered">Windows knows its input method.</param>
/// <param name="Enabled">It is on this user's list of input methods.</param>
/// <param name="Active">It is the one in use in this session.</param>
/// <param name="HostRunning">The host of this user runs from the install folder and its pipe is there.</param>
/// <param name="RestartPending">A new text service waits for a restart of Windows.</param>
public sealed record ArasoState(string? InstalledVersion, string? InstallFolder, bool Registered, bool Enabled, bool Active, bool HostRunning, bool RestartPending)
{
    public static readonly ArasoState Unknown = new(null, null, false, false, false, false, false);

    public bool IsInstalled => InstalledVersion is not null && InstallFolder is not null;

    /// <summary>What to do next.</summary>
    public ArasoStep Next => !IsInstalled || !Registered ? ArasoStep.NotPresent
        : Active ? (HostRunning ? ArasoStep.Ready : ArasoStep.StartHost)
        : ArasoStep.Enable;
}

public enum ArasoStep { NotPresent, Enable, StartHost, Ready }

/// <summary>A word of the user's own in dictionary.json.</summary>
public sealed record ArasoWord(string Keys, string Language);

/// <summary>
/// Araso's files as JSON nodes, so whatever Orbit doesn't know about stays as it was. Only the user's
/// own entries for every app are shown or changed: learned words and per-app entries are Araso's.
/// A file that isn't what it should be throws <see cref="JsonException"/> and is never overwritten.
/// </summary>
public static class ArasoFiles
{
    private static readonly JsonSerializerOptions Writing = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static IReadOnlyList<ArasoWord> Words(string? json) =>
        Array(json).OfType<JsonObject>().Where(IsOwnWord).Where(e => Text(e, "as") is not null && Text(e, "keys") is not null)
            .Select(e => new ArasoWord(Text(e, "keys")!, Text(e, "as")!)).ToList();

    /// <summary>One entry per word, as in Araso: this replaces one learned or added before, but not a per-app one.</summary>
    public static string WithWord(string? json, ArasoWord word)
    {
        var entries = Array(json);
        Remove(entries, e => Text(e, "keys") == word.Keys && e["apps"] is null);
        entries.Add(new JsonObject { ["keys"] = word.Keys, ["as"] = word.Language, ["from"] = "user" });
        return entries.ToJsonString(Writing);
    }

    public static string WithoutWord(string? json, string keys)
    {
        var entries = Array(json);
        Remove(entries, e => IsOwnWord(e) && Text(e, "keys") == keys);
        return entries.ToJsonString(Writing);
    }

    /// <summary>Each shortcut with its first phrase; Araso allows several, which stay until the shortcut is changed.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Replacements(string? json)
    {
        var found = new List<KeyValuePair<string, string>>();
        foreach (var (shortcut, value) in Object(json))
        {
            var phrase = value switch
            {
                JsonValue one when one.TryGetValue<string>(out var text) => text,
                JsonArray many when many.Count > 0 && many[0] is JsonValue first && first.TryGetValue<string>(out var text) => text,
                _ => null,
            };
            if (shortcut.Length > 0 && phrase is not null) found.Add(new(shortcut, phrase));
        }
        return found;
    }

    public static string WithReplacement(string? json, string shortcut, string phrase)
    {
        var map = Object(json);
        map[shortcut] = new JsonArray(phrase);
        return map.ToJsonString(Writing);
    }

    public static string WithoutReplacement(string? json, string shortcut)
    {
        var map = Object(json);
        map.Remove(shortcut);
        return map.ToJsonString(Writing);
    }

    /// <summary>Orbit owns Araso's updates: its own check stays off (docs/plan/02-araso.md).</summary>
    public static string WithUpdatesOff(string? json)
    {
        var settings = Object(json);
        settings["checkForUpdates"] = false;
        return settings.ToJsonString(Writing);
    }

    private static bool IsOwnWord(JsonObject entry) => Text(entry, "from") == "user" && entry["apps"] is null;

    private static string? Text(JsonObject entry, string key) =>
        entry[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static void Remove(JsonArray entries, Func<JsonObject, bool> matches)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i] is JsonObject entry && matches(entry)) entries.RemoveAt(i);
    }

    private static JsonArray Array(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonNode.Parse(json) as JsonArray ?? throw new JsonException("Not a list");

    private static JsonObject Object(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Not an object");
}

/// <summary>How a change to one of Araso's files went.</summary>
public enum ArasoEdit { Done, SettingsOpen, Unreadable, KeptChanging, Failed }

/// <summary>
/// Reads and changes the files in %APPDATA%\Araso. The change is worked out on what is there, and
/// redone once if the file moved meanwhile: the host saves learned words at any time. One backup is
/// kept beside the file, and the host is told to read it again.
/// </summary>
public sealed class ArasoFileStore(string folder, Func<bool> settingsOpen, Action tellHost)
{
    private const long LargestFile = 16 * 1024 * 1024;
    public const string Dictionary = "dictionary.json", Replacements = "replacements.json", Settings = "settings.json";

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Araso");

    /// <summary>The file's text, or null when it isn't there. Throws when it can't be read.</summary>
    public string? Read(string name)
    {
        var path = Path.Combine(folder, name);
        return File.Exists(path) ? SharedFile.ReadText(path, LargestFile) : null;
    }

    public ArasoEdit Edit(string name, Func<string?, string> change)
    {
        if (settingsOpen()) return ArasoEdit.SettingsOpen;
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var before = Read(name);
                var after = change(before);
                if (Read(name) != before) continue;
                var path = Path.Combine(folder, name);
                if (before is not null) File.Copy(path, path + ".orbit-backup", overwrite: true);
                AtomicFile.WriteAllText(path, after);
                tellHost();
                return ArasoEdit.Done;
            }
            return ArasoEdit.KeptChanging;
        }
        catch (JsonException)
        {
            Log.Warn($"Araso's {name} could not be understood; left as it is");
            return ArasoEdit.Unreadable;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Araso's {name} could not be changed ({e.GetType().Name})");
            return ArasoEdit.Failed;
        }
    }
}
