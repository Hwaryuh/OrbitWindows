using static Orbit.App.SelfTestParts;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orbit.App.Features;
using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.Input;
using Orbit.Core.Settings;

namespace Orbit.App.Settings;

/// <summary>
/// <c>--selftest-shortcuts [--render file.png]</c>: the real page and matcher with a temporary
/// settings file, in a window outside every monitor. The hooks don't run: key presses are handed to
/// the matcher, so nothing is typed and the user's settings aren't touched.
/// </summary>
internal static class ShortcutsSelfTest
{
    private const uint K = 0x4B, J = 0x4A, Delete = 0x2E, Escape = 0x1B;
    private const Modifiers CtrlAlt = Modifiers.Ctrl | Modifiers.Alt;

    private sealed class Held(params uint[] down) : IKeyState
    {
        public bool IsDown(uint vk) => down.Contains(vk);
    }

    private static readonly Held Chord = new(HotkeyMatcher.VkLControl, HotkeyMatcher.VkLMenu), Nothing = new();

    public static bool Run(string? render)
    {
        var folder = NewFolder();
        var results = new SelfTestLog("Shortcuts");
        var preferences = new Preferences(Path.Combine(folder, "settings.json"), Pref.Defaults());
        var hooks = new HookThread();
        var foreground = new ForegroundTracker();
        try
        {
            var services = new AppServices(preferences, new ActionRunner(), hooks, foreground, (_, _) => { });
            var recorder = new ShortcutsFeature();
            recorder.Start(services);
            int ran = 0;
            services.Actions.Register(OrbitAction.Scratchpad, () => ran++);
            services.Actions.Register(OrbitAction.DarkMode, () => { });
            services.Actions.Register(OrbitAction.Translate, () => { });
            var original = new Hotkey(CtrlAlt, 0x54);
            services.Shortcuts.Declare(new Hotkey(CtrlAlt, HotkeyMatcher.VkSpace), key: Orbit.Core.Input.ShortcutRegistry.Ring);
            int translate = services.Shortcuts.Declare(original, OrbitAction.Translate);
            services.Shortcuts.PassesToFront = _ => false;
            services.Shortcuts.Apply();
            var matcher = hooks.Hotkeys;

            int Press(uint key, IKeyState held)
            {
                var decision = matcher.KeyDown(key, held);
                matcher.KeyUp(key);
                if (decision.Pressed != 0) recorder.OnHook(new HookEvent(HookNotice.HotkeyDown, Hotkey: decision.Pressed));
                return decision.Pressed;
            }

            results.Check("a default is in place", Press(0x54, Chord) == translate);
            results.Check("an action without keys has none", services.Shortcuts.For(OrbitAction.Scratchpad) is null);

            var page = new ShortcutsPage(services);
            var host = new ScrollViewer { Content = new Border { Padding = new Thickness(24), Child = page } };
            var window = new Window
            {
                Left = -32000, Top = -32000, Width = 720, Height = 560, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
                Background = Brushes.White, Content = host,
            };
            window.Show();
            Pump();

            // The rows: the ring first, then Translate, Scratchpad and DarkMode in the groups they fall in.
            void ClickChange(OrbitAction action)
            {
                var button = All<Button>(page).Where(b => (b.Content as string) == Strings.ShortcutChange).ElementAt(RowsInPageOrder(services).IndexOf(action.Id()));
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
                Pump();
            }

            ClickChange(OrbitAction.Scratchpad);
            results.Check("a recording shows the prompt", Texts(page).Contains(Strings.ShortcutPress));
            Press(K, Chord);
            Pump();
            results.Check("the pressed keys are taken", services.Shortcuts.For(OrbitAction.Scratchpad) == new Hotkey(CtrlAlt, K));
            results.Check("the page shows them and a reset", Texts(page).Contains("K") && !Texts(page).Contains(Strings.ShortcutPress));
            results.Check("the new keys run the action", Press(K, Chord) != 0 && ran == 1);
            results.Check("the recording is over", HotkeyMatcher.Recorded(Press(J, Chord)) is null);

            ClickChange(OrbitAction.DarkMode);
            Press(K, Chord);
            Pump();
            results.Check("keys another action holds are refused", services.Shortcuts.For(OrbitAction.DarkMode) is null);
            results.Check("and the page says which", Texts(page).Contains(Strings.ShortcutInUse(OrbitAction.Scratchpad.Title())));

            ClickChange(OrbitAction.DarkMode);
            Press(0x41, Nothing);
            Pump();
            results.Check("a bare letter is refused", services.Shortcuts.For(OrbitAction.DarkMode) is null && Texts(page).Contains(Strings.ShortcutNeedsModifier));

            ClickChange(OrbitAction.Translate);
            Press(Escape, Nothing);
            Pump();
            results.Check("Esc cancels", services.Shortcuts.For(OrbitAction.Translate) == original);

            ClickChange(OrbitAction.Translate);
            Press(Delete, Nothing);
            Pump();
            results.Check("Delete removes", services.Shortcuts.For(OrbitAction.Translate) is null && Press(0x54, Chord) == 0);

            services.Shortcuts.Reset(OrbitAction.Translate.Id());
            results.Check("reset brings the default back", services.Shortcuts.For(OrbitAction.Translate) == original && Press(0x54, Chord) == translate);

            // Every way out of a recording leaves the shortcuts working.
            ClickChange(OrbitAction.DarkMode);
            results.Check("keys are recorded during a recording", HotkeyMatcher.Recorded(matcher.KeyDown(J, Chord).Pressed) is not null);
            matcher.KeyUp(J);
            window.Content = null;
            Pump();
            results.Check("leaving the page ends a recording", Press(0x54, Chord) == translate);

            window.Content = host;
            Pump();
            ClickChange(OrbitAction.DarkMode);
            recorder.OnHook(new HookEvent(HookNotice.HooksReinstalled));
            Pump();
            results.Check("the hooks coming back ends a recording", !Texts(page).Contains(Strings.ShortcutPress));

            var saved = ShortcutMap.Parse(preferences.Get<string>(Pref.Shortcuts));
            results.Check("only the difference is stored", saved.Count == 1 && saved[OrbitAction.Scratchpad.Id()] == new Hotkey(CtrlAlt, K));

            if (render is not null) Render(window, render);

            window.Close();
        }
        catch (Exception e)
        {
            results.Check($"no exception ({e.GetType().Name} at {e.TargetSite?.Name})", false);
        }
        finally
        {
            foreground.Dispose();
            preferences.Dispose();
            TryDeleteFolder(folder, "Shortcuts");
        }
        return results.Finish();
    }


    /// <summary>The places in the order the page shows them: the ring, then each group as it first appears.</summary>
    private static List<string> RowsInPageOrder(AppServices services)
    {
        var rows = new List<string> { Orbit.Core.Input.ShortcutRegistry.Ring };
        rows.AddRange(services.Shortcuts.Keys().Select(OrbitActions.FromId).OfType<OrbitAction>().GroupBy(a => a.GroupTitle()).SelectMany(g => g).Select(a => a.Id()));
        return rows;
    }

    private static List<string> Texts(DependencyObject root)
    {
        var texts = All<TextBlock>(root).Select(t => t.Text).ToList();
        return texts;
    }
}
