using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orbit.Core.Actions;

/// <summary>
/// Every Orbit capability as one action, shared by the ring, the launcher, the tray and the
/// shortcuts (Orbit: Core/OrbitAction.swift). The camelCase name is the id stored in settings
/// (<c>ring.slots</c>), the same as Orbit's raw values. Excluded features (window snapping,
/// passwords, notch, menu bar, fans, battery) are not here. Titles live in the app, which owns
/// the visible text.
/// </summary>
[JsonConverter(typeof(OrbitActionJsonConverter))]
public enum OrbitAction
{
    CaptureArea, CaptureWindow, CaptureScreen, CaptureText, CaptureRepeat, CaptureScrolling, CaptureTimed,
    RecordArea, RecordWindow, RecordScreen, StopRecording, RecentCaptures, PinLastCapture, ColorPicker,
    Clipboard, Snippets,
    Launcher, Apps, Displays, KeepAwake, Settings, Shelf, Translate, SpellCheck, CheckUpdates,
    MediaPlayPause, MediaNext, MediaPrevious,
    LockScreen, SleepDisplay, DarkMode, DesktopIcons, HiddenFiles, EmptyTrash, EjectAll, CleanKeyboard,
}

public enum ActionGroup { Capture, Clipboard, Orbit, System, Media }

public static class OrbitActions
{
    public static IReadOnlyList<OrbitAction> All { get; } = Enum.GetValues<OrbitAction>();

    /// <summary>The id in settings and in Orbit's files: <c>captureArea</c>.</summary>
    public static string Id(this OrbitAction action) => JsonNamingPolicy.CamelCase.ConvertName(action.ToString());

    public static OrbitAction? FromId(string id) =>
        All.Cast<OrbitAction?>().FirstOrDefault(a => a!.Value.Id() == id);

    public static ActionGroup Group(this OrbitAction action) => action switch
    {
        OrbitAction.CaptureArea or OrbitAction.CaptureWindow or OrbitAction.CaptureScreen or OrbitAction.CaptureText
            or OrbitAction.CaptureRepeat or OrbitAction.CaptureScrolling or OrbitAction.CaptureTimed or OrbitAction.RecordArea
            or OrbitAction.RecordWindow or OrbitAction.RecordScreen or OrbitAction.StopRecording or OrbitAction.RecentCaptures
            or OrbitAction.PinLastCapture or OrbitAction.ColorPicker => ActionGroup.Capture,
        OrbitAction.Clipboard or OrbitAction.Snippets => ActionGroup.Clipboard,
        OrbitAction.MediaPlayPause or OrbitAction.MediaNext or OrbitAction.MediaPrevious => ActionGroup.Media,
        OrbitAction.LockScreen or OrbitAction.SleepDisplay or OrbitAction.DarkMode or OrbitAction.DesktopIcons
            or OrbitAction.HiddenFiles or OrbitAction.EmptyTrash or OrbitAction.EjectAll or OrbitAction.CleanKeyboard => ActionGroup.System,
        _ => ActionGroup.Orbit,
    };

    /// <summary>Search words for the launcher, English and Korean, as Orbit's (Mac app names swapped for Windows ones).</summary>
    public static string Keywords(this OrbitAction action)
    {
        var specific = action switch
        {
            OrbitAction.ColorPicker => "color picker eyedropper hex rgb 컬러 색상 스포이드",
            OrbitAction.Apps => "apps applications start menu grid all programs 앱 응용 프로그램 시작 메뉴 모든 앱",
            OrbitAction.Displays => "display monitor brightness contrast volume ddc external screen dim twinkle tray 모니터 디스플레이 밝기 대비 음량 화면 어둡게 외장 모니터",
            OrbitAction.CaptureTimed => "timer delay countdown 타이머 지연",
            OrbitAction.CaptureScrolling => "scrolling screenshot long page full page stitch 스크롤 캡처 긴 화면 전체 페이지",
            OrbitAction.RecentCaptures => "recent captures library gallery screenshots recordings attach history 최근 캡처 갤러리 스크린샷 모음 첨부",
            OrbitAction.RecordArea or OrbitAction.RecordWindow or OrbitAction.RecordScreen or OrbitAction.StopRecording =>
                "record recording video screen recorder mp4 gif sharex 녹화 화면 녹화 동영상 영상",
            OrbitAction.KeepAwake => "awake caffeine sleep powertoys awake 잠자기 방지 카페인",
            OrbitAction.Shelf => "shelf drop files stash 선반 파일",
            OrbitAction.Translate => "translate deepl papago google 번역 번역기",
            OrbitAction.SpellCheck => "spell spelling check grammar proofread typo spacing 맞춤법 검사 띄어쓰기 문법 교정 오타 맞춤법 검사기",
            OrbitAction.Snippets => "snippet snippets text expander expansion abbreviation keyword template 스니펫 상용구 자동 완성 텍스트 확장",
            OrbitAction.LockScreen => "lock 화면 잠금",
            OrbitAction.DarkMode => "dark light mode theme 다크모드 라이트모드",
            OrbitAction.DesktopIcons => "desktop icons hide show 바탕 화면 아이콘",
            OrbitAction.HiddenFiles => "hidden files explorer show hide 숨김 파일 탐색기",
            OrbitAction.MediaPlayPause or OrbitAction.MediaNext or OrbitAction.MediaPrevious =>
                "music song play pause next previous skip spotify now playing 음악 노래 재생 일시정지 다음 이전",
            OrbitAction.CleanKeyboard => "clean keyboard wipe disable keys lock keyboard 키보드 청소 닦기 키 잠그기",
            OrbitAction.CheckUpdates => "update updates upgrade new version release download 업데이트 새 버전 최신 버전 릴리즈",
            _ => "",
        };
        var group = action.Group() switch
        {
            ActionGroup.Capture => "screenshot capture snap grab image ocr text 캡처 스크린샷",
            ActionGroup.Clipboard => "clipboard history paste copy 클립보드",
            ActionGroup.System => "system windows lock sleep dark appearance explorer desktop recycle bin eject 시스템 잠금 다크모드 휴지통",
            _ => "orbit",
        };
        return $"{group} {specific}".Trim();
    }
}

/// <summary>Reads and writes an action as its id (<c>"captureArea"</c>); an unknown id reads as an error.</summary>
public sealed class OrbitActionJsonConverter : JsonConverter<OrbitAction>
{
    public override OrbitAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        OrbitActions.FromId(reader.GetString() ?? "") ?? throw new JsonException($"Unknown action '{reader.GetString()}'");

    public override void Write(Utf8JsonWriter writer, OrbitAction value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Id());
}
