using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Media.MediaFoundation;

namespace Orbit.Core.Capture.Recording;

/// <summary>
/// H.264 and an AAC sound track in MP4 through Media Foundation's sink writer, fed with BGRA D3D11 textures and 16-bit PCM (Orbit:
/// RecordingWriter.swift, which writes HEVC; HEVC on Windows needs a paid extension). The writer
/// shares the recording's device, so frames stay on the GPU: the sink writer adds the video
/// processor that turns BGRA into the encoder's NV12. Not thread-safe; create and use it on one MTA
/// thread. Times are 100 ns ticks.
/// </summary>
internal sealed unsafe class Mp4Writer : IDisposable
{
    private const uint ProgressiveInterlace = 2; // MFVideoInterlace_Progressive

    private readonly IMFSinkWriter _writer;
    private readonly IMFDXGIDeviceManager? _manager;
    private readonly uint _stream, _audio;
    private bool _finished;

    public int Width { get; }
    public int Height { get; }
    public bool Hardware { get; }

    private Mp4Writer(IMFSinkWriter writer, IMFDXGIDeviceManager? manager, uint stream, uint audio, int width, int height, bool hardware)
    {
        _writer = writer;
        _manager = manager;
        _stream = stream;
        _audio = audio;
        Width = width;
        Height = height;
        Hardware = hardware;
    }

    /// <summary>A writer at <paramref name="path"/>; hardware encoding when the GPU offers it, else Windows' software encoder.</summary>
    public static Mp4Writer Create(string path, ID3D11Device device, int width, int height, int fps)
    {
        MediaFoundation.Start();
        try
        {
            try
            {
                return Create(path, device, width, height, fps, hardware: true);
            }
            catch (COMException e)
            {
                Log.Warn($"Hardware video encoding could not be set up (0x{e.HResult:X8}); using the software encoder");
                AppPaths.TryDelete(path);
                return Create(path, device, width, height, fps, hardware: false);
            }
        }
        catch
        {
            MediaFoundation.Stop();
            throw;
        }
    }

    /// <summary>
    /// A writer fed from system memory, for exporting: frames through <see cref="WriteBgra"/>. Width
    /// and height must be even and positive, and <paramref name="sound"/> says whether it has a track.
    /// </summary>
    public static Mp4Writer CreateInMemory(string path, int width, int height, int fps, bool sound)
    {
        if (width <= 0 || height <= 0 || width % 2 != 0 || height % 2 != 0 || width > 16384 || height > 16384)
            throw new ArgumentException($"A video can't be {width} x {height}");
        MediaFoundation.Start();
        try
        {
            return Create(path, null, width, height, fps, hardware: true, sound);
        }
        catch
        {
            MediaFoundation.Stop();
            throw;
        }
    }

