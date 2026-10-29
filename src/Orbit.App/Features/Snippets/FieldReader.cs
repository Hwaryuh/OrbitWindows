using System.Windows.Automation;
using System.Windows.Automation.Text;
using Orbit.Core;

namespace Orbit.App.Features.Snippets;

/// <summary>The focused field as UI Automation shows it: the text just before the caret, and whether it is a password.</summary>
internal readonly record struct FieldView(string? BeforeCaret, bool IsPassword, bool HasSelection, int[]? Element);

/// <summary>
/// Reads the focused text field through UI Automation (TextPattern), on a worker with a short
/// limit: an app that never answers costs one worker, not the UI. Late answers are dropped by the
/// caller. Never logs what it read.
/// </summary>
internal static class FieldReader
{
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);
    private static int _running;

    /// <summary>Up to <paramref name="length"/> characters before the caret; null text when the field won't say or there is a selection.</summary>
    public static async Task<FieldView?> ReadAsync(int length)
    {
        // One read at a time: a stuck provider must not pile workers up.
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return null;
        var work = Task.Run(() =>
        {
            try
            {
                return Read(length);
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        });
        var done = await Task.WhenAny(work, Task.Delay(Limit));
        return done == work ? work.Result : null;
    }

    /// <summary>The focused element's id now, to see that focus didn't move since the read.</summary>
    public static async Task<int[]?> FocusedAsync()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return null;
        var work = Task.Run(() =>
        {
            try
            {
                return AutomationElement.FocusedElement?.GetRuntimeId();
            }
            catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                return null;
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        });
        var done = await Task.WhenAny(work, Task.Delay(Limit));
        return done == work ? work.Result : null;
    }

    private static FieldView? Read(int length)
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null) return null;
            var id = element.GetRuntimeId();
            bool password = element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty) is true;
            if (password) return new FieldView(null, true, false, id);
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return new FieldView(null, false, false, id);
            var selection = ((TextPattern)pattern).GetSelection();
            if (selection.Length != 1) return new FieldView(null, false, selection.Length > 1, id);
            var caret = selection[0];
            // A selection: Backspace would take it, not the keyword.
            if (caret.CompareEndpoints(TextPatternRangeEndpoint.Start, caret, TextPatternRangeEndpoint.End) != 0) return new FieldView(null, false, true, id);
            var range = caret.Clone();
            range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -length);
            return new FieldView(range.GetText(length + 2), false, false, id);
        }
        catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            Log.Info($"The focused field could not be read ({e.GetType().Name})");
            return null;
        }
    }
}
