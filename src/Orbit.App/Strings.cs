using System.Globalization;

namespace Orbit.App;

/// <summary>Text people see. Korean in the polite form (합니다체) when Windows is in Korean, else English.</summary>
internal static class Strings
{
    /// <summary>Windows' display language is Korean.</summary>
    public static readonly bool Korean = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko";

    /// <summary>The Korean or the English text, by Windows' display language.</summary>
    public static string T(string korean, string english) => Korean ? korean : english;

    public static string AppName => "Orbit";
    public static string MenuMonitorReport => T("모니터 정보 기록", "Record Monitor Info");
    public static string MenuOpenLogs => T("로그 폴더 열기", "Open Log Folder");
    public static string MenuQuit => T("종료", "Quit");

    public static string AlreadyRunning => T("Orbit이 이미 실행 중입니다.", "Orbit is already running.");
    public static string SomethingFailed => T("문제가 생겨 작업을 끝내지 못했습니다. 다시 시도하세요.", "Something went wrong. Please try again.");
    public static string MonitorReportSaved => T("모니터 정보를 로그에 기록했습니다.", "Monitor info was written to the log.");
    public static string CaptureFailed => T("화면을 캡처하지 못했습니다. 다시 시도하세요.", "The screen could not be captured. Please try again.");
    public static string CaptureCancelled => T("모니터 구성이 바뀌어 캡처를 취소했습니다.", "The monitor setup changed, so the capture was cancelled.");

    // Capture
    public static string CaptureHintArea => T("드래그해서 캡처  ·  Space 창 선택  ·  Enter 전체 화면  ·  Esc 취소", "Drag to capture  ·  Space for window  ·  Enter full screen  ·  Esc to cancel");
    public static string CaptureHintWindow => T("창을 누르세요  ·  Space 영역 선택  ·  Esc 취소", "Click a window  ·  Space for area  ·  Esc to cancel");
    public static string CaptureHintText => T("글자를 복사할 영역을 고르세요  ·  Esc 취소", "Select text to copy  ·  Esc to cancel");
    public static string KeyLockRatio => T("비율 고정", "Lock ratio");
    public static string KeyFromCenter => T("가운데부터", "From center");
    public static string KeyRatio => T("비율", "Ratio");
    public static string KeyLastArea => T("마지막 영역", "Last area");
    public static string KeyCopyColor => T("색 복사", "Copy color");
    public static string KeyMoveSelection => T("선택 옮기기", "Move selection");
    public static string KeyNudge => T("미세 조정", "Nudge");
    public static string KeySelectArea => T("영역 선택", "Select an area");
    public static string KeyCaptureIt => T("캡처하기", "Capture it");
    public static string KeyPickAgain => T("다시 고르기", "Pick again");
    public static string KeyAction => T("동작", "Action");
    public static string CaptureActionCopy => T("복사만", "Copy only");
    public static string CaptureActionSave => T("파일로 저장", "Save to file");
    public static string CaptureActionText => T("글자 인식", "Recognize text");
    public static string CaptureCopied => T("캡처를 복사했습니다.", "Capture copied.");
    public static string CaptureSaved => T("캡처를 저장했습니다.", "Capture saved.");
    public static string CaptureStored => T("캡처를 보관했습니다.", "Capture kept.");
    public static string CaptureSaveFailed => T("캡처를 저장하지 못했습니다.", "The capture could not be saved.");
    public static string CaptureNoLastArea => T("다시 캡처할 지난 영역이 없습니다.", "There is no previous area to capture again.");
    public static string TextNotFound => T("글자를 찾지 못했습니다.", "No text found.");
    public static string TextCopied(int words) => T($"{words}단어를 복사했습니다.", words == 1 ? "Copied 1 word." : $"Copied {words} words.");
    public static string TextNoLanguage => T("글자 인식 언어가 없습니다. 설정 > 시간 및 언어 > 언어에서 언어 팩을 추가하세요.", "No text recognition language is installed. Add a language pack in Settings > Time & language > Language.");
    public static string CaptureActionPin => T("화면에 고정", "Pin to screen");

