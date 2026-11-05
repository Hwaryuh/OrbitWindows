using System.Text.Json.Nodes;
using Orbit.Core.Typing;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Orbit.Tests;

public sealed class FieldShadowTests
{
    private static EngineEdit Mark(string text, int at, int length) => new(EngineEdit.Mark, text, [at, length]);

    private static EngineEdit Insert(string text, int at, int length) => new(EngineEdit.Insert, text, [at, length]);

    [Fact]
    public void ComposingHangulTakesBackOnlyWhatChanged()
    {
        var shadow = new FieldShadow();
        Assert.Equal(new FieldEdit(0, "ㅎ"), shadow.Apply(Mark("ㅎ", 0, 0)));
        Assert.Equal(new FieldEdit(1, "하"), shadow.Apply(Mark("하", 0, 1)));
        Assert.Equal(new FieldEdit(1, "한"), shadow.Apply(Mark("한", 0, 1)));
        Assert.Equal(new FieldEdit(1, "하나"), shadow.Apply(Mark("하나", 0, 1)));
        Assert.Equal(new FieldEdit(0, "도"), shadow.Apply(Mark("하나도", 0, 2)));
        Assert.True(shadow.HasWord);
    }

    [Fact]
    public void AWordChangingLanguageReplacesIt()
    {
        var shadow = new FieldShadow();
        shadow.Apply(Mark("ㅊ", 0, 0));
        Assert.Equal(new FieldEdit(1, "ch"), shadow.Apply(Mark("ch", 0, 1)));
    }

    [Fact]
    public void ConfirmingAWordAsShownTypesNothingAndEndsIt()
    {
        var shadow = new FieldShadow();
        shadow.Apply(Mark("안녕", 0, 0));
        Assert.Equal(new FieldEdit(0, ""), shadow.Apply(Insert("안녕", 0, 2)));
        Assert.False(shadow.HasWord);
        Assert.Equal(new FieldEdit(0, " "), shadow.Apply(Insert(" ", 2, 0)));
        Assert.Equal([3, 0], shadow.Context().Selection);
        Assert.Equal("", shadow.Context().Text);
    }

    [Fact]
    public void AnEditPastTheHeldWordIsRefused()
    {
        var shadow = new FieldShadow();
        shadow.Apply(Insert("이전 ", 0, 0));
        shadow.Apply(Mark("글", 3, 0));
        Assert.Null(shadow.Apply(Insert("고침", 0, 2)));
    }

    [Fact]
    public void AnEditAwayFromTheCaretIsRefused()
    {
        var shadow = new FieldShadow();
        shadow.Apply(Mark("가나다", 0, 0));
        Assert.Null(shadow.Apply(Mark("마", 0, 1)));
    }

    [Fact]
    public void BackspacesAreBounded()
    {
        var shadow = new FieldShadow();
        string word = new('a', FieldShadow.MaxDeletes + 1);
        shadow.Apply(Mark(word, 0, 0));
        Assert.Null(shadow.Apply(Mark("한", 0, word.Length)));
    }

    [Fact]
    public void ABackspaceTakesAWholeCodePoint()
    {
        Assert.Equal(new FieldEdit(1, "a"), FieldShadow.Trim("😀", "a"));
        // Two emoji sharing a first unit: the shared half isn't kept.
        Assert.Equal(new FieldEdit(1, "😁"), FieldShadow.Trim("😀", "😁"));
    }

    [Fact]
    public void AnEmptyMarkTakesTheWordAwayAndLeavesTheCaretThere()
    {
        var shadow = new FieldShadow();
        shadow.Apply(Insert("앞", 0, 0));
        shadow.Apply(Mark("글", 1, 0));
        Assert.Equal(new FieldEdit(1, ""), shadow.Apply(Mark("", 1, 1)));
        Assert.False(shadow.HasWord);
        Assert.Equal([1, 0], shadow.Context().Selection);
        Assert.Null(shadow.Context().Marked);
    }

    [Fact]
    public void AMarkAfterTheWordStartKeepsWhatIsBeforeIt()
    {
        var shadow = new FieldShadow();
        shadow.Apply(Mark("가나", 0, 0));
        Assert.Equal(new FieldEdit(1, "라"), shadow.Apply(Mark("라", 1, 1)));
        var context = shadow.Context();
        Assert.Equal("가라", context.Text);
        Assert.Equal([2, 0], context.Selection);
        Assert.Equal([1, 1], context.Marked!);
    }

