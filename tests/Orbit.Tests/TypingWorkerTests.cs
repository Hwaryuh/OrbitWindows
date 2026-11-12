using Orbit.Core.Typing;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Orbit.Tests;

internal sealed class FakeTypingHost : ITypingHost
{
    public TypingPlace PlaceResult { get; set; } = new(TypingPause.None, true, false);
    public Queue<FieldSnapshot?> Fields { get; } = new();
    public FieldSnapshot? DefaultField { get; set; } = new(false, true, null, false, [1]);
    public bool ImeAnswers { get; set; } = true;
    public Queue<bool> ImeReplies { get; } = new();
    public int ImeAsks { get; private set; }
    public bool Accepts { get; set; } = true;
    public int Reads { get; private set; }
    public int ImeReleases { get; private set; }
    public List<INPUT[]> Sent { get; } = [];
    public bool ShiftDown { get; set; }
    public bool ShortcutDown { get; set; }
    public nint FocusWindow { get; set; } = 1;

    public TypingPlace Place() => PlaceResult;

    public FieldSnapshot? ReadField(int length)
    {
        Reads++;
        return Fields.Count > 0 ? Fields.Dequeue() : DefaultField;
    }

    public bool HoldIme()
    {
        ImeAsks++;
        return ImeReplies.Count > 0 ? ImeReplies.Dequeue() : ImeAnswers;
    }

    public void ReleaseIme() => ImeReleases++;

    public bool Send(INPUT[] events)
    {
        lock (Sent) Sent.Add(events);
        return Accepts;
    }

    public void Wait(int milliseconds)
    {
    }
}

public sealed class TypingCoreTests
{
    private sealed class Engine(params EngineReply[] replies) : ITypingEngine
    {
        private int _next;
        public int Keys { get; private set; }
        public int LetGoes { get; private set; }

        public EngineReply Key(EngineKey key, EngineContext context)
        {
            Keys++;
            return replies[_next++];
        }

        public void LetGo() => LetGoes++;
    }

    private static EngineReply Marks(string text, int at, int length) => new(true, [new EngineEdit(EngineEdit.Mark, text, [at, length])], false);

    private static KeyEvent Key(uint vk = 0x41, bool down = true, long at = 1000, bool ctrl = false) =>
        new(vk, 0x1E, down, false, false, ctrl, false, false, false, at);

    private static (TypingCore Core, TypingGate Gate, FakeTypingHost Host) Make(ITypingEngine engine, FakeTypingHost? host = null)
    {
        host ??= new FakeTypingHost();
        var gate = new TypingGate();
        var core = new TypingCore(engine, host, gate) { Enabled = true };
        core.Look(false);
        return (core, gate, host);
    }

    private static bool IsRaw(KeyPlan plan, uint vk) =>
        plan.Events.Length == 1 && (uint)plan.Events[0].Anonymous.ki.wVk == vk && plan.ReachesApp;

    [Fact]
    public void KeysAreTakenInATextBox()
    {
        var (core, gate, _) = Make(new Engine());
        Assert.True(gate.Active);
        Assert.Equal(TypingPause.None, core.Pause);
    }

    [Theory]
    [InlineData(TypingPause.OwnWindow)]
    [InlineData(TypingPause.ExcludedApp)]
    [InlineData(TypingPause.Terminal)]
    public void ThePlaceCanRuleKeysOut(TypingPause pause)
    {
        var (core, gate, host) = Make(new Engine(), new FakeTypingHost { PlaceResult = new TypingPlace(pause, true, false) });
        Assert.False(gate.Active);
        Assert.Equal(pause, core.Pause);
        Assert.Equal(0, host.Reads);
    }

    [Fact]
    public void APasswordFieldIsLeftAlone()
    {
        var (core, gate, _) = Make(new Engine(), new FakeTypingHost { DefaultField = new FieldSnapshot(true, true, null, false, [1]) });
        Assert.False(gate.Active);
        Assert.Equal(TypingPause.Password, core.Pause);
    }

    [Fact]
    public void SomethingThatTakesNoTextIsLeftAlone()
    {
        var (core, gate, _) = Make(new Engine(), new FakeTypingHost { DefaultField = new FieldSnapshot(false, false, null, false, [1]) });
        Assert.False(gate.Active);
        Assert.Equal(TypingPause.NotText, core.Pause);
    }

