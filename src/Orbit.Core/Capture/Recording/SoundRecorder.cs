using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Orbit.Core.Capture.Recording;

/// <summary>A piece of the mix, at its position on the sound timeline.</summary>
internal readonly record struct SoundChunk(long Position, short[] Pcm);

/// <summary>
/// The recording's sound (Orbit: the audio half of RecordingWriter.swift, but one mixed track):
/// a thread that reads app sound and the microphone about every 10 ms, places their packets on
/// one timeline and hands the mix on in chunks, 100 ms behind now (time for late packets). It never
/// touches the MP4 writer: the recording's writer thread takes the chunks from <see cref="Chunks"/>.
/// App sound is read all along and mixed as silence while off; the microphone listens only while on.
/// </summary>
internal sealed class SoundRecorder : IDisposable
{
    private static readonly long Latency = AudioTimeline.Rate / 10; // 100 ms in frames
    private static readonly long Headroom = AudioTimeline.Rate / 2;
    private const int StampMs = 60; // time for the last packets before the stop

    private SoundInput? _app;
    private SoundInput? _mic;
    private readonly AudioTimeline _timeline = new();
    private readonly PacketPlacer _appPlacer = new(), _micPlacer = new();
    private readonly List<SoundPacket> _packets = [];
    private readonly Thread _thread;
    private volatile bool _appOn, _micOn;
    private volatile bool _stopping;
    private long _stopAt;
    private bool _micRunning;

    /// <summary>Ticks (QPC, 100 ns) at position 0.</summary>
    public long Start { get; }

    public ConcurrentQueue<SoundChunk> Chunks { get; } = new();

    /// <summary>Whether a microphone button makes sense: one is there and was not refused.</summary>
    public MicState Mic { get; private set; }

    /// <summary>The microphone could not be opened when it was switched on (from the sound thread).</summary>
    public event Action<MicState>? MicFailed;

    public bool AppSound
    {
        get => _appOn;
        set => _appOn = value;
    }

    public bool Microphone
    {
        get => _micOn;
        set => _micOn = value;
    }

    private SoundRecorder(SoundInput? app, SoundInput? mic, MicState micState, bool appOn, bool micOn)
    {
        _app = app;
        _mic = mic;
        Mic = micState;
        _appOn = appOn;
        _micOn = micOn && micState == MicState.Ready;
        Start = SampleClock.Now();
        _thread = new Thread(Run) { IsBackground = true, Name = "Orbit recording sound" };
    }

    /// <summary>
    /// Opens the inputs (the microphone only when it is on) and starts the thread. Without any
    /// input it still runs and hands on silence, so the video always has its sound track.
    /// </summary>
    public static SoundRecorder Open(bool appOn, bool micOn)
    {
        var app = SoundInput.OpenAppSound();
        try
        {
            app?.Start();
        }
        catch (COMException e)
        {
            Log.Warn($"App sound could not start (0x{e.HResult:X8})");
            app?.Dispose();
            app = null;
        }
        SoundInput? mic = null;
        MicState state;
        if (micOn) mic = SoundInput.OpenMicrophone(out state);
        else state = SoundInput.HasMicrophone() ? MicState.Ready : MicState.Missing;
        var recorder = new SoundRecorder(app, mic, state, appOn, micOn);
        recorder._thread.Start();
        return recorder;
    }

    /// <summary>Asks the thread to write up to <paramref name="stopAt"/> (QPC ticks) and end; waits at most 2 s.</summary>
    public void Stop(long stopAt)
    {
        Interlocked.Exchange(ref _stopAt, stopAt);
        _stopping = true;
        if (_thread.IsAlive && !_thread.Join(TimeSpan.FromSeconds(2))) Log.Warn("The recording sound thread did not end in time");
    }

    /// <summary>The thread is still handing on sound; when it is not, the writer fills in silence itself.</summary>
    public bool Running => _thread.IsAlive && !_ended;

    private volatile bool _ended;