    [Fact]
    public void AfterAResetNothingOldIsOffered()
    {
        var shadow = new FieldShadow();
        shadow.Apply(Mark("가나", 0, 0));
        shadow.Reset();
        Assert.False(shadow.HasWord);
        Assert.Null(shadow.Context().Marked);
        Assert.Equal("", shadow.Context().Text);
        Assert.Equal(new FieldEdit(0, "다"), shadow.Apply(Mark("다", 2, 0)));
    }
}

public sealed class TypingGateTests
{
    private const uint A = 0x41, Back = 0x08, Left = 0x25, Enter = 0x0D, LShift = 0xA0, LControl = 0xA2, Hangul = 0x15;

    private static TypingGate Gate(bool active = true, bool word = false) => new() { Active = active, HasWord = word };

    [Fact]
    public void NothingIsTakenWhenNotActive()
    {
        var gate = Gate(active: false);
        Assert.Equal(GateAction.Pass, gate.Decide(A, true, false));
        Assert.Equal(GateAction.Pass, gate.Decide(A, false, false));
    }

    [Fact]
    public void ALetterIsQueuedAndItsUpDropped()
    {
        var gate = Gate();
        Assert.Equal(GateAction.Queue, gate.Decide(A, true, false));
        gate.Finished();
        Assert.False(gate.Busy);
        Assert.Equal(GateAction.Drop, gate.Decide(A, false, false));
        Assert.Equal(GateAction.Pass, gate.Decide(A, false, false));
    }

    [Fact]
    public void WhileBusyEveryEventKeepsItsPlace()
    {
        var gate = Gate();
        gate.Decide(A, true, false);
        Assert.Equal(GateAction.Queue, gate.Decide(LControl, true, false));
        Assert.Equal(GateAction.Queue, gate.Decide(LShift, true, false));
        Assert.Equal(GateAction.Queue, gate.Decide(Left, true, false));
        Assert.Equal(GateAction.Queue, gate.Decide(A, false, false));
    }

    [Fact]
    public void BusyOutlastsTheQueueUntilSendingIsDone()
    {
        var gate = Gate();
        gate.Decide(A, true, false);
        gate.Decide(A, true, false);
        gate.Finished();
        Assert.True(gate.Busy);
        gate.Finished();
        Assert.False(gate.Busy);
    }

    [Fact]
    public void ShortcutsPassWithNoWordHeld()
    {
        var gate = Gate();
        Assert.Equal(GateAction.Pass, gate.Decide(LControl, true, false));
        Assert.Equal(GateAction.Pass, gate.Decide(A, true, shortcutHeld: true));
        Assert.Equal(GateAction.Pass, gate.Decide(A, false, shortcutHeld: true));
        Assert.Equal(GateAction.Pass, gate.Decide(LControl, false, false));
    }

    [Fact]
    public void CtrlWaitsForAHeldWordToSettle()
    {
        var gate = Gate(word: true);
        Assert.Equal(GateAction.Queue, gate.Decide(LControl, true, false));
    }

    [Fact]
    public void ShiftPassesOnItsOwn()
    {
        var gate = Gate(word: true);
        Assert.Equal(GateAction.Pass, gate.Decide(LShift, true, false));
        Assert.Equal(GateAction.Pass, gate.Decide(LShift, false, false));
    }

    [Fact]
    public void KeysThatDoNotTypeWaitOnlyForAHeldWord()
    {
        Assert.Equal(GateAction.Pass, Gate().Decide(Left, true, false));
        Assert.Equal(GateAction.Pass, Gate().Decide(Back, true, false));
        Assert.Equal(GateAction.Pass, Gate().Decide(Enter, true, false));
        Assert.Equal(GateAction.Queue, Gate(word: true).Decide(Left, true, false));
        Assert.Equal(GateAction.Queue, Gate(word: true).Decide(Back, true, false));
        Assert.Equal(GateAction.Queue, Gate(word: true).Decide(Enter, true, false));
    }

    [Fact]
    public void TheHangulKeyIsAlwaysTakenWhenActive()
    {
        Assert.Equal(GateAction.Queue, Gate().Decide(Hangul, true, false));
        Assert.Equal(GateAction.Pass, Gate(active: false).Decide(Hangul, true, false));
    }