    [Fact]
    public void AnUnreadableWindowWithNoCaretIsLeftAlone()
    {
        var host = new FakeTypingHost { DefaultField = null, PlaceResult = new TypingPlace(TypingPause.None, true, false, MayTakeText: false) };
        var (core, gate, _) = Make(new Engine(), host);
        Assert.False(gate.Active);
        Assert.Equal(TypingPause.NotText, core.Pause);
    }

    [Fact]
    public void AnUnreadableWindowWithACaretStillTakesKeys()
    {
        var (core, gate, _) = Make(new Engine(), new FakeTypingHost { DefaultField = null });
        Assert.True(gate.Active);
        Assert.Equal(TypingPause.None, core.Pause);
        Assert.True(core.Unread);
    }

    [Fact]
    public void AnImeThatDoesNotAnswerPauses()
    {
        var (core, gate, _) = Make(new Engine(), new FakeTypingHost { ImeAnswers = false });
        Assert.False(gate.Active);
        Assert.Equal(TypingPause.ImeNotResponding, core.Pause);
    }

    [Fact]
    public void GoingFromOnToPausedGivesTheImeBack()
    {
        var host = new FakeTypingHost();
        var (core, _, _) = Make(new Engine(), host);
        host.PlaceResult = new TypingPlace(TypingPause.Terminal, true, false);
        core.Look(false);
        Assert.Equal(1, host.ImeReleases);
    }

    [Fact]
    public void TurningOffTakesNoKeys()
    {
        var (core, gate, _) = Make(new Engine());
        core.Enabled = false;
        core.Look(false);
        Assert.False(gate.Active);
        Assert.Equal(TypingPause.Off, core.Pause);
    }

    [Fact]
    public void ABoxNeverReadSendsTheWholeWordAsTypedAndTheEngineNeverSeesIt()
    {
        var engine = new Engine();
        var host = new FakeTypingHost { DefaultField = null };
        var (core, _, _) = Make(engine, host);
        Assert.True(IsRaw(core.Plan(Key()), 0x41));
        // Readable by now, but this word has begun as typed, and stays so.
        host.DefaultField = new FieldSnapshot(false, true, null, false, [1]);
        Assert.True(IsRaw(core.Plan(Key(0x42)), 0x42));
        Assert.True(IsRaw(core.Plan(Key(0x43)), 0x43));
        Assert.Equal(0, engine.Keys);
    }

    [Fact]
    public void AfterASpaceTheNextWordGoesToTheEngineAgain()
    {
        var engine = new Engine(Marks("ㅎ", 0, 0));
        var host = new FakeTypingHost { DefaultField = null };
        var (core, _, _) = Make(engine, host);
        core.Plan(Key());
        Assert.True(IsRaw(core.Plan(Key(0x20)), 0x20));
        host.DefaultField = new FieldSnapshot(false, true, null, false, [1]);
        Assert.False(core.Plan(Key()).ReachesApp);
        Assert.Equal(1, engine.Keys);
    }

    [Fact]
    public void ASlowReadInABoxAlreadyClearedDoesNotStopTyping()
    {
        var engine = new Engine(Marks("ㅎ", 0, 0));
        var host = new FakeTypingHost();
        var (core, _, _) = Make(engine, host);
        host.Fields.Enqueue(null);
        Assert.False(core.Plan(Key()).ReachesApp);
        Assert.Equal(1, engine.Keys);
    }

    [Fact]
    public void ASlowReadAfterFocusMovedCountsAsNeverRead()
    {
        var engine = new Engine();
        var host = new FakeTypingHost();
        var (core, _, _) = Make(engine, host);
        host.FocusWindow = 2;
        host.Fields.Enqueue(null);
        Assert.True(IsRaw(core.Plan(Key()), 0x41));
        Assert.Equal(0, engine.Keys);
    }

    [Fact]
    public void WhereTheBoxReportsLateItsTextIsNotCompared()
    {
        var engine = new Engine(Marks("ㅊ", 0, 0), Marks("ch", 0, 1));
        var host = new FakeTypingHost { PlaceResult = new TypingPlace(TypingPause.None, true, true) };
        var (core, _, _) = Make(engine, host);
        core.Plan(Key());
        // The accessibility tree hasn't caught up with the ㅊ just sent.
        host.Fields.Enqueue(new FieldSnapshot(false, true, "", false, [1]));
        var plan = core.Plan(Key(0x48));
        Assert.False(plan.ReachesApp);
        Assert.Equal(0, engine.LetGoes);
    }

