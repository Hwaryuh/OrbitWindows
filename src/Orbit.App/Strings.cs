using System.Globalization;

namespace Orbit.App;

/// <summary>Text people see. Korean in the polite form (합니다체) when Windows is in Korean, else English.</summary>
internal static class Strings
{
    private static readonly bool Korean = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko";

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

    public static string CaptureResult(int width, int height, double match, string source) =>
        T($"{width}×{height} 픽셀을 {source}로 캡처했습니다. 화면과 {match:P2} 일치합니다.",
          $"Captured {width}×{height} pixels with {source}. {match:P2} match the screen.");
}