    [Fact]
    public void AKeySentOnKeepsItsRealUp()
    {
        var gate = Gate(word: true);
        gate.Decide(Left, true, false);
        gate.SentOn(Left);
        gate.Finished();
        Assert.Equal(GateAction.Pass, gate.Decide(Left, false, false));
    }

    [Fact]
    public void ARepeatTakenAfterTheAppHasTheKeyLeavesItsUpToTheApp()
    {
        var gate = Gate();
        Assert.Equal(GateAction.Pass, gate.Decide(A, true, shortcutHeld: true));
        Assert.Equal(GateAction.Queue, gate.Decide(A, true, false));
        gate.Finished();
        Assert.Equal(GateAction.Pass, gate.Decide(A, false, false));
    }

    [Fact]
    public void ARepeatQueuedBehindOneSentOnLeavesItsUpToTheApp()
    {
        var gate = Gate(word: true);
        gate.Decide(Left, true, false);
        gate.Decide(Left, true, false);
        gate.SentOn(Left);
        gate.Finished();
        gate.Finished();
        Assert.Equal(GateAction.Pass, gate.Decide(Left, false, false));
    }

    [Fact]
    public void ASentOnKeyStaysTheAppsWhenARepeatIsQueuedAfterIt()
    {
        var gate = Gate(word: true);
        gate.Decide(Left, true, false);
        gate.SentOn(Left);
        gate.Decide(Left, true, false);
        Assert.False(gate.TakeUp(Left));
    }

    [Fact]
    public void AKeyStartsFreshAfterItsUp()
    {
        var gate = Gate();
        gate.Decide(A, true, shortcutHeld: true);
        gate.Decide(A, false, shortcutHeld: true);
        gate.Decide(A, true, false);
        gate.Finished();
        Assert.Equal(GateAction.Drop, gate.Decide(A, false, false));
    }

    [Fact]
    public void AQueuedUpOfATakenKeyIsDroppedOnce()
    {
        var gate = Gate();
        gate.Decide(A, true, false);
        gate.Decide(A, false, false);
        Assert.True(gate.TakeUp(A));
        Assert.False(gate.TakeUp(A));
    }

    [Fact]
    public void TheUpOfATakenKeyIsStillDroppedWhenPausedMidKey()
    {
        var gate = Gate();
        gate.Decide(A, true, false);
        gate.Finished();
        gate.Active = false;
        Assert.Equal(GateAction.Drop, gate.Decide(A, false, false));
    }
}

public sealed class KeyOutputTests
{
    private static List<(ushort Vk, ushort Scan, bool Up, bool Unicode)> Events(INPUT[] inputs) =>
        [.. inputs.Select(i => (
            (ushort)i.Anonymous.ki.wVk,
            i.Anonymous.ki.wScan,
            (i.Anonymous.ki.dwFlags & KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP) != 0,
            (i.Anonymous.ki.dwFlags & KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE) != 0))];

    [Fact]
    public void BackspacesThenHangulAsUnicode()
    {
        var events = Events(KeyOutput.Build([new FieldEdit(1, "한")], false, false, true));
        Assert.Equal(
            [((ushort)8, (ushort)0x0E, false, false), (8, 0x0E, true, false), (0, '한', false, true), (0, '한', true, true)],
            events);
    }

    [Fact]
    public void EveryEventCarriesTheMarker()
    {
        foreach (var input in KeyOutput.Build([new FieldEdit(2, "aB한")], false, false, true))
            Assert.Equal(KeyOutput.Marker, input.Anonymous.ki.dwExtraInfo);
    }

    [Fact]
    public void LettersGoAsTheirKeys()
    {
        var events = Events(KeyOutput.Build([new FieldEdit(0, "a")], false, false, true));
        Assert.Equal([((ushort)'A', (ushort)0x1E, false, false), ('A', 0x1E, true, false)], events);
    }

    [Fact]
    public void ACapitalIsWrappedInShift()
    {
        var events = Events(KeyOutput.Build([new FieldEdit(0, "Q")], false, false, true));
        Assert.Equal(
            [((ushort)0xA0, (ushort)0x2A, false, false), ('Q', 0x10, false, false), ('Q', 0x10, true, false), (0xA0, 0x2A, true, false)],
            events);
    }