    [Fact]
    public void APasswordFoundAtAWordStartNeverReachesTheEngine()
    {
        var engine = new Engine();
        var host = new FakeTypingHost();
        var (core, gate, _) = Make(engine, host);
        host.Fields.Enqueue(new FieldSnapshot(true, true, null, false, [1]));
        Assert.True(IsRaw(core.Plan(Key()), 0x41));
        Assert.Equal(0, engine.Keys);
        Assert.False(gate.Active);
        Assert.Equal(TypingPause.Password, core.Pause);
    }

    [Fact]
    public void APasswordFoundAtAWordStartGivesTheImeBack()
    {
        var host = new FakeTypingHost();
        var (core, _, _) = Make(new Engine(), host);
        host.Fields.Enqueue(new FieldSnapshot(true, true, null, false, [1]));
        core.Plan(Key());
        Assert.Equal(1, host.ImeReleases);
    }

    [Fact]
    public void FocusMovingUnderAHeldWordChecksTheNewBoxBeforeTheEngine()
    {
        var engine = new Engine(Marks("ㅎ", 0, 0));
        var host = new FakeTypingHost();
        var (core, gate, _) = Make(engine, host);
        core.Plan(Key());
        host.FocusWindow = 2;
        host.Fields.Enqueue(new FieldSnapshot(true, true, null, false, [2]));
        Assert.True(IsRaw(core.Plan(Key(0x42)), 0x42));
        Assert.Equal(1, engine.Keys);
        Assert.Equal(1, engine.LetGoes);
        Assert.False(gate.Active);
    }

    private static readonly FieldSnapshot Terminal = new(false, true, "", false, [1], IsTerminal: true);

    [Fact]
    public void ATerminalInsideAnAppIsLeftAlone()
    {
        var (core, gate, _) = Make(new Engine(), new FakeTypingHost { DefaultField = Terminal });
        Assert.False(gate.Active);
        Assert.Equal(TypingPause.Terminal, core.Pause);
    }

    [Fact]
    public void ATerminalFoundAtAWordStartNeverReachesTheEngine()
    {
        var engine = new Engine();
        var host = new FakeTypingHost();
        var (core, gate, _) = Make(engine, host);
        host.Fields.Enqueue(Terminal);
        Assert.True(IsRaw(core.Plan(Key()), 0x41));
        Assert.Equal(0, engine.Keys);
        Assert.False(gate.Active);
    }

    [Fact]
    public void ATerminalIsTakenWhenTheUserAsked()
    {
        var engine = new Engine(Marks("ㅎ", 0, 0));
        var host = new FakeTypingHost { DefaultField = Terminal, PlaceResult = new TypingPlace(TypingPause.None, true, true, AllowTerminals: true) };
        var (core, gate, _) = Make(engine, host);
        Assert.True(gate.Active);
        Assert.False(core.Plan(Key()).ReachesApp);
        Assert.Equal(1, engine.Keys);
    }

    [Fact]
    public void APasswordIsLeftAloneEvenWhereTerminalsAreAllowed()
    {
        var host = new FakeTypingHost
        {
            DefaultField = new FieldSnapshot(true, true, null, false, [1], IsTerminal: true),
            PlaceResult = new TypingPlace(TypingPause.None, true, true, AllowTerminals: true),
        };
        var (core, gate, _) = Make(new Engine(), host);
        Assert.False(gate.Active);
        Assert.Equal(TypingPause.Password, core.Pause);
    }

    [Fact]
    public void AWordGoesOutAsEdits()
    {
        var (core, _, _) = Make(new Engine(Marks("ㅎ", 0, 0)));
        var plan = core.Plan(Key());
        Assert.False(plan.ReachesApp);
        Assert.Equal(2, plan.Events.Length);
        Assert.True((plan.Events[0].Anonymous.ki.dwFlags & KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE) != 0);
    }

    [Fact]
    public void WhenPausedAKeyGoesOnAsItCame()
    {
        var engine = new Engine();
        var (core, gate, _) = Make(engine);
        gate.Active = false;
        Assert.True(IsRaw(core.Plan(Key()), 0x41));
        Assert.Equal(0, engine.Keys);
    }

