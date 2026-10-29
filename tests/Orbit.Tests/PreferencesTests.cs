using Orbit.Core.Settings;

namespace Orbit.Tests;

public sealed class PreferencesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "orbit-tests-" + Guid.NewGuid().ToString("N"));
    private string File => Path.Combine(_folder, "settings.json");

    private static readonly Dictionary<string, object?> Defaults = new()
    {
        ["a.number"] = 500,
        ["a.flag"] = true,
        ["a.text"] = "google",
        ["a.list"] = new[] { "x.exe" },
    };

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void UnsetKeysReadTheirDefaults()
    {
        using var prefs = new Preferences(File, Defaults);
        Assert.Equal(500, prefs.Get<int>("a.number"));
        Assert.True(prefs.Get<bool>("a.flag"));
        Assert.Equal(["x.exe"], prefs.Get<string[]>("a.list"));
        Assert.Equal(0, prefs.Get<int>("not.registered"));
        Assert.False(System.IO.File.Exists(File)); // nothing written until something changes
    }

    [Fact]
    public void ValuesSurviveAReopen()
    {
        using (var prefs = new Preferences(File, Defaults))
        {
            prefs.Set("a.number", 42);
            prefs.Set("a.text", "deepl");
        }
        using var again = new Preferences(File, Defaults);
        Assert.Equal(42, again.Get<int>("a.number"));
        Assert.Equal("deepl", again.Get<string>("a.text"));
        Assert.True(again.Get<bool>("a.flag"));
    }

    [Fact]
    public void ChangedFiresOnlyForARealChange()
    {
        using var prefs = new Preferences(File, Defaults);
        var changed = new List<string>();
        prefs.Changed += changed.Add;
        prefs.Set("a.number", 7);
        prefs.Set("a.number", 7);
        prefs.Reset("a.number");
        prefs.Reset("a.number");
        Assert.Equal(["a.number", "a.number"], changed);
        Assert.Equal(500, prefs.Get<int>("a.number"));
    }

    [Fact]
    public void AWrongTypeReadsTheDefault()
    {
        Directory.CreateDirectory(_folder);
        System.IO.File.WriteAllText(File, """{ "a.number": "lots" }""");
        using var prefs = new Preferences(File, Defaults);
        Assert.Equal(500, prefs.Get<int>("a.number"));
    }

    [Fact]
    public void ABrokenFileIsKeptAsideAndDefaultsApply()
    {
        Directory.CreateDirectory(_folder);
        System.IO.File.WriteAllText(File, "{ not json");
        using var prefs = new Preferences(File, Defaults);
        Assert.Equal(500, prefs.Get<int>("a.number"));
        Assert.True(System.IO.File.Exists(File + ".broken"));
    }

    [Fact]
    public void OrbitsDefaultsAreAllReadable()
    {
        using var prefs = new Preferences(File, Pref.Defaults());
        Assert.Equal(500, prefs.Get<int>(Pref.HistoryLimit));
        Assert.Equal(0.4, prefs.Get<double>(Pref.TranslateInterval));
        Assert.Equal("ko", prefs.Get<string>(Pref.TranslatePrimary));
        Assert.Contains("1password.exe", prefs.Get<string[]>(Pref.IgnoredApps));
    }
}

public sealed class StartupRegistrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orbit-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void OnlyAVelopackLayoutCanRegister()
    {
        var app = Path.Combine(_root, "current", "Orbit.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(app)!);
        Assert.False(StartupRegistration.CanRegister(app));
        File.WriteAllText(Path.Combine(_root, "Update.exe"), "");
        Assert.True(StartupRegistration.CanRegister(app));
        Assert.False(StartupRegistration.CanRegister(@"C:\src\Orbit\bin\Debug\Orbit.exe"));
    }
}