    // Capture previews and pins
    public static string CardHint => T("클릭해서 편집  ·  오른쪽 클릭으로 더 보기", "Click to edit  ·  right-click for more");
    public static string CardEdit => T("편집", "Edit");
    public static string CardCopyText => T("글자 복사", "Copy Text");
    public static string CardPin => T("화면에 고정", "Pin to Screen");
    public static string CardSaveToFolder => T("폴더에 저장", "Save to Folder");
    public static string CardShowInExplorer => T("파일 탐색기에서 보기", "Show in File Explorer");
    public static string CardTuck => T("옆으로 치우기", "Tuck to the Side");
    public static string CardClose => T("닫기", "Close");
    public static string CardMoveToRecycleBin => T("휴지통으로 이동", "Move to Recycle Bin");
    public static string CardShowAll => T("캡처 보기", "Show captures");
    public static string MovedToRecycleBin => T("휴지통으로 옮겼습니다.", "Moved to the Recycle Bin.");
    public static string MovedToRecycleBinDetail => T("휴지통에서 되살릴 수 있습니다.", "You can restore it from the Recycle Bin.");
    public static string RecycleFailed => T("휴지통으로 옮기지 못했습니다.", "Could not move it to the Recycle Bin.");
    public static string CaptureFileMissing => T("캡처 파일을 찾지 못했습니다.", "The capture file could not be found.");
    public static string PinUnpin => T("고정 해제", "Unpin");
    public static string PinUnpinHint => T("고정을 풀고 미리보기로 돌려놓습니다.", "Unpin and put it back with the previews.");
    public static string PinCloseHint => T("닫기 (Esc)", "Close (Esc)");
    public static string ColorCopied(string hex) => T($"{hex} 색을 복사했습니다.", $"{hex} copied to the clipboard.");

    public static string MenuSettings => T("설정", "Settings");
    public static string ActionNotReady(string title) => T($"{title} 기능은 아직 준비 중입니다.", $"{title} is not ready yet.");
    public static string StartupChangeFailed => T("로그인할 때 실행 설정을 바꾸지 못했습니다.", "The sign-in setting could not be changed.");

    // Launcher
    public static string LauncherPlaceholder => T("앱, 명령, 설정 검색…", "Search apps, commands, settings…");
    public static string LauncherEmpty => T("결과가 없습니다", "No results");
    public static string LauncherEmptyHint => T("앱 이름, ‘캡처’ 같은 명령, 12*8 같은 계산을 입력하세요.", "Try an app name, a command like “capture”, or math like 12*8.");
    public static string LauncherActions => T("작업", "Actions");
    public static string LauncherSearchActions => T("작업 검색…", "Search actions…");
    public static string SectionCalculator => T("계산기", "Calculator");
    public static string SectionSuggestions => T("추천", "Suggestions");
    public static string SectionRecentApps => T("최근 앱", "Recent Apps");
    public static string SectionCommands => T("명령", "Commands");
    public static string SectionApps => T("앱", "Applications");
    public static string SectionWindows => T("열린 창", "Open Windows");
    public static string SectionWindowsSettings => T("Windows 설정", "Windows Settings");
    public static string SectionWeb => T("웹", "Web");
    public static string AccessoryApp => T("앱", "Application");
    public static string AccessoryWindow => T("창", "Window");
    public static string AccessoryMinimized => T("최소화됨", "Minimized");
    public static string ActionOpen => T("열기", "Open");
    public static string ActionRunCommand => T("명령 실행", "Run Command");
    public static string ActionSwitchToWindow => T("창으로 전환", "Switch to Window");
    public static string ActionCloseWindow => T("창 닫기", "Close Window");
    public static string ActionCopyAnswer => T("답 복사", "Copy Answer");
    public static string ActionOpenFileLocation => T("파일 위치 열기", "Open File Location");
    public static string ActionRunAsAdmin => T("관리자 권한으로 실행", "Run as Administrator");
    public static string ActionCopyPath => T("경로 복사", "Copy Path");
    public static string ActionSearch => T("검색", "Search");
    public static string SearchGoogleFor(string query) => T($"Google에서 “{query}” 검색", $"Search Google for “{query}”");
    public static string SectionCurrency => T("환율", "Currency");
    public static string SectionConversion => T("변환", "Conversion");
    public static string SectionWorldClock => T("세계 시각", "World Clock");
    public static string SectionWeather => T("날씨", "Weather");
    public static string SectionFiles => T("파일", "Files");
    public static string AccessoryFile => T("파일", "File");
    public static string ActionCopy => T("복사", "Copy");
    public static string RatesLoading => T("환율을 불러오는 중입니다…", "Getting exchange rates…");
    public static string RatesFailed => T("환율을 불러오지 못했습니다. 인터넷 연결을 확인하세요.", "Exchange rates could not be loaded. Check your internet connection.");
    public static string RatesFrom(DateTimeOffset when) =>
        T($"{when.LocalDateTime:g} 기준", $"Rates from {when.LocalDateTime:g}");
    public static string ClockSame => T("여기와 같은 시각입니다", "Same time as here");
    public static string ClockAhead(string span) => T($"여기보다 {span} 빠릅니다", $"{span} ahead");
    public static string ClockBehind(string span) => T($"여기보다 {span} 느립니다", $"{span} behind");
    public static string Hours(int hours, int minutes) => (hours, minutes) switch
    {
        (_, 0) => T($"{hours}시간", hours == 1 ? "1 hour" : $"{hours} hours"),
        (0, _) => T($"{minutes}분", $"{minutes} minutes"),
        _ => T($"{hours}시간 {minutes}분", $"{hours} h {minutes} min"),
    };
    public static string WeatherLoading => T("날씨를 확인하는 중입니다…", "Checking the weather…");
    public static string WeatherFailed => T("날씨를 불러오지 못했습니다.", "The weather could not be loaded.");
    public static string HighLow(int high, int low) => T($"최고 {high}° 최저 {low}°", $"High {high}° Low {low}°");
    public static string WeatherLabel(Orbit.Core.Launcher.WeatherKind kind) => kind switch
    {
        Orbit.Core.Launcher.WeatherKind.Clear => T("맑음", "Clear sky"),
        Orbit.Core.Launcher.WeatherKind.PartlyCloudy => T("구름 조금", "Partly cloudy"),
        Orbit.Core.Launcher.WeatherKind.Cloudy => T("흐림", "Cloudy"),
        Orbit.Core.Launcher.WeatherKind.Fog => T("안개", "Fog"),
        Orbit.Core.Launcher.WeatherKind.Drizzle => T("이슬비", "Drizzle"),
        Orbit.Core.Launcher.WeatherKind.Rain => T("비", "Rain"),
        Orbit.Core.Launcher.WeatherKind.Snow => T("눈", "Snow"),
        Orbit.Core.Launcher.WeatherKind.Thunderstorm => T("뇌우", "Thunderstorm"),
        _ => T("날씨", "Weather"),
    };
    public static string Copied => T("복사했습니다.", "Copied.");
    public static string CopyFailed => T("클립보드에 복사하지 못했습니다. 다시 시도하세요.", "Could not copy to the clipboard. Please try again.");
    public static string OpenFailed => T("열지 못했습니다.", "Could not open it.");