    [Fact]
    public void AQueuedUpOfATakenKeyGoesNowhere()
    {
        var (core, gate, _) = Make(new Engine());
        gate.Decide(0x41, true, false);
        Assert.Empty(core.Plan(Key(down: false)).Events);
    }

    [Fact]
    public void AQueuedUpOfAKeyTheAppHasGoesOn()
    {
        var (core, gate, _) = Make(new Engine());
        gate.Decide(0x25, true, false);
        gate.SentOn(0x25);
        var plan = core.Plan(Key(0x25, down: false));
        Assert.Single(plan.Events);
        Assert.False(plan.ReachesApp);
    }

    [Fact]
    public void ACheckedEditThatDoesNotMatchTheBoxTouchesNothing()
    {
        var engine = new Engine(Marks("가나", 0, 0), Marks("hi", 0, 2));
        var host = new FakeTypingHost();
        var (core, _, _) = Make(engine, host);
        core.Plan(Key());
        host.Fields.Enqueue(new FieldSnapshot(false, true, "다른 글", false, [1]));
        host.Fields.Enqueue(new FieldSnapshot(false, true, "다른 글", false, [1]));
        var plan = core.Plan(Key(0x42));
        Assert.True(IsRaw(plan, 0x42));
        Assert.Equal(1, engine.LetGoes);
    }

    [Fact]
    public void ACheckThatLagsIsReadAgain()
    {
        var engine = new Engine(Marks("가나", 0, 0), Marks("hi", 0, 2));
        var host = new FakeTypingHost();
        var (core, _, _) = Make(engine, host);
        core.Plan(Key());
        host.Fields.Enqueue(new FieldSnapshot(false, true, "가", false, [1]));
        host.Fields.Enqueue(new FieldSnapshot(false, true, "앞 가나", false, [1]));
        var plan = core.Plan(Key(0x42));
        Assert.False(plan.ReachesApp);
        Assert.Equal(0, engine.LetGoes);
    }

    [Fact]
    public void OneBackspaceGoesUncheckedInOrdinaryApps()
    {
        var engine = new Engine(Marks("ㅎ", 0, 0), Marks("하", 0, 1));
        var host = new FakeTypingHost();
        var (core, _, _) = Make(engine, host);
        core.Plan(Key());
        int reads = host.Reads;
        core.Plan(Key(0x4B));
        Assert.Equal(reads, host.Reads);
    }

    [Fact]
    public void WhereTextChangesItselfEveryEditIsChecked()
    {
        var engine = new Engine(Marks("ㅎ", 0, 0), Marks("하", 0, 1));
        var host = new FakeTypingHost { PlaceResult = new TypingPlace(TypingPause.None, true, true) };
        var (core, _, _) = Make(engine, host);
        core.Plan(Key());
        int reads = host.Reads;
        core.Plan(Key(0x4B));
        Assert.Equal(reads + 1, host.Reads);
    }

    [Fact]
    public void ASelectionTakesOneMoreBackspace()
    {
        var engine = new Engine(Marks("ㅎ", 0, 0), Marks("하", 0, 1));
        var host = new FakeTypingHost { PlaceResult = new TypingPlace(TypingPause.None, true, true) };
        var (core, _, _) = Make(engine, host);
        core.Plan(Key());
        host.Fields.Enqueue(new FieldSnapshot(false, true, null, true, [1]));
        var plan = core.Plan(Key(0x4B));
        Assert.Equal(6, plan.Events.Length);
    }

    [Fact]
    public void WithCtrlDownNoEditGoesOut()
    {
        var engine = new Engine(Marks("ㅎ", 0, 0));
        var host = new FakeTypingHost { ShortcutDown = true };
        var (core, _, _) = Make(engine, host);
        Assert.True(IsRaw(core.Plan(Key()), 0x41));
    }

    [Fact]
    public void AWordLeftIdleIsLetGo()
    {
        var engine = new Engine(Marks("ㅎ", 0, 0), Marks("ㅏ", 0, 0));
        var (core, _, _) = Make(engine);
        core.Plan(Key(at: 1000));
        core.Plan(Key(at: 12_000));
        Assert.Equal(1, engine.LetGoes);
    }

