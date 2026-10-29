namespace Orbit.Core;

/// <summary>Where Orbit keeps its files.</summary>
public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\Orbit\<paramref name="name"/>, created if missing.</summary>
    public static string LocalFolder(string name)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orbit", name);
        Directory.CreateDirectory(folder);
        return folder;
    }
}