    // Settings window
    public static string SettingsTitle => T("Orbit 설정", "Orbit Settings");
    public static string PageGeneral => T("일반", "General");
    public static string PageRing => T("링", "Ring");
    public static string PageLauncher => T("런처", "Launcher");
    public static string PageCapture => T("캡처", "Capture");
    public static string PageClipboard => T("클립보드", "Clipboard");
    public static string PageTranslate => T("번역", "Translate");
    public static string PageSnippets => T("스니펫", "Snippets");
    public static string PageKeepAwake => T("잠자기 방지", "Keep Awake");
    public static string PageSound => T("앱별 볼륨", "App Volume");
    public static string PageSystem => T("시스템", "System");
    public static string PageDisplays => T("디스플레이", "Displays");
    public static string PageAbout => T("정보", "About");
    public static string LaunchAtSignIn => T("로그인할 때 Orbit 실행", "Open Orbit at sign-in");
    public static string LaunchAtSignInDetail => T("Windows에 로그인하면 Orbit이 트레이에서 시작됩니다.", "Orbit starts in the tray when you sign in to Windows.");
    // Ring page
    public static string RingSubtitle => T("포인터 주위에 동작이 모입니다. 슬롯을 누른 뒤 동작을 고르세요.", "Actions around your pointer. Click a slot, then pick what it does.");
    public static string RingSlotsLabel => T("슬롯", "slots");
    public static string RingFewer => T("슬롯 줄이기", "Fewer slots");
    public static string RingMore => T("슬롯 늘리기", "More slots");
    public static string SlotName(int? compass, int index) => compass switch
    {
        0 => T("위", "Top"), 1 => T("오른쪽 위", "Top right"), 2 => T("오른쪽", "Right"), 3 => T("오른쪽 아래", "Bottom right"),
        4 => T("아래", "Bottom"), 5 => T("왼쪽 아래", "Bottom left"), 6 => T("왼쪽", "Left"), 7 => T("왼쪽 위", "Top left"),
        _ => T($"{index + 1}번", $"Slot {index + 1}"),
    };
    public static string SlotTitle(string name) => T($"{name} 슬롯", $"{name} slot");
    public static string RingLibraryHint => T("동작을 누르거나 링 위로 끌어다 놓으세요.", "Click an action, or drag it onto the ring.");
    public static string RingSearchActions => T("동작 검색", "Search actions");
    public static string RingOnRing => T("링에 있음", "On ring");
    public static string RingPresets => T("프리셋", "Presets");
    public static string RingPresetApplied => T("프리셋을 적용했습니다.", "Preset applied.");
    public static string RingUndo => T("되돌리기", "Undo");
    public static string RingMine => T("내 링", "My ring");
    public static string RingMineDetail => T("편집할 때마다 저장되는 나만의 배치", "Your own layout, saved as you edit");
    public static string RingPresetName(Orbit.Core.Ring.BuiltInRing kind) => kind switch
    {
        Orbit.Core.Ring.BuiltInRing.Balanced => T("균형", "Balanced"),
        Orbit.Core.Ring.BuiltInRing.Screenshots => T("스크린샷", "Screenshots"),
        _ => T("일상", "Everyday"),
    };
    public static string RingPresetDetail(Orbit.Core.Ring.BuiltInRing kind) => kind switch
    {
        Orbit.Core.Ring.BuiltInRing.Balanced => T("조금씩 다 있는 배치", "A bit of everything"),
        Orbit.Core.Ring.BuiltInRing.Screenshots => T("모든 캡처 방식", "Every capture mode"),
        _ => T("클립보드 먼저", "Clipboard first"),
    };
    public static string RingSavedDetail => T("저장한 프리셋", "Saved preset");
    public static string RingSaveAsPreset => T("프리셋으로 저장", "Save as preset");
    public static string RingSaveDetail => T("지금 링을 그대로 보관합니다", "Keep the ring as it is now");
    public static string RingPresetNamePlaceholder => T("프리셋 이름", "Preset name");
    public static string RingDefaultPresetName => T("내 프리셋", "My preset");
    public static string Save => T("저장", "Save");
    public static string Cancel => T("취소", "Cancel");
    public static string Rename => T("이름 바꾸기", "Rename");
    public static string RingUpdatePreset => T("지금 링으로 바꾸기", "Update to Current Ring");
    public static string Delete => T("삭제", "Delete");
    public static string RingOpenTitle => T("링 열기", "Open the ring");
    public static string RingKeyboard => T("키보드", "Keyboard");
    public static string RingKeyboardDetail => T("누른 채 방향을 고르고 놓습니다. 짧게 눌렀다 떼면 고를 때까지 열려 있습니다.", "Hold, aim, release. Or tap, then choose.");
    public static string RingKeyboardFixed => T("단축키 변경은 아직 준비 중입니다.", "Changing the shortcut is not ready yet.");
    public static string RingMouse => T("마우스 버튼으로 열기", "Open with a mouse button");
    public static string RingMouseDetail => T("고른 버튼을 누르고 있으면 링이 열립니다. 짧게 누르면 원래 동작을 그대로 합니다.", "Hold the button you pick to open the ring. A quick click still does what it always did.");
    public static string RingMiddleButton => T("가운데 버튼", "Middle button");
    public static string RingBackButton => T("뒤로 버튼", "Back button");
    public static string RingForwardButton => T("앞으로 버튼", "Forward button");
    public static string RingSideButtonsHint => T("뒤로와 앞으로는 대부분의 마우스 옆면에 있는 두 버튼입니다.", "Back and forward are the two buttons on the side of most mice.");
    public static string RingMouseOffHint => T("켜면 버튼을 고를 수 있습니다.", "Turn it on to pick a button.");
    public static string RingChoosing => T("고르기", "Choosing");
    public static string RingRest => T("머무르면 실행", "Rest to activate");
    public static string RingRestDetail => T("동작 위에 잠시 머무르면 채워진 뒤 실행됩니다.", "Resting on an action fills it, then runs it.");
    public static string RingRestTime => T("머무는 시간", "Rest time");
    public static string Seconds(double seconds) => T($"{seconds:0.00}초", $"{seconds:0.00}s");
    public static string RingRestFooter => T("짧으면 빠르고, 길면 실수로 고르는 일이 줄어듭니다.", "Shorter feels snappier; longer prevents accidental picks.");

    public static string PageNotReady => T("이 기능은 아직 준비 중입니다.", "This feature is not ready yet.");
    public static string AboutVersion(string version) => T($"버전 {version}", $"Version {version}");
    public static string AboutLogs => T("로그 폴더 열기", "Open Log Folder");
    public static string AboutNotice => T(
        "Orbit for Windows는 3x-haust/Orbit(MIT)을 바탕으로 만들었습니다. 한/영 자동 전환은 nyttastudio/araso(MIT, 한국어 빈도 목록 CC BY-SA 4.0)를 사용합니다.",
        "Orbit for Windows is based on 3x-haust/Orbit (MIT). Automatic Korean/English switching uses nyttastudio/araso (MIT; Korean frequency list CC BY-SA 4.0).");
}
