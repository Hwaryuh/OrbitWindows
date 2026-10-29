using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;

namespace Orbit.Core.Launcher;

/// <summary>One app as Start's All Apps lists it: desktop programs and Store apps alike.</summary>
/// <param name="ParsingName">Its name inside shell:AppsFolder: an AppUserModelID or a known-folder path.</param>
/// <param name="TargetPath">The program file a desktop app starts; null for Store apps, which have none to show or elevate.</param>
public sealed record AppEntry(string Name, string ParsingName, string? TargetPath = null);

/// <summary>An icon's pixels: 32-bit premultiplied BGRA, top-down.</summary>
public sealed record IconPixels(int Width, int Height, byte[] Pixels);

/// <summary>
/// The shell's COM objects want one single-threaded apartment: every call runs on this thread,
/// one at a time, and gets its result back as a task.
/// </summary>
public sealed class ShellWorker : IDisposable
{
    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _thread;

    public ShellWorker()
    {
        _thread = new Thread(() =>
        {
            foreach (var job in _work.GetConsumingEnumerable()) job();
        }) { Name = "Orbit shell", IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> Run<T>(Func<T> job)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Add(() =>
        {
            try { done.SetResult(job()); }
            catch (Exception e) { done.SetException(e); }
        });
        return done.Task;
    }

    public void Dispose() => _work.CompleteAdding();
}

/// <summary>
/// The installed apps, read from shell:AppsFolder (Orbit: Launcher/AppIndex.swift scans
/// /Applications). The list is replaced whole when a scan finishes; a scan older than the asked
/// age runs again in the background while the old list keeps serving. One scan at a time.
/// </summary>
public sealed class AppIndex(ShellWorker worker)
{
    /// <summary>Entries of All Apps that aren't apps: links to documents and pages.</summary>
    private static readonly string[] NotApps = [".url", ".txt", ".chm", ".pdf", ".htm", ".html", ".rtf", ".ini", ".log"];

    private readonly object _gate = new();
    private IReadOnlyList<AppEntry> _apps = [];
    private DateTime _scannedAt = DateTime.MinValue;
    private Task? _scan;

    public IReadOnlyList<AppEntry> Apps => Volatile.Read(ref _apps);

    /// <summary>Raised on a pool thread after a scan replaced the list.</summary>
    public event Action? Changed;

