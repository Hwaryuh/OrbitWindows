using System.Runtime.InteropServices;
using Orbit.Core.Audio;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;

namespace Orbit.Core.Capture.Recording;

/// <summary>Whether the microphone can be used for this recording.</summary>
public enum MicState { Ready, Missing, Denied, Failed }

/// <summary>One packet read from a capture client: interleaved floats, its QPC time (0 when unknown).</summary>
internal readonly record struct SoundPacket(float[] Samples, int Frames, long Qpc, bool Discontinuity);

/// <summary>
/// A shared-mode WASAPI capture client asking for 48 kHz float (Windows converts): the app sound
/// of every program but Orbit (process loopback) or the default microphone. Polled; use from one
/// thread after it is made. Read is called often enough that the 200 ms buffer never fills.
/// </summary>
internal sealed unsafe class SoundInput : IDisposable
{
    private const long BufferTicks = 2_000_000; // 200 ms
    private const uint Discontinuity = 0x1, Silent = 0x2, TimestampError = 0x4; // AUDCLNT_BUFFERFLAGS_*
    private const int AccessDenied = unchecked((int)0x80070005), NotFound = unchecked((int)0x80070490);

    private readonly IAudioClient _client;
    private readonly IAudioCaptureClient _capture;
    private bool _running;

    public int Channels { get; }

    private SoundInput(IAudioClient client, IAudioCaptureClient capture, int channels)
    {
        _client = client;
        _capture = capture;
        Channels = channels;
    }

    /// <summary>Every program's sound but Orbit's own (and its child processes'); null when Windows can't give it.</summary>
    public static SoundInput? OpenAppSound()
    {
        IAudioClient? client = null;
        try
        {
            client = ActivateLoopback((uint)Environment.ProcessId);
            if (client is null) return null;
            var input = Init(client, 2, PInvoke.AUDCLNT_STREAMFLAGS_LOOPBACK);
            client = null;
            return input;
        }
        catch (COMException e)
        {
            Log.Warn($"App sound could not be opened for recording (0x{e.HResult:X8})");
            return null;
        }
        finally
        {
            ComObjects.Release(client);
        }
    }

    /// <summary>Whether there is a microphone at all (nothing is opened).</summary>
    public static bool HasMicrophone()
    {
        var device = AudioDevices.Default(EDataFlow.eCapture, ERole.eConsole);
        ComObjects.Release(device);
        return device is not null;
    }

    /// <summary>The default microphone, mono; <paramref name="state"/> says why not when null.</summary>
    public static SoundInput? OpenMicrophone(out MicState state)
    {
        var device = AudioDevices.Default(EDataFlow.eCapture, ERole.eConsole);
        if (device is null)
        {
            state = MicState.Missing;
            return null;
        }
        IAudioClient? client = null;
        try
        {
            device.Activate(typeof(IAudioClient).GUID, CLSCTX.CLSCTX_ALL, null, out var obj);
            client = (IAudioClient)obj;
            var input = Init(client, 1, 0);
            client = null;
            state = MicState.Ready;
            return input;
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            state = e.HResult switch
            {
                AccessDenied => MicState.Denied,
                NotFound => MicState.Missing,
                _ => MicState.Failed,
            };
            Log.Warn($"The microphone could not be opened for recording: {state} (0x{e.HResult:X8})");
            return null;
        }
        finally
        {
            ComObjects.Release(client);
            ComObjects.Release(device);
        }
    }

    private static SoundInput Init(IAudioClient client, int channels, uint flags)
    {
        var format = new WAVEFORMATEX
        {
            wFormatTag = (ushort)PInvoke.WAVE_FORMAT_IEEE_FLOAT,
            nChannels = (ushort)channels,
            nSamplesPerSec = AudioTimeline.Rate,
            wBitsPerSample = 32,
            nBlockAlign = (ushort)(channels * 4),
            nAvgBytesPerSec = (uint)(AudioTimeline.Rate * channels * 4),
        };
        client.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
            flags | PInvoke.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | PInvoke.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY, BufferTicks, 0, format);
        client.GetService(out IAudioCaptureClient capture);
        return new SoundInput(client, capture, channels);
    }

    /// <summary>Process loopback is activated asynchronously; this waits for it (at most 3 s).</summary>
    private static IAudioClient? ActivateLoopback(uint pid)
    {
        // Off the stack: if activation outlives the wait below, what it may still read stays valid (and is leaked).
        var parameters = (AUDIOCLIENT_ACTIVATION_PARAMS*)NativeMemory.AllocZeroed((nuint)sizeof(AUDIOCLIENT_ACTIVATION_PARAMS));
        parameters->ActivationType = AUDIOCLIENT_ACTIVATION_TYPE.AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
        parameters->ProcessLoopbackParams.TargetProcessId = pid;
        parameters->ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE.PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE;
        var variant = new PROPVARIANT();
        variant.vt = VARENUM.VT_BLOB;
        variant.blob.cbSize = (uint)sizeof(AUDIOCLIENT_ACTIVATION_PARAMS);
        variant.blob.pBlobData = (byte*)parameters;
        var done = new Activation();
        IActivateAudioInterfaceAsyncOperation? operation = null;
        bool finished = false;
        try
        {
            PInvoke.ActivateAudioInterfaceAsync(PInvoke.VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, typeof(IAudioClient).GUID, variant, done, out operation)
                .ThrowOnFailure();
            finished = done.Wait(TimeSpan.FromSeconds(3));
            if (!finished)
            {
                Log.Warn("App sound did not open in time");
                return null;
            }
            operation.GetActivateResult(out HRESULT result, out object activated);
            result.ThrowOnFailure();
            return (IAudioClient)activated;
        }
        finally
        {
            ComObjects.Release(operation);
            if (finished || operation is null) NativeMemory.Free(parameters);
        }
    }

    /// <summary>Told when activation is done; agile, as Windows calls it from any thread.</summary>
    [ComVisible(true)]
    private sealed class Activation : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        private readonly ManualResetEventSlim _done = new();

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation) => _done.Set();

        public bool Wait(TimeSpan timeout) => _done.Wait(timeout);
    }

    public void Start()
    {
        if (_running) return;
        _client.Start();
        _running = true;
    }

    /// <summary>Stops listening and drops what was buffered.</summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _client.Stop();
        _client.Reset();
    }

    /// <summary>Every packet waiting now, in order.</summary>
    public void Read(List<SoundPacket> into)
    {
        if (!_running) return;
        while (true)
        {
            _capture.GetNextPacketSize(out uint next);
            if (next == 0) return;
            _capture.GetBuffer(out byte* data, out uint frames, out uint flags, out _, out ulong qpc);
            try
            {
                var samples = new float[frames * Channels];
                if ((flags & Silent) == 0 && frames > 0) new ReadOnlySpan<float>(data, samples.Length).CopyTo(samples);
                bool stamped = (flags & TimestampError) == 0 && qpc != 0;
                into.Add(new SoundPacket(samples, (int)frames, stamped ? (long)qpc : 0, (flags & Discontinuity) != 0));
            }
            finally
            {
                _capture.ReleaseBuffer(frames);
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (_running) _client.Stop();
        }
        catch (COMException)
        {
        }
        _running = false;
        ComObjects.Release(_capture);
        ComObjects.Release(_client);
    }
}
