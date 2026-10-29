using Orbit.Core.Actions;

namespace Orbit.App;

/// <summary>
/// The names people see for each action: the full title (launcher, settings) and the short one
/// (the ring's hub). English and Korean from Orbit's own strings (Localizable.xcstrings); the
/// Windows wording where macOS's doesn't fit ("휴지통", "바탕 화면").
/// </summary>
internal static class ActionText
{
    private record struct Names(string English, string Korean, string ShortEnglish, string ShortKorean);

    private static readonly Dictionary<OrbitAction, Names> All = new()
    {
        [OrbitAction.CaptureArea] = new("Capture Area", "영역 캡처", "Capture", "캡처"),
        [OrbitAction.CaptureWindow] = new("Capture Window", "창 캡처", "Window Shot", "창 캡처"),
        [OrbitAction.CaptureScreen] = new("Capture Screen", "전체 화면 캡처", "Full Screen", "전체 화면"),
        [OrbitAction.CaptureText] = new("Copy Text from Screen", "화면 글자 복사", "Grab Text", "글자 복사"),
        [OrbitAction.CaptureRepeat] = new("Repeat Last Capture", "마지막 캡처 반복", "Repeat", "반복"),
        [OrbitAction.CaptureScrolling] = new("Capture Scrolling Area", "스크롤 캡처", "Scrolling", "스크롤"),
        [OrbitAction.CaptureTimed] = new("Capture Area with Timer", "타이머로 영역 캡처", "Timer Shot", "타이머 캡처"),
        [OrbitAction.RecordArea] = new("Record Area", "영역 녹화", "Record", "녹화"),
        [OrbitAction.RecordWindow] = new("Record Window", "창 녹화", "Record Window", "창 녹화"),
        [OrbitAction.RecordScreen] = new("Record Screen", "화면 녹화", "Record Screen", "화면 녹화"),
        [OrbitAction.StopRecording] = new("Stop Recording", "녹화 중지", "Stop", "중지"),
        [OrbitAction.RecentCaptures] = new("Recent Captures", "최근 캡처", "Captures", "캡처"),
        [OrbitAction.PinLastCapture] = new("Pin Last Capture", "마지막 캡처 고정", "Pin", "고정"),
        [OrbitAction.ColorPicker] = new("Pick Color from Screen", "화면에서 색 추출", "Pick Color", "색 추출"),
        [OrbitAction.Clipboard] = new("Clipboard History", "클립보드 기록", "Clipboard", "클립보드"),
        [OrbitAction.Snippets] = new("Search Snippets", "스니펫 찾기", "Snippets", "스니펫"),
        [OrbitAction.Launcher] = new("Open Launcher", "런처 열기", "Launcher", "런처"),
        [OrbitAction.Apps] = new("Apps", "앱", "Apps", "앱"),
        [OrbitAction.Displays] = new("Display Brightness", "디스플레이 밝기", "Brightness", "밝기"),
        [OrbitAction.KeepAwake] = new("Keep Awake", "잠자기 방지", "Awake", "잠자기 방지"),
        [OrbitAction.Settings] = new("Orbit Settings", "Orbit 설정", "Settings", "설정"),
        [OrbitAction.Shelf] = new("Shelf", "선반", "Shelf", "선반"),
        [OrbitAction.Translate] = new("Translate Clipboard", "클립보드 번역", "Translate", "번역"),
        [OrbitAction.SpellCheck] = new("Check Spelling", "맞춤법 검사", "Spelling", "맞춤법"),
        [OrbitAction.CheckUpdates] = new("Check for Updates", "업데이트 확인", "Updates", "업데이트"),
        [OrbitAction.MediaPlayPause] = new("Play/Pause", "재생/일시정지", "Play/Pause", "재생/일시정지"),
        [OrbitAction.MediaNext] = new("Next Song", "다음 곡", "Next Song", "다음 곡"),
        [OrbitAction.MediaPrevious] = new("Previous Song", "이전 곡", "Previous Song", "이전 곡"),
        [OrbitAction.LockScreen] = new("Lock Screen", "화면 잠금", "Lock", "잠금"),
        [OrbitAction.SleepDisplay] = new("Turn Display Off", "화면 끄기", "Display Off", "화면 끄기"),
        [OrbitAction.DarkMode] = new("Toggle Dark Mode", "다크 모드 전환", "Dark Mode", "다크 모드"),
        [OrbitAction.DesktopIcons] = new("Hide/Show Desktop Icons", "바탕 화면 아이콘 숨기기/보기", "Desktop Icons", "바탕 화면 아이콘"),
        [OrbitAction.HiddenFiles] = new("Show/Hide Hidden Files", "숨김 파일 보기/숨기기", "Hidden Files", "숨김 파일"),
        [OrbitAction.EmptyTrash] = new("Empty Recycle Bin", "휴지통 비우기", "Recycle Bin", "휴지통"),
        [OrbitAction.EjectAll] = new("Eject All Drives", "모든 드라이브 꺼내기", "Eject All", "모두 꺼내기"),
        [OrbitAction.CleanKeyboard] = new("Clean Keyboard", "키보드 청소", "Clean Keys", "키보드 청소"),
    };

    public static string Title(this OrbitAction action) => Strings.Korean ? All[action].Korean : All[action].English;

    public static string ShortTitle(this OrbitAction action) => Strings.Korean ? All[action].ShortKorean : All[action].ShortEnglish;

    /// <summary>Every action has its names; checked at startup so a new action can't ship without them.</summary>
    public static IEnumerable<OrbitAction> Missing() => OrbitActions.All.Where(a => !All.ContainsKey(a));
}
