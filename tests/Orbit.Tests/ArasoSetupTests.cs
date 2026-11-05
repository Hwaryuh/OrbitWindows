using Orbit.Core.Araso;

namespace Orbit.Tests;

public sealed class ArasoSetupTests : IDisposable
{
    private sealed class FakeMachine : IArasoMachine
    {
        public List<string> Calls { get; } = [];
        public bool FailRegister32 { get; set; }
        public bool FailRegister64 { get; set; }
        public bool FailState { get; set; }
        public List<string> AtRestart { get; } = [];

        public bool Register(string dll, bool is32Bit)
        {
            Calls.Add(is32Bit ? "register32" : "register64");
            Assert.True(File.Exists(dll), "registers a file that is there");
            return !(is32Bit ? FailRegister32 : FailRegister64);
        }

        public bool FailUnregister64 { get; set; }

        public bool Unregister(string dll, bool is32Bit)
        {
            Calls.Add(is32Bit ? "unregister32" : "unregister64");
            return is32Bit || !FailUnregister64;
        }

        public void SetStartup(string command) => Calls.Add("startup " + command);
        public void ClearStartup() => Calls.Add("clearStartup");

        public void WriteState(string version, string folder)
        {
            if (FailState) throw new UnauthorizedAccessException();
            Calls.Add($"state {version}");
        }

