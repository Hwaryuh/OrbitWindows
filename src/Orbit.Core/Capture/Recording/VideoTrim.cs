namespace Orbit.Core.Capture.Recording;

/// <summary>The kept part of a video: the two ends stay in the video and a tenth of a second apart (Orbit: VideoEditorModel).</summary>
public sealed class VideoTrim(TimeSpan length)
{
    public static readonly TimeSpan Shortest = TimeSpan.FromSeconds(0.1);
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(0.05);

    public TimeSpan Length { get; } = length;
    public TimeSpan Start { get; private set; }
    public TimeSpan End { get; private set; } = length;

    public void SetStart(TimeSpan at)
    {
        var latest = End - Shortest;
        Start = at < TimeSpan.Zero ? TimeSpan.Zero : at > latest ? (latest < TimeSpan.Zero ? TimeSpan.Zero : latest) : at;
    }

    public void SetEnd(TimeSpan at)
    {
        var earliest = Start + Shortest;
        End = at > Length ? Length : at < earliest ? (earliest > Length ? Length : earliest) : at;
    }

    public void Reset()
    {
        Start = TimeSpan.Zero;
        End = Length;
    }

    /// <summary>Whether what would be exported differs from the file. A hair at either end doesn't count.</summary>
    public bool IsEdited(bool muted) => muted || Start > Slack || End < Length - Slack;

    /// <summary>A time moved inside the kept part.</summary>
    public TimeSpan Clamped(TimeSpan at) => at < Start ? Start : at > End ? End : at;

    /// <summary>"1:02.5", as Orbit's editor.</summary>
    public static string Clock(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds / 100}";
    }
}
