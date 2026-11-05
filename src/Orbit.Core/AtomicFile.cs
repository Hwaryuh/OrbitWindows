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

/// <summary>Reading a file another program owns: never waits on its lock, never takes in more than a settings file should be.</summary>
public static class SharedFile
{
    /// <exception cref="IOException">It can't be read, or is larger than <paramref name="largest"/> bytes.</exception>
    public static string ReadText(string path, long largest)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > largest) throw new IOException("Too large for a settings file");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
