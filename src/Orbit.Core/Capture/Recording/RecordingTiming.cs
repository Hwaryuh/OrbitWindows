using System.Diagnostics;

namespace Orbit.Core.Capture.Recording;

/// <summary>
/// The size of a recording: even sides (H.264 stores colour at half resolution), at least
/// <see cref="Min"/> px, and within what H.264 level 5.2 and common hardware encoders take
/// (4096 px a side, 36,864 macroblocks a frame, as 4096 x 2304).
/// </summary>
public static class RecordingSize
{
    public const int Min = 16;
    public const int MaxSide = 4096;
    public const int MaxMacroblocks = 36864;

    public enum Verdict { Ok, TooSmall, TooBig }

    public static Verdict Fit(int width, int height, out int evenWidth, out int evenHeight)
    {
        evenWidth = width & ~1;
        evenHeight = height & ~1;
        if (evenWidth < Min || evenHeight < Min) return Verdict.TooSmall;
        long macroblocks = (long)((evenWidth + 15) / 16) * ((evenHeight + 15) / 16);
        if (evenWidth > MaxSide || evenHeight > MaxSide || macroblocks > MaxMacroblocks) return Verdict.TooBig;
        return Verdict.Ok;
    }

    /// <summary>About 0.1 bit per pixel per frame: sharp text without huge files; 4 to 40 Mbit/s.</summary>
    public static uint Bitrate(int width, int height, int fps) =>
        (uint)Math.Clamp((double)width * height * fps * 0.1, 4_000_000, 40_000_000);
}

/// <summary>
/// Which captured frames go into the video. Windows.Graphics.Capture sends a frame whenever the
/// screen changes, at up to the monitor's refresh rate; a frame is kept once at least three
/// quarters of a frame interval have passed since the last kept one (the slack absorbs the jitter
/// of a 60 Hz monitor recorded at 60 fps). Times are 100 ns ticks.
/// </summary>
public sealed class FramePacer(int fps)
{
    private readonly long _gap = fps > 0 ? TimeSpan.TicksPerSecond / fps * 3 / 4 : throw new ArgumentOutOfRangeException(nameof(fps));
    private long? _last;

    public bool Keep(long time)
    {
        if (_last is { } last && time - last < _gap) return false;
        _last = time;
        return true;
    }
}

/// <summary>
/// Start and length of each sample. A frame is held until the next one comes, and then written with
/// the gap as its length; at stop the held frame lasts until the stop. A still screen therefore
/// becomes one long frame, and the video is as long as the recording was. Times are 100 ns ticks
/// on the capture's clock; samples start at 0.
/// </summary>
public sealed class SampleClock
{
    private long? _origin;
    private long _held = -1;

    public bool HasFrame => _origin is not null;

    /// <summary>The first frame's time on the capture clock: the video's zero.</summary>
    public long? Origin => _origin;

    /// <summary>The held frame's start (from the origin).</summary>
    public long Held => _held;

    /// <summary>
    /// Takes a new frame. False when it is not later than the held one (it is dropped). Otherwise
    /// <paramref name="write"/> is the held frame's start and length, or null for the first frame.
    /// </summary>
    public bool Accept(long time, out (long Start, long Duration)? write)
    {
        write = null;
        if (_origin is not { } origin)
        {
            _origin = time;
            _held = 0;
            return true;
        }
        long at = time - origin;
        if (at <= _held) return false;
        write = (_held, at - _held);
        _held = at;
        return true;
    }

    /// <summary>The held frame's start and length up to <paramref name="stop"/> (at least 1 ms); null without frames.</summary>
    public (long Start, long Duration)? End(long stop)
    {
        if (_origin is not { } origin) return null;
        long duration = Math.Max(stop - origin - _held, TimeSpan.TicksPerMillisecond);
        return (_held, duration);
    }

    /// <summary>The whole length once <see cref="End"/> is known.</summary>
    public static TimeSpan Length((long Start, long Duration) last) => TimeSpan.FromTicks(last.Start + last.Duration);

    /// <summary>Now on the clock Windows.Graphics.Capture stamps frames with (QPC), in 100 ns ticks.</summary>
    public static long Now() => Stopwatch.GetElapsedTime(0).Ticks;
}
