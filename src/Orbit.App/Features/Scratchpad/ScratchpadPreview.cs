using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig.Extensions.TaskLists;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.Core.Scratchpad;
using Md = Markdig.Syntax;
using MdInline = Markdig.Syntax.Inlines;

namespace Orbit.App.Features.Scratchpad;

/// <summary>
/// Turns a note's Markdown into a read-only document. A task box flips through
/// <paramref name="flip"/>, which edits the source and says the new state, or null when the preview
/// is out of date. Only http and https links open; images and raw HTML stay text, so a note never
/// fetches anything or runs anything. A line break in a note stays a line break.
/// </summary>
internal static class ScratchpadPreview
{
    private const int MaxDepth = 16;

    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(110, 110, 110)).Frozen();
    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(228, 228, 228)).Frozen();
    private static readonly Brush CodeFill = new SolidColorBrush(Color.FromRgb(244, 244, 244)).Frozen();
    private static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas, Malgun Gothic");
    private static readonly double[] HeadingSizes = [22, 18, 16, 15, 14, 14];

    public static FlowDocument Build(string text, FontFamily font, Func<int, bool?> flip)
    {
        var document = new FlowDocument
        {
            FontFamily = font, FontSize = 14, Foreground = Brushes.Black, Background = Brushes.White,
            PagePadding = new Thickness(14, 10, 14, 10), TextAlignment = TextAlignment.Left,
        };
        try
        {
            var renderer = new Renderer(text, flip);
            renderer.AddBlocks(ScratchpadMarkdown.Parse(text), document.Blocks, 0);
        }
        catch (Exception e)
        {
            Orbit.Core.Log.Warn($"Scratchpad preview failed: {e.GetType().Name}");
            document.Blocks.Clear();
            document.Blocks.Add(Literal(text));
        }
        return document;
    }

    private static Paragraph Literal(string text)
    {
        var paragraph = new Paragraph();
        AddLines(paragraph.Inlines, text);
        return paragraph;
    }

    private static void AddLines(InlineCollection into, string text)
    {
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) into.Add(new LineBreak());
            into.Add(new Run(lines[i]));
        }
    }

    private sealed class Renderer(string source, Func<int, bool?> flip)
    {
        public void AddBlocks(Md.ContainerBlock blocks, BlockCollection into, int depth)
        {
            foreach (var block in blocks)
                if (Convert(block, depth) is { } converted) into.Add(converted);
        }

        private Block? Convert(Md.Block block, int depth)
        {
            if (depth > MaxDepth) return Literal(SourceOf(block));
            switch (block)
            {
                case Md.HeadingBlock heading:
                    var title = new Paragraph
                    {
                        FontSize = HeadingSizes[Math.Clamp(heading.Level, 1, HeadingSizes.Length) - 1],
                        FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, depth == 0 && heading.Line == 0 ? 0 : 12, 0, 6),
                    };
                    AddInlines(heading.Inline, title.Inlines, 0);
                    return title;
                case Md.ParagraphBlock text:
                    var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                    AddInlines(text.Inline, paragraph.Inlines, 0);
                    return paragraph;
                case Md.QuoteBlock quote:
                    var section = new Section
                    {
                        BorderBrush = Line, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 0, 0, 0),
                        Margin = new Thickness(0, 0, 0, 8), Foreground = Muted,
                    };
                    AddBlocks(quote, section.Blocks, depth + 1);
                    return section;
                case Md.ListBlock items:
                    return ListOf(items, depth);
                case Md.ThematicBreakBlock:
                    return new Paragraph { BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), FontSize = 2, Margin = new Thickness(0, 6, 0, 10) };
                case Md.LeafBlock { Inline: null } leaf when leaf is Md.CodeBlock or Md.HtmlBlock:
                    var code = new Paragraph { FontFamily = CodeFont, FontSize = 13, Background = CodeFill, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 8) };
                    AddLines(code.Inlines, leaf.Lines.ToString());
                    return code;
                case Md.LinkReferenceDefinitionGroup:
                    return null;
                case Md.ContainerBlock container:
                    var group = new Section();
                    AddBlocks(container, group.Blocks, depth + 1);
                    return group;
                default:
                    return Literal(SourceOf(block));
            }
        }

        private List ListOf(Md.ListBlock items, int depth)
        {
            bool allTasks = items.OfType<Md.ListItemBlock>().All(StartsWithBox);
            var list = new List
            {
                MarkerStyle = allTasks ? TextMarkerStyle.None : items.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                // A list of boxes has no marker to line up with, but one inside a list still steps in.
                Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(allTasks && items.Parent is not Md.ListItemBlock ? 2 : 22, 0, 0, 0),
            };
            if (items.IsOrdered && int.TryParse(items.OrderedStart, out int start) && start > 0) list.StartIndex = start;
            foreach (var item in items.OfType<Md.ListItemBlock>())
            {
                var row = new ListItem();
                AddBlocks(item, row.Blocks, depth + 1);
                foreach (var part in row.Blocks) part.Margin = new Thickness(0, 0, 0, 2);
                list.ListItems.Add(row);
            }
            return list;
        }

        private static bool StartsWithBox(Md.ListItemBlock item) =>
            item.Count > 0 && item[0] is Md.ParagraphBlock { Inline.FirstChild: TaskList box } && ScratchpadMarkdown.IsTaskBox(box);

        private void AddInlines(MdInline.ContainerInline? container, InlineCollection into, int depth)
        {
            if (container is null) return;
            foreach (var inline in container)
                if (Convert(inline, depth) is { } converted) into.Add(converted);
        }

        private Inline? Convert(MdInline.Inline inline, int depth)
        {
            if (depth > MaxDepth) return new Run(SourceOf(inline));
            switch (inline)
            {
                case MdInline.LiteralInline literal:
                    return new Run(literal.Content.ToString());
                case TaskList box:
                    return ScratchpadMarkdown.IsTaskBox(box) ? BoxOf(box) : new Run(SourceOf(box));
                case MdInline.EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
                    else if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeights.SemiBold;
                    else span.FontStyle = FontStyles.Italic;
                    AddInlines(emphasis, span.Inlines, depth + 1);
                    return span;
                case MdInline.CodeInline code:
                    return new Run(code.Content) { FontFamily = CodeFont, Background = CodeFill };
                case MdInline.LineBreakInline:
                    return new LineBreak();
                case MdInline.AutolinkInline auto:
                    return LinkOf(auto.Url, new Run(auto.Url));
                case MdInline.LinkInline { IsImage: true } image:
                    var alt = new Span();
                    AddInlines(image, alt.Inlines, depth + 1);
                    return alt.Inlines.Count > 0 ? alt : new Run(image.Url ?? "");
                case MdInline.LinkInline link:
                    var words = new Span();
                    AddInlines(link, words.Inlines, depth + 1);
                    if (words.Inlines.Count == 0) words.Inlines.Add(new Run(link.Url ?? ""));
                    return LinkOf(link.Url, words);
                case MdInline.HtmlInline html:
                    return new Run(html.Tag);
                case MdInline.HtmlEntityInline entity:
                    return new Run(entity.Transcoded.ToString());
                case MdInline.ContainerInline other:
                    var inner = new Span();
                    AddInlines(other, inner.Inlines, depth + 1);
                    return inner;
                default:
                    return new Run(SourceOf(inline));
            }
        }

        private InlineUIContainer BoxOf(TaskList box)
        {
            int at = box.Span.Start;
            var check = new CheckBox { IsChecked = box.Checked, Focusable = false, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = System.Windows.Input.Cursors.Hand };
            check.Click += (_, _) => check.IsChecked = flip(at) ?? check.IsChecked;
            return new InlineUIContainer(check) { BaselineAlignment = BaselineAlignment.Center };
        }

        private static Inline LinkOf(string? url, Inline words)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return words;
            var link = new Hyperlink(words) { Foreground = Brushes.Black, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = uri.AbsoluteUri };
            link.Click += (_, _) =>
            {
                try
                {
                    Shell.Open(uri.AbsoluteUri);
                }
                catch (Exception e)
                {
                    Orbit.Core.Log.Warn($"Scratchpad link did not open: {e.GetType().Name}");
                    Toast.Show(Strings.ScratchpadLinkFailed);
                }
            };
            return link;
        }

        private string SourceOf(Md.MarkdownObject item)
        {
            int start = Math.Clamp(item.Span.Start, 0, source.Length), end = Math.Clamp(item.Span.End + 1, start, source.Length);
            return source[start..end];
        }
    }
}
