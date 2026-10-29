using System.Diagnostics;

namespace Orbit.Core.Launcher;

/// <summary>A page of Windows' Settings app, opened by its ms-settings: URI.</summary>
public sealed record SettingsPane(string English, string Korean, string Uri, string Keywords);

/// <summary>
/// Windows Settings pages the launcher finds by name (Orbit: SmartAnswers, <c>panes</c>, which
/// opens System Settings by its URLs). Names as Windows 11 shows them, with search words.
/// </summary>
public static class SettingsPanes
{
    public static readonly IReadOnlyList<SettingsPane> All =
    [
        new("Display", "디스플레이", "ms-settings:display", "screen monitor resolution scale brightness night light hdr 화면 모니터 해상도 배율 밝기 야간 모드"),
        new("Sound", "소리", "ms-settings:sound", "audio volume speaker microphone output input 사운드 음량 스피커 마이크 출력 입력"),
        new("Notifications", "알림", "ms-settings:notifications", "focus do not disturb 방해 금지 집중"),
        new("Power & battery", "전원 및 배터리", "ms-settings:powersleep", "power sleep battery saver 전원 절전 배터리"),
        new("Storage", "저장소", "ms-settings:storagesense", "disk space cleanup 디스크 용량 정리"),
        new("Multitasking", "멀티태스킹", "ms-settings:multitasking", "snap windows desktops 창 맞춤 가상 데스크톱"),
        new("Clipboard", "클립보드", "ms-settings:clipboard", "history win+v 기록"),
        new("Bluetooth & devices", "Bluetooth 및 장치", "ms-settings:bluetooth", "bluetooth devices pair 블루투스 장치 연결"),
        new("Mouse", "마우스", "ms-settings:mousetouchpad", "pointer scroll cursor 포인터 스크롤 커서"),
        new("Touchpad", "터치패드", "ms-settings:devices-touchpad", "trackpad gestures 제스처"),
        new("Printers & scanners", "프린터 및 스캐너", "ms-settings:printers", "printer scanner 프린터 스캐너"),
        new("Network & internet", "네트워크 및 인터넷", "ms-settings:network", "network internet ethernet 네트워크 인터넷 이더넷"),
        new("Wi-Fi", "Wi-Fi", "ms-settings:network-wifi", "wifi wireless 와이파이 무선"),
        new("VPN", "VPN", "ms-settings:network-vpn", "vpn"),
        new("Personalization", "개인 설정", "ms-settings:personalization", "wallpaper theme colors 배경 테마 색"),
        new("Background", "배경", "ms-settings:personalization-background", "wallpaper desktop 배경 화면"),
        new("Colors", "색", "ms-settings:colors", "dark mode light mode accent 다크 모드 라이트 모드 강조 색"),
        new("Taskbar", "작업 표시줄", "ms-settings:taskbar", "tray system tray 트레이"),
        new("Installed apps", "설치된 앱", "ms-settings:appsfeatures", "apps uninstall programs 앱 제거 프로그램"),
        new("Default apps", "기본 앱", "ms-settings:defaultapps", "default browser open with 기본 브라우저 연결 프로그램"),
        new("Startup apps", "시작 프로그램", "ms-settings:startupapps", "startup login 시작 로그인"),
        new("Accounts", "계정", "ms-settings:yourinfo", "account user profile 계정 사용자 프로필"),
        new("Sign-in options", "로그인 옵션", "ms-settings:signinoptions", "password pin windows hello 암호 핀"),
        new("Date & time", "날짜 및 시간", "ms-settings:dateandtime", "clock timezone 시계 시간대"),
        new("Language & region", "언어 및 지역", "ms-settings:regionlanguage", "language region locale 언어 지역"),
        new("Typing", "입력", "ms-settings:typing", "keyboard autocorrect 키보드 자동 고침"),
        new("Keyboard", "키보드", "ms-settings:keyboard", "keyboard layout ime 키보드 자판 입력기"),
        new("Accessibility", "접근성", "ms-settings:easeofaccess", "accessibility contrast magnifier narrator 고대비 돋보기 내레이터"),
        new("Privacy & security", "개인 정보 및 보안", "ms-settings:privacy", "privacy permissions security 권한 보안"),
        new("Windows Security", "Windows 보안", "ms-settings:windowsdefender", "antivirus defender firewall 바이러스 방화벽"),
        new("Windows Update", "Windows 업데이트", "ms-settings:windowsupdate", "update upgrade 업데이트"),
        new("About", "정보", "ms-settings:about", "about pc name specs version 정보 사양 버전"),
    ];

    public static void Open(SettingsPane pane) => Process.Start(new ProcessStartInfo(pane.Uri) { UseShellExecute = true });
}
