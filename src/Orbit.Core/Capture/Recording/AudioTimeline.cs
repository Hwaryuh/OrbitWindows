namespace Orbit.Core.Capture.Recording;

/// <summary>
/// The recording's sound as it is mixed: 48 kHz stereo, positions in frames from the moment the
/// sound thread started. Sources add into a float ring; what lies before <see cref="Written"/> is
/// final and has been handed on as 16-bit PCM. A packet that lands before it (late) or too far after
/// it (beyond the ring) loses that part, counted in <see cref="Trimmed"/>. Use from one thread.
/// </summary>
public sealed class AudioTimeline
{
    public const int Rate = 48_000;
    public const int Channels = 2;

    private readonly float[] _mix;
    private readonly int _capacity;

    public AudioTimeline(int capacityFrames = Rate * 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityFrames, 1);
        _capacity = capacityFrames;
        _mix = new float[capacityFrames * Channels];
    }

    /// <summary>Frames handed on so far: the next <see cref="Take"/> starts here.</summary>
    public long Written { get; private set; }

    public long Trimmed { get; private set; }

    public int Capacity => _capacity;

    /// <summary>Adds <paramref name="samples"/> (interleaved, mono or stereo; mono goes to both sides) at <paramref name="at"/>, times <paramref name="gain"/>.</summary>
    public void Add(long at, ReadOnlySpan<float> samples, int channels, float gain)
    {
        if (channels is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(channels));
        int frames = samples.Length / channels;
        long start = Math.Max(at, Written), end = Math.Min(at + frames, Written + _capacity);
        if (end <= start)
        {
            Trimmed += frames;
            return;
        }
        Trimmed += frames - (end - start);
        if (gain == 0) return;
        for (long p = start; p < end; p++)
        {
            int i = (int)(p % _capacity) * Channels;
            int s = (int)(p - at) * channels;
            float left = samples[s], right = channels == 2 ? samples[s + 1] : left;
            _mix[i] += left * gain;
            _mix[i + 1] += right * gain;
        }
    }

    /// <summary>
    /// The mix from <see cref="Written"/> up to <paramref name="until"/> (at most the ring) as 16-bit
    /// stereo PCM, clipped; null when there is nothing new. <paramref name="start"/> is its position.
    /// </summary>
    public short[]? Take(long until, out long start)
    {
        start = Written;
        long end = Math.Min(until, Written + _capacity);
        if (end <= Written) return null;
        var pcm = new short[(end - Written) * Channels];
        int k = 0;
        for (long p = Written; p < end; p++)
        {
            int i = (int)(p % _capacity) * Channels;
            pcm[k++] = Pcm(_mix[i]);
            pcm[k++] = Pcm(_mix[i + 1]);
            _mix[i] = 0;
            _mix[i + 1] = 0;
        }
        Written = end;
        return pcm;
    }

    private static short Pcm(float v) => (short)MathF.Round(Math.Clamp(v, -1f, 1f) * short.MaxValue);

    /// <summary>100 ns ticks as whole frames (rounded down).</summary>
    public static long Frames(long ticks) => ticks >= 0 ? ticks * Rate / TimeSpan.TicksPerSecond : -((-ticks * Rate + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond);

    /// <summary>Frames as 100 ns ticks (rounded down): consecutive spans computed this way meet exactly.</summary>
    public static long Ticks(long frames) => frames * TimeSpan.TicksPerSecond / Rate;
}

/// <summary>
/// Where each packet of one source goes. Packets follow each other without gaps unless the
/// packet's own time (its QPC stamp) is more than <see cref="Tolerance"/> away from the end of the
/// last one, or the device reports a break; then it goes at its own time. Reset when the source is
/// stopped and started again.
/// </summary>
public sealed class PacketPlacer
{
    /// <summary>40 ms.</summary>
    public const long Tolerance = AudioTimeline.Rate * 40 / 1000;

    private long? _next;

    public long Place(long estimate, int frames, bool discontinuity)
    {
        long at = _next is { } next && !discontinuity && Math.Abs(estimate - next) <= Tolerance ? next : estimate;
        _next = at + frames;
        return at;
    }

    public void Reset() => _next = null;
}

/// <summary>
/// The part of a sound chunk that belongs in the video, whose time zero is the first frame:
/// <paramref name="origin"/> and <paramref name="stop"/> are positions on the sound timeline.
/// </summary>
public static class AudioCut
{
    /// <returns>False when nothing of it is inside; else the frames to skip and keep and the sample's start and length in ticks.</returns>
    public static bool Fit(long position, int frames, long origin, long stop, out int skip, out int count, out long start, out long duration)
    {
        long from = Math.Max(position, origin), to = Math.Min(position + frames, stop);
        skip = (int)Math.Max(0, from - position);
        count = (int)Math.Max(0, to - from);
        start = AudioTimeline.Ticks(from - origin);
        duration = AudioTimeline.Ticks(to - origin) - start;
        return count > 0;
    }
}
