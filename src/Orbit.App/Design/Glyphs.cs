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

    /// <summary>Monospaced, for keywords, numbers and code.</summary>
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public const string Search = "\uE721";
    public const string Settings = "\uE713";
    public const string Volume = "\uE767";
    public const string Mute = "\uE74F";
    public const string Microphone = "\uE720";
    public const string MicOff = "\uEC54";
    public const string Calculator = "\uE8EF";
    public const string Globe = "\uE774";
    public const string Window = "\uE737";
    public const string Copy = "\uE8C8";
    public const string Enter = "\uE751";
    public const string Close = "\uE711";
    public const string Folder = "\uE8B7";
    public const string OpenInNew = "\uE8A7";
    public const string Keyboard = "\uE765";
    public const string Shield = "\uE7EF";
    public const string Currency = "\uE825";
    public const string Ruler = "\uECC6";
    public const string Clock = "\uE121";
    public const string Cloud = "\uE753";
    public const string Sun = "\uE706";
    public const string Document = "\uE8A5";
    public const string Preview = "\uE890";
    public const string Edit = "\uE70F";
    public const string Pointer = "\uE962";
    public const string Network = "\uE839";

    private static readonly Dictionary<OrbitAction, string> ForAction = new()
    {
        [OrbitAction.CaptureArea] = "\uE7A8",
        [OrbitAction.CaptureWindow] = "\uE737",
        [OrbitAction.CaptureScreen] = "\uE7F4",
        [OrbitAction.CaptureText] = "\uE8D2",
        [OrbitAction.CaptureRepeat] = "\uE81C",
        [OrbitAction.CaptureScrolling] = "\uE8A5",
        [OrbitAction.CaptureTimed] = "\uE916",
        [OrbitAction.RecordArea] = "\uE7C8",
        [OrbitAction.RecordWindow] = "\uE7C8",
        [OrbitAction.RecordScreen] = "\uE7C8",
        [OrbitAction.StopRecording] = "\uE71A",
        [OrbitAction.RecentCaptures] = "\uE8B9",
        [OrbitAction.PinLastCapture] = "\uE718",
        [OrbitAction.ColorPicker] = "\uE790",
        [OrbitAction.Clipboard] = "\uF0E3",
        [OrbitAction.Snippets] = "\uE8E4",
        [OrbitAction.CleanUrl] = "\uE71B",
        [OrbitAction.Launcher] = Search,
        [OrbitAction.Apps] = "\uF0E2",
        [OrbitAction.Displays] = "\uE706",
        [OrbitAction.KeepAwake] = "\uEA80",
        [OrbitAction.Settings] = Settings,
        [OrbitAction.Shelf] = "\uED25",
        [OrbitAction.Translate] = "\uF2B7",
        [OrbitAction.SpellCheck] = "\uE70F",
        [OrbitAction.CheckUpdates] = "\uE896",
        [OrbitAction.MediaPlayPause] = "\uE768",
        [OrbitAction.MediaNext] = "\uE893",
        [OrbitAction.MediaPrevious] = "\uE892",
        [OrbitAction.LockScreen] = "\uE72E",
        [OrbitAction.SleepDisplay] = "\uE708",
        [OrbitAction.DarkMode] = "\uE793",
        [OrbitAction.DesktopIcons] = "\uE8FC",
        [OrbitAction.HiddenFiles] = "\uE890",
        [OrbitAction.EmptyTrash] = "\uE74D",
        [OrbitAction.EjectAll] = "\uE88E",
        [OrbitAction.CleanKeyboard] = Keyboard,
        [OrbitAction.PortManager] = "\uE968",
        [OrbitAction.Scratchpad] = "\uE70B",
        [OrbitAction.BrightnessUp] = "\uE706",
        [OrbitAction.BrightnessDown] = "\uEC8A",
    };

    public static string Of(OrbitAction action) => ForAction.GetValueOrDefault(action, "\uE946");
}