    private static Mp4Writer Create(string path, ID3D11Device? device, int width, int height, int fps, bool hardware, bool sound = true)
    {
        uint token = 0;
        IMFDXGIDeviceManager? manager = null;
        if (device is not null) PInvoke.MFCreateDXGIDeviceManager(&token, out manager).ThrowOnFailure();
        IMFAttributes? attributes = null;
        IMFMediaType? output = null, input = null;
        IMFSinkWriter? writer = null;
        try
        {
            PInvoke.MFCreateAttributes(out attributes, 4).ThrowOnFailure();
            if (manager is not null)
            {
                manager.ResetDevice(device!, token);
                attributes.SetUnknown(PInvoke.MF_SINK_WRITER_D3D_MANAGER, manager);
            }
            attributes.SetUINT32(PInvoke.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, hardware ? 1u : 0u);
            attributes.SetGUID(PInvoke.MF_TRANSCODE_CONTAINERTYPE, PInvoke.MFTranscodeContainerType_MPEG4);
            PInvoke.MFCreateSinkWriterFromURL(path, null, attributes, out writer).ThrowOnFailure();

            ulong size = ((ulong)(uint)width << 32) | (uint)height;
            ulong rate = ((ulong)(uint)fps << 32) | 1u;
            ulong square = (1ul << 32) | 1u;

            PInvoke.MFCreateMediaType(out output).ThrowOnFailure();
            output.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Video);
            output.SetGUID(PInvoke.MF_MT_SUBTYPE, PInvoke.MFVideoFormat_H264);
            output.SetUINT32(PInvoke.MF_MT_AVG_BITRATE, RecordingSize.Bitrate(width, height, fps));
            output.SetUINT32(PInvoke.MF_MT_INTERLACE_MODE, ProgressiveInterlace);
            output.SetUINT64(PInvoke.MF_MT_FRAME_SIZE, size);
            output.SetUINT64(PInvoke.MF_MT_FRAME_RATE, rate);
            output.SetUINT64(PInvoke.MF_MT_PIXEL_ASPECT_RATIO, square);
            uint stream;
            writer.AddStream(output, out stream);

            PInvoke.MFCreateMediaType(out input).ThrowOnFailure();
            input.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Video);
            input.SetGUID(PInvoke.MF_MT_SUBTYPE, PInvoke.MFVideoFormat_RGB32);
            input.SetUINT32(PInvoke.MF_MT_INTERLACE_MODE, ProgressiveInterlace);
            input.SetUINT64(PInvoke.MF_MT_FRAME_SIZE, size);
            input.SetUINT64(PInvoke.MF_MT_FRAME_RATE, rate);
            input.SetUINT64(PInvoke.MF_MT_PIXEL_ASPECT_RATIO, square);
            // Positive: the first row is the top one, as in a D3D texture.
            input.SetUINT32(PInvoke.MF_MT_DEFAULT_STRIDE, (uint)(width * 4));
            writer.SetInputMediaType(stream, input, null);
            uint audio = sound ? AddAudio(writer) : uint.MaxValue;
            writer.BeginWriting();

