using System.Text.Json;
using System.Text.Json.Nodes;
using Orbit.Core.Araso;

namespace Orbit.Tests;

public sealed class ArasoTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "orbit-araso-" + Guid.NewGuid().ToString("N"));

    public ArasoTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private static ArasoState State(bool installed = true, bool registered = true, bool active = false, bool host = true) =>
        new(installed ? "0.5.1" : null, installed ? "C:\\Araso" : null, registered, registered, active, host, false);

    [Fact]
    public void TheNextStepFollowsWhatIsTrue()
    {
        Assert.Equal(ArasoStep.NotPresent, State(installed: false, registered: false).Next);
        Assert.Equal(ArasoStep.NotPresent, State(registered: false).Next);
        Assert.Equal(ArasoStep.Enable, State().Next);
        Assert.Equal(ArasoStep.Enable, State(host: false).Next);
        Assert.Equal(ArasoStep.Ready, State(active: true).Next);
        Assert.Equal(ArasoStep.StartHost, State(active: true, host: false).Next);
    }

    [Fact]
    public void AnUninstallKeyWithoutTheProgramIsNotInstalled() =>
        Assert.False(new ArasoState("0.5.1", null, false, false, false, false, false).IsInstalled);

    private const string Dictionary = """
        [{"keys":"chrao","as":"korean","from":"learned","strength":"weak","last":"2026-10-17"},
         {"keys":"hoge","as":"japanese","from":"user"},
         {"keys":"vs","as":"english","from":"user","apps":["code.exe"]},
         {"keys":"note","text":"memo","from":"user"},
         {"keys":"mine","as":"english","from":"user","future":{"a":1}}]
        """;

    [Fact]
    public void OnlyOwnWordsForEveryAppAreShown()
    {
        var words = ArasoFiles.Words(Dictionary);
        Assert.Equal([new ArasoWord("hoge", "japanese"), new ArasoWord("mine", "english")], words);
        Assert.Empty(ArasoFiles.Words(null));
        Assert.Empty(ArasoFiles.Words(""));
    }

    [Fact]
    public void AddingKeepsEverythingElseWhole()
    {
        var after = JsonNode.Parse(ArasoFiles.WithWord(Dictionary, new ArasoWord("new", "korean")))!.AsArray();
        Assert.Equal(6, after.Count);
        Assert.Equal("weak", after[0]!["strength"]!.GetValue<string>());
        Assert.Equal(1, after[4]!["future"]!["a"]!.GetValue<int>());
        Assert.Equal("user", after[5]!["from"]!.GetValue<string>());
    }

    [Fact]
    public void AddingAWordReplacesALearnedOneButNotAPerAppOne()
    {
        var learned = ArasoFiles.Words(ArasoFiles.WithWord(Dictionary, new ArasoWord("chrao", "english")));
        Assert.Contains(new ArasoWord("chrao", "english"), learned);
        var array = JsonNode.Parse(ArasoFiles.WithWord(Dictionary, new ArasoWord("chrao", "english")))!.AsArray();
        Assert.Single(array, e => e!["keys"]!.GetValue<string>() == "chrao");

        var perApp = JsonNode.Parse(ArasoFiles.WithWord(Dictionary, new ArasoWord("vs", "korean")))!.AsArray();
        Assert.Equal(2, perApp.Count(e => e!["keys"]!.GetValue<string>() == "vs"));
    }

    [Fact]
    public void RemovingLeavesLearnedAndPerAppEntries()
    {
        foreach (var keep in new[] { "chrao", "vs" })
            Assert.Equal(5, JsonNode.Parse(ArasoFiles.WithoutWord(Dictionary, keep))!.AsArray().Count);
        Assert.Equal(4, JsonNode.Parse(ArasoFiles.WithoutWord(Dictionary, "hoge"))!.AsArray().Count);
    }

    [Theory]
    [InlineData("{\"a\":1}")]
    [InlineData("not json")]
    public void ADictionaryThatIsNotAListIsRefused(string json) =>
        Assert.ThrowsAny<JsonException>(() => ArasoFiles.WithWord(json, new ArasoWord("a", "korean")));

    [Fact]
    public void ReplacementsComeInBothShapes()
    {
        const string json = """{"주소":["서울시","부산시"],"よろ":"よろしく","":"x","bad":1,"empty":[]}""";
        Assert.Equal(new Dictionary<string, string> { ["주소"] = "서울시", ["よろ"] = "よろしく" }, ArasoFiles.Replacements(json).ToDictionary(p => p.Key, p => p.Value));
    }

    [Fact]
    public void ChangingAReplacementLeavesTheOthersAndStaysReadable()
    {
        const string json = """{"주소":["서울시","부산시"],"keep":{"future":true}}""";
        var added = ArasoFiles.WithReplacement(json, "btw", "그런데");
        Assert.Contains("그런데", added);
        var node = JsonNode.Parse(added)!;
        Assert.Equal(2, node["주소"]!.AsArray().Count);
        Assert.True(node["keep"]!["future"]!.GetValue<bool>());
        Assert.Null(JsonNode.Parse(ArasoFiles.WithoutReplacement(added, "btw"))!["btw"]);
    }

    [Fact]
    public void TurningUpdatesOffKeepsTheOtherSettings()
    {
        var node = JsonNode.Parse(ArasoFiles.WithUpdatesOff("""{"pauseKey":"x","checkForUpdates":true}"""))!;
        Assert.False(node["checkForUpdates"]!.GetValue<bool>());
        Assert.Equal("x", node["pauseKey"]!.GetValue<string>());
        Assert.False(JsonNode.Parse(ArasoFiles.WithUpdatesOff(null))!["checkForUpdates"]!.GetValue<bool>());
    }

    private ArasoFileStore Store(bool open = false, Action? told = null) => new(_folder, () => open, told ?? (() => { }));

    private string At(string name) => Path.Combine(_folder, name);

    [Fact]
    public void AnEditWritesKeepsABackupAndTellsTheHost()
    {
        File.WriteAllText(At("f.json"), "before");
        int told = 0;
        Assert.Equal(ArasoEdit.Done, Store(told: () => told++).Edit("f.json", _ => "after"));
        Assert.Equal("after", File.ReadAllText(At("f.json")));
        Assert.Equal("before", File.ReadAllText(At("f.json.orbit-backup")));
        Assert.Equal(1, told);
    }

    [Fact]
    public void AMissingFileIsCreated()
    {
        Assert.Equal(ArasoEdit.Done, Store().Edit("new.json", text => text ?? "made"));
        Assert.Equal("made", File.ReadAllText(At("new.json")));
        Assert.False(File.Exists(At("new.json.orbit-backup")));
    }

    [Fact]
    public void NothingIsWrittenWhileArasoSettingsAreOpen()
    {
        File.WriteAllText(At("f.json"), "before");
        Assert.Equal(ArasoEdit.SettingsOpen, Store(open: true).Edit("f.json", _ => "after"));
        Assert.Equal("before", File.ReadAllText(At("f.json")));
    }

    [Fact]
    public void AFileThatCannotBeUnderstoodIsNeverOverwritten()
    {
        File.WriteAllText(At("dictionary.json"), "{broken");
        Assert.Equal(ArasoEdit.Unreadable, Store().Edit("dictionary.json", text => ArasoFiles.WithWord(text, new ArasoWord("a", "korean"))));
        Assert.Equal("{broken", File.ReadAllText(At("dictionary.json")));
        Assert.False(File.Exists(At("dictionary.json.orbit-backup")));
    }

    [Fact]
    public void AChangeMeanwhileIsRedoneOnTheNewText()
    {
        File.WriteAllText(At("f.json"), "one");
        int calls = 0;
        Assert.Equal(ArasoEdit.Done, Store().Edit("f.json", text =>
        {
            // The host saves a learned word while Orbit works, once.
            if (calls++ == 0) File.WriteAllText(At("f.json"), "two");
            return text + "+mine";
        }));
        Assert.Equal("two+mine", File.ReadAllText(At("f.json")));
    }

    [Fact]
    public void AFileThatKeepsChangingIsLeftAlone()
    {
        File.WriteAllText(At("f.json"), "0");
        int calls = 0;
        Assert.Equal(ArasoEdit.KeptChanging, Store().Edit("f.json", text =>
        {
            File.WriteAllText(At("f.json"), (++calls).ToString());
            return "mine";
        }));
        Assert.Equal("2", File.ReadAllText(At("f.json")));
    }
}
