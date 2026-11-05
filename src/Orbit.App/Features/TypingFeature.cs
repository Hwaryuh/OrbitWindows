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

    private readonly WINEVENTPROC _focusCallback;
    private AppServices? _services;
    private TypingWorker? _worker;
    private HWINEVENTHOOK _focusHook;

    public TypingFeature() => _focusCallback = (_, _, _, _, _, _, _) => _worker?.FocusMoved();

    private static string Folder => Path.Combine(AppContext.BaseDirectory, "araso");

    /// <summary>Whether this build carries the engine.</summary>
    public bool Bundled { get; } = File.Exists(Path.Combine(Folder, "ArasoBridge.dll"));

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
        services.Preferences.Changed += key =>
        {
            if (key == Pref.TypingEnabled) Apply();
            else if (key is Pref.TypingExcludedApps or Pref.TypingInTerminals) _worker?.FrontMoved();
        };
        services.Foreground.Changed += () => _worker?.FrontMoved();
        Apply();
    }

    public void Dispose()
    {
        if (!_focusHook.IsNull) PInvoke.UnhookWinEvent(_focusHook);
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
        // Only while on: focus events are frequent.
        if (on && _focusHook.IsNull)
            _focusHook = PInvoke.SetWinEventHook(EventObjectFocus, EventObjectFocus, HMODULE.Null, _focusCallback, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
        else if (!on && !_focusHook.IsNull)
        {
            PInvoke.UnhookWinEvent(_focusHook);
            _focusHook = default;
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
        private const ushort LanguageKorean = 0x0412, LanguageUsEnglish = 0x0409;

        private readonly ImeMode _ime = new();

        public nint FocusWindow => ImeMode.FocusWindow(out _);

        public bool ShiftDown => OrbitInput.IsDown(HotkeyMatcher.VkShift);

        public bool ShortcutDown => OrbitInput.IsDown(HotkeyMatcher.VkControl) || OrbitInput.IsDown(HotkeyMatcher.VkMenu)
            || OrbitInput.IsDown(HotkeyMatcher.VkLWin) || OrbitInput.IsDown(HotkeyMatcher.VkRWin);

        public TypingPlace Place()
        {
            string process = services.Foreground.ForegroundProcess;
            string className = ClassName(PInvoke.GetForegroundWindow());
            var language = ImeMode.Language(ImeMode.FocusWindow(out _));
            bool usKeys = language is LanguageKorean or LanguageUsEnglish;
            bool chromium = className.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal);
            var prefs = services.Preferences;
            TypingPause pause;
            // Orbit's own windows read as "", as does a program it may not open (an elevated one).
            if (process.Length == 0) pause = TypingPause.OwnWindow;
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

        private static string ClassName(HWND window)
        {
            Span<char> name = stackalloc char[64];
            return new string(name[..PInvoke.GetClassName(window, name)]);
        }
    }
}
