using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;

namespace Orbit.Core.Capture.Recording;

/// <summary>A picture of a video, top row first, 4 bytes per pixel (blue, green, red, unused).</summary>
public sealed record VideoPicture(int Width, int Height, byte[] Pixels);

/// <summary>What to keep of a video. Times are from its start.</summary>
public readonly record struct KeptPart(TimeSpan Start, TimeSpan End);

/// <summary>
/// Trim, mute and GIF export of a recording (Orbit: VideoTools.swift), and the editor's filmstrip,
/// through Media Foundation on a thread of its own. The original is only read. Every output is
/// written beside its name as a draft and renamed when finished, so a failure or a cancel leaves no
/// half a file. Both streams share one time line: a sample's place is its time in the source minus
/// the kept part's start, so sound and picture stay as they were.
/// </summary>
public static class VideoExport
{
    /// <summary>GIF limits, as Orbit: 15 frames a second at most, about 450 frames, 800 wide.</summary>
    public const int GifMaxWidth = 800, GifMaxFrames = 450;
    public const double GifMaxFps = 15, GifMinFps = 3;

    private static readonly long TicksPerSecond = TimeSpan.TicksPerSecond;

    /// <summary>The frames per second a GIF of this length uses.</summary>
    public static double GifFps(TimeSpan length) =>
        Math.Min(GifMaxFps, Math.Max(GifMinFps, GifMaxFrames / Math.Max(length.TotalSeconds, 0.001)));

    public static Task ExportMp4Async(string source, string output, KeptPart part, bool muted, CancellationToken cancel = default) =>
        OnThread(() => WriteDraft(output, draft => Mp4(source, draft, part, muted, cancel)));

    public static Task ExportGifAsync(string source, string output, KeptPart part, CancellationToken cancel = default) =>
        OnThread(() => WriteDraft(output, draft => Gif(source, draft, part, cancel)));

    /// <summary>Up to <paramref name="count"/> pictures spread over the video, none wider or taller than <paramref name="side"/>.</summary>
    public static Task<List<VideoPicture>> FilmstripAsync(string source, TimeSpan length, int count, int side, CancellationToken cancel = default) =>
        OnThread(() =>
        {
            var pictures = new List<VideoPicture>();
            using var reader = new VideoReader(source, sound: false);
            var (width, height) = FitWithin(reader.Width, reader.Height, side, side);
            for (int i = 0; i < count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                long at = (long)(length.Ticks * (i + 0.5) / count);
                reader.Seek(at);
                // A seek lands on an earlier key frame: decode on to the frame showing at that time.
                VideoReader.Sample? frame;
                while ((frame = reader.NextVideo()) is not null && frame.Time + frame.Duration <= at) frame.Dispose();
                if (frame is null) break;
                using (frame)
                {
                    // ToArray: the reader reuses its frame, and a small video isn't scaled into a new one.
                    pictures.Add(new VideoPicture(width, height, Scaled(reader.CopyFrame(frame), reader.Width, reader.Height, width, height).ToArray()));
                }
            }
            return pictures;
        });

    /// <summary>A short silent video of changing color, for the editor's self-test; no window and no sound device.</summary>
    public static Task WriteTestVideoAsync(string path, int width, int height, TimeSpan length) => OnThread(() =>
    {
        const int fps = 30;
        using var writer = Mp4Writer.CreateInMemory(path, width, height, fps, sound: false);
        var pixels = new byte[width * height * 4];
        long each = TicksPerSecond / fps;
        int frames = (int)(length.TotalSeconds * fps);
        for (int frame = 0; frame < frames; frame++)
        {
            Array.Fill(pixels, (byte)(frame * 255 / frames));
            writer.WriteBgra(pixels, width * 4, frame * each, each);
        }
        writer.Finish();
    });

