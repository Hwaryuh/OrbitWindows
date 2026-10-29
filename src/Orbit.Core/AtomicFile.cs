namespace Orbit.Core;

/// <summary>Writing a file so a crash mid-write leaves the previous one: a temporary file, then a replace.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}
