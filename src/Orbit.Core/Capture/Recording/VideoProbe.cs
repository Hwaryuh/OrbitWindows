using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;

namespace Orbit.Core.Capture.Recording;

/// <summary>What a video file holds: its size, frames and length, read without decoding.</summary>
public sealed record VideoInfo(int Width, int Height, int Frames, TimeSpan Length);

/// <summary>Reads a recording back through Media Foundation's source reader (the recording self-test).</summary>
public static class VideoProbe
{
    private const uint FirstVideoStream = 0xFFFFFFFC; // MF_SOURCE_READER_FIRST_VIDEO_STREAM
    private const uint EndOfStream = 0x2; // MF_SOURCE_READERF_ENDOFSTREAM

    public static unsafe VideoInfo Read(string path)
    {
        MediaFoundation.Start();
        IMFSourceReader? reader = null;
        IMFMediaType? type = null;
        try
        {
            PInvoke.MFCreateSourceReaderFromURL(path, null, out reader).ThrowOnFailure();
            reader.GetNativeMediaType(FirstVideoStream, 0, out type);
            type.GetUINT64(PInvoke.MF_MT_FRAME_SIZE, out ulong size);
            int frames = 0;
            long end = 0;
            while (true)
            {
                reader.ReadSample(FirstVideoStream, 0, out _, out uint flags, out long time, out IMFSample? sample);
                if (sample is not null)
                {
                    frames++;
                    sample.GetSampleDuration(out long duration);
                    end = Math.Max(end, time + duration);
                    Marshal.ReleaseComObject(sample);
                }
                if ((flags & EndOfStream) != 0) break;
            }
            return new VideoInfo((int)(size >> 32), (int)(size & 0xFFFFFFFF), frames, TimeSpan.FromTicks(end));
        }
        finally
        {
            ComObjects.Release(type);
            ComObjects.Release(reader);
            MediaFoundation.Stop();
        }
    }
}