    /// <summary>The size within the limits, in proportion and never larger than it was.</summary>
    public static (int Width, int Height) FitWithin(int width, int height, int maxWidth, int maxHeight)
    {
        double scale = Math.Min(1, Math.Min((double)maxWidth / width, (double)maxHeight / height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>The picture at a smaller size, each pixel the average of the ones it covers; text stays readable.</summary>
    public static byte[] Scaled(byte[] pixels, int width, int height, int toWidth, int toHeight)
    {
        if (width == toWidth && height == toHeight) return pixels;
        var result = new byte[toWidth * toHeight * 4];
        for (int y = 0; y < toHeight; y++)
        {
            int top = y * height / toHeight, bottom = Math.Max(top + 1, (y + 1) * height / toHeight);
            for (int x = 0; x < toWidth; x++)
            {
                int left = x * width / toWidth, right = Math.Max(left + 1, (x + 1) * width / toWidth);
                int blue = 0, green = 0, red = 0, count = (right - left) * (bottom - top);
                for (int sy = top; sy < bottom; sy++)
                {
                    int at = (sy * width + left) * 4;
                    for (int sx = left; sx < right; sx++, at += 4)
                    {
                        blue += pixels[at];
                        green += pixels[at + 1];
                        red += pixels[at + 2];
                    }
                }
                int to = (y * toWidth + x) * 4;
                result[to] = (byte)(blue / count);
                result[to + 1] = (byte)(green / count);
                result[to + 2] = (byte)(red / count);
                result[to + 3] = 255;
            }
        }
        return result;
    }

    private static void WriteDraft(string output, Action<string> write)
    {
        string draft = output + ".part";
        try
        {
            write(draft);
            File.Move(draft, output, overwrite: true);
        }
        finally
        {
            AppPaths.TryDelete(draft);
        }
    }

    private static void Mp4(string source, string draft, KeptPart part, bool muted, CancellationToken cancel)
    {
        using var reader = new VideoReader(source, sound: !muted);
        long start = part.Start.Ticks, end = part.End.Ticks;
        using var writer = Mp4Writer.CreateInMemory(draft, reader.Width & ~1, reader.Height & ~1, reader.Fps, reader.HasSound);
        reader.Seek(start);
        bool pictureLeft = true, soundLeft = reader.HasSound, wroteFrame = false;
        short[] pcm = [];
        while (pictureLeft || soundLeft)
        {
            cancel.ThrowIfCancellationRequested();
            var next = reader.Next(pictureLeft, soundLeft);
            if (next.Kind == VideoReader.SampleKind.PictureEnded)
            {
                pictureLeft = false;
                continue;
            }
            if (next.Kind == VideoReader.SampleKind.SoundEnded)
            {
                soundLeft = false;
                continue;
            }
            using var sample = next;
            long time = sample.Time, stop = sample.Time + sample.Duration;
            if (sample.Kind == VideoReader.SampleKind.Picture)
            {
                if (time >= end) pictureLeft = false;
                // A frame that began before the kept part and still shows at its start is its first frame.
                else if (stop > start)
                {
                    long from = Math.Max(time, start);
                    writer.WriteBgra(reader.CopyFrame(sample), reader.Stride(), from - start, Math.Min(stop, end) - from);
                    wroteFrame = true;
                }
            }
            else
            {
                if (time >= end) soundLeft = false;
                else if (stop > start) WriteSound(writer, reader, sample, start, end, ref pcm);
            }
        }
        if (!wroteFrame) throw new InvalidOperationException("Nothing in the kept part");
        writer.Finish();
    }

    /// <summary>The part of a sound sample inside the kept part, cut on whole sample frames.</summary>
    private static void WriteSound(Mp4Writer writer, VideoReader reader, VideoReader.Sample sample, long start, long end, ref short[] pcm)
    {
        int frames = reader.CopySound(sample, ref pcm);
        if (frames == 0) return;
        long time = sample.Time;
        int skip = time < start ? FramesIn(start - time) : 0;
        int last = time + TicksOf(frames) > end ? FramesIn(end - time) : frames;
        int count = Math.Min(last, frames) - skip;
        if (count <= 0) return;
        writer.WriteAudio(pcm, skip, count, time + TicksOf(skip) - start, TicksOf(count));
    }

    private static int FramesIn(long ticks) => (int)Math.Round((double)ticks * AudioTimeline.Rate / TicksPerSecond);

    private static long TicksOf(int frames) => frames * TicksPerSecond / AudioTimeline.Rate;

    private static void Gif(string source, string draft, KeptPart part, CancellationToken cancel)
    {
        using var reader = new VideoReader(source, sound: false);
        var length = part.End - part.Start;
        if (length <= TimeSpan.Zero) throw new InvalidOperationException("Nothing in the kept part");
        double fps = GifFps(length);
        int total = Math.Max(1, (int)(length.TotalSeconds * fps));
        var (width, height) = FitWithin(reader.Width, reader.Height, GifMaxWidth, int.MaxValue);
        using var file = new FileStream(draft, FileMode.Create, FileAccess.Write, FileShare.None);
        var gif = new GifWriter(file, width, height);
        var delays = new GifDelays(fps);

        reader.Seek(part.Start.Ticks);
        byte[]? last = null;
        int written = 0;
        // The newest frame showing at each output time; frames repeated in the source are just frames.
        while (written < total)
        {
            cancel.ThrowIfCancellationRequested();
            if (reader.NextVideo() is not { } sample) break;
            using (sample)
            {
                long stop = sample.Time + sample.Duration;
                long nextOutput = part.Start.Ticks + (long)(written * TicksPerSecond / fps);
                if (stop <= nextOutput) continue;
                last = Scaled(reader.CopyFrame(sample), reader.Width, reader.Height, width, height);
                while (written < total && part.Start.Ticks + (long)(written * TicksPerSecond / fps) < stop)
                {
                    gif.AddFrame(last, delays.Next());
                    written++;
                }
            }
        }
        if (last is null) throw new InvalidOperationException("Nothing in the kept part");
        // The source ended early: the last frame holds, so the length stays right.
        for (; written < total; written++) gif.AddFrame(last, delays.Next());
        gif.Finish();
    }

    private static Task OnThread(Action work) => OnThread<object?>(() =>
    {
        work();
        return null;
    });

    /// <summary>Media Foundation's objects stay on one MTA thread, away from the UI.</summary>
    private static Task<T> OnThread<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (OperationCanceledException)
            {
                done.SetCanceled();
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        }) { IsBackground = true, Name = "Orbit video export" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return done.Task;
    }
}

/// <summary>The delay of each GIF frame in hundredths of a second, carrying the rounding so the length holds.</summary>
public sealed class GifDelays(double fps)
{
    private double _owed;

    public int Next()
    {
        _owed += 100 / fps;
        int delay = Math.Max(2, (int)Math.Round(_owed));
        _owed -= delay;
        return delay;
    }
}

/// <summary>Decoded samples of one video: pictures in RGB32 and sound as 16-bit PCM. Use it on one MTA thread.</summary>
internal sealed unsafe class VideoReader : IDisposable
{
    /// <summary>Gaps in a stream come as reads with no sample. This many in a row is a reader gone wrong, not a gap.</summary>
    private const int MostEmptyReads = 100_000;

    public enum SampleKind { Picture, Sound, PictureEnded, SoundEnded, Nothing }

    private readonly IMFSourceReader _reader;
    private readonly uint _videoIndex;
    private readonly uint _audioIndex = uint.MaxValue;
    private readonly int _stride;
    private byte[] _frame = [];

    public int Width { get; }
    public int Height { get; }
    public int Fps { get; }
    public bool HasSound { get; }

    public VideoReader(string path, bool sound)
    {
        MediaFoundation.Start();
        IMFAttributes? attributes = null;
        IMFMediaType? wanted = null, current = null;
        try
        {
            PInvoke.MFCreateAttributes(out attributes, 1).ThrowOnFailure();
            attributes.SetUINT32(PInvoke.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
            PInvoke.MFCreateSourceReaderFromURL(path, attributes, out _reader).ThrowOnFailure();
            _reader.SetStreamSelection(SourceReader.AllStreams, false);
            _reader.SetStreamSelection(SourceReader.FirstVideo, true);

            PInvoke.MFCreateMediaType(out wanted).ThrowOnFailure();
            wanted.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Video);
            wanted.SetGUID(PInvoke.MF_MT_SUBTYPE, PInvoke.MFVideoFormat_RGB32);
            _reader.SetCurrentMediaType(SourceReader.FirstVideo, wanted);
            _reader.GetCurrentMediaType(SourceReader.FirstVideo, out current);
            current.GetUINT64(PInvoke.MF_MT_FRAME_SIZE, out ulong size);
            Width = (int)(size >> 32);
            Height = (int)(size & 0xFFFFFFFF);
            _stride = ReadStride(current, Width);
            try
            {
                current.GetUINT64(PInvoke.MF_MT_FRAME_RATE, out ulong rate);
                uint top = (uint)(rate >> 32), bottom = (uint)rate;
                Fps = bottom == 0 ? 30 : Math.Clamp((int)Math.Round((double)top / bottom), 1, 120);
            }
            catch (COMException)
            {
                Fps = 30;
            }
            _videoIndex = StreamIndex(PInvoke.MFMediaType_Video);
            if (sound)
            {
                _audioIndex = StreamIndex(PInvoke.MFMediaType_Audio);
                HasSound = _audioIndex != uint.MaxValue && SelectSound();
            }
        }
        catch
        {
            if (_reader is not null) Marshal.ReleaseComObject(_reader);
            MediaFoundation.Stop();
            throw;
        }
        finally
        {
            ComObjects.Release(attributes);
            ComObjects.Release(wanted);
            ComObjects.Release(current);
        }
    }

    /// <summary>Negative means the rows run bottom to top. Windows may leave it out for RGB32: then top to bottom.</summary>
    private static int ReadStride(IMFMediaType type, int width)
    {
        try
        {
            type.GetUINT32(PInvoke.MF_MT_DEFAULT_STRIDE, out uint stride);
            return unchecked((int)stride);
        }
        catch (COMException)
        {
            return width * 4;
        }
    }

    private uint StreamIndex(Guid major)
    {
        for (uint i = 0; i < 16; i++)
        {
            IMFMediaType? type = null;
            try
            {
                _reader.GetNativeMediaType(i, 0, out type);
                type.GetGUID(PInvoke.MF_MT_MAJOR_TYPE, out Guid found);
                if (found == major) return i;
            }
            catch (COMException)
            {
                return uint.MaxValue;
            }
            finally
            {
                ComObjects.Release(type);
            }
        }
        return uint.MaxValue;
    }

    private bool SelectSound()
    {
        IMFMediaType? wanted = null;
        try
        {
            _reader.SetStreamSelection(_audioIndex, true);
            PInvoke.MFCreateMediaType(out wanted).ThrowOnFailure();
            wanted.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Audio);
            wanted.SetGUID(PInvoke.MF_MT_SUBTYPE, PInvoke.MFAudioFormat_PCM);
            wanted.SetUINT32(PInvoke.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            wanted.SetUINT32(PInvoke.MF_MT_AUDIO_SAMPLES_PER_SECOND, AudioTimeline.Rate);
            wanted.SetUINT32(PInvoke.MF_MT_AUDIO_NUM_CHANNELS, AudioTimeline.Channels);
            _reader.SetCurrentMediaType(_audioIndex, wanted);
            return true;
        }
        catch (COMException e)
        {
            Log.Warn($"A recording's sound could not be read (0x{e.HResult:X8}); exporting without it");
            return false;
        }
        finally
        {
            ComObjects.Release(wanted);
        }
    }

    /// <summary>Goes to the last key frame at or before this time; samples before it still come and are dropped by the caller.</summary>
    public void Seek(long ticks)
    {
        var position = new PROPVARIANT();
        position.Anonymous.Anonymous.vt = VARENUM.VT_I8;
        position.Anonymous.Anonymous.Anonymous.hVal = Math.Max(0, ticks);
        _reader.SetCurrentPosition(Guid.Empty, position);
    }

    /// <summary>The next sample of the streams still wanted. Which stream has ended is told per stream.</summary>
    public Sample Next(bool picture, bool sound)
    {
        for (int empty = 0; empty < MostEmptyReads; empty++)
        {
            uint stream = picture && sound ? SourceReader.AnyStream : picture ? _videoIndex : _audioIndex;
            _reader.ReadSample(stream, 0, out uint actual, out uint flags, out long time, out IMFSample? sample);
            if ((flags & SourceReader.Error) != 0)
            {
                ComObjects.Release(sample);
                throw new InvalidOperationException("The video reader reported an error");
            }
            if (sample is not null)
            {
                bool isPicture = actual == _videoIndex;
                sample.GetSampleDuration(out long duration);
                if (duration <= 0)
                {
                    sample.GetTotalLength(out uint bytes);
                    duration = isPicture
                        ? TimeSpan.TicksPerSecond / Fps
                        : bytes / (AudioTimeline.Channels * sizeof(short)) * TimeSpan.TicksPerSecond / AudioTimeline.Rate;
                }
                return new Sample(isPicture ? SampleKind.Picture : SampleKind.Sound, sample, time, duration);
            }
            if ((flags & SourceReader.EndOfStream) != 0)
                return new Sample(actual == _videoIndex ? SampleKind.PictureEnded : SampleKind.SoundEnded, null, 0, 0);
        }
        throw new InvalidOperationException("The video reader stopped giving samples");
    }

    /// <summary>The next picture; null at the end of the video.</summary>
    public Sample? NextVideo()
    {
        var next = Next(picture: true, sound: false);
        return next.Kind == SampleKind.Picture ? next : null;
    }

    /// <summary>The stride of what <see cref="CopyFrame"/> returns: top row first, with no padding.</summary>
    public int Stride() => Width * 4;

    /// <summary>The picture top row first. The returned array is reused by the next call.</summary>
    public byte[] CopyFrame(Sample sample)
    {
        int rowBytes = Width * 4;
        if (_frame.Length != rowBytes * Height) _frame = new byte[rowBytes * Height];
        IMFMediaBuffer? buffer = null;
        try
        {
            sample.Native!.ConvertToContiguousBuffer(out buffer);
            byte* data;
            uint length;
            buffer.Lock(&data, null, &length);
            try
            {
                int pitch = Math.Abs(_stride);
                if (pitch < rowBytes || length < (ulong)pitch * (ulong)Height) throw new InvalidOperationException("A video frame was smaller than its size says");
                fixed (byte* to = _frame)
                {
                    for (int y = 0; y < Height; y++)
                    {
                        byte* row = data + (_stride < 0 ? Height - 1 - y : y) * pitch;
                        Buffer.MemoryCopy(row, to + y * rowBytes, rowBytes, rowBytes);
                    }
                }
            }
            finally
            {
                buffer.Unlock();
            }
            return _frame;
        }
        finally
        {
            ComObjects.Release(buffer);
        }
    }

    /// <summary>The sample's stereo frames into <paramref name="pcm"/>; how many.</summary>
    public int CopySound(Sample sample, ref short[] pcm)
    {
        IMFMediaBuffer? buffer = null;
        try
        {
            sample.Native!.ConvertToContiguousBuffer(out buffer);
            byte* data;
            uint length;
            buffer.Lock(&data, null, &length);
            try
            {
                int shorts = (int)(length / sizeof(short));
                if (pcm.Length < shorts) pcm = new short[shorts];
                new ReadOnlySpan<byte>(data, shorts * sizeof(short)).CopyTo(MemoryMarshal.AsBytes(pcm.AsSpan()));
                return shorts / AudioTimeline.Channels;
            }
            finally
            {
                buffer.Unlock();
            }
        }
        finally
        {
            ComObjects.Release(buffer);
        }
    }

    public void Dispose()
    {
        Marshal.ReleaseComObject(_reader);
        MediaFoundation.Stop();
    }

    internal sealed class Sample(SampleKind kind, IMFSample? native, long time, long duration) : IDisposable
    {
        public SampleKind Kind { get; } = kind;
        public IMFSample? Native { get; } = native;
        public long Time { get; } = time;
        public long Duration { get; } = duration;

        public void Dispose() => ComObjects.Release(Native);
    }
}
