using static Orbit.App.SelfTestParts;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orbit.Core;
using Orbit.Core.Scratchpad;
using Orbit.Core.Settings;

namespace Orbit.App.Features.Scratchpad;

/// <summary>
/// <c>--selftest-scratchpad [--render file.png]</c>: the real pad with a temporary file, in a
/// window outside every monitor that never takes focus. Checks what the unit tests can't: a tab
/// that opens in preview has undo, a box flips as one step, and links and hostile notes stay safe.
/// Runs before the hooks and the tray, so nothing shows and the user's own notes aren't touched.
/// </summary>
internal static class ScratchpadSelfTest
{
    private const string Sample =
        "# 오늘 할 일\r\n\r\nSome **bold**, *italic*, ~~gone~~ and `code`.\r\nA second line.\r\n\r\n" +
        "- [ ] 첫째\r\n  - [x] nested\r\n- [ ] 셋째 [ ] not a box\r\n\r\n> - [ ] quoted\r\n\r\n" +
        "1. one\r\n2. two\r\n\r\n---\r\n\r\n```\r\ncode block\r\n```\r\n\r\n" +
        "[safe](https://example.com) [unsafe](file:///C:/Windows/notepad.exe) ![pic](https://example.com/a.png) <b>raw</b>\r\n";

    public static bool Run(Preferences preferences, string? render)
    {
        var folder = NewFolder();
        var results = new SelfTestLog("Scratchpad");
        try
        {
            var store = new ScratchpadStore(Path.Combine(folder, "scratchpad.json"));
            var document = store.Load(Strings.ScratchpadTabName, DateTimeOffset.Now);
            document.Update(document.Selected, p => p with { Text = Sample, Preview = true });
            var window = new ScratchpadWindow(store, document, preferences)
            {
                Left = -32000, Top = -32000, ShowActivated = false,
            };
            window.Show();
            Pump();

            var viewer = All<FlowDocumentScrollViewer>(window).Single();
            var editor = All<TextBox>(window).Single(b => b.AcceptsReturn);
            results.Check("a tab that opens in preview shows the preview", viewer.IsVisible && editor.Visibility == Visibility.Hidden);

            var boxes = All<CheckBox>(viewer.Document).ToList();
            results.Check("only real task boxes become boxes", boxes.Count == 4);
            results.Check("box states follow the source", boxes.Select(b => b.IsChecked == true).SequenceEqual([false, true, false, false]));
            results.Check("only http and https links open", All<Hyperlink>(viewer.Document).Count() == 1);
            results.Check("pictures are never fetched", !All<Image>(viewer.Document).Any());

            var shown = viewer.Document;
            Click(boxes[0]);
            results.Check("a click flips the first box in the source", document.Current.Text == Flipped(Sample, "- [ ] 첫째", "- [x] 첫째"));
            results.Check("the box shows the new state with no rebuild", boxes[0].IsChecked == true && viewer.Document == shown);
            Click(boxes[1]);
            results.Check("a nested box flips with CRLF before it", document.Current.Text.Contains("  - [ ] nested") && document.Current.Text.Contains("- [x] 첫째"));
            Click(boxes[3]);
            results.Check("a quoted box flips", document.Current.Text.Contains("> - [x] quoted"));

            editor.Undo();
            editor.Undo();
            results.Check("each flip is one undo step", document.Current.Text == Flipped(Sample, "- [ ] 첫째", "- [x] 첫째"));
            results.Check("undo rebuilds the preview", viewer.Document != shown && All<CheckBox>(viewer.Document).First().IsChecked == true);
            editor.Undo();
            results.Check("undo returns to the original", document.Current.Text == Sample);
            editor.Redo();
            results.Check("redo flips again", document.Current.Text == Flipped(Sample, "- [ ] 첫째", "- [x] 첫째"));

            // Text changed under a stale box: nothing edited, and the preview is rebuilt.
            var stale = All<CheckBox>(viewer.Document).First();
            var staleDocument = viewer.Document;
            editor.Text = "no boxes";
            Pump();
            Click(stale);
            results.Check("a stale box edits nothing", document.Current.Text == "no boxes" && viewer.Document != staleDocument);
            editor.Undo();
            Pump();

            foreach (var (name, text) in new[] { ("deep quote", new string('>', 5000) + "a"), ("deep list", string.Concat(Enumerable.Repeat("- ", 5000)) + "a"), ("nested 100", new string('>', 100) + " a"), ("brackets", new string('[', 50000)), ("empty", "") })
            {
                var built = ScratchpadPreview.Build(text, window.FontFamily, _ => null);
                results.Check($"hostile note builds: {name}", text.Length == 0 || built.Blocks.Count > 0);
            }

            if (render is not null)
            {
                Pump();
                Render(window, render);
            }

            string final = document.Current.Text;
            window.Quit();
            var back = new ScratchpadStore(store.Path).Load(Strings.ScratchpadTabName, DateTimeOffset.Now);
            results.Check("the file keeps the text and the preview state", back.Current.Text == final && back.Current.Preview);
        }
        catch (Exception e)
        {
            results.Check($"no exception ({e.GetType().Name} at {e.TargetSite?.Name})", false);
        }
        finally
        {
            TryDeleteFolder(folder, "Scratchpad");
        }
        return results.Finish();
    }

    private static string Flipped(string text, string from, string to) => text.Replace(from, to);

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
}
