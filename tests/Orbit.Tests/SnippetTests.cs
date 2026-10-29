using System.Globalization;
using Orbit.Core.Snippets;

namespace Orbit.Tests;

public class SnippetBufferTests
{
    private static SnippetBuffer Typed(string text)
    {
        var b = new SnippetBuffer();
        b.Type(text);
        return b;
    }

    [Fact]
    public void ImmediateMatchesTheLongestKeyword()
    {
        Assert.Equal(new SnippetMatch(";addr", null), Typed("hi ;addr").Match([";a", ";addr"], SnippetTrigger.Immediate));
        Assert.Null(Typed("hi ;add").Match([";addr"], SnippetTrigger.Immediate));
    }

    [Fact]
    public void DelimiterTriggerWaitsForSpaceOrTab()
    {
        Assert.Null(Typed(";date").Match([";date"], SnippetTrigger.Delimiter));
        Assert.Equal(new SnippetMatch(";date", ' '), Typed(";date ").Match([";date"], SnippetTrigger.Delimiter));
        Assert.Equal(new SnippetMatch(";date", '\t'), Typed(";date\t").Match([";date"], SnippetTrigger.Delimiter));
    }

    [Fact]
    public void ComposingKeywordsWaitForTheDelimiterEvenWhenImmediate()
    {
        bool Wait(string k) => Dubeolsik.EndsComposing(k);
        Assert.Null(Typed(";addr").Match([";addr"], SnippetTrigger.Immediate, Wait));
        Assert.Equal(new SnippetMatch(";addr", ' '), Typed(";addr ").Match([";addr"], SnippetTrigger.Immediate, Wait));
        // A keyword ending in a symbol needs no wait.
        Assert.Equal(new SnippetMatch(";a;", null), Typed(";a;").Match([";a;"], SnippetTrigger.Immediate, Wait));
    }

    [Fact]
    public void BufferKeepsTheLast48AndBackspacesPairs()
    {
        var b = Typed(new string('x', 60));
        Assert.Equal(48, b.Text.Length);
        b.Reset();
        b.Type("a😀");
        b.Backspace();
        Assert.Equal("a", b.Text);
        b.Backspace();
        b.Backspace();
        Assert.Equal("", b.Text);
        Assert.Null(b.Match(["a"], SnippetTrigger.Immediate));
    }
}

public class SnippetTemplateTests
{
    private static readonly SnippetTemplate.Context Context = new()
    {
        Clipboard = "CLIP",
        Now = new DateTime(2026, 10, 30, 14, 5, 9),
        Inputs = new Dictionary<string, string> { ["Name"] = "민지" },
        Lookup = name => name switch { "sig" => "-- {input:Name}", "loop" => "{snippet:loop}", _ => null },
        Culture = CultureInfo.InvariantCulture,
    };

    [Fact]
    public void PlaceholdersFillIn()
    {
        var r = SnippetTemplate.Render("Hi {input:Name}, {clipboard} on {date:yyyy-MM-dd} at {time:HH:mm}{cursor}!", Context);
        Assert.Equal("Hi 민지, CLIP on 2026-10-30 at 14:05!", r.Text);
        Assert.Equal(1, r.CursorFromEnd);
    }

    [Fact]
    public void NestedSnippetsAndUnknownBraces()
    {
        Assert.Equal("{nope} -- 민지", SnippetTemplate.Render("{nope} {snippet:sig}", Context).Text);
        // A loop stops after five levels.
        Assert.Equal("", SnippetTemplate.Render("{snippet:loop}", Context).Text);
        Assert.Equal("{snippet:missing}", SnippetTemplate.Render("{snippet:missing}", Context).Text);
    }

    [Fact]
    public void InputsInOrderOnceWithNesting()
    {
        Assert.Equal(["Name", "Topic"], SnippetTemplate.Inputs("{input:Name} {input:Topic} {snippet:sig} {input: Name }", Context.Lookup));
    }

    [Fact]
    public void UnicodePatternsBecomeDotNet()
    {
        Assert.Equal("yyyy-MM-dd dddd ddd hh:mm tt", SnippetTemplate.DotNetPattern("yyyy-MM-dd EEEE EEE hh:mm a"));
        Assert.Equal("Friday", SnippetTemplate.Render("{date:EEEE}", Context).Text);
    }

