namespace Orbit.Core;

/// <summary>Where Orbit keeps its files.</summary>
public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\Orbit\<paramref name="name"/>, created if missing: logs, caches, this machine's files.</summary>
    public static string LocalFolder(string name) =>
        Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orbit", name));

    /// <summary>%APPDATA%\Orbit, created if missing: settings and data that follow the user (as Araso's %APPDATA%\Araso).</summary>
    public static string RoamingFolder() =>
        Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Orbit"));

    private static string Create(string folder)
    {
        Directory.CreateDirectory(folder);
        return folder;
    }
}