    [Fact]
    public void HeldShiftIsLiftedForALowerCaseLetterAndPutBack()
    {
        var events = Events(KeyOutput.Build([new FieldEdit(0, "a")], true, false, true));
        Assert.Equal(
            [((ushort)0xA0, (ushort)0x2A, true, false), ('A', 0x1E, false, false), ('A', 0x1E, true, false), (0xA0, 0x2A, false, false)],
            events);
    }

    [Fact]
    public void HeldShiftStaysForACapital()
    {
        var events = Events(KeyOutput.Build([new FieldEdit(0, "A")], true, false, true));
        Assert.Equal([((ushort)'A', (ushort)0x1E, false, false), ('A', 0x1E, true, false)], events);
    }

    [Fact]
    public void CapsLockFlipsLettersOnly()
    {
        Assert.Equal(2, KeyOutput.Build([new FieldEdit(0, "A")], false, true, true).Length);
        Assert.Equal(4, KeyOutput.Build([new FieldEdit(0, "a")], false, true, true).Length);
        Assert.Equal(4, KeyOutput.Build([new FieldEdit(0, "!")], false, true, true).Length);
        Assert.Equal(2, KeyOutput.Build([new FieldEdit(0, "1")], false, true, true).Length);
    }

    [Fact]
    public void AnotherLayoutGetsUnicodeOnly()
    {
        Assert.All(Events(KeyOutput.Build([new FieldEdit(0, "aZ1")], false, false, false)), e => Assert.True(e.Unicode));
    }

    [Theory]
    [InlineData('1', 0x31, 0x02, false)]
    [InlineData('!', 0x31, 0x02, true)]
    [InlineData('0', 0x30, 0x0B, false)]
    [InlineData('-', 0xBD, 0x0C, false)]
    [InlineData('p', 0x50, 0x19, false)]
    [InlineData('[', 0xDB, 0x1A, false)]
    [InlineData('l', 0x4C, 0x26, false)]
    [InlineData(';', 0xBA, 0x27, false)]
    [InlineData('"', 0xDE, 0x28, true)]
    [InlineData('~', 0xC0, 0x29, true)]
    [InlineData('\\', 0xDC, 0x2B, false)]
    [InlineData('m', 0x4D, 0x32, false)]
    [InlineData('?', 0xBF, 0x35, true)]
    [InlineData(' ', 0x20, 0x39, false)]
    public void KeysSitWhereAUsKeyboardHasThem(char c, int vk, int scan, bool shift)
    {
        var events = Events(KeyOutput.Build([new FieldEdit(0, c.ToString())], false, false, true));
        var key = events[shift ? 1 : 0];
        Assert.Equal(vk, key.Vk);
        Assert.Equal(scan, key.Scan);
        Assert.Equal(shift ? 4 : 2, events.Count);
    }

    [Fact]
    public void AnEmptyEditSendsNothing()
    {
        Assert.Empty(KeyOutput.Build([new FieldEdit(0, "")], true, false, true));
    }
}

public sealed class TypingSessionTests
{
    private sealed class FakeEngine(params EngineReply[] replies) : ITypingEngine
    {
        private int _next;
        public int LetGoes { get; private set; }
        public List<EngineKey> Keys { get; } = [];

        public EngineReply Key(EngineKey key, EngineContext context)
        {
            Keys.Add(key);
            return replies[_next++];
        }

        public void LetGo() => LetGoes++;
    }

    private static EngineReply Took(params EngineEdit[] edits) => new(true, edits, false);

    private static EngineKey Letter => new(0x41, 0x1E);

    [Fact]
    public void EditsAreTurnedIntoKeystrokes()
    {
        var session = new TypingSession(new FakeEngine(Took(new EngineEdit(EngineEdit.Mark, "ㅎ", [0, 0]))));
        var output = session.Press(Letter);
        Assert.Equal([new FieldEdit(0, "ㅎ")], output.Edits);
        Assert.False(output.SendKey);
        Assert.True(session.HasWord);
    }

