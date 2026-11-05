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
    public static string CodeCopied => T("QR 코드를 복사했습니다.", "Copied QR code.");
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
    public static string CaptureActionEdit => T("편집", "Edit");

    // Recording
    public static string RecordHintArea => T("드래그해서 녹화  ·  Space 창 선택  ·  Enter 전체 화면  ·  Esc 취소", "Drag to record  ·  Space for window  ·  Enter full screen  ·  Esc to cancel");
    public static string RecordHintWindow => T("녹화할 창을 누르세요  ·  Space 영역 선택  ·  Esc 취소", "Click a window to record  ·  Space for area  ·  Esc to cancel");
    public static string RecordStop => T("녹화 중지", "Stop Recording");
    public static string RecordDiscard => T("녹화 버리기", "Discard Recording");
    public static string RecordingSaved => T("녹화를 저장했습니다.", "Recording saved.");
    public static string RecordingDiscarded => T("녹화를 버렸습니다.", "Recording discarded.");
    public static string RecordingStartFailed => T("녹화를 시작할 수 없습니다.", "Recording could not start.");
    public static string RecordingSaveFailed => T("녹화 파일을 저장하지 못했습니다.", "The recording could not be saved.");
    public static string RecordingTooBig => T("영역이 너무 커서 녹화할 수 없습니다.", "That area is too big to record.");
    public static string RecordingTooBigDetail => T("가로세로 4096 픽셀 이하로 고르세요.", "Pick an area of at most 4096 pixels a side.");
    public static string RecordingTooSmall => T("영역이 너무 작아서 녹화할 수 없습니다.", "That area is too small to record.");
    public static string RecordingTooShort => T("녹화가 너무 짧아서 저장하지 않았습니다.", "The recording was too short to keep.");
    public static string RecordingWindowClosed => T("녹화하던 창이 닫혀서 녹화를 마쳤습니다.", "The window closed, so the recording ended.");
    public static string CardOpenVideo => T("동영상 열기", "Open Video");
    public static string CardHintVideo => T("클릭해서 재생  ·  오른쪽 클릭으로 더 보기", "Click to play  ·  right-click for more");
    public static string VideoCopied => T("동영상을 복사했습니다.", "Video copied.");
    public static string RecordSection => T("녹화", "Recording");
    public static string RecordStartRow => T("녹화 시작", "Start recording");
    public static string RecordFrameRate => T("초당 프레임", "Frames per second");
    public static string RecordFrameRateDetail => T("60은 움직임이 부드럽고, 30은 파일이 작습니다.", "60 moves more smoothly; 30 makes smaller files.");
    public static string RecordShowCursor => T("마우스 포인터 보이기", "Show the pointer");
    public static string RecordClicksRow => T("클릭 강조", "Highlight clicks");
    public static string RecordClicksDetail => T("영역과 화면 녹화에 표시됩니다. 창 녹화와 포인터를 끈 녹화에는 표시되지 않습니다.", "Shows in area and screen recordings, not in window recordings or with the pointer off.");
    public static string RecordClicksOff => T("클릭 강조 끄기", "Stop highlighting clicks");
    public static string RecordClicksOn => T("클릭 강조 켜기", "Highlight clicks");
    public static string RecordCountdownRow => T("카운트다운", "Countdown");
    public static string RecordCountdownDetail => T("녹화를 시작하기 전에 초를 셉니다. Esc를 누르면 취소합니다.", "Counts before recording starts. Esc cancels.");
    public static string RecordCountdownOff => T("끔", "Off");
    public static string RecordCountdownSeconds(int seconds) => T($"{seconds}초", $"{seconds} s");
    public static string RecordCountdownCancel => T("Esc를 누르면 취소합니다", "Esc to cancel");
    public static string RecordingAlreadyRunning => T("이미 녹화 중입니다.", "Already recording.");
    public static string CaptureActionRecord => T("이 영역 녹화", "Record this area");
    public static string RecordAppSound => T("앱 소리 녹음", "Record app sound");
    public static string RecordMic => T("마이크 녹음", "Record microphone");
    public static string RecordSoundDetail => T("녹화 중에도 언제든 켜고 끌 수 있습니다.", "Switch it on or off any time while recording.");
    public static string RecordAppSoundOff => T("앱 소리 끄기", "Mute app sound");
    public static string RecordAppSoundOn => T("앱 소리 켜기", "Record app sound");
    public static string RecordMicOff => T("마이크 끄기", "Mute microphone");
    public static string RecordMicOn => T("마이크 켜기", "Unmute microphone");
    public static string RecordMicUnavailable => T("마이크를 쓸 수 없습니다.", "The microphone can't be used.");
    public static string RecordMicDenied => T("설정 > 개인 정보 > 마이크에서 데스크톱 앱의 마이크 사용을 켜세요.", "Turn on microphone access for desktop apps in Settings > Privacy > Microphone.");
    public static string RecordMicMissing => T("연결된 마이크가 없습니다.", "No microphone is connected.");

    // Capture editor
    public static string EditorTitle => T("캡처 편집", "Annotate");
    public static string EditorToolTitle(Orbit.Core.Capture.Editor.EditorTool tool) => tool switch
    {
        Orbit.Core.Capture.Editor.EditorTool.Select => T("선택", "Select"),
        Orbit.Core.Capture.Editor.EditorTool.Arrow => T("화살표", "Arrow"),
        Orbit.Core.Capture.Editor.EditorTool.Line => T("선", "Line"),
        Orbit.Core.Capture.Editor.EditorTool.Rect => T("사각형", "Rectangle"),
        Orbit.Core.Capture.Editor.EditorTool.Ellipse => T("타원", "Ellipse"),
        Orbit.Core.Capture.Editor.EditorTool.Pen => T("펜", "Pen"),
        Orbit.Core.Capture.Editor.EditorTool.Highlight => T("형광펜", "Highlighter"),
        Orbit.Core.Capture.Editor.EditorTool.Text => T("글자", "Text"),
        Orbit.Core.Capture.Editor.EditorTool.Counter => T("번호", "Counter"),
        Orbit.Core.Capture.Editor.EditorTool.Blur => T("흐림", "Blur"),
        Orbit.Core.Capture.Editor.EditorTool.Pixelate => T("모자이크", "Pixelate"),
        Orbit.Core.Capture.Editor.EditorTool.Erase => T("지우기", "Erase"),
        Orbit.Core.Capture.Editor.EditorTool.Spotlight => T("스포트라이트", "Spotlight"),
        Orbit.Core.Capture.Editor.EditorTool.Loupe => T("돋보기", "Magnifier"),
        Orbit.Core.Capture.Editor.EditorTool.Measure => T("자", "Ruler"),
        Orbit.Core.Capture.Editor.EditorTool.Picker => T("색 추출", "Color Picker"),
        _ => T("자르기", "Crop"),
    };
    public static string EditorStyle => T("스타일", "Style");
    public static string EditorThickness => T("두께", "Thickness");
    public static string EditorSize => T("크기", "Size");
    public static string EditorFilled => T("채우기", "Filled");
    public static string EditorCorners => T("모서리", "Corners");
    public static string EditorShape => T("모양", "Shape");
    public static string EditorStraight => T("곧게", "Straight");
    public static string EditorCurved => T("휘게", "Curved");
    public static string EditorWeight => T("굵기", "Weight");
    public static string EditorWeightName(Orbit.Core.Capture.Editor.TextWeight weight) => weight switch
    {
        Orbit.Core.Capture.Editor.TextWeight.Regular => T("보통", "Regular"),
        Orbit.Core.Capture.Editor.TextWeight.Semibold => T("중간", "Medium"),
        Orbit.Core.Capture.Editor.TextWeight.Bold => T("굵게", "Bold"),
        _ => T("아주 굵게", "Heavy"),
    };
    public static string EditorTextStyle => T("모양", "Style");
    public static string EditorTextStyleName(Orbit.Core.Capture.Editor.TextStyle style) => style switch
    {
        Orbit.Core.Capture.Editor.TextStyle.Shadow => T("그림자", "Shadow"),
        Orbit.Core.Capture.Editor.TextStyle.Plain => T("기본", "Plain"),
        Orbit.Core.Capture.Editor.TextStyle.Pill => T("라벨", "Label"),
        _ => T("외곽선", "Outline"),
    };
    public static string EditorStrength => T("강도", "Strength");
    public static string EditorTextNote => T("글자를 두 번 누르면 고칠 수 있습니다.", "Double-click a text to change it.");
    public static string EditorCounterNote => T("누를 때마다 다음 번호를 붙입니다.", "Each click places the next number.");
    public static string EditorLoupeNote => T("볼 곳에서 렌즈를 둘 곳까지 끄세요.", "Drag from the spot to where the lens goes.");
    public static string EditorEraseNote => T("영역을 주변 색으로 덮습니다. 단색 배경의 글자를 지울 때 좋습니다.", "Covers an area with the color around it, good for removing text from a plain background.");
    public static string EditorSpotlightNote => T("그린 상자 바깥을 어둡게 합니다.", "Dims everything outside the boxes you draw.");
    public static string EditorPickerNote => T("누르면 그 색의 16진수 코드를 복사합니다.", "Click to copy a color's hex code.");
    public static string EditorNothingNote => T("이 도구에는 조정할 것이 없습니다.", "Nothing to adjust for this tool.");
    public static string EditorTextHint => T("입력하세요 (Shift+Enter 줄바꿈)", "Type… (Shift+Enter new line)");
    public static string EditorAlwaysOnTop => T("항상 위", "Always on top");
    public static string EditorAlwaysOnTopOff => T("항상 위: 꺼짐", "Always on top: off");
    public static string EditorUndo => T("실행 취소 (Ctrl+Z)", "Undo (Ctrl+Z)");
    public static string EditorRedo => T("다시 실행 (Ctrl+Shift+Z)", "Redo (Ctrl+Shift+Z)");
    public static string EditorResetCrop => T("자르기 초기화", "Reset Crop");
    public static string EditorSaveHint => T("저장 (Ctrl+S) · 다른 이름으로 저장 (Ctrl+Shift+S)", "Save (Ctrl+S) · Save As (Ctrl+Shift+S)");
    public static string EditorCopyHint => T("복사 (Ctrl+C)", "Copy (Ctrl+C)");
    public static string EditorFitHint => T("화면에 맞추기 (Ctrl+0)", "Fit (Ctrl+0)");
    public static string EditorColor(int index) => T($"색 {index}", $"Color {index}");
    public static string EditorPngFiles => T("PNG 그림", "PNG image");
    // Capture library and page
    public static string LibraryTitle => T("최근 캡처", "Recent Captures");
    public static string LibraryEmpty => T("아직 캡처가 없습니다", "No captures yet");
    public static string LibraryEmptyDetail => T("찍은 스크린샷이 여기에 나타납니다.", "Screenshots you take show up here.");
    public static string LibraryHint => T("다른 앱으로 끌어서 첨부  ·  Enter 편집  ·  Space 열기", "Drag into any app to attach  ·  Enter to edit  ·  Space to open");
    public static string LibraryOpenFolder => T("폴더 열기", "Open Folder");
    public static string LibraryOpenFolderHint => T("캡처 폴더를 파일 탐색기에서 엽니다.", "Open the captures folder in File Explorer");
    public static string LibraryOpen => T("열기", "Open");
    public static string LibraryCloseHint => T("닫기 (Esc)", "Close (Esc)");
    public static string CapturedWhen(DateTime when, DateTime now)
    {
        var time = when.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (when.Date == now.Date) return T($"오늘 {time}", $"Today {time}");
        if (when.Date == now.Date.AddDays(-1)) return T($"어제 {time}", $"Yesterday {time}");
        if (when.Year == now.Year) return T($"{when.Month}월 {when.Day}일", when.ToString("MMM d", CultureInfo.GetCultureInfo("en-US")));
        return T($"{when.Year}. {when.Month}. {when.Day}.", when.ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-US")));
    }
    public static string CapturePageSubtitle => T("스크린샷, 편집, 화면 고정, 글자 인식.", "Screenshots, annotation, pinning and text recognition.");
    public static string CaptureRecent => T("최근", "Recent");
    public static string CaptureShowAll => T("모두 보기", "Show All");
    public static string CaptureAfter => T("캡처한 뒤", "After capture");
    public static string CaptureCopyToClipboard => T("클립보드에 복사", "Copy to clipboard");
    public static string CapturePlaySound => T("소리 내기", "Play sound");
    public static string CaptureKeepPreview => T("미리보기 표시 시간", "Keep preview on screen");
    public static string CaptureKeepPreviewDetail => T("마우스를 올려 두면 계속 보입니다.", "Hovering keeps it open.");
    public static string ShortSeconds(double seconds) => T($"{seconds:0.##}초", $"{seconds:0.##} s");
    public static string WholeSeconds(int seconds) => T($"{seconds}초", $"{seconds} s");
    public static string CaptureUntilClosed => T("닫을 때까지", "Until closed");
    public static string CapturePreviewPosition => T("미리보기 위치", "Preview position");
    public static string CapturePreviewPositionDetail => T("모서리를 고르세요. 미리보기를 화면 가장자리 쪽으로 밀면 닫힙니다.", "Pick a corner. Swipe a preview toward the screen edge to dismiss it.");
    public static string CapturePreviewTry => T("미리 보기", "Preview");
    public static string CaptureCornerName(string corner) => corner switch
    {
        "topLeft" => T("왼쪽 위", "Top left"),
        "topRight" => T("오른쪽 위", "Top right"),
        "bottomLeft" => T("왼쪽 아래", "Bottom left"),
        _ => T("오른쪽 아래", "Bottom right"),
    };
    public static string CaptureSaving => T("저장", "Saving");
    public static string CaptureSaveTo => T("저장 위치", "Save to");
    public static string CaptureChange => T("바꾸기…", "Change…");
    public static string CaptureWhileSelecting => T("고르는 동안", "While selecting");
    public static string CaptureShowKeyHints => T("단축키 안내 표시", "Show key hints");
    public static string CaptureShowKeyHintsDetail => T("화면 아래에 단축키 띠를 보여 줍니다. 몇 초 뒤 사라지고 키를 누르면 다시 나타납니다.", "A strip of shortcuts at the bottom of the screen. It fades after a few seconds and comes back when you press a key.");
    public static string CaptureWhileDragging => T("끄는 동안", "While you drag");
    public static string CaptureBeforeRelease => T("손을 떼기 전에", "Then, before you let go");
    public static string CaptureBeforeReleaseFooter => T("같은 키를 다시 누르면 평소대로 처리합니다.", "Press the same key again for the usual steps after capture.");
    public static string TipSpaceSwitch => T("영역 선택과 창 선택을 바꿉니다", "Switch between area and window");
    public static string TipEnterScreen => T("모니터 전체를 캡처합니다", "Capture the whole display");
    public static string TipClickWindow => T("포인터 아래 창을 캡처합니다", "Capture the window under the pointer");
    public static string TipTabRatio => T("비율을 바꿉니다: 자유, 1:1, 4:3, 3:2, 16:9, 9:16 (Shift+Tab 거꾸로)", "Cycle the aspect ratio: free, 1:1, 4:3, 3:2, 16:9, 9:16 (Shift+Tab goes back)");
    public static string TipRLast => T("마지막으로 캡처한 영역을 다시 불러옵니다. Enter로 캡처합니다", "Bring back the last captured area, then Enter to capture it");
    public static string TipCopyColor => T("돋보기 아래 색을 16진수 코드로 복사합니다", "Copy the color under the magnifier as a hex code");
    public static string TipShiftRatio => T("누르고 있으면 비율을 고정합니다 (비율을 고르지 않았으면 정사각형)", "Hold to lock the aspect ratio (a square unless a ratio is picked)");
    public static string TipAltCenter => T("누르고 있으면 시작한 곳을 가운데로 키웁니다", "Hold to grow the area from where you started");
    public static string TipSpaceMove => T("누르고 있으면 영역 전체를 옮깁니다", "Hold to move the whole area");
    public static string TipArrows => T("모서리를 1픽셀씩, Shift와 함께 10픽셀씩 옮깁니다", "Nudge the corner by 1 px, with Shift by 10 px");
    public static string TipCopyOnly => T("복사만 합니다", "Copy only");
    public static string TipSave => T("파일로 저장합니다", "Save to file");
    public static string TipEdit => T("편집기에서 엽니다", "Open in Editor");
    public static string TipPin => T("화면에 고정합니다", "Pin on screen");
    public static string TipText => T("글자를 인식해 복사합니다", "Recognize text and copy it");
    public static string KeyClick => T("클릭", "Click");
    // Clipboard
    public static string ClipSearch => T("클립보드 검색", "Search clipboard");
    public static string ClipAll => T("전체", "All");
    public static string ClipPinned => T("고정됨", "Pinned");
    public static string ClipKindName(Orbit.Core.Clipboard.ClipKind kind) => kind switch
    {
        Orbit.Core.Clipboard.ClipKind.Text => T("텍스트", "Text"),
        Orbit.Core.Clipboard.ClipKind.Link => T("링크", "Link"),
        Orbit.Core.Clipboard.ClipKind.Image => T("이미지", "Image"),
        Orbit.Core.Clipboard.ClipKind.File => T("파일", "File"),
        _ => T("색", "Color"),
    };
    public static string ClipPaused => T("일시 정지됨", "Paused");
    public static string ClipPausedHint => T("클립보드 기록이 일시 정지되었습니다. 누르면 다시 시작합니다.", "Clipboard recording is paused. Click to resume.");
    public static string ClipHintPaste => T("붙여넣기", "Paste");
    public static string ClipHintPlain => T("서식 없이", "Plain");
    public static string ClipHintPin => T("고정", "Pin");
    public static string ClipHintFilter => T("필터", "Filter");
    public static string ClipEmpty => T("아직 아무것도 없습니다", "Nothing here yet");
    public static string ClipEmptyDetail => T("복사한 텍스트, 링크, 이미지, 파일이 여기에 나타납니다.", "Anything you copy will appear here: text, links, images and files.");
    public static string ClipNoMatch(string query) => T($"“{query}”와 일치하는 항목이 없습니다", $"No matches for “{query}”");
    public static string ClipNoMatchDetail => T("다른 단어로 찾거나 ↓로 필터를 바꾸세요.", "Try a different word, or press ↓ to change the filter.");
    public static string ClipUnknownApp => T("알 수 없는 앱", "Unknown app");
    public static string ClipCharacters(int count, int lines) => lines > 1
        ? T($"{count}자 · {lines}줄", $"{count} characters · {lines} lines")
        : T($"{count}자", $"{count} characters");
    public static string ClipFiles(int count) => T($"파일 {count}개", count == 1 ? "1 file" : $"{count} files");
    public static string ClipPaste => T("붙여넣기", "Paste");
    public static string ClipPastePlain => T("서식 없이 붙여넣기", "Paste as Plain Text");
    public static string ClipPin => T("고정", "Pin");
    public static string ClipUnpin => T("고정 해제", "Unpin");
    public static string ClipOpenLink => T("링크 열기", "Open Link");
    public static string ClipNothingToPaste => T("붙여넣을 창이 없습니다. 복사만 했습니다.", "There is no window to paste into. It was copied.");
    public static string ClipNotInFront => T("붙여넣을 창을 앞으로 가져오지 못했습니다. 복사만 했습니다.", "The window could not be brought to the front. It was copied.");
    public static string ClipBlocked => T("관리자 권한으로 실행 중인 창에는 붙여넣을 수 없습니다. 복사만 했습니다.", "Orbit cannot paste into a window running as administrator. It was copied.");
    public static string ClipChanged => T("그 사이 다른 내용이 복사되어 붙여넣지 않았습니다.", "Something else was copied meanwhile, so nothing was pasted.");
    public static string ClipPageSubtitle => T("복사한 모든 것을 찾아서 바로 붙여넣습니다.", "Everything you copy, searchable and one keystroke away.");
    public static string ClipOpenHistory => T("기록 열기", "Open History");
    public static string ClipRecording => T("기록 중", "Recording");
    public static string ClipRecordingPaused => T("기록이 일시 정지되었습니다", "Recording is paused");
    public static string ClipStoredHere(int count) => T($"{count}개 항목, 이 PC에만 저장됩니다.", $"{count} items, stored on this PC only.");
    public static string ClipPause => T("일시 정지", "Pause");
    public static string ClipPause15 => T("15분", "15 minutes");
    public static string ClipPause60 => T("1시간", "1 hour");
    public static string ClipPauseUntilResumed => T("다시 시작할 때까지", "Until I resume");
    public static string ClipResume => T("다시 시작", "Resume");
    public static string ClipPasteDirectly => T("바로 붙여넣기", "Paste directly");
    public static string ClipPasteDirectlyDetail => T("항목을 고르면 붙여넣습니다. 끄면 복사만 합니다.", "Choosing an item pastes it. Off: it's only copied.");
    public static string ClipKeep => T("기록 개수", "Keep history");
    public static string ClipKeepDetail => T("고정한 항목은 항상 남습니다.", "Pinned items are always kept.");
    public static string ClipClear => T("기록 지우기", "Clear history");
    public static string ClipClearDetail => T("고정한 항목은 남습니다.", "Pinned items stay.");
    public static string ClipClearButton => T("지우기…", "Clear…");
    public static string ClipClearConfirm => T("클립보드 기록을 지울까요? 고정한 항목은 남습니다. 되돌릴 수 없습니다.", "Clear the clipboard history? Pinned items stay. This can't be undone.");
    public static string ClipNeverRecord => T("기록하지 않을 앱", "Never record from");
    public static string ClipNeverRecordFooter => T("비밀번호 관리자가 숨김으로 표시한 복사는 항상 건너뜁니다.", "Copies that password managers mark as private are always skipped.");
    public static string ClipAddApp => T("앱 추가…", "Add app…");
    public static string ClipChoose => T("고르기", "Choose");
    public static string ClipRemove => T("빼기", "Remove");
    public static string ClipPrograms => T("프로그램", "Programs");
    // Clean URL
    public static string CleanUrlRemoved(int count) => T($"추적 정보 {count}개를 지웠습니다.", count == 1 ? "Removed 1 tracker." : $"Removed {count} trackers.");
    public static string CleanUrlNothing => T("지울 추적 정보가 없습니다.", "There are no trackers to remove.");
    public static string CleanUrlNoLink => T("클립보드에 링크가 없습니다.", "There is no link on the clipboard.");
    public static string CleanUrlTitle => T("링크 정리", "Clean links");
    public static string CleanUrlAuto => T("복사한 링크 자동 정리", "Clean copied links automatically");
    public static string CleanUrlAutoDetail => T("링크 하나만 복사하면 추적 정보를 바로 지웁니다. 클립보드 기록을 멈춰도 동작합니다.", "When you copy a single link, its trackers go at once. Works while clipboard recording is paused.");
    public static string CleanUrlLast => T("마지막으로 지운 이름", "Last removed");
    public static string CleanUrlNone => T("아직 없습니다", "Nothing yet");
    public static string CleanUrlBuiltIn => T("기본으로 지우는 이름", "Removed by default");
    public static string CleanUrlBuiltInDetail => T("누르면 끄거나 켭니다. 꺼진 이름은 지우지 않습니다.", "Click to switch one off or on. Names that are off stay in links.");
    public static string CleanUrlAdded => T("직접 추가한 이름", "Your names");
    public static string CleanUrlAddHint => T("이름 입력", "Parameter name");
    public static string CleanUrlAdd => T("추가", "Add");
    public static string CleanUrlFooter => T("사이트별 이름(si, t 등)은 그 사이트의 링크에서만 지웁니다. 링크 하나만 있을 때만 정리합니다.", "Site names (si, t and others) are removed only from links to that site. Only a clipboard that is one link is cleaned.");
    // Translate
    public static string TranslateCopyFirst => T("먼저 텍스트를 복사하세요.", "Copy some text first.");
    public static string TranslatePrivate => T("비밀번호 관리자가 숨긴 내용은 번역하지 않습니다.", "Content a password manager marked as private is not translated.");
    public static string TranslateAuto => T("자동", "Auto");
    public static string TranslateSwap => T("방향 바꾸기 (Ctrl+Shift+S)", "Swap direction (Ctrl+Shift+S)");
    public static string TranslateInto => T("번역할 언어", "Translate into…");
    public static string TranslateClose => T("닫기 (Esc)", "Close (Esc)");
    public static string TranslateTryAgain => T("다시 시도", "Try Again");
    public static string TranslateUseGoogle => T("Google 사용", "Use Google");
    public static string TranslateReplace => T("바꾸기", "Replace");
    public static string TranslateCopied => T("번역을 복사했습니다.", "Translation copied.");
    public static string TranslateCharacters(int count) => T($"{count}자", $"{count} characters");
    public static string TranslateDrag => T("끌어서 옮기기", "Drag to move");
    public static string TranslateFailure(Orbit.Core.Translate.TranslationFailure failure) => failure switch
    {
        Orbit.Core.Translate.TranslationFailure.NeedsKey => T("설정 > 번역에서 DeepL API 키를 넣으세요.", "Add a DeepL API key in Settings > Translate."),
        Orbit.Core.Translate.TranslationFailure.BadKey => T("DeepL이 API 키를 받아들이지 않았습니다.", "DeepL didn't accept the API key."),
        Orbit.Core.Translate.TranslationFailure.Quota => T("DeepL의 이번 달 무료 사용량을 다 썼습니다.", "DeepL's monthly free limit is used up."),
        Orbit.Core.Translate.TranslationFailure.RateLimited => T("요청이 너무 많습니다. 잠시 뒤에 다시 시도하세요.", "Too many requests. Try again in a moment."),
        Orbit.Core.Translate.TranslationFailure.Offline => T("인터넷에 연결되어 있지 않은 것 같습니다.", "You seem to be offline."),
        _ => T("번역 서비스가 응답하지 않았습니다. 잠시 뒤에 다시 시도하세요.", "The translation service didn't answer. Try again in a moment."),
    };
    public static string TranslatePageSubtitle => T("Ctrl+C를 두 번 누르면 선택한 글을 번역합니다.", "Press Ctrl+C twice to translate what you selected.");
    public static string TranslateDoubleCopy => T("Ctrl+C 두 번으로 번역", "Translate with Ctrl+C twice");
    public static string TranslateDoubleCopyDetail => T("두 번째 Ctrl+C로 클립보드가 바뀔 때만 번역합니다.", "Translates only when the second Ctrl+C changes the clipboard.");
    public static string TranslateInterval => T("두 번 누르는 간격", "Double-press window");
    public static string TranslateEngineTitle => T("번역 엔진", "Engine");
    public static string TranslateGoogleDetail => T("설정 없이 무료로 씁니다. 글이 Google로 전송됩니다. 공식 API가 아니어서 막힐 수 있습니다.", "Free, no setup. Text is sent to Google. Not an official API; it may be limited.");
    public static string TranslateDeepLDetail => T("가장 자연스럽습니다. 무료 API 키로 한 달 500,000자까지 씁니다.", "Most natural results. Free API key: 500,000 characters a month.");
    public static string TranslateDeepLKey => T("DeepL API 키", "DeepL API key");
    public static string TranslateDeepLKeyDetail => T("이 Windows 사용자만 읽을 수 있게 암호화해서 저장합니다.", "Stored encrypted so only this Windows user can read it.");
    public static string TranslateDeepLKeySaved => T("키를 저장했습니다.", "Key saved.");
    public static string TranslateDeepLKeyRemoved => T("키를 지웠습니다.", "Key removed.");
    public static string TranslateDeepLKeyHint => T("키를 붙여넣으세요", "Paste your key");
    public static string TranslatePrimary => T("기본 언어", "Translate into");
    public static string TranslatePrimaryDetail => T("글이 이미 이 언어면 두 번째 언어로 번역합니다.", "Text already in it goes to the second language.");
    public static string TranslateSecondary => T("두 번째 언어", "Second language");
    public static string TranslateFormality => T("말투 (DeepL)", "Formality (DeepL)");
    public static string TranslateFormalityName(Orbit.Core.Translate.Formality f) => f switch
    {
        Orbit.Core.Translate.Formality.Formal => T("격식", "Formal"),
        Orbit.Core.Translate.Formality.Casual => T("편하게", "Casual"),
        _ => T("자동", "Auto"),
    };
    public static string TranslateFontSize => T("글자 크기", "Text size");
    public static string TranslateShowSource => T("원문 보이기", "Show the original");
    public static string TranslateCloseOnBlur => T("바깥을 누르면 닫기", "Close when clicking elsewhere");
    // Snippets
    public static string SnippetStarterDate => T("오늘 날짜", "Today's date");
    public static string SnippetStarterTime => T("지금 시각", "Current time");
    public static string SnippetStarterEmail => T("이메일 주소", "Email address");
    public static string SnippetStarterReply => T("답장 인사", "Reply greeting");
    public static string SnippetStarterReplyContent => T("{input:이름}님, 안녕하세요.\n\n{cursor}\n\n감사합니다.", "Hi {input:Name},\n\n{cursor}\n\nThanks!");
    public static string SnippetExamples => T("예시", "Examples");
    public static string SnippetImported => T("가져온 항목", "Imported");
    public static string SnippetSearch => T("스니펫 검색", "Search snippets");
    public static string SnippetUntitled => T("이름 없음", "Untitled");
    public static string SnippetInsert => T("넣기", "Insert");
    public static string SnippetNone => T("스니펫이 없습니다", "No snippets");
    public static string SnippetNoneDetail => T("설정 > 스니펫에서 만드세요.", "Make one in Settings > Snippets.");
    public static string SnippetCancel => T("취소", "Cancel");
    public static string SnippetPageSubtitle => T("키워드를 입력하면 정해 둔 글로 바뀝니다.", "Type a keyword and it turns into your text.");
    public static string SnippetEnabled => T("입력하면 바로 바꾸기", "Expand as you type");
    public static string SnippetEnabledDetail => T("키워드를 확인할 수 있는 입력란에서만 바꿉니다. 한글 자판에서는 스페이스나 탭 뒤에 바꿉니다.", "Only in fields where Orbit can see the keyword. With the Korean keyboard, after Space or Tab.");
    public static string SnippetTrigger => T("바꾸는 때", "Expand");
    public static string SnippetTriggerImmediate => T("키워드를 다 치면", "Right away");
    public static string SnippetTriggerDelimiter => T("스페이스나 탭 뒤", "After Space or Tab");
    public static string SnippetInTerminals => T("터미널에서도 바꾸기", "Expand in terminals");
    public static string SnippetInTerminalsDetail => T("Windows Terminal, 명령 프롬프트, WezTerm, Alacritty, mintty", "Windows Terminal, Command Prompt, WezTerm, Alacritty, mintty");
    public static string SnippetList => T("스니펫", "Snippets");
    public static string SnippetNew => T("새 스니펫", "New Snippet");
    public static string SnippetImport => T("가져오기…", "Import…");
    public static string SnippetExport => T("내보내기…", "Export…");
    public static string SnippetImportedCount(int count) => T($"{count}개를 가져왔습니다.", count == 1 ? "Imported 1 snippet." : $"Imported {count} snippets.");
    public static string SnippetImportFailed => T("파일을 읽지 못했습니다.", "The file could not be read.");
    public static string SnippetExported => T("내보냈습니다.", "Exported.");
    public static string SnippetName => T("이름", "Name");
    public static string SnippetKeyword => T("키워드", "Keyword");
    public static string SnippetContent => T("내용", "Content");
    public static string SnippetFolder => T("폴더", "Folder");
    public static string SnippetOn => T("사용", "On");
    public static string SnippetClash(string other) => T($"“{other}” 키워드와 겹칩니다. 짧은 쪽이 먼저 바뀝니다.", $"Clashes with “{other}”: the shorter one expands first.");
    public static string SnippetPlaceholders => T("{clipboard} 클립보드, {cursor} 커서 위치, {date} {time} {datetime} 날짜와 시각 ({date:yyyy-MM-dd}처럼 형식 지정), {input:이름} 넣을 때 묻기, {snippet:키워드} 다른 스니펫, {uuid}", "{clipboard}, {cursor}, {date} {time} {datetime} (with a pattern such as {date:yyyy-MM-dd}), {input:Name} asks when inserting, {snippet:keyword} another snippet, {uuid}");
    public static string SnippetExcluded => T("바꾸지 않을 앱", "Never expand in");
    public static string SnippetFiles => T("스니펫 파일", "Snippet files");
    // Keep Awake
    public static string AwakeStarted => T("잠자기를 막고 있습니다.", "Keeping your PC awake.");
    public static string AwakeStopped => T("잠자기가 원래대로 돌아왔습니다.", "Sleep is back to normal.");
    public static string AwakeFinished => T("잠자기 방지가 끝났습니다.", "Keep awake finished.");
    public static string AwakeAppQuit(string name) => T($"{name} 종료로 잠자기가 원래대로 돌아왔습니다.", $"{name} quit. Sleep is back to normal.");
    public static string AwakeBatteryLow(int percent) => T($"배터리 {percent}%라서 잠자기가 원래대로 돌아왔습니다.", $"Battery at {percent}%. Sleep is back to normal.");
    public static string AwakeFailed => T("잠자기를 막지 못했습니다.", "Couldn't keep your PC awake.");
    public static string AwakeIndefinite => T("끌 때까지", "Until you turn it off");
    public static string AwakeUntil(string time) => T($"{time}까지", $"Until {time}");
    public static string AwakeWhile(string name) => T($"{name} 실행 중에", $"While {name} is open");
    public static string AwakeSubtitle => T("PC가 잠들지 않게 합니다.", "Keep your PC from sleeping.");
    public static string AwakeOn => T("깨어 있는 중", "Awake");
    public static string AwakeOff => T("꺼져 있음", "Off");
    public static string AwakeRemaining(string left) => T($"남은 시간 {left}", $"{left} left");
    public static string AwakeStop => T("멈추기", "Stop");
    public static string AwakeStart => T("시작", "Start");
    public static string AwakeStartSession => T("세션 시작", "Start a session");
    public static string AwakeForever => T("끌 때까지", "Until off");
    public static string AwakeMinutes(int m) => T($"{m}분", $"{m}m");
    public static string AwakeHours(int h) => T($"{h}시간", $"{h}h");
    public static string AwakeUntilTime => T("정한 시각까지", "Until a time");
    public static string AwakeTimeInvalid => T("시각을 13:30처럼 입력하세요.", "Enter a time such as 13:30.");
    public static string AwakeWhileApp => T("프로그램이 실행되는 동안", "While a program is open");
    public static string AwakeWhileAppDetail => T("그 프로그램을 모두 닫으면 끝납니다. 다운로드나 렌더링에 좋습니다.", "Ends when every window of it is closed and it quits. Good for downloads and renders.");
    public static string AwakeKeepScreen => T("화면도 켜 두기", "Keep the screen on");
    public static string AwakeKeepScreenDetail => T("끄면 화면은 평소대로 꺼지고 PC만 깨어 있습니다.", "Off: the screen sleeps as usual and the PC stays awake.");
    public static string AwakeSafety => T("안전", "Safety");
    public static string AwakeBatteryFloor => T("배터리가 적으면 멈추기", "Stop when the battery is low");
    public static string AwakeBatteryFloorDetail => T("배터리로 쓸 때만 해당합니다.", "Only while running on battery.");
    public static string AwakeOffChip => T("끔", "Off");
    public static string AwakeFooter => T("덮개를 닫거나 전원 단추를 누르면 평소대로 잠듭니다.", "Closing the lid or pressing the power button still sleeps.");
    // Sound
    public static string SoundSubtitle => T("프로그램마다 소리 크기를 정합니다.", "Set the volume of each program.");
    public static string SoundOutput => T("출력 장치 전체", "Output");
    public static string SoundApps => T("프로그램", "Programs");
    public static string SoundSystem => T("시스템 소리", "System sounds");
    public static string SoundMute => T("음소거", "Mute");
    public static string SoundNoApps => T("지금 소리를 내는 프로그램이 없습니다.", "No program has sound open right now.");
    public static string SoundNoOutput => T("출력 장치가 없습니다.", "There is no output device.");
    public static string SoundFooter => T("Windows가 프로그램마다 소리 크기를 기억합니다. 100%보다 크게 할 수는 없습니다.", "Windows remembers each program's level. It can't go above 100%.");
    // System
    public static string SysDarkOn => T("다크 모드로 바꿨습니다.", "Dark mode on.");
    public static string SysDarkOff => T("라이트 모드로 바꿨습니다.", "Light mode on.");
    public static string SysIconsShown => T("바탕 화면 아이콘을 보입니다.", "Desktop icons shown.");
    public static string SysIconsHidden => T("바탕 화면 아이콘을 숨겼습니다.", "Desktop icons hidden.");
    public static string SysNoDesktop => T("바탕 화면을 찾지 못했습니다.", "The desktop could not be found.");
    public static string SysHiddenShown => T("숨김 파일을 보입니다.", "Hidden files shown.");
    public static string SysHiddenHidden => T("숨김 파일을 숨겼습니다.", "Hidden files hidden.");
    public static string SysRecycleConfirm => T("휴지통을 비울까요? 지운 항목은 되살릴 수 없습니다.", "Empty the Recycle Bin? Items in it are deleted for good.");
    public static string SysRecycleAlreadyEmpty => T("휴지통이 이미 비어 있습니다.", "The Recycle Bin is already empty.");
    public static string SysRecycleEmptied => T("휴지통을 비웠습니다.", "Recycle Bin emptied.");
    public static string SysRecycleFailed => T("휴지통을 비우지 못했습니다.", "The Recycle Bin could not be emptied.");
    public static string SysNothingToEject => T("꺼낼 드라이브가 없습니다.", "Nothing to eject.");
    public static string SysEjected(int n) => T($"드라이브 {n}개를 꺼냈습니다.", n == 1 ? "Ejected 1 drive." : $"Ejected {n} drives.");
    public static string SysInUse(int n) => T($"드라이브 {n}개는 사용 중이라 꺼내지 못했습니다.", n == 1 ? "1 drive is in use." : $"{n} drives are in use.");
    public static string SysLockFailed => T("화면을 잠그지 못했습니다.", "The screen could not be locked.");
    public static string SysSubtitle => T("CPU, 메모리, GPU, 디스크, 네트워크와 많이 쓰는 프로그램.", "Processor, memory, GPU, disk, network and the busiest programs.");
    public static string SysMemory => T("메모리", "Memory");
    public static string SysDisk => T("디스크", "Disk");
    public static string SysDiskFree(string free) => T($"{free} 남음", $"{free} free");
    public static string SysNetwork => T("네트워크", "Network");
    public static string SysBattery => T("배터리", "Battery");
    public static string SysCharging => T("전원 연결됨", "Plugged in");
    public static string SysNoBattery => T("배터리 없음", "No battery");
    public static string SysUptime => T("켜진 시간", "Uptime");
    public static string SysUptimeValue(TimeSpan t) => t.TotalDays >= 1 ? T($"{(int)t.TotalDays}일 {t.Hours}시간", $"{(int)t.TotalDays}d {t.Hours}h") : T($"{t.Hours}시간 {t.Minutes}분", $"{t.Hours}h {t.Minutes}m");
    public static string SysTopPrograms => T("많이 쓰는 프로그램", "Busiest programs");
    public static string SysActions => T("빠른 동작", "Quick actions");
    public static string SysThreads(int n) => T($"스레드 {n}개", $"{n} threads");
    public static string SysNotAvailable => T("알 수 없음", "Unavailable");
    // Scratchpad
    public static string ScratchpadTitle => T("메모장", "Scratchpad");
    public static string ScratchpadTabName(int n) => T($"메모 {n}", $"Note {n}");
    public static string ScratchpadNewTab => T("새 메모 (Ctrl+T)", "New note (Ctrl+T)");
    public static string ScratchpadCloseTab => T("메모 닫기 (Ctrl+W)", "Close note (Ctrl+W)");
    public static string ScratchpadPin => T("항상 위에 고정", "Keep on top");
    public static string ScratchpadMore => T("더 보기", "More");
    public static string ScratchpadHide => T("숨기기 (Esc)", "Hide (Esc)");
    public static string ScratchpadShowPreview => T("미리 보기 (Ctrl+E)", "Preview (Ctrl+E)");
    public static string ScratchpadShowEditor => T("편집 (Ctrl+E)", "Edit (Ctrl+E)");
    public static string ScratchpadLinkFailed => T("링크를 열 수 없습니다.", "Couldn't open the link.");
    public static string ScratchpadCopyAll => T("이 메모 모두 복사", "Copy this note");
    public static string ScratchpadCopied => T("메모를 복사했습니다.", "Note copied.");
    public static string ScratchpadReopen => T("닫은 메모 다시 열기", "Reopen closed note");
    public static string ScratchpadClosed => T("메모를 닫았습니다.", "Note closed.");
    public static string ScratchpadClosedHint => T("Ctrl+Shift+T로 다시 열 수 있습니다.", "Ctrl+Shift+T brings it back.");
    public static string ScratchpadClearAfter => T("오래된 메모 닫기", "Close old notes");
    public static string ScratchpadClearNever => T("안 함", "Never");
    public static string ScratchpadClearDays(int days) => T($"{days}일 동안 고치지 않은 메모", days == 1 ? "Unedited for a day" : $"Unedited for {days} days");
    public static string ScratchpadClearHint => T("메모장을 열 때 확인하고, 닫은 메모 목록에 남깁니다.", "Checked when the pad opens; they stay in the closed list.");
    public static string ScratchpadNotFound => T("찾지 못했습니다.", "Not found.");
    public static string ScratchpadMatches(int count) => T($"{count}개 찾음", count == 1 ? "1 match" : $"{count} matches");
    public static string ScratchpadSaveFailed => T("메모를 저장하지 못했습니다. 다음에 고칠 때 다시 저장합니다.", "The note couldn't be saved. Orbit tries again on the next edit.");
    public static string ScratchpadLostAtQuit => T("메모를 저장하지 못해 모든 메모를 클립보드에 복사했습니다. 다른 곳에 붙여 넣으세요.", "The notes couldn't be saved, so all of them were copied to the clipboard. Paste them somewhere safe.");
    public static string ScratchpadNotSaving => T("메모 파일을 읽지 못해 저장하지 않습니다. Orbit을 다시 시작하세요.", "The notes file couldn't be read, so nothing is saved. Restart Orbit.");
    // Displays
    public static string DisplaysSubtitle => T("모든 화면의 밝기, 대비, 음량입니다. 외장 모니터는 DDC/CI로 직접 조절합니다.", "Brightness, contrast and volume for every screen. External monitors are set directly over DDC/CI.");
    public static string DisplaysAskAgain => T("다시 확인", "Ask again");
    public static string DisplaysAsking => T("화면을 확인하고 있습니다…", "Looking for screens…");
    public static string DisplaysBuiltInName => T("내장 화면", "Built-in display");
    public static string DisplaysMonitorName => T("모니터", "Monitor");
    public static string DisplaysKindBuiltIn => T("내장 화면 · Windows로 밝기를 조절합니다", "Built-in · set through Windows");
    public static string DisplaysKindDdc => T("DDC/CI · 모니터와 직접 통신합니다", "DDC/CI · Orbit talks to the monitor directly");
    public static string DisplaysKindNoDdc => T("이 모니터는 DDC/CI에 응답하지 않았습니다. 모니터 메뉴에서 DDC/CI를 켜세요.", "This monitor didn't answer over DDC/CI. Turn DDC/CI on in the monitor's menu.");
    public static string DisplaysKindNoPanel => T("이 화면은 Windows에서 밝기를 조절할 수 없습니다.", "Windows can't set this screen's brightness.");
    public static string DisplaysKindDuplicated => T("화면 복제 중이라 어느 모니터인지 알 수 없어 조절하지 않습니다.", "This screen is duplicated, so which monitor a change reaches can't be told; Orbit leaves it alone.");
    public static string DisplaysUnreachable => T("모니터가 응답하지 않습니다. 다시 확인하세요.", "The monitor stopped answering. Ask again.");
    public static string DisplaysBrightness => T("밝기", "Brightness");
    public static string DisplaysContrast => T("대비", "Contrast");
    public static string DisplaysVolume => T("음량", "Volume");
    public static string DisplaysMute => T("음소거", "Mute");
    public static string DisplaysUnmute => T("음소거 해제", "Unmute");
    public static string DisplaysNoMonitor => T("연결된 외장 모니터가 없습니다", "No external monitor connected");
    public static string DisplaysNoMonitorHint => T("모니터를 연결하면 여기에 나타납니다. 모니터가 허용하면 DDC/CI로 백라이트를 직접 조절합니다.", "Plug one in and it shows up here. Orbit sets its backlight over DDC/CI when the monitor allows it.");
    public static string DisplaysPresets => T("프리셋", "Presets");
    public static string DisplaysQuickLevels => T("빠른 밝기", "Quick levels");
    public static string DisplaysQuickLevelsHint => T("모든 화면을 한 번에 바꿉니다. 저장한 프리셋은 오른쪽 클릭으로 지웁니다.", "Every screen at once. Right-click one of yours to delete it.");
    public static string DisplaysBright => T("밝게", "Bright");
    public static string DisplaysComfort => T("편안하게", "Comfort");
    public static string DisplaysDim => T("어둡게", "Dim");
    public static string DisplaysSaveCurrent => T("지금 밝기 저장", "Save current levels");
    public static string DisplaysSaveCurrentHint => T("화면마다 지금 밝기를 이름 붙여 저장합니다.", "Keeps each screen's brightness as it is now, under a name.");
    public static string DisplaysSave => T("저장", "Save");
    public static string DisplaysPresetName => T("이름", "Name");
    public static string DisplaysDeletePreset => T("프리셋 삭제", "Delete preset");
    public static string DisplaysPresetApplied => T("밝기를 바꿨습니다.", "Brightness set.");
    public static string DisplaysActions => T("밝기 동작", "Brightness actions");
    public static string DisplaysActionsFooter => T("노트북 밝기 키는 Windows가 직접 처리해 Orbit이 가로챌 수 없습니다. 링, 런처, 단축키에 '밝기 올리기/내리기'를 넣어 쓰세요.", "A laptop's brightness keys are handled by Windows itself and can't be taken over. Put Brightness Up/Down on the ring, in the launcher or on a shortcut.");
    public static string DisplaysTarget => T("어느 화면", "Which screen");
    public static string DisplaysTargetHint => T("밝기 동작이 바꿀 화면입니다.", "The screen the brightness actions change.");
    public static string DisplaysTargetPointer => T("포인터가 있는 화면", "Under the pointer");
    public static string DisplaysTargetMain => T("주 화면", "Main");
    public static string DisplaysTargetAll => T("모두", "All");
    public static string DisplaysHud => T("밝기 표시", "Show the level");
    public static string DisplaysHudHint => T("밝기를 바꾸면 그 화면 아래에 밝기 막대를 잠시 보여 줍니다.", "Shows a brightness bar at the bottom of the screen that changed.");
    public static string DisplaysNothingToSet => T("밝기를 조절할 수 있는 화면이 없습니다.", "No screen here can change its brightness.");
    // Ports
    public static string PagePorts => T("포트", "Ports");
    public static string PortsSubtitle => T("프로그램이 연결을 기다리는 포트입니다. 개발 서버처럼 포트를 붙잡고 있는 프로그램을 찾아 닫을 수 있습니다.", "Ports programs listen on. Find the program holding a port, such as a dev server, and close it.");
    public static string PortsSearch => T("포트, 프로그램, PID로 찾기", "Find by port, program or PID");
    public static string PortsRefresh => T("새로 고침", "Refresh");
    public static string PortsShowUdp => T("UDP 포트도 보기", "Show UDP ports");
    public static string PortsCount(int tcp, int udp) => T($"TCP 수신 대기 {tcp}개 · UDP {udp}개", $"{tcp} TCP listening · {udp} UDP");
    public static string PortsLoading => T("포트를 읽고 있습니다…", "Reading ports…");
    public static string PortsEmpty => T("연결을 기다리는 포트가 없습니다.", "No listening ports.");
    public static string PortsNoMatch => T("찾는 포트가 없습니다.", "No ports match.");
    public static string PortsPartial => T("포트 목록 일부를 읽지 못했습니다. 새로 고침하세요.", "Some ports could not be read. Refresh to try again.");
    public static string PortsAllInterfaces => T("모든 인터페이스", "All interfaces");
    public static string PortsAllInterfacesHelp => T("모든 네트워크 인터페이스에서 연결을 받습니다. 방화벽이 막지 않으면 같은 네트워크의 다른 기기가 연결할 수 있습니다.", "Listening on every network interface, so other devices on the network may connect unless a firewall stops them.");
    public static string PortsUnknown => T("알 수 없는 프로그램", "Unknown program");
    public static string PortsClose => T("닫기", "Close");
    public static string PortsForce => T("강제 종료", "Force quit");
    public static string PortsCloseConfirm(string name, int port, int pid) => T(
        $"{name}의 창을 모두 닫을까요?\n포트 {port}을(를) 쓰는 프로그램(PID {pid})입니다. 저장하지 않은 내용이 있으면 프로그램이 물어봅니다.",
        $"Close every window of {name}?\nIt is the program using port {port} (PID {pid}). It asks about unsaved work itself.");
    public static string PortsForceConfirm(string name, int port, int pid) => T(
        $"{name}을(를) 강제로 종료할까요?\n포트 {port}을(를) 쓰는 프로그램(PID {pid})입니다. 저장하지 않은 내용은 사라지고, 이 프로그램이 쓰는 다른 포트도 모두 닫힙니다.",
        $"Force {name} to quit?\nIt is the program using port {port} (PID {pid}). Unsaved work is lost, and every other port it uses closes too.");
    public static string PortsClosed(string name) => T($"{name}을(를) 종료했습니다.", $"{name} quit.");
    public static string PortsStillRunning => T("닫으라고 요청했지만 아직 실행 중입니다. 저장할지 묻는 창이 떠 있을 수 있습니다.", "Asked to close, but it is still running. It may be asking about unsaved work.");
    public static string PortsNoWindow => T("닫을 창이 없습니다. 끝내려면 강제 종료하세요.", "It has no window to close. Use Force quit to end it.");
    public static string PortsGone => T("이미 종료된 프로그램입니다.", "That program has already quit.");
    public static string PortsChanged => T("목록을 읽은 뒤 프로그램이 바뀌었습니다. 새로 고침하세요.", "The program changed since the list was read. Refresh and try again.");
    public static string PortsDenied => T("권한이 없어 종료할 수 없습니다.", "Orbit isn't allowed to end it.");
    public static string PortsFailed => T("종료하지 못했습니다.", "It could not be ended.");
    public static string PortsBlockSystem => T("Windows 구성 요소라 종료할 수 없습니다.", "Part of Windows; it can't be ended here.");
    public static string PortsBlockSelf => T("Orbit입니다.", "This is Orbit.");
    public static string PortsBlockDenied => T("관리자 권한으로 실행 중이거나 다른 사용자의 프로그램이라 종료할 수 없습니다.", "Running as administrator or as another user, so it can't be ended here.");
    public static string PortsBlockUnverified => T("새로 고침한 뒤 종료할 수 있습니다.", "Refresh to be able to end it.");
    public static string PortsCopyPort => T("포트 복사", "Copy port");
    public static string PortsCopyAddress => T("주소:포트 복사", "Copy address:port");
    public static string PortsOpenBrowser => T("브라우저에서 열기", "Open in browser");
    public static string PortsCopied => T("복사했습니다.", "Copied.");
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
    public static string PageShortcuts => T("단축키", "Shortcuts");
    public static string ShortcutsSubtitle => T("모든 동작에 원하는 키를 지정할 수 있습니다.", "Give any action the keys you want.");
    public static string ShortcutRing => T("링 열기 (누르고 있기)", "Open the ring (hold)");
    public static string ShortcutNone => T("없음", "None");
    public static string ShortcutChange => T("바꾸기", "Change");
    public static string ShortcutReset => T("되돌리기", "Reset");
    public static string ShortcutPress => T("키를 누르세요. Esc는 취소, Delete는 지우기입니다.", "Press the keys. Esc cancels, Delete removes.");
    public static string ShortcutNeedsModifier => T("Ctrl, Alt, Win 중 하나와 함께 누르세요.", "Press it with Ctrl, Alt or Win.");
    public static string ShortcutArasoKey => T("한/영 키와 한자 키는 Araso가 쓰므로 지정할 수 없습니다.", "The Korean/English and Hanja keys belong to Araso.");
    public static string ShortcutReserved => T("Windows가 쓰는 키라서 지정할 수 없습니다.", "Windows keeps those keys.");
    public static string ShortcutInUse(string holder) => T($"이미 '{holder}'에 쓰고 있습니다.", $"Already used for \"{holder}\".");
    public static string ShortcutKnownOwner(string owners) => T($"{owners}에서도 쓰는 키입니다. Orbit이 먼저 받으므로 그쪽 기능은 실행되지 않습니다.", $"{owners} also use these keys. Orbit gets them first, so that won't run.");
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
        "Orbit for Windows는 3x-haust/Orbit(MIT)을 바탕으로 만들었습니다. 한/영 자동 전환은 nyttastudio/araso(MIT, 한국어 빈도 목록 CC BY-SA 4.0)를, QR 코드 인식은 ZXing.Net(Apache 2.0)을, 메모장 미리 보기는 Markdig(BSD-2-Clause)을 사용합니다.",
        "Orbit for Windows is based on 3x-haust/Orbit (MIT). Automatic Korean/English switching uses nyttastudio/araso (MIT; Korean frequency list CC BY-SA 4.0). QR codes are read with ZXing.Net (Apache 2.0). The Scratchpad preview uses Markdig (BSD-2-Clause).");
}
