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
    public static string MenuCaptureTest => T("캡처 테스트", "Capture Test");
    public static string MenuMonitorReport => T("모니터 정보 기록", "Record Monitor Info");
    public static string MenuOpenLogs => T("로그 폴더 열기", "Open Log Folder");
    public static string MenuQuit => T("종료", "Quit");

    public static string AlreadyRunning => T("Orbit이 이미 실행 중입니다.", "Orbit is already running.");
    public static string SomethingFailed => T("문제가 생겨 작업을 끝내지 못했습니다. 다시 시도하세요.", "Something went wrong. Please try again.");
    public static string MonitorReportSaved => T("모니터 정보를 로그에 기록했습니다.", "Monitor info was written to the log.");
    public static string CaptureFailed => T("화면을 캡처하지 못했습니다. 다시 시도하세요.", "The screen could not be captured. Please try again.");
    public static string CaptureCancelled => T("모니터 구성이 바뀌어 캡처를 취소했습니다.", "The monitor setup changed, so the capture was cancelled.");

    public static string CaptureResultTitle => T("캡처 테스트 결과", "Capture test result");

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
    public static string PageNotReady => T("이 기능은 아직 준비 중입니다.", "This feature is not ready yet.");
    public static string AboutVersion(string version) => T($"버전 {version}", $"Version {version}");
    public static string AboutLogs => T("로그 폴더 열기", "Open Log Folder");
    public static string AboutNotice => T(
        "Orbit for Windows는 3x-haust/Orbit(MIT)을 바탕으로 만들었습니다. 한/영 자동 전환은 nyttastudio/araso(MIT, 한국어 빈도 목록 CC BY-SA 4.0)를 사용합니다.",
        "Orbit for Windows is based on 3x-haust/Orbit (MIT). Automatic Korean/English switching uses nyttastudio/araso (MIT; Korean frequency list CC BY-SA 4.0).");

    public static string CaptureResult(int width, int height, double match, string source) =>
        T($"{width}×{height} 픽셀을 {source}로 캡처했습니다. 화면과 {match:P2} 일치합니다.",
          $"Captured {width}×{height} pixels with {source}. {match:P2} match the screen.");
}
