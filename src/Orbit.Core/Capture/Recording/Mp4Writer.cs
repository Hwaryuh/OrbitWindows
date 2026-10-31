using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Media.MediaFoundation;

namespace Orbit.Core.Capture.Recording;

/// <summary>
/// H.264 in MP4 through Media Foundation's sink writer, fed with BGRA D3D11 textures (Orbit:
/// RecordingWriter.swift, which writes HEVC; HEVC on Windows needs a paid extension). The writer
/// shares the recording's device, so frames stay on the GPU: the sink writer adds the video
/// processor that turns BGRA into the encoder's NV12. Not thread-safe; create and use it on one MTA
/// thread. Times are 100 ns ticks.
/// </summary>
internal sealed unsafe class Mp4Writer : IDisposable
{
    private const uint ProgressiveInterlace = 2; // MFVideoInterlace_Progressive

    private readonly IMFSinkWriter _writer;
    private readonly IMFDXGIDeviceManager _manager;
    private readonly uint _stream;
    private bool _finished;

    public int Width { get; }
    public int Height { get; }
    public bool Hardware { get; }

    private Mp4Writer(IMFSinkWriter writer, IMFDXGIDeviceManager manager, uint stream, int width, int height, bool hardware)
    {
        _writer = writer;
        _manager = manager;
        _stream = stream;
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

    private static Mp4Writer Create(string path, ID3D11Device device, int width, int height, int fps, bool hardware)
    {
        uint token;
        PInvoke.MFCreateDXGIDeviceManager(&token, out var manager).ThrowOnFailure();
        IMFAttributes? attributes = null;
        IMFMediaType? output = null, input = null;
        IMFSinkWriter? writer = null;
        try
        {
            manager.ResetDevice(device, token);
            PInvoke.MFCreateAttributes(out attributes, 4).ThrowOnFailure();
            attributes.SetUnknown(PInvoke.MF_SINK_WRITER_D3D_MANAGER, manager);
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
            writer.BeginWriting();

            var result = new Mp4Writer(writer, manager, stream, width, height, hardware);
            writer = null;
            return result;
        }
        catch
        {
            if (writer is not null) Marshal.ReleaseComObject(writer);
            Marshal.ReleaseComObject(manager);
            throw;
        }
        finally
        {
            ComObjects.Release(attributes);
            ComObjects.Release(output);
            ComObjects.Release(input);
        }
    }

    /// <summary>One frame. The texture must not change afterwards: the encoder may still read it.</summary>
    public void Write(ID3D11Texture2D texture, long start, long duration)
    {
        IMFMediaBuffer? buffer = null;
        IMFSample? sample = null;
        try
        {
            var iid = typeof(ID3D11Texture2D).GUID;
            PInvoke.MFCreateDXGISurfaceBuffer(&iid, texture, 0, false, out buffer).ThrowOnFailure();
            uint length;
            ((IMF2DBuffer)buffer).GetContiguousLength(out length);
            buffer.SetCurrentLength(length);
            PInvoke.MFCreateSample(out sample).ThrowOnFailure();
            sample.AddBuffer(buffer);
            sample.SetSampleTime(start);
            sample.SetSampleDuration(duration);
            _writer.WriteSample(_stream, sample);
        }
        finally
        {
            ComObjects.Release(sample);
            ComObjects.Release(buffer);
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
        Marshal.ReleaseComObject(_manager);
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
