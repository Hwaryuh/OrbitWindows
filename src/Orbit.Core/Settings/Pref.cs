namespace Orbit.Core.Settings;

/// <summary>
/// Preference keys and defaults, Orbit's own (Core/Storage.swift, <c>Pref</c>) where the feature
/// is in this port. Keys of excluded features (window snapping, menu bar, notch, fans, battery)
/// are left out.
/// </summary>
public static class Pref
{
    public const string HistoryLimit = "clipboard.historyLimit";
    public const string IgnoredApps = "clipboard.ignoredApps";
    public const string PausedUntil = "clipboard.pausedUntil";
    public const string PasteDirectly = "clipboard.pasteDirectly";
    public const string CaptureSaveFolder = "capture.saveFolder";
    public const string CaptureAutoCopy = "capture.autoCopy";
    public const string CaptureSound = "capture.sound";
    public const string CaptureThumbnailSeconds = "capture.thumbnailSeconds";
    public const string CaptureThumbnailCorner = "capture.thumbnailCorner";
    public const string CaptureKeyHints = "capture.keyHints";
    /// <summary>The editor floats over other apps' windows (Orbit: EditorWindow.floatingKey).</summary>
    public const string CaptureEditorFloating = "capture.editorFloating";
    /// <summary>[x, y, width, height] in virtual-screen physical pixels, and the monitor's device name.</summary>
    public const string CaptureLastArea = "capture.lastArea";
    public const string TimedCaptureSeconds = "capture.timerSeconds";
    public const string Onboarded = "app.onboarded";
    public const string LaunchAtSignIn = "app.launchAtSignIn";
    public const string AwakeDisplay = "awake.keepDisplayOn";
    public const string AwakeBatteryFloor = "awake.batteryFloor";
    /// <summary>The running session, so it comes back if Orbit restarts (Orbit: KeepAwake.resumeKey).</summary>
    public const string AwakeResume = "awake.resume";
    public const string ShelfEdge = "shelf.edgeReveal";
    public const string TranslateDoubleCopy = "translate.doubleCopy";
    public const string TranslateInterval = "translate.interval";
    public const string TranslateEngine = "translate.engine";
    public const string TranslatePrimary = "translate.primary";
    public const string TranslateSecondary = "translate.secondary";
    public const string TranslateFormality = "translate.formality";
    public const string TranslateFontSize = "translate.fontSize";
    public const string TranslateShowSource = "translate.showSource";
    public const string TranslateCloseOnBlur = "translate.closeOnBlur";
    /// <summary>The DeepL key, DPAPI-protected for the current user (Orbit keeps it in the Keychain).</summary>
    public const string TranslateDeepLKey = "translate.deeplKey";
    public const string SpellEnabled = "spell.enabled";
    public const string SpellTripleCopy = "spell.tripleCopy";
    public const string SpellWebConsent = "spell.web.consent";
    public const string SpellCombine = "spell.combine";
    public const string SpellCloseAfterReplace = "spell.closeAfterReplace";
    /// <summary>Clean URL (from Vorssaint's behavior): every copied tracking link cleaned at once.</summary>
    public const string CleanUrlAuto = "cleanUrl.auto";
    /// <summary>Names the user added; removed everywhere.</summary>
    public const string CleanUrlAdded = "cleanUrl.added";
    /// <summary>Built-in names the user switched off ("utm_*" for the prefix).</summary>
    public const string CleanUrlDisabled = "cleanUrl.disabled";
    /// <summary>What the last automatic clean removed, for the settings page.</summary>
    public const string CleanUrlLastRemoved = "cleanUrl.lastRemoved";
    /// <summary>Snippets expand as they're typed (Orbit: SnippetStore keys).</summary>
    public const string SnippetsEnabled = "snippets.enabled";
    /// <summary>"immediate" or "delimiter".</summary>
    public const string SnippetsTrigger = "snippets.trigger";
    public const string SnippetsExcludedApps = "snippets.excludedApps";
    public const string SnippetsInTerminals = "snippets.inTerminals";
    /// <summary>The screen the brightness actions change: "pointer", "main" or "all" (Orbit: displays.keyTarget).</summary>
    public const string DisplaysTarget = "displays.keyTarget";
    /// <summary>Orbit's brightness bar after a change (Orbit: displays.osd).</summary>
    public const string DisplaysHud = "displays.osd";
    /// <summary>The user's brightness presets, as JSON.</summary>
    public const string DisplaysPresets = "displays.presets";
    /// <summary>The scratchpad stays on top and open when another app is clicked.</summary>
    public const string ScratchpadPinned = "scratchpad.pinned";
    /// <summary>Tabs unedited for this many days close when the pad opens; 0 keeps them.</summary>
    public const string ScratchpadClearDays = "scratchpad.clearDays";
    public const string RingMouse = "ring.mouse";
    public const string RingMouseButton = "ring.mouseButton";
    public const string RingSlots = "ring.slots";
    public const string RingHover = "ring.hover";
    public const string RingDwell = "ring.dwell";

    public static IReadOnlyDictionary<string, object?> Defaults() => new Dictionary<string, object?>
    {
        [HistoryLimit] = 500,
        [PasteDirectly] = true,
        [CaptureAutoCopy] = true,
        [CaptureSound] = true,
        [CaptureThumbnailSeconds] = 8.0,
        [CaptureThumbnailCorner] = "bottomRight",
        [CaptureKeyHints] = true,
        [CaptureEditorFloating] = true,
        [TimedCaptureSeconds] = 5,
        [LaunchAtSignIn] = true,
        [AwakeDisplay] = true,
        [AwakeBatteryFloor] = 20,
        [AwakeResume] = "",
        [ShelfEdge] = true,
        [TranslateDoubleCopy] = true,
        [TranslateInterval] = 0.4,
        [TranslateEngine] = "google",
        [TranslatePrimary] = "ko",
        [TranslateSecondary] = "en",
        [TranslateFormality] = "auto",
        [TranslateFontSize] = 16.0,
        [TranslateShowSource] = true,
        [TranslateCloseOnBlur] = true,
        [TranslateDeepLKey] = "",
        [SpellEnabled] = true,
        [SpellTripleCopy] = false,
        [SpellWebConsent] = false,
        [SpellCombine] = false,
        [SpellCloseAfterReplace] = true,
        [CleanUrlAuto] = false,
        [CleanUrlAdded] = Array.Empty<string>(),
        [CleanUrlDisabled] = Array.Empty<string>(),
        [CleanUrlLastRemoved] = Array.Empty<string>(),
        [SnippetsEnabled] = true,
        [SnippetsTrigger] = "immediate",
        // Password managers, as Orbit's list by bundle ID.
        [SnippetsExcludedApps] = new[] { "1password.exe", "bitwarden.exe", "keepass.exe", "keepassxc.exe", "dashlane.exe", "enpass.exe", "lastpass.exe" },
        [SnippetsInTerminals] = true,
        [DisplaysTarget] = "pointer",
        [DisplaysHud] = true,
        [DisplaysPresets] = "",
        [ScratchpadPinned] = false,
        [ScratchpadClearDays] = 0,
        [RingMouse] = true,
        [RingMouseButton] = "middle",
        [RingHover] = true,
        [RingDwell] = 0.45,
        // Windows has no Desktop default for screenshots: Pictures\Screenshots, as Snipping Tool uses.
        [CaptureSaveFolder] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"),
        // Process names, lowercase. Password managers, as Orbit's bundle-ID list.
        [IgnoredApps] = new[] { "1password.exe", "bitwarden.exe", "keepass.exe", "keepassxc.exe", "dashlane.exe" },
    };
}