    [Fact]
    public void AFocusEventForTheSameBoxKeepsTheWord()
    {
        var engine = new Engine(Marks("ㅎ", 0, 0));
        var (core, _, _) = Make(engine);
        core.Plan(Key());
        core.Look(sameBoxKeepsWord: true);
        Assert.Equal(0, engine.LetGoes);
    }

    [Fact]
    public void AFocusEventForAnotherBoxLetsTheWordGo()
    {
        var engine = new Engine(Marks("ㅎ", 0, 0));
        var host = new FakeTypingHost();
        var (core, _, _) = Make(engine, host);
        core.Plan(Key());
        host.DefaultField = new FieldSnapshot(false, true, null, false, [2]);
        core.Look(sameBoxKeepsWord: true);
        Assert.Equal(1, engine.LetGoes);
    }

    [Fact]
    public void AnotherProgramLetsTheWordGoEvenIfItLooksTheSame()
    {
        var engine = new Engine(Marks("ㅎ", 0, 0));
        var (core, _, _) = Make(engine);
        core.Plan(Key());
        core.Look(sameBoxKeepsWord: false);
        Assert.Equal(1, engine.LetGoes);
    }

    [Fact]
    public void EventsRefusedLetTheWordGo()
    {
        var engine = new Engine(Marks("ㅎ", 0, 0));
        var (core, gate, _) = Make(engine);
        core.Plan(Key());
        core.Sent(false);
        Assert.Equal(1, engine.LetGoes);
        Assert.False(gate.HasWord);
    }

    [Fact]
    public void EventsAcceptedPublishTheWord()
    {
        var (core, gate, _) = Make(new Engine(Marks("ㅎ", 0, 0)));
        core.Plan(Key());
        core.Sent(true);
        Assert.True(gate.HasWord);
    }
}