            var result = new Mp4Writer(writer, manager, stream, audio, width, height, hardware);
            writer = null;
            return result;
        }
        catch
        {
            if (writer is not null) Marshal.ReleaseComObject(writer);
            if (manager is not null) Marshal.ReleaseComObject(manager);
            throw;
        }
        finally
        {
            ComObjects.Release(attributes);
            ComObjects.Release(output);
            ComObjects.Release(input);
        }
    }

    /// <summary>The sound track: AAC, 48 kHz stereo at 160 kbit/s, fed with 16-bit PCM.</summary>
    private static uint AddAudio(IMFSinkWriter writer)
    {
        const int channels = AudioTimeline.Channels, rate = AudioTimeline.Rate;
        IMFMediaType? output = null, input = null;
        try
        {
            PInvoke.MFCreateMediaType(out output).ThrowOnFailure();
            output.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Audio);
            output.SetGUID(PInvoke.MF_MT_SUBTYPE, PInvoke.MFAudioFormat_AAC);
            output.SetUINT32(PInvoke.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            output.SetUINT32(PInvoke.MF_MT_AUDIO_SAMPLES_PER_SECOND, rate);
            output.SetUINT32(PInvoke.MF_MT_AUDIO_NUM_CHANNELS, channels);
            output.SetUINT32(PInvoke.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 160_000 / 8);
            writer.AddStream(output, out uint stream);

            PInvoke.MFCreateMediaType(out input).ThrowOnFailure();
            input.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Audio);
            input.SetGUID(PInvoke.MF_MT_SUBTYPE, PInvoke.MFAudioFormat_PCM);
            input.SetUINT32(PInvoke.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            input.SetUINT32(PInvoke.MF_MT_AUDIO_SAMPLES_PER_SECOND, rate);
            input.SetUINT32(PInvoke.MF_MT_AUDIO_NUM_CHANNELS, channels);
            input.SetUINT32(PInvoke.MF_MT_AUDIO_BLOCK_ALIGNMENT, channels * 2);
            input.SetUINT32(PInvoke.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, rate * channels * 2);
            writer.SetInputMediaType(stream, input, null);
            return stream;
        }
        finally
        {
            ComObjects.Release(output);
            ComObjects.Release(input);
        }
    }

    /// <summary>Sound: <paramref name="count"/> stereo frames of <paramref name="pcm"/> from frame <paramref name="skip"/>.</summary>
    public void WriteAudio(short[] pcm, int skip, int count, long start, long duration)
    {
        uint bytes = (uint)(count * AudioTimeline.Channels * sizeof(short));
        IMFMediaBuffer? buffer = null;
        try
        {
            PInvoke.MFCreateMemoryBuffer(bytes, out buffer).ThrowOnFailure();
            byte* data;
            buffer.Lock(&data, null, null);
            try
            {
                MemoryMarshal.AsBytes(pcm.AsSpan(skip * AudioTimeline.Channels, count * AudioTimeline.Channels)).CopyTo(new Span<byte>(data, (int)bytes));
            }
            finally
            {
                buffer.Unlock();
            }
            buffer.SetCurrentLength(bytes);
            WriteBuffer(_audio, buffer, start, duration);
        }
        finally
        {
            ComObjects.Release(buffer);
        }
    }

    /// <summary>One frame. The texture must not change afterwards: the encoder may still read it.</summary>
    public void Write(ID3D11Texture2D texture, long start, long duration)
    {
        IMFMediaBuffer? buffer = null;
        try
        {
            var iid = typeof(ID3D11Texture2D).GUID;
            PInvoke.MFCreateDXGISurfaceBuffer(&iid, texture, 0, false, out buffer).ThrowOnFailure();
            uint length;
            ((IMF2DBuffer)buffer).GetContiguousLength(out length);
            buffer.SetCurrentLength(length);
            WriteBuffer(_stream, buffer, start, duration);
        }
        finally
        {
            ComObjects.Release(buffer);
        }
    }

    /// <summary>One frame from memory, top row first, <paramref name="stride"/> bytes a row; only the writer's width and height are used.</summary>
    public void WriteBgra(byte[] pixels, int stride, long start, long duration)
    {
        int rowBytes = Width * 4;
        uint bytes = (uint)(rowBytes * Height);
        IMFMediaBuffer? buffer = null;
        try
        {
            PInvoke.MFCreateMemoryBuffer(bytes, out buffer).ThrowOnFailure();
            byte* data;
            buffer.Lock(&data, null, null);
            try
            {
                for (int y = 0; y < Height; y++)
                    pixels.AsSpan(y * stride, rowBytes).CopyTo(new Span<byte>(data + y * rowBytes, rowBytes));
            }
            finally
            {
                buffer.Unlock();
            }
            buffer.SetCurrentLength(bytes);
            WriteBuffer(_stream, buffer, start, duration);
        }
        finally
        {
            ComObjects.Release(buffer);
        }
    }

    /// <summary>One buffer as a sample of <paramref name="stream"/> at <paramref name="start"/> for <paramref name="duration"/> ticks.</summary>
    private void WriteBuffer(uint stream, IMFMediaBuffer buffer, long start, long duration)
    {
        IMFSample? sample = null;
        try
        {
            PInvoke.MFCreateSample(out sample).ThrowOnFailure();
            sample.AddBuffer(buffer);
            sample.SetSampleTime(start);
            sample.SetSampleDuration(duration);
            _writer.WriteSample(stream, sample);
        }
        finally
        {
            ComObjects.Release(sample);
        }
    }

    /// <summary>Writes the file's index; without it the MP4 does not play.</summary>
    public void Finish()
    {
        if (_finished) return;
        _finished = true;
        _writer.Finalize();
    }

    public void Dispose()
    {
        Marshal.ReleaseComObject(_writer);
        if (_manager is not null) Marshal.ReleaseComObject(_manager);
        MediaFoundation.Stop();
    }
}

/// <summary>MFStartup and MFShutdown, counted: Media Foundation stays up while any writer or reader is alive.</summary>
internal static class MediaFoundation
{
    private static readonly object Gate = new();
    private static int _users;

    public static void Start()
    {
        lock (Gate)
        {
            if (_users == 0) PInvoke.MFStartup(PInvoke.MF_VERSION, 0).ThrowOnFailure();
            _users++;
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            if (_users == 0) return;
            if (--_users == 0) PInvoke.MFShutdown();
        }
    }
}
