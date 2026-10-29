using System.Globalization;

namespace Orbit.App;

/// <summary>Text people see. Korean in the polite form (합니다체) when Windows is in Korean, else English.</summary>
internal static class Strings
{
    /// <summary>Windows' display language is Korean.</summary>
    public static readonly bool Korean = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko";

    private static string T(string korean, string english) => Korean ? korean : english;

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
