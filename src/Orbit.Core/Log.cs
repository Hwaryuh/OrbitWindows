namespace Orbit.Core;

/// <summary>
/// The app's log: English lines in %LOCALAPPDATA%\Orbit\logs, one file per day. Never log typed
/// text, clipboard content or translations; log kinds and counts instead.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _folder;

    public static string Folder => _folder ??= AppPaths.LocalFolder("logs");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? error = null) =>
        Write("ERROR", error is null ? message : $"{message}: {error.GetType().Name}: {error.Message}");

    private static void Write(string level, string message)
    {
        var now = DateTime.Now;
        var line = $"{now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        System.Diagnostics.Debug.Write(line);
        try
        {
            lock (Gate)
                File.AppendAllText(Path.Combine(Folder, $"orbit-{now:yyyyMMdd}.log"), line);
        }
        catch (IOException)
        {
            // A log that can't be written must never take the app down.
        }
    }
}
