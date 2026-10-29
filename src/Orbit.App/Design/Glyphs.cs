using System.Windows.Media;
using Orbit.Core.Actions;

namespace Orbit.App.Design;

/// <summary>
/// Icons from Windows 11's Segoe Fluent Icons (Segoe MDL2 Assets where it's missing), standing in
/// for Orbit's SF Symbols. Each code point was checked on a rendered sheet.
/// </summary>
internal static class Glyphs
{
    public static readonly FontFamily Font = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public const string Search = "";
    public const string Settings = "";
    public const string Calculator = "";
    public const string Globe = "";
    public const string Window = "";
    public const string Copy = "";
    public const string Enter = "";
    public const string Close = "";
    public const string Folder = "";
    public const string OpenInNew = "";
    public const string Keyboard = "";

    private static readonly Dictionary<OrbitAction, string> ForAction = new()
    {
        [OrbitAction.CaptureArea] = "",
        [OrbitAction.CaptureWindow] = "",
        [OrbitAction.CaptureScreen] = "",
        [OrbitAction.CaptureText] = "",
        [OrbitAction.CaptureRepeat] = "",
        [OrbitAction.CaptureScrolling] = "",
        [OrbitAction.CaptureTimed] = "",
        [OrbitAction.RecordArea] = "",
        [OrbitAction.RecordWindow] = "",
        [OrbitAction.RecordScreen] = "",
        [OrbitAction.StopRecording] = "",
        [OrbitAction.RecentCaptures] = "",
        [OrbitAction.PinLastCapture] = "",
        [OrbitAction.ColorPicker] = "",
        [OrbitAction.Clipboard] = "",
        [OrbitAction.Snippets] = "",
        [OrbitAction.Launcher] = Search,
        [OrbitAction.Apps] = "",
        [OrbitAction.Displays] = "",
        [OrbitAction.KeepAwake] = "",
        [OrbitAction.Settings] = Settings,
        [OrbitAction.Shelf] = "",
        [OrbitAction.Translate] = "",
        [OrbitAction.SpellCheck] = "",
        [OrbitAction.CheckUpdates] = "",
        [OrbitAction.MediaPlayPause] = "",
        [OrbitAction.MediaNext] = "",
        [OrbitAction.MediaPrevious] = "",
        [OrbitAction.LockScreen] = "",
        [OrbitAction.SleepDisplay] = "",
        [OrbitAction.DarkMode] = "",
        [OrbitAction.DesktopIcons] = "",
        [OrbitAction.HiddenFiles] = "",
        [OrbitAction.EmptyTrash] = "",
        [OrbitAction.EjectAll] = "",
        [OrbitAction.CleanKeyboard] = Keyboard,
    };

    public static string Of(OrbitAction action) => ForAction.GetValueOrDefault(action, "");
}
