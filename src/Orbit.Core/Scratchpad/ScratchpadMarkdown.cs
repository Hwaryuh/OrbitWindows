using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;

namespace Orbit.Core.Scratchpad;

/// <summary>
/// The pad's Markdown: parsing for the preview and flipping a task box in the source. Positions
/// from <see cref="Parse"/> are indexes into the text as given (CRLF, quotes and nested lists too),
/// which the tests hold it to.
/// </summary>
public static class ScratchpadMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseAutoLinks()
        .UseTaskLists()
        .UsePreciseSourceLocation()
        .Build();

    public static MarkdownDocument Parse(string text) => Markdown.Parse(text, Pipeline);

    /// <summary>
    /// Whether the parser's task box really starts a list item. Markdig also reports a "[ ]" further
    /// along or in a later paragraph, which GitHub shows as plain text and so does the preview.
    /// </summary>
    public static bool IsTaskBox(TaskList box) =>
        box.Parent is { ParentBlock: ParagraphBlock { Parent: ListItemBlock item } paragraph } line
        && line.FirstChild == box
        && item.Count > 0 && item[0] == paragraph;

    /// <summary>
    /// The mark the box at <paramref name="bracket"/> changes to (' ' or 'x'), or null when the text
    /// there is no longer a box, so a stale position never edits anything else.
    /// </summary>
    public static char? ToggledMark(string text, int bracket)
    {
        if (bracket < 0 || bracket + 2 >= text.Length || text[bracket] != '[' || text[bracket + 2] != ']') return null;
        return text[bracket + 1] switch
        {
            ' ' => 'x',
            'x' or 'X' => ' ',
            _ => null,
        };
    }
}