public sealed class TerminalElementTests
{
    [Theory]
    [InlineData("xterm-helper-textarea", true)]
    [InlineData("foo xterm-helper-textarea bar", true)]
    [InlineData("min-w-0 flex-1 bg-transparent", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AnElectronTerminalIsKnownByItsInputElement(string? className, bool terminal) =>
        Assert.Equal(terminal, Orbit.Core.Processes.IsTerminalElement(className));
}

public sealed class HeldModifiersTests
{
    [Fact]
    public void BothSidesCount()
    {
        var held = new HeldModifiers();
        held.Note(0xA0, true);
        held.Note(0xA1, true);
        held.Note(0xA0, false);
        Assert.True(held.Shift);
        Assert.False(held.Shortcut);
        held.Note(0xA1, false);
        Assert.False(held.Shift);
    }

    [Theory]
    [InlineData(0xA2)]
    [InlineData(0xA3)]
    [InlineData(0xA4)]
    [InlineData(0xA5)]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    public void ShortcutModifiers(uint vk)
    {
        var held = new HeldModifiers();
        held.Note(vk, true);
        Assert.True(held.Shortcut);
        held.Note(vk, false);
        Assert.False(held.Shortcut);
    }

    [Fact]
    public void OtherKeysChangeNothing()
    {
        var held = new HeldModifiers();
        held.Note(0x41, true);
        Assert.False(held.Shift || held.Shortcut);
    }
}

public sealed class TypingWorkerTests
{
    private sealed class SlowEngine : ITypingEngine
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public bool Blocks { get; set; }

        public EngineReply Key(EngineKey key, EngineContext context)
        {
            if (Blocks)
            {
                Entered.Set();
                Release.Wait(TimeSpan.FromSeconds(10));
            }
            return new EngineReply(true, [new EngineEdit(EngineEdit.Mark, "늦음", [0, 0])], false);
        }

        public void LetGo()
        {
        }
    }

    private static TypingWorker Started(ITypingEngine engine, FakeTypingHost host)
    {
        var worker = new TypingWorker(() => engine, host);
        worker.Start(true);
        WaitFor(() => worker.Gate.Active);
        return worker;
    }

    private static void WaitFor(Func<bool> condition)
    {
        var limit = Environment.TickCount64 + 5000;
        while (!condition() && Environment.TickCount64 < limit) Thread.Sleep(5);
        Assert.True(condition());
    }

    private static ushort[] Keys(FakeTypingHost host)
    {
        lock (host.Sent) return [.. host.Sent.SelectMany(e => e).Where(i => i.Anonymous.ki.wVk != 0).Select(i => (ushort)i.Anonymous.ki.wVk)];
    }

    [Fact]
    public void AWordGoesThroughTheQueue()
    {
        var host = new FakeTypingHost();
        using var worker = Started(new SlowEngine(), host);
        Assert.True(worker.KeyArrived(0x41, 0x1E, false, true, false));
        WaitFor(() => !worker.Gate.Busy);
        Assert.True(worker.Gate.HasWord);
        lock (host.Sent) Assert.Single(host.Sent);
    }

    [Fact]
    public void ABoxThatCouldNotBeReadIsLookedAtAgainWithoutAnEvent()
    {
        // Chromium after Alt+Tab: the first read is refused, and a canvas sends no focus event later.
        var host = new FakeTypingHost { DefaultField = new FieldSnapshot(false, false, null, false, [1]) };
        host.Fields.Enqueue(null);
        using var worker = new TypingWorker(() => new SlowEngine(), host);
        worker.Start(true);
        WaitFor(() => host.Reads >= 2 && worker.Pause == TypingPause.NotText);
        Assert.False(worker.Gate.Active);
    }

    [Fact]
    public void AnImeTooBusyToAnswerIsAskedAgainWithoutAnEvent()
    {
        // A busy app's IME misses the first look, and focus stays where it is.
        var host = new FakeTypingHost();
        host.ImeReplies.Enqueue(false);
        using var worker = new TypingWorker(() => new SlowEngine(), host);
        worker.Start(true);
        WaitFor(() => host.ImeAsks >= 2 && worker.Pause == TypingPause.None);
        Assert.True(worker.Gate.Active);
    }

    [Fact]
    public void AnEngineThatCannotStartFailsQuietly()
    {
        var host = new FakeTypingHost();
        using var worker = new TypingWorker(() => throw new DllNotFoundException(), host);
        worker.Start(true);
        WaitFor(() => worker.Failed);
        Assert.False(worker.Gate.Active);
        Assert.False(worker.KeyArrived(0x41, 0x1E, false, true, false));
    }

    [Fact]
    public void AStallSendsKeysAsTypedInOrderAndTheLateAnswerIsDropped()
    {
        var engine = new SlowEngine { Blocks = true };
        var host = new FakeTypingHost();
        using var worker = Started(engine, host);

        worker.KeyArrived(0x41, 0x1E, false, true, false);
        Assert.True(engine.Entered.Wait(TimeSpan.FromSeconds(5)));
        worker.KeyArrived(0x41, 0x1E, false, false, false);
        worker.KeyArrived(0x42, 0x30, false, true, false);

        worker.CheckStall(Environment.TickCount64 + 1000);
        Assert.False(worker.Gate.Busy);
        // A's down, A's up, B's down: every event, in the order it came.
        Assert.Equal(new ushort[] { 0x41, 0x41, 0x42 }, Keys(host));

        int before;
        lock (host.Sent) before = host.Sent.Count;
        engine.Blocks = false;
        engine.Release.Set();
        WaitFor(() => worker.Gate.Active);
        lock (host.Sent) Assert.Equal(before, host.Sent.Count);
        Assert.False(worker.Gate.HasWord);
    }

    [Fact]
    public void AfterAStallTheUpOfAReplayedKeyReachesTheApp()
    {
        var engine = new SlowEngine { Blocks = true };
        var host = new FakeTypingHost();
        using var worker = Started(engine, host);

        worker.KeyArrived(0x41, 0x1E, false, true, false);
        Assert.True(engine.Entered.Wait(TimeSpan.FromSeconds(5)));
        worker.CheckStall(Environment.TickCount64 + 1000);
        Assert.False(worker.KeyArrived(0x41, 0x1E, false, false, false));
        engine.Blocks = false;
        engine.Release.Set();
    }

    private sealed class ThrowingEngine : ITypingEngine
    {
        public EngineReply Key(EngineKey key, EngineContext context) => throw new NotSupportedException();

        public void LetGo() => throw new NotSupportedException();
    }

    [Fact]
    public void AnyFailureStillDeliversTheKeyAndKeepsTheThread()
    {
        var host = new FakeTypingHost();
        using var worker = Started(new ThrowingEngine(), host);
        worker.KeyArrived(0x41, 0x1E, false, true, false);
        WaitFor(() => !worker.Gate.Busy);
        worker.KeyArrived(0x42, 0x30, false, true, false);
        WaitFor(() => !worker.Gate.Busy);
        Assert.Equal(new ushort[] { 0x41, 0x42 }, Keys(host));
    }

    [Fact]
    public void ClosingDeliversKeysAlreadyTaken()
    {
        var engine = new SlowEngine { Blocks = true };
        var host = new FakeTypingHost();
        var worker = Started(engine, host);
        worker.KeyArrived(0x41, 0x1E, false, true, false);
        Assert.True(engine.Entered.Wait(TimeSpan.FromSeconds(5)));
        worker.KeyArrived(0x42, 0x30, false, true, false);
        engine.Release.Set();
        engine.Blocks = false;
        worker.Dispose();
        Assert.False(worker.Gate.Busy);
        Assert.Equal(new ushort[] { 0x41, 0x42 }, Keys(host).Where(k => k is 0x41 or 0x42).Distinct().ToArray());
    }

    [Fact]
    public void AStallOutsideTheEngineAlsoEnds()
    {
        var host = new BlockingLookHost();
        using var worker = new TypingWorker(() => new SlowEngine(), host);
        worker.Start(true);
        WaitFor(() => worker.Gate.Active);

        host.Blocks = true;
        worker.FocusMoved();
        Assert.True(host.Entered.Wait(TimeSpan.FromSeconds(5)));
        worker.KeyArrived(0x41, 0x1E, false, true, false);
        worker.CheckStall(Environment.TickCount64 + 1000);
        Assert.False(worker.Gate.Busy);

        host.Blocks = false;
        host.Release.Set();
        WaitFor(() => worker.Gate.Active);

        // Back to normal: a key is taken again, and the next check finds nothing to do.
        Assert.True(worker.KeyArrived(0x42, 0x30, false, true, false));
        WaitFor(() => !worker.Gate.Busy);
        Assert.True(worker.Gate.HasWord);
    }

    private sealed class BlockingLookHost : ITypingHost
    {
        private readonly FakeTypingHost _inner = new();
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public volatile bool Blocks;

        public bool ShiftDown => false;
        public bool ShortcutDown => false;
        public nint FocusWindow => 1;

        public TypingPlace Place()
        {
            if (Blocks)
            {
                Entered.Set();
                Release.Wait(TimeSpan.FromSeconds(10));
            }
            return _inner.Place();
        }

        public FieldSnapshot? ReadField(int length) => _inner.ReadField(length);
        public bool HoldIme() => true;
        public void ReleaseIme() { }
        public bool Send(INPUT[] events) => _inner.Send(events);
        public void Wait(int milliseconds) { }
    }

    [Fact]
    public void NoStallNoReplay()
    {
        var host = new FakeTypingHost();
        using var worker = Started(new SlowEngine(), host);
        worker.CheckStall(Environment.TickCount64);
        lock (host.Sent) Assert.Empty(host.Sent);
        Assert.True(worker.Gate.Active);
    }

    [Fact]
    public void TheModifiersHeldTravelWithTheKey()
    {
        var engine = new RecordingEngine();
        var host = new FakeTypingHost();
        using var worker = Started(engine, host);
        worker.KeyArrived(0xA0, 0x2A, false, true, false);
        worker.KeyArrived(0x41, 0x1E, false, true, true);
        WaitFor(() => !worker.Gate.Busy);
        Assert.True(engine.Last.Shift);
        Assert.True(engine.Last.CapsLock);
    }

    private sealed class RecordingEngine : ITypingEngine
    {
        public EngineKey Last { get; private set; }

        public EngineReply Key(EngineKey key, EngineContext context)
        {
            Last = key;
            return new EngineReply(true, [], false);
        }

        public void LetGo()
        {
        }
    }

    [Fact]
    public void TurningOffStopsTakingKeys()
    {
        var host = new FakeTypingHost();
        using var worker = Started(new SlowEngine(), host);
        worker.SetEnabled(false);
        WaitFor(() => !worker.Gate.Active);
        Assert.False(worker.KeyArrived(0x41, 0x1E, false, true, false));
        Assert.Equal(TypingPause.Off, worker.Pause);
    }
}
