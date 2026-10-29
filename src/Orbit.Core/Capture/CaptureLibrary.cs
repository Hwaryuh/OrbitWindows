namespace Orbit.Core.Capture;

/// <summary>A picture in the library window: its file and when it was taken.</summary>
public sealed record LibraryEntry(string Path, DateTime Taken);

/// <summary>
/// What the Recent Captures window lists (Orbit: CaptureLibrary.swift, <c>reload</c>): Orbit's
/// capture folder, plus Orbit's own files in the save folder when that is somewhere else. A copy
/// saved there has the library file's name and is listed once. Newest first, at most 200.
/// </summary>
public static class CaptureLibrary
{
    public const int Limit = 200;

    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };

    public static IReadOnlyList<LibraryEntry> List(string libraryFolder, string? saveFolder, int limit = Limit)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<LibraryEntry>();
        Scan(libraryFolder, onlyOrbit: false, seen, found);
        if (!string.IsNullOrWhiteSpace(saveFolder) && !SameFolder(saveFolder, libraryFolder))
            // The save folder is often a busy one: only Orbit's own files from there.
            Scan(saveFolder, onlyOrbit: true, seen, found);
        return found.OrderByDescending(e => e.Taken).Take(limit).ToList();
    }

    /// <summary>Whether two paths name the same folder, whatever their case or trailing separator.</summary>
    public static bool SameFolder(string a, string b)
    {
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void Scan(string folder, bool onlyOrbit, HashSet<string> seen, List<LibraryEntry> found)
    {
        try
        {
            var dir = new DirectoryInfo(folder);
            if (!dir.Exists) return;
            foreach (var file in dir.EnumerateFiles())
            {
                if (!Kinds.Contains(file.Extension) || (file.Attributes & FileAttributes.Hidden) != 0) continue;
                if (onlyOrbit && !file.Name.StartsWith("Orbit", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(file.Name)) continue;
                var taken = file.CreationTime;
                found.Add(new LibraryEntry(file.FullName, taken > DateTime.MinValue.AddYears(1) ? taken : file.LastWriteTime));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // One folder that can't be read leaves the other's pictures listed.
            Log.Warn($"A capture folder could not be listed ({e.GetType().Name})");
        }
    }
}
