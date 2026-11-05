using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;

namespace Orbit.Core.Capture.Recording;

/// <summary>Media Foundation's stream numbers and flags for <c>IMFSourceReader</c>.</summary>
internal static class SourceReader
{
    public const uint FirstVideo = 0xFFFFFFFC; // MF_SOURCE_READER_FIRST_VIDEO_STREAM
    public const uint FirstAudio = 0xFFFFFFFD; // MF_SOURCE_READER_FIRST_AUDIO_STREAM
    public const uint AnyStream = 0xFFFFFFFE; // MF_SOURCE_READER_ANY_STREAM
    public const uint AllStreams = 0xFFFFFFFE; // MF_SOURCE_READER_ALL_STREAMS
    public const uint Error = 0x1; // MF_SOURCE_READERF_ERROR
    public const uint EndOfStream = 0x2; // MF_SOURCE_READERF_ENDOFSTREAM
}

/// <summary>What a video file holds: its size, frames and length, read without decoding.</summary>
/// <param name="Sound">The first sound track's length; null without one.</param>
public sealed record VideoInfo(int Width, int Height, int Frames, TimeSpan Length, TimeSpan? Sound);

/// <summary>Reads a recording back through Media Foundation's source reader (the recording self-test).</summary>
public static class VideoProbe
{
    public static unsafe VideoInfo Read(string path)
    {
        MediaFoundation.Start();
        IMFSourceReader? reader = null;
        IMFMediaType? type = null;
        try
        {
            PInvoke.MFCreateSourceReaderFromURL(path, null, out reader).ThrowOnFailure();
            reader.GetNativeMediaType(SourceReader.FirstVideo, 0, out type);
            type.GetUINT64(PInvoke.MF_MT_FRAME_SIZE, out ulong size);
            var (frames, end) = ReadAll(reader, SourceReader.FirstVideo);
            TimeSpan? sound = null;
            try
            {
                sound = TimeSpan.FromTicks(ReadAll(reader, SourceReader.FirstAudio).End);
            }
            catch (COMException)
            {
                // No sound track.
            }
            return new VideoInfo((int)(size >> 32), (int)(size & 0xFFFFFFFF), frames, TimeSpan.FromTicks(end), sound);
        }
        finally
        {
            ComObjects.Release(type);
            ComObjects.Release(reader);
            MediaFoundation.Stop();
        }
    }

    /// <summary>Every sample of one stream: how many, and where the last one ends.</summary>
    private static (int Samples, long End) ReadAll(IMFSourceReader reader, uint stream)
    {
        int samples = 0;
        long end = 0;
        while (true)
        {
            reader.ReadSample(stream, 0, out _, out uint flags, out long time, out IMFSample? sample);
            if (sample is not null)
            {
                samples++;
                sample.GetSampleDuration(out long duration);
                end = Math.Max(end, time + duration);
                Marshal.ReleaseComObject(sample);
            }
            if ((flags & SourceReader.EndOfStream) != 0) return (samples, end);
        }
    }
}