    [Fact]
    public void AKeyTheEngineLeavesGoesOnAndTheBoxIsForgotten()
    {
        var engine = new FakeEngine(Took(new EngineEdit(EngineEdit.Mark, "ㅎ", [0, 0])), new EngineReply(false, [], false));
        var session = new TypingSession(engine);
        session.Press(Letter);
        var output = session.Press(new EngineKey(0x25, 0x4B, Extended: true));
        Assert.True(output.SendKey);
        Assert.False(session.HasWord);
        Assert.Equal(1, engine.LetGoes);
    }

    [Fact]
    public void AReplayGoesOnAfterItsEdits()
    {
        var engine = new FakeEngine(new EngineReply(false, [new EngineEdit(EngineEdit.Insert, "x", [0, 0])], true));
        var output = new TypingSession(engine).Press(new EngineKey(0x0D, 0x1C));
        Assert.Equal([new FieldEdit(0, "x")], output.Edits);
        Assert.True(output.SendKey);
    }

    [Fact]
    public void ARefusedEditStopsTheRestAndForgets()
    {
        var engine = new FakeEngine(Took(
            new EngineEdit(EngineEdit.Mark, "ㅎ", [0, 0]),
            new EngineEdit(EngineEdit.Insert, "멀리", [99, 1]),
            new EngineEdit(EngineEdit.Insert, "끝", null)));
        var session = new TypingSession(engine);
        var output = session.Press(Letter);
        Assert.Equal([new FieldEdit(0, "ㅎ")], output.Edits);
        Assert.False(session.HasWord);
        Assert.Equal(1, engine.LetGoes);
    }

    [Fact]
    public void TheHangulKeyNeverReachesTheApp()
    {
        var engine = new FakeEngine(new EngineReply(false, [], true));
        var output = new TypingSession(engine).Press(new EngineKey(0x15, 0x72));
        Assert.Empty(output.Edits);
        Assert.False(output.SendKey);
    }

    [Fact]
    public void TheHangulKeyChangesAWordThroughTheEngine()
    {
        var engine = new FakeEngine(Took(new EngineEdit(EngineEdit.Mark, "ㅎ", [0, 0])), Took(new EngineEdit(EngineEdit.Mark, "g", [0, 1])));
        var session = new TypingSession(engine);
        session.Press(Letter);
        var output = session.Press(new EngineKey(0x15, 0x72));
        Assert.Equal(0x15, engine.Keys[1].Vk);
        Assert.Equal([new FieldEdit(1, "g")], output.Edits);
        Assert.True(session.HasWord);
    }

    [Fact]
    public void ForgettingWithNoWordDoesNotBotherTheEngine()
    {
        var engine = new FakeEngine();
        new TypingSession(engine).Forget();
        Assert.Equal(0, engine.LetGoes);
    }
}

public sealed class EngineSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "orbit-typing-" + Guid.NewGuid().ToString("N"));

    public EngineSettingsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private string File_ => Path.Combine(_folder, "settings.json");

    [Fact]
    public void ReturnReachesTheApp()
    {
        EngineBridge.PrepareSettings(File_);
        Assert.False(JsonNode.Parse(File.ReadAllText(File_))!["returnConfirmsOnly"]!.GetValue<bool>());
    }

    [Fact]
    public void OtherSettingsAreKept()
    {
        File.WriteAllText(File_, """{"fixSlips":true,"returnConfirmsOnly":true}""");
        EngineBridge.PrepareSettings(File_);
        var settings = JsonNode.Parse(File.ReadAllText(File_))!;
        Assert.True(settings["fixSlips"]!.GetValue<bool>());
        Assert.False(settings["returnConfirmsOnly"]!.GetValue<bool>());
    }

    [Fact]
    public void AnUnreadableFileIsReplaced()
    {
        File.WriteAllText(File_, "not json");
        EngineBridge.PrepareSettings(File_);
        Assert.False(JsonNode.Parse(File.ReadAllText(File_))!["returnConfirmsOnly"]!.GetValue<bool>());
    }
}

/// <summary>
/// The real engine, when tools/araso/build.ps1 has built it. Otherwise these tests return at once,
/// so a pass here says nothing about the engine on a machine without the build.
/// </summary>
public sealed class TypingEngineTests
{
    private static readonly Lazy<EngineBridge?> Engine = new(Load);