    [Fact]
    public void CursorCountsTextElements()
    {
        Assert.Equal(2, SnippetTemplate.Render("{cursor}👍🏽!", Context).CursorFromEnd);
        Assert.Equal(0, SnippetTemplate.Render("plain", Context).CursorFromEnd);
    }
}

public class DubeolsikTests
{
    [Theory]
    [InlineData(";addr", ";ㅁㅇㅇㄱ")]
    [InlineData("dkssud", "안녕")]
    [InlineData("rkqt", "값")]
    [InlineData("rkqtk", "갑사")]
    [InlineData("dnjsgk", "원하")]
    [InlineData("12", "12")]
    public void ComposesAsTheKoreanKeyboard(string keys, string expected) => Assert.Equal(expected, Dubeolsik.Compose(keys));

    [Fact]
    public void LettersAndComposing()
    {
        Assert.True(Dubeolsik.HasLetters(";a1"));
        Assert.False(Dubeolsik.HasLetters(";;1"));
        Assert.True(Dubeolsik.EndsComposing(";addr"));
        Assert.False(Dubeolsik.EndsComposing(";addr;"));
    }
}

public sealed class SnippetStoreTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "orbit-snippets-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        foreach (var f in new[] { _file, _file + ".tmp", _file + ".damaged" }) if (File.Exists(f)) File.Delete(f);
    }

    private SnippetStore Store() => new(_file, [new Snippet { Name = "Date", Keyword = ";date", Content = "{date}" }]);

    [Fact]
    public void FirstRunGetsTheExamplesAndEditsPersist()
    {
        var store = Store();
        Assert.Single(store.All);
        var added = store.Add(new Snippet { Name = "Mail", Keyword = ";mail", Content = "me@example.com" });
        store.MarkUsed(added.Id);
        store.Update(store.All[0] with { Enabled = false });
        var again = Store();
        Assert.Equal(2, again.All.Count);
        Assert.Single(again.Active);
        Assert.Equal(1, again.All.First(s => s.Keyword == ";mail").Used);
        Assert.Equal("{date}", again.ContentNamed("DATE"));
    }

    [Fact]
    public void ClashesDependOnTheTrigger()
    {
        var store = Store();
        var longer = new Snippet { Keyword = ";dates" };
        Assert.NotNull(store.Clash(longer, SnippetTrigger.Immediate));
        Assert.Null(store.Clash(longer, SnippetTrigger.Delimiter));
        Assert.NotNull(store.Clash(new Snippet { Keyword = ";date" }, SnippetTrigger.Delimiter));
    }

    [Fact]
    public void ImportsRaycastAlfredAndCsv()
    {
        var store = Store();
        Assert.Equal(2, store.Import("""[{"name":"Sig","keyword":";sig","text":"Bye %clipboard"},{"alfredsnippet":{"name":"Hi","keyword":";hi","snippet":"Hello %|"}}]""", "Imported"));
        Assert.Equal("Bye {clipboard}", store.All.First(s => s.Keyword == ";sig").Content);
        Assert.Equal("Hello {cursor}", store.All.First(s => s.Keyword == ";hi").Content);
        Assert.Equal(1, store.Import("abbreviation,content,label\n;addr,\"Seoul, \"\"Korea\"\"\nline2\",Address\n;date,dup,Dup\n", "Imported"));
        Assert.Equal("Seoul, \"Korea\"\nline2", store.All.First(s => s.Keyword == ";addr").Content);
        Assert.Contains("\"keyword\": \";addr\"", store.Export());
    }

    [Fact]
    public void SearchByNameKeywordAndContent()
    {
        var store = Store();
        store.Add(new Snippet { Name = "Address", Keyword = ";addr", Content = "Seoul" });
        Assert.Equal(";addr", store.Search("seoul")[0].Keyword);
        Assert.Equal(";addr", store.Search("addr")[0].Keyword);
        Assert.Equal(2, store.Search("").Count);
    }

    [Fact]
    public void ADamagedFileIsSetAside()
    {
        File.WriteAllText(_file, "{ broken");
        Assert.Single(Store().All);
        Assert.True(File.Exists(_file + ".damaged"));
    }
}