        public void ClearState() => Calls.Add("clearState");
        public void ClearLeftovers() => Calls.Add("clearLeftovers");
        public void DeleteAtRestart(string path) => AtRestart.Add(path);
    }

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "orbit-setup-" + Guid.NewGuid().ToString("N"));
    private readonly FakeMachine _machine = new();

    private string Bundle => Path.Combine(_folder, "bundle");
    private string Root => Path.Combine(_folder, "program");
    private ArasoSetup Setup => new(Bundle, Root, _machine);

    public ArasoSetupTests()
    {
        Directory.CreateDirectory(Path.Combine(Bundle, "x86"));
        Directory.CreateDirectory(Path.Combine(Bundle, "Resources"));
        File.WriteAllText(Path.Combine(Bundle, "version.txt"), "0.5.1\r\n");
        File.WriteAllText(Path.Combine(Bundle, "ArasoHost.exe"), "host");
        File.WriteAllText(Path.Combine(Bundle, "ArasoTip.dll"), "tip");
        File.WriteAllText(Path.Combine(Bundle, "x86", "ArasoTip.dll"), "tip32");
        File.WriteAllText(Path.Combine(Bundle, "Resources", "words.txt"), "words");
    }

    public void Dispose() => Directory.Delete(_folder, true);

    private string Installed => Path.Combine(Root, "0.5.1");

    [Fact]
    public void TheBundleVersionIsRead()
    {
        Assert.Equal("0.5.1", ArasoSetup.BundledVersion(Bundle));
        Assert.Null(ArasoSetup.BundledVersion(Path.Combine(_folder, "nothing")));
        File.Delete(Path.Combine(Bundle, "ArasoTip.dll"));
        Assert.Null(ArasoSetup.BundledVersion(Bundle));
    }

    [Fact]
    public void WithoutABundleNothingHappens()
    {
        File.Delete(Path.Combine(Bundle, "version.txt"));
        Assert.Equal(ArasoSetupResult.NoBundle, Setup.Install());
        Assert.Empty(_machine.Calls);
        Assert.False(Directory.Exists(Root));
    }

    [Fact]
    public void WhatSaysSetUpIsWrittenLast()
    {
        Assert.Equal(ArasoSetupResult.Done, Setup.Install());
        Assert.Equal("words", File.ReadAllText(Path.Combine(Installed, "Resources", "words.txt")));
        Assert.Equal(["register64", "register32", $"startup \"{Path.Combine(Installed, "ArasoHost.exe")}\" --server", "state 0.5.1"], _machine.Calls);
        Assert.False(Directory.Exists(Installed + ".part"));
    }

    [Fact]
    public void A32BitFailureUndoesTheRest()
    {
        _machine.FailRegister32 = true;
        Assert.Equal(ArasoSetupResult.RegisterFailed, Setup.Install());
        Assert.DoesNotContain(_machine.Calls, c => c.StartsWith("state") || c.StartsWith("startup"));
        Assert.Contains("unregister64", _machine.Calls);
        Assert.Contains("unregister32", _machine.Calls);
        Assert.Contains("clearStartup", _machine.Calls);
    }

    [Fact]
    public void AStateFailureUndoesTheRest()
    {
        _machine.FailState = true;
        Assert.Equal(ArasoSetupResult.StateFailed, Setup.Install());
        Assert.Contains("unregister64", _machine.Calls);
        Assert.Contains("clearState", _machine.Calls);
    }

    [Fact]
    public void ARunThatDiedInTheCopyIsRedone()
    {
        Directory.CreateDirectory(Installed + ".part");
        File.WriteAllText(Path.Combine(Installed + ".part", "ArasoHost.exe"), "half");
        Assert.Equal(ArasoSetupResult.Done, Setup.Install());
        Assert.Equal("host", File.ReadAllText(Path.Combine(Installed, "ArasoHost.exe")));
    }

    [Fact]
    public void AnIncompleteFolderIsReplaced()
    {
        Directory.CreateDirectory(Installed);
        File.WriteAllText(Path.Combine(Installed, "ArasoHost.exe"), "host");
        Assert.Equal(ArasoSetupResult.Done, Setup.Install());
        Assert.True(File.Exists(Path.Combine(Installed, "x86", "ArasoTip.dll")));
    }

    [Fact]
    public void ACompleteFolderIsLeftAloneSoFilesInUseAreNeverReplaced()
    {
        Setup.Install();
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tip = Path.Combine(Installed, "ArasoTip.dll");
        File.SetLastWriteTimeUtc(tip, stamp);
        // Held open as an app holds a loaded DLL.
        using (new FileStream(tip, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Equal(ArasoSetupResult.Done, Setup.Install());
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(tip));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData(".hidden")]
    public void AVersionThatIsNotOneIsRefused(string version)
    {
        File.WriteAllText(Path.Combine(Bundle, "version.txt"), version);
        Assert.Equal(ArasoSetupResult.CopyFailed, Setup.Install());
        Assert.False(Directory.Exists(Root));
    }

    [Fact]
    public void RemovingUndoesAll()
    {
        Setup.Install();
        _machine.Calls.Clear();
        Assert.Equal(ArasoSetupResult.Done, Setup.Remove(Installed));
        Assert.Equal(["unregister64", "unregister32", "clearStartup", "clearState", "clearLeftovers"], _machine.Calls);
        Assert.False(Directory.Exists(Root));
    }

    [Fact]
    public void AFailedStepDoesNotStopTheOthersAndIsReported()
    {
        Setup.Install();
        _machine.Calls.Clear();
        _machine.FailUnregister64 = true;
        Assert.Equal(ArasoSetupResult.CleanupFailed, Setup.Remove(Installed));
        Assert.Contains("unregister32", _machine.Calls);
        Assert.Contains("clearLeftovers", _machine.Calls);
        Assert.False(Directory.Exists(Root));
    }

    [Fact]
    public void RemovingDoesNotFollowALinkOutOfTheFolder()
    {
        Setup.Install();
        var outside = Path.Combine(_folder, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "mine");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(Installed, "link"), outside);
        }
        catch (IOException)
        {
            return; // Symbolic links need developer mode or elevation here.
        }
        Setup.Remove(Installed);
        Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
    }

    [Fact]
    public void FilesInUseWaitForARestart()
    {
        Setup.Install();
        var host = Path.Combine(Installed, "ArasoHost.exe");
        using (new FileStream(host, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(ArasoSetupResult.Done, Setup.Remove(Installed));
        Assert.Contains(host, _machine.AtRestart);
        Assert.Contains(Root, _machine.AtRestart);
    }

    [Fact]
    public void RemovingWithNothingThereIsFine()
    {
        Assert.Equal(ArasoSetupResult.Done, Setup.Remove(null));
        Assert.Contains("clearLeftovers", _machine.Calls);
    }

    [Fact]
    public void OnlyTheBareArgumentsCount()
    {
        Assert.Null(ArasoElevated.Run([]));
        Assert.Null(ArasoElevated.Run(["--other"]));
        Assert.Null(ArasoElevated.Run(["--araso-setup", "C:/evil"]));
    }
}