    private static EngineBridge? Load()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            string stage = Path.Combine(folder.FullName, "artifacts", "araso-stage");
            if (File.Exists(Path.Combine(stage, "ArasoBridge.dll")))
                return EngineBridge.Start(stage, Path.Combine(Path.GetTempPath(), "orbit-typing-engine-" + Guid.NewGuid().ToString("N")));
        }
        return null;
    }

    private static readonly Lock Gate = new();

    private static EngineKey KeyFor(char c) => c switch
    {
        ' ' => new EngineKey(0x20, 0x39),
        '\b' => new EngineKey(0x08, 0x0E),
        '\r' => new EngineKey(0x0D, 0x1C),
        '\u0015' => new EngineKey(0x15, 0x72),
        '\u0011' => new EngineKey(0xA2, 0x1D, Ctrl: true),
        '%' => new EngineKey(0x25, 0x4B, Extended: true),
        _ => new EngineKey(char.ToUpperInvariant(c), Scan(char.ToLowerInvariant(c))),
    };

    private static int Scan(char c)
    {
        const string Rows = "qwertyuiop\0\0\0\0asdfghjkl\0\0\0\0\0zxcvbnm";
        return Rows.IndexOf(c) + 0x10;
    }

    /// <summary>Plays keys into a pretend box the way the app's box would take the keystrokes.</summary>
    private static (string Box, List<bool> Sent) Type(TypingSession session, string keys)
    {
        var box = new System.Text.StringBuilder();
        var sent = new List<bool>();
        foreach (char c in keys)
        {
            var output = session.Press(KeyFor(c));
            foreach (var edit in output.Edits)
            {
                box.Length -= edit.Deletes;
                box.Append(edit.Text);
            }
            sent.Add(output.SendKey);
        }
        session.Forget();
        return (box.ToString(), sent);
    }

    [Theory]
    [InlineData("dkssud ", "안녕 ")]
    [InlineData("hello ", "hello ")]
    [InlineData("rmfjsep ", "그런데 ")]
    [InlineData("chromedptj ", "chrome에서 ")]
    [InlineData("github\bdp ", "githu에 ")]
    public void WordsComeOutAsTheEngineDecides(string keys, string expected)
    {
        lock (Gate)
        {
            if (Engine.Value is not { } engine) return;
            Assert.Equal(expected, Type(new TypingSession(engine), keys).Box);
        }
    }

    [Fact]
    public void BackspaceCanEmptyAWordAndTypingGoesOn()
    {
        lock (Gate)
        {
            if (Engine.Value is not { } engine) return;
            Assert.Equal("안녕", Type(new TypingSession(engine), "g\bdkssud").Box);
        }
    }

    [Fact]
    public void TheHangulKeyKeepsTheWordHeld()
    {
        lock (Gate)
        {
            if (Engine.Value is not { } engine) return;
            var session = new TypingSession(engine);
            session.Press(KeyFor('d'));
            session.Press(KeyFor('\u0015'));
            Assert.True(session.HasWord);
            session.Forget();
        }
    }

    [Fact]
    public void ReturnGoesToTheAppAfterTheWord()
    {
        lock (Gate)
        {
            if (Engine.Value is not { } engine) return;
            var (box, sent) = Type(new TypingSession(engine), "gkrh\r");
            Assert.Equal("하고", box);
            Assert.True(sent[^1]);
        }
    }

    [Fact]
    public void CtrlSettlesTheWordBeforeItGoesOn()
    {
        lock (Gate)
        {
            if (Engine.Value is not { } engine) return;
            var session = new TypingSession(engine);
            var (box, sent) = Type(session, "dkssud\u0011");
            Assert.Equal("안녕", box);
            Assert.True(sent[^1]);
            Assert.False(session.HasWord);
        }
    }

    [Fact]
    public void AnArrowGoesToTheAppAndEndsTheWord()
    {
        lock (Gate)
        {
            if (Engine.Value is not { } engine) return;
            var session = new TypingSession(engine);
            var (box, sent) = Type(session, "dkssud%");
            Assert.Equal("안녕", box);
            Assert.True(sent[^1]);
            Assert.False(session.HasWord);
        }
    }

    [Fact]
    public void TheHangulKeyChangesTheHeldWord()
    {
        lock (Gate)
        {
            if (Engine.Value is not { } engine) return;
            Assert.Equal("dkssud", Type(new TypingSession(engine), "dkssud\u0015").Box);
        }
    }
}
