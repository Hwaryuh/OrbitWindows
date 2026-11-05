using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Orbit.Core.Scratchpad;

namespace Orbit.Tests;

public class ScratchpadMarkdownTests
{
    [Theory]
    [InlineData("- [ ] a\n- [x] b\n", 2)]
    [InlineData("intro\r\n\r\n- [ ] a\r\n- [X] b\r\n", 2)]
    [InlineData("- [ ] a\n  - [x] b\n    - [ ] c\n", 3)]
    [InlineData("- [ ] a\r\n    - [x] b\r\n", 2)]
    [InlineData("> - [ ] a\n> - [x] b\n", 2)]
    [InlineData("> text\r\n>\r\n> - [ ] a\r\n>   - [x] b\r\n", 2)]
    [InlineData("-\t[ ] a\n\t- [x] b\n", 2)]
    [InlineData("1. [ ] a\n2. [x] b\n", 2)]
    [InlineData("한글 문단입니다 😀\n\n- [ ] 할 일\n- [x] 끝\n", 2)]
    [InlineData("- [ ] a\n\n- [x] b\n", 2)]
    public void EveryBoxPointsAtItsBracketsInTheSource(string text, int count)
    {
        var boxes = ScratchpadMarkdown.Parse(text).Descendants<TaskList>().ToList();
        Assert.Equal(count, boxes.Count);
        foreach (var box in boxes)
        {
            Assert.True(ScratchpadMarkdown.IsTaskBox(box));
            Assert.Equal(3, box.Span.Length);
            Assert.Equal('[', text[box.Span.Start]);
            Assert.Equal(']', text[box.Span.Start + 2]);
            Assert.Equal(box.Checked, text[box.Span.Start + 1] != ' ');
        }
    }

    [Theory]
    [InlineData("[ ] a\n")]
    [InlineData("- b [ ] c\n")]
    [InlineData("- b\n\n  [ ] c\n")]
    [InlineData("```\n- [ ] a\n```\n")]
    public void OnlyABoxThatStartsAListItemCounts(string text)
    {
        Assert.DoesNotContain(ScratchpadMarkdown.Parse(text).Descendants<TaskList>(), ScratchpadMarkdown.IsTaskBox);
    }

    [Theory]
    [InlineData("- [ ] a", 2, 'x')]
    [InlineData("- [x] a", 2, ' ')]
    [InlineData("- [X] a", 2, ' ')]
    public void AMarkFlips(string text, int at, char mark) => Assert.Equal(mark, ScratchpadMarkdown.ToggledMark(text, at));

    [Theory]
    [InlineData("- [ ] a", 0)]
    [InlineData("- [ ] a", -1)]
    [InlineData("- [ ] a", 100)]
    [InlineData("- [ ", 2)]
    [InlineData("- [y] a", 2)]
    [InlineData("", 0)]
    public void AStalePositionEditsNothing(string text, int at) => Assert.Null(ScratchpadMarkdown.ToggledMark(text, at));

    [Theory]
    [InlineData(">", 5000)]
    [InlineData("- ", 5000)]
    [InlineData("[", 50000)]
    [InlineData("*", 50000)]
    public void HostileNotesNeverOverflowTheStack(string piece, int times)
    {
        // Markdig refuses deep nesting with an ArgumentException; the preview then shows plain text.
        var text = string.Concat(Enumerable.Repeat(piece, times)) + "a";
        var thrown = Record.Exception(() => ScratchpadMarkdown.Parse(text));
        Assert.True(thrown is null or ArgumentException, thrown?.GetType().Name);
    }

    [Fact]
    public void PreviewSurvivesASaveAndOldFilesHaveNone()
    {
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var doc = ScratchpadDocument.New("Note 1", now);
        doc.Update(doc.Selected, p => p with { Preview = true });
        var back = System.Text.Json.JsonSerializer.Deserialize<ScratchpadDocument>(doc.Serialize())!.Sanitized(n => $"Note {n}", now);
        Assert.True(back.Current.Preview);

        var old = """{"Pads":[{"Id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","Name":"Old","Text":"t","Modified":"2026-10-30T12:00:00+00:00"}],"Selected":"6f9619ff-8b86-d011-b42d-00c04fc964ff","Closed":[]}""";
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<ScratchpadDocument>(old)!.Current.Preview);
    }
}