    public void RefreshIfOlderThan(TimeSpan age)
    {
        lock (_gate)
        {
            if (_scan is { IsCompleted: false } || DateTime.UtcNow - _scannedAt < age) return;
            _scan = worker.Run(Scan).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Log.Error("App scan failed; keeping the previous list", t.Exception?.InnerException);
                    return;
                }
                Volatile.Write(ref _apps, t.Result);
                lock (_gate) _scannedAt = DateTime.UtcNow;
                Log.Info($"Apps scanned: {t.Result.Count}");
                Changed?.Invoke();
            }, TaskScheduler.Default);
        }
    }

    public static void Launch(AppEntry app) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{app.ParsingName}") { UseShellExecute = false });

    /// <summary>The icon of an app (its parsing name in shell:AppsFolder) or of any file path.</summary>
    public Task<IconPixels?> IconAsync(string appOrPath, int size) => worker.Run(() => Icon(appOrPath, size));

    private static unsafe IReadOnlyList<AppEntry> Scan()
    {
        var result = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Guid folderId = PInvoke.FOLDERID_AppsFolder, itemIid = typeof(IShellItem).GUID;
        PInvoke.SHGetKnownFolderItem(&folderId, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, HANDLE.Null, &itemIid, out var folderObject).ThrowOnFailure();
        var folder = (IShellItem)folderObject;
        IEnumShellItems? items = null;
        try
        {
            Guid bhid = PInvoke.BHID_EnumItems, enumIid = typeof(IEnumShellItems).GUID;
            void* raw = null;
            try
            {
                folder.BindToHandler(null!, &bhid, &enumIid, out raw);
                items = (IEnumShellItems)Marshal.GetObjectForIUnknown((nint)raw);
            }
            finally
            {
                if (raw is not null) Marshal.Release((nint)raw);
            }

            var one = new IShellItem[1];
            while (true)
            {
                uint fetched = 0;
                items.Next(1, one, &fetched);
                if (fetched == 0) break;
                var item = one[0];
                string name, parsing;
                string? target;
                try
                {
                    name = DisplayName(item, SIGDN.SIGDN_NORMALDISPLAY);
                    parsing = DisplayName(item, SIGDN.SIGDN_PARENTRELATIVEPARSING);
                    target = TargetOf(item, parsing);
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parsing)) continue;
                if (NotApps.Any(e => parsing.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;
                if (seen.Add(parsing)) result.Add(new AppEntry(name, parsing, target));
            }
        }
        finally
        {
            if (items is not null) Marshal.ReleaseComObject(items);
            Marshal.ReleaseComObject(folder);
        }
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }

    /// <summary>
    /// The file a Start menu entry runs: its shortcut's target, else a parsing name of the form
    /// "{known folder}\rest" resolved. Null when there is no file (Store apps) or it is gone.
    /// </summary>
    private static unsafe string? TargetOf(IShellItem item, string parsing)
    {
        // Not a shortcut: no target property, and the call fails.
        if (item is IShellItem2 properties &&
            ShellString(text => { properties.GetString(PInvoke.PKEY_Link_TargetParsingPath, out var value); *text = value; }) is { } path &&
            Path.IsPathRooted(path) && File.Exists(path))
            return path;

        int end = parsing.IndexOf("}\\", StringComparison.Ordinal);
        if (!parsing.StartsWith('{') || end < 0 || !Guid.TryParse(parsing[..(end + 1)], out var folder)) return null;
        if (PInvoke.SHGetKnownFolderPath(in folder, 0, null, out var root).Failed) return null;
        using (root)
        {
            var resolved = Path.Combine(root.ToString() ?? "", parsing[(end + 2)..]);
            return File.Exists(resolved) ? resolved : null;
        }
    }

    private static unsafe string DisplayName(IShellItem item, SIGDN kind) => ShellString(text => item.GetDisplayName(kind, text)) ?? "";

    private unsafe delegate void StringGetter(PWSTR* text);

    /// <summary>A string a shell call allocates for its caller: read and freed; null if the call failed.</summary>
    private static unsafe string? ShellString(StringGetter get)
    {
        PWSTR text = default;
        try
        {
            get(&text);
            return text.ToString();
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (text.Value is not null) Marshal.FreeCoTaskMem((nint)text.Value);
        }
    }

    private static unsafe IconPixels? Icon(string appOrPath, int size)
    {
        var parsing = Path.IsPathRooted(appOrPath) ? appOrPath : $"shell:AppsFolder\\{appOrPath}";
        Guid iid = typeof(IShellItemImageFactory).GUID;
        object factoryObject;
        fixed (char* text = parsing)
            if (PInvoke.SHCreateItemFromParsingName(text, null, &iid, out factoryObject).Failed) return null;
        var factory = (IShellItemImageFactory)factoryObject;
        HBITMAP bitmap = default;
        try
        {
            factory.GetImage(new SIZE(size, size), SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_BIGGERSIZEOK, &bitmap);
            return bitmap.IsNull ? null : Pixels(bitmap);
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (!bitmap.IsNull) PInvoke.DeleteObject(bitmap);
            Marshal.ReleaseComObject(factory);
        }
    }

    private static unsafe IconPixels? Pixels(HBITMAP bitmap)
    {
        BITMAP info;
        if (PInvoke.GetObject(bitmap, sizeof(BITMAP), &info) == 0) return null;
        int width = info.bmWidth, height = Math.Abs(info.bmHeight);
        var dc = PInvoke.CreateCompatibleDC(HDC.Null);
        try
        {
            return Capture.BitmapBits.Read(dc, bitmap, width, height) is { } pixels ? new IconPixels(width, height, pixels) : null;
        }
        finally
        {
            PInvoke.DeleteDC(dc);
        }
    }
}
