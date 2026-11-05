using System.IO;
using Orbit.App.Features.Snippets;
using Orbit.Core;
using Orbit.Core.Clipboard;
using Orbit.Core.Input;
using Orbit.Core.Settings;
using Orbit.Core.Typing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Orbit.App.Features;

/// <summary>
/// Automatic Korean/English typing from Orbit's own process: the hook takes keys, the engine decides,
/// and the text goes in as plain typed text. Nothing to install. Off until the user turns it on.
/// </summary>
internal sealed class TypingFeature : IFeature
{
    private const uint EventObjectFocus = 0x8005;
    private const uint EventSystemForeground = 0x0003;

    private readonly WINEVENTPROC _focusCallback;
    private readonly WINEVENTPROC _frontCallback;
    private AppServices? _services;
    private TypingWorker? _worker;
    private HWINEVENTHOOK _focusHook;
    private HWINEVENTHOOK _frontHook;

    public TypingFeature()
    {
        _focusCallback = (_, _, _, _, _, _, _) => _worker?.FocusMoved();
        _frontCallback = (_, _, _, _, _, _, _) => _worker?.FrontMoved();
    }

    private static string Folder => Path.Combine(AppContext.BaseDirectory, "engine");

    /// <summary>Whether this build carries the engine.</summary>
    public bool Bundled { get; } = File.Exists(Path.Combine(Folder, EngineBridge.LibraryName));

    /// <summary>The engine couldn't be loaded.</summary>
    public bool Failed => _worker?.Failed ?? false;

    /// <summary>Keys are being taken right now. Any thread.</summary>
    public bool IsTaking => _worker?.Gate.Active ?? false;

    /// <summary>Failure or a change of state, on the UI thread.</summary>
    public event Action? Changed;

    public void Start(AppServices services)
    {
        _services = services;
        services.Typing = this;
        // Focus inside Orbit's own windows moves without a window event: WPF controls have no window of their own.
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.UIElement), System.Windows.Input.Keyboard.GotKeyboardFocusEvent,
            new System.Windows.Input.KeyboardFocusChangedEventHandler((_, _) => _worker?.FocusMoved()), handledEventsToo: true);
        services.Preferences.Changed += key =>
        {
            if (key == Pref.TypingEnabled) Apply();
            else if (key is Pref.TypingExcludedApps or Pref.TypingInTerminals) _worker?.FrontMoved();
        };
        Apply();
    }

    public void Dispose()
    {
        if (!_focusHook.IsNull) PInvoke.UnhookWinEvent(_focusHook);
        if (!_frontHook.IsNull) PInvoke.UnhookWinEvent(_frontHook);
        if (_services is not null) _services.Hooks.Typing = null;
        _worker?.Dispose();
    }

    private void Apply()
    {
        bool on = _services!.Preferences.Get<bool>(Pref.TypingEnabled) && Bundled;
        if (on && _worker is null)
        {
            var services = _services;
            string support = Path.Combine(AppPaths.RoamingFolder(), "typing-engine");
            _worker = new TypingWorker(() => EngineBridge.Start(Folder, support), new TypingHost(services));
            _worker.PauseChanged += OnPauseChanged;
            _worker.Start(true);
            services.Hooks.Typing = _worker;
        }
        else
        {
            _worker?.SetEnabled(on);
        }
        // Only while on: focus events are frequent. Every window that comes to the front, not only another
        // program: the foreground tracker says nothing between two windows it names alike, such as an
        // elevated program and Orbit's launcher.
        Follow(on, ref _focusHook, EventObjectFocus, _focusCallback);
        Follow(on, ref _frontHook, EventSystemForeground, _frontCallback);
    }

    private static void Follow(bool on, ref HWINEVENTHOOK hook, uint winEvent, WINEVENTPROC callback)
    {
        if (on && hook.IsNull)
        {
            hook = PInvoke.SetWinEventHook(winEvent, winEvent, HMODULE.Null, callback, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
            if (hook.IsNull) Log.Warn($"Typing could not follow window event 0x{winEvent:X}");
        }
        else if (!on && !hook.IsNull)
        {
            PInvoke.UnhookWinEvent(hook);
            hook = default;
        }
    }

    private void OnPauseChanged()
    {
        Log.Info($"Typing: {_worker?.Pause}");
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());
    }

    /// <summary>Windows and the settings, for the typing thread.</summary>
    private sealed class TypingHost(AppServices services) : ITypingHost
    {
        private const int ReadLimitMs = 60;
        private const ushort LanguageUsEnglish = 0x0409;

        private readonly ImeMode _ime = new();

        public nint FocusWindow => ImeMode.FocusWindow(out _);

        public bool ShiftDown => OrbitInput.IsDown(HotkeyMatcher.VkShift);

        public bool ShortcutDown => OrbitInput.IsDown(HotkeyMatcher.VkControl) || OrbitInput.IsDown(HotkeyMatcher.VkMenu)
            || OrbitInput.IsDown(HotkeyMatcher.VkLWin) || OrbitInput.IsDown(HotkeyMatcher.VkRWin);

        public TypingPlace Place()
        {
            // One reading of the window in front for all of it: the tracker's name may be from a window before.
            var front = PInvoke.GetForegroundWindow();
            uint id = ProcessOf(front);
            string process = Processes.FileName(id);
            string className = ClassName(front);
            var language = ImeMode.Language(ImeMode.FocusWindow(out _));
            // Letters go as keys only where no IME can catch them. On the Korean layout they go as Unicode:
            // Chrome's IME stays in Hangul mode through WM_IME_CONTROL and composes them (seen 2026-11-06).
            bool usKeys = language == LanguageUsEnglish;
            bool chromium = className.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal);
            var prefs = services.Preferences;
            TypingPause pause;
            // Orbit's own windows take typing too, the launcher above all, and the box decides as anywhere.
            // A program it may not open (an elevated one) also reads as "", and its keys can't be sent.
            if (id == (uint)Environment.ProcessId) pause = TypingPause.None;
            else if (front.IsNull || process.Length == 0) pause = TypingPause.OwnWindow;
            else if (className.StartsWith("Shell_", StringComparison.Ordinal)) pause = TypingPause.NotText;
            else if (ClipboardRules.IsIgnored(process, prefs.Get<string[]>(Pref.TypingExcludedApps))) pause = TypingPause.ExcludedApp;
            else if (!prefs.Get<bool>(Pref.TypingInTerminals) && Processes.Terminals.Contains(process)) pause = TypingPause.Terminal;
            else pause = TypingPause.None;
            return new TypingPlace(pause, usKeys, chromium, prefs.Get<bool>(Pref.TypingInTerminals));
        }

        public FieldSnapshot? ReadField(int length)
        {
            if (FieldReader.ReadWithin(length, ReadLimitMs) is not { } view) return null;
            ImeMode.FocusWindow(out bool hasCaret);
            return new FieldSnapshot(view.IsPassword, view.TakesText || hasCaret, view.BeforeCaret, view.HasSelection, view.Element, view.IsTerminal);
        }

        public bool HoldIme() => _ime.Hold();

        public void ReleaseIme() => _ime.Release();

        public bool Send(INPUT[] events) => KeyOutput.Send(events);

        public void Wait(int milliseconds) => Thread.Sleep(milliseconds);

        private static unsafe uint ProcessOf(HWND window)
        {
            if (window.IsNull) return 0;
            uint process = 0;
            PInvoke.GetWindowThreadProcessId(window, &process);
            return process;
        }

        private static string ClassName(HWND window)
        {
            Span<char> name = stackalloc char[64];
            return new string(name[..PInvoke.GetClassName(window, name)]);
        }
    }
}
