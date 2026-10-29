using System.Globalization;
using Orbit.Core.Display;
using Orbit.Core.Settings;

namespace Orbit.Core.Capture;

/// <summary>An area captured before: physical pixels of the virtual screen, and the monitor it was on.</summary>
public sealed record LastArea(PixelRect Rect, string Monitor);

/// <summary>
/// Where captures go (Orbit: CaptureService.swift, <c>store</c>, <c>save</c>, <c>lastArea</c>). Every
/// capture is written to the library folder as "Orbit yyyy-MM-dd at HH.mm.ss.png"; saving copies it
/// into the save folder. The library is in %LOCALAPPDATA% rather than %APPDATA%: pictures are too
/// big for a roaming profile. The newest 30 are kept as the recent list, as Orbit.
/// </summary>
public sealed class CaptureStore(Preferences preferences, string folder)
{
    public const int RecentCount = 30;

    private readonly object _gate = new();
    private List<string>? _recent;

    public CaptureStore(Preferences preferences) : this(preferences, AppPaths.LocalFolder("Captures")) { }

    public string Folder => folder;

    /// <summary>The newest captures first, at most <see cref="RecentCount"/>.</summary>
    public IReadOnlyList<string> Recent
    {
        get
        {
            lock (_gate) return (_recent ??= Load()).ToList();
        }
    }

    /// <summary>Writes a PNG into the library under a new name; a half-written file is removed.</summary>
    public string Write(byte[] png, DateTime when)
    {
        Directory.CreateDirectory(folder);
        // The list from the folder before this file is in it, or the file would be listed twice.
        lock (_gate) _recent ??= Load();
        var path = FreeName(folder, FileName(when));
        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                stream.Write(png);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
        lock (_gate)
        {
            _recent!.Insert(0, path);
            if (_recent.Count > RecentCount) _recent.RemoveRange(RecentCount, _recent.Count - RecentCount);
        }
        return path;
    }

    /// <summary>Takes a file off the recent list once it is gone from the library (moved to the Recycle Bin).</summary>
    public void Forget(string libraryFile)
    {
        lock (_gate) _recent?.RemoveAll(p => string.Equals(p, libraryFile, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A copy of a library file in the save folder (Pictures\Screenshots unless chosen); the copy's path.</summary>
    public string SaveCopy(string libraryFile)
    {
        var target = preferences.Get<string>(Pref.CaptureSaveFolder);
        // Saving into the library itself would only make a numbered twin.
        if (CaptureLibrary.SameFolder(target, Path.GetDirectoryName(libraryFile) ?? "")) return libraryFile;
        Directory.CreateDirectory(target);
        var dest = FreeName(target, Path.GetFileName(libraryFile));
        File.Copy(libraryFile, dest);
        return dest;
    }

    public LastArea? LastArea
    {
        get
        {
            var values = preferences.Get<string[]>(Pref.CaptureLastArea);
            if (values is not { Length: 5 }) return null;
            Span<int> n = stackalloc int[4];
            for (int i = 0; i < 4; i++)
                if (!int.TryParse(values[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i])) return null;
            var rect = new PixelRect(n[0], n[1], n[2], n[3]);
            return rect.IsEmpty ? null : new LastArea(rect, values[4]);
        }
        set
        {
            if (value is null) preferences.Reset(Pref.CaptureLastArea);
            else
            {
                var r = value.Rect;
                preferences.Set(Pref.CaptureLastArea, new[]
                {
                    r.X.ToString(CultureInfo.InvariantCulture), r.Y.ToString(CultureInfo.InvariantCulture),
                    r.Width.ToString(CultureInfo.InvariantCulture), r.Height.ToString(CultureInfo.InvariantCulture), value.Monitor,
                });
            }
        }
    }

    /// <summary>
    /// The last area on the monitors as they are now: its monitor must still be there and hold it
    /// (a changed layout or scale makes it meaningless).
    /// </summary>
    public static PixelRect? Usable(LastArea? last, IReadOnlyList<MonitorInfo> monitors)
    {
        if (last is null) return null;
        var monitor = monitors.FirstOrDefault(m => m.DeviceName == last.Monitor);
        if (monitor is null) return null;
        var inside = last.Rect.Intersect(monitor.Bounds);
        return inside == last.Rect ? inside : null;
    }

    /// <summary>"Orbit 2026-10-30 at 14.05.09.png", as Orbit names captures.</summary>
    public static string FileName(DateTime when) =>
        "Orbit " + when.ToString("yyyy-MM-dd 'at' HH.mm.ss", CultureInfo.InvariantCulture) + ".png";

    /// <summary>The name, or the name with " 2", " 3", … before the extension when it is taken.</summary>
    internal static string FreeName(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        if (!File.Exists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            path = Path.Combine(folder, $"{stem} {i}{extension}");
            if (!File.Exists(path)) return path;
        }
    }

    private List<string> Load()
    {
        if (!Directory.Exists(folder)) return [];
        return new DirectoryInfo(folder).EnumerateFiles("*.png")
            .OrderByDescending(f => f.CreationTimeUtc).Take(RecentCount).Select(f => f.FullName).ToList();
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