    private void Run()
    {
        try
        {
            while (!_stopping)
            {
                Thread.Sleep(10);
                SwitchMic();
                long now = SampleClock.Now();
                Poll(now, now - Ticks(Latency));
            }
            long stopAt = Interlocked.Read(ref _stopAt);
            int wait = (int)Math.Max(0, (stopAt - SampleClock.Now()) / TimeSpan.TicksPerMillisecond + StampMs);
            if (wait > 0) Thread.Sleep(Math.Min(wait, 500));
            // Packets are placed by when they were read; only what is handed on stops at the stop.
            Poll(SampleClock.Now(), stopAt);
            if (_timeline.Trimmed > 0) Log.Info($"Recording sound: {_timeline.Trimmed} frames came too late or too early and were left out");
        }
        catch (Exception e)
        {
            // Never silently: the writer then fills the rest with silence.
            Log.Error("Recording sound stopped", e);
        }
        finally
        {
            _ended = true;
            _app?.Dispose();
            _app = null;
            _mic?.Dispose();
            _mic = null;
        }
    }

    private static long Ticks(long frames) => AudioTimeline.Ticks(frames);

    /// <summary>Reads everything waiting (placed against <paramref name="now"/>) and hands on the mix up to <paramref name="until"/>.</summary>
    private void Poll(long now, long until)
    {
        long nowAt = AudioTimeline.Frames(now - Start);
        // Room in the ring for what may come in now, after a stall (each hand-on is at most the ring).
        while (nowAt + Headroom - _timeline.Written > _timeline.Capacity) Hand(nowAt + Headroom - _timeline.Capacity);
        if (!Mix(_app, _appPlacer, _appOn ? 1f : 0f, nowAt))
        {
            _app?.Dispose();
            _app = null;
        }
        if (_micRunning && !Mix(_mic, _micPlacer, 1f, nowAt))
        {
            _micRunning = false;
            _micOn = false;
            _mic?.Dispose();
            _mic = null;
            Mic = MicState.Failed;
            MicFailed?.Invoke(MicState.Failed);
        }
        Hand(AudioTimeline.Frames(until - Start));
    }

    /// <summary>False when the input failed (a device pulled out); it is then dropped and the rest goes on.</summary>
    private bool Mix(SoundInput? input, PacketPlacer placer, float gain, long nowAt)
    {
        if (input is null) return true;
        _packets.Clear();
        try
        {
            input.Read(_packets);
        }
        catch (COMException e)
        {
            Log.Warn($"A recording sound input stopped (0x{e.HResult:X8})");
            return false;
        }
        // A packet without a stamp is counted back from now: it ends where the frames after it begin.
        long after = _packets.Sum(p => (long)p.Frames);
        foreach (var packet in _packets)
        {
            long estimate = packet.Qpc != 0 ? AudioTimeline.Frames(packet.Qpc - Start) : nowAt - after;
            after -= packet.Frames;
            long at = placer.Place(estimate, packet.Frames, packet.Discontinuity);
            _timeline.Add(at, packet.Samples, input.Channels, gain);
        }
        return true;
    }

    private void Hand(long until)
    {
        if (_timeline.Take(until, out long start) is { } pcm) Chunks.Enqueue(new SoundChunk(start, pcm));
    }

    /// <summary>Starts or stops the microphone to match <see cref="Microphone"/>; opens it the first time it is wanted.</summary>
    private void SwitchMic()
    {
        bool want = _micOn;
        if (want == _micRunning) return;
        try
        {
            if (want)
            {
                if (_mic is null)
                {
                    _mic = SoundInput.OpenMicrophone(out var state);
                    if (_mic is null)
                    {
                        Mic = state;
                        _micOn = false;
                        MicFailed?.Invoke(state);
                        return;
                    }
                }
                _micPlacer.Reset();
                _mic.Start();
                _micRunning = true;
            }
            else
            {
                _micRunning = false;
                _mic?.Stop();
            }
        }
        catch (COMException e)
        {
            Log.Warn($"The microphone could not be switched (0x{e.HResult:X8})");
            _micRunning = false;
            _micOn = false;
            Mic = MicState.Failed;
            MicFailed?.Invoke(MicState.Failed);
        }
    }

    public void Dispose()
    {
        _stopping = true;
        Interlocked.CompareExchange(ref _stopAt, SampleClock.Now(), 0);
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(2));
        else if (_thread.ThreadState == ThreadState.Unstarted)
        {
            _app?.Dispose();
            _mic?.Dispose();
        }
    }
}
