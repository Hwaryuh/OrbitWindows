using Orbit.Core.Capture.Recording;

namespace Orbit.Tests;

public class AudioTimelineTests
{
    [Fact]
    public void MonoGoesToBothSidesAndSourcesAdd()
    {
        var timeline = new AudioTimeline(100);
        timeline.Add(0, [0.25f, -0.25f, 0.5f, 0.5f], 2, 1);
        timeline.Add(0, [0.25f, 0.25f], 1, 1);
        var pcm = timeline.Take(2, out long start);
        Assert.Equal(0, start);
        Assert.Equal(new short[] { 16384, 0, 24575, 24575 }, pcm);
    }

    [Fact]
    public void GainZeroIsSilenceAndLoudSumsAreClipped()
    {
        var timeline = new AudioTimeline(100);
        timeline.Add(0, [0.9f, 0.9f], 2, 0);
        timeline.Add(1, [0.8f, -0.8f], 2, 1);
        timeline.Add(1, [0.8f, -0.8f], 2, 1);
        var pcm = timeline.Take(2, out _);
        Assert.Equal(new short[] { 0, 0, short.MaxValue, -short.MaxValue }, pcm);
    }

    [Fact]
    public void TakeIsContiguousAndClearsWhatItHandedOn()
    {
        var timeline = new AudioTimeline(4);
        timeline.Add(0, [1, 1, 1, 1], 2, 0.5f);
        Assert.NotNull(timeline.Take(2, out long first));
        Assert.Null(timeline.Take(2, out _));
        // The ring wraps over the frames already handed on: they start again from silence.
        timeline.Add(4, [0.5f, 0.5f], 2, 1);
        var pcm = timeline.Take(5, out long second);
        Assert.Equal((0, 2), (first, second));
        Assert.Equal(new short[] { 0, 0, 0, 0, 16384, 16384 }, pcm);
    }

    [Fact]
    public void LatePacketsAndPacketsBeyondTheRingAreTrimmed()
    {
        var timeline = new AudioTimeline(4);
        timeline.Take(3, out _);
        timeline.Add(1, [1, 1, 1, 1, 1, 1], 1, 1); // frames 1..6: 1 and 2 are late, 3..6 fit
        timeline.Add(7, [1, 1], 1, 1); // beyond the ring (3..6)
        Assert.Equal(4, timeline.Trimmed);
        var pcm = timeline.Take(10, out long start);
        Assert.Equal(3, start);
        Assert.Equal(8, pcm!.Length); // only the ring: 4 frames
        Assert.All(pcm, s => Assert.Equal(short.MaxValue, s));
    }

    [Theory]
    [InlineData(10_000_000, 48_000)]
    [InlineData(208, 0)] // under a frame
    [InlineData(209, 1)]
    [InlineData(-1, -1)]
    public void TicksBecomeWholeFrames(long ticks, long frames) => Assert.Equal(frames, AudioTimeline.Frames(ticks));

    [Fact]
    public void ConsecutiveSpansMeetExactly()
    {
        long a = AudioTimeline.Ticks(480), b = AudioTimeline.Ticks(481), c = AudioTimeline.Ticks(961);
        Assert.Equal(c - a, (b - a) + (c - b));
        Assert.Equal(100_000, a);
    }
}

public class PacketPlacerTests
{
    [Fact]
    public void PacketsFollowEachOtherWhenTheirStampIsClose()
    {
        var placer = new PacketPlacer();
        Assert.Equal(1000, placer.Place(1000, 480, false));
        // 10 ms of jitter: still right after the last packet.
        Assert.Equal(1480, placer.Place(1960, 480, false));
    }

    [Fact]
    public void AGapOrABreakGoesToTheStamp()
    {
        var placer = new PacketPlacer();
        placer.Place(0, 480, false);
        Assert.Equal(480 + PacketPlacer.Tolerance + 1, placer.Place(480 + PacketPlacer.Tolerance + 1, 480, false));
        long next = 480 + PacketPlacer.Tolerance + 1 + 480;
        Assert.Equal(next + 5, placer.Place(next + 5, 480, discontinuity: true));
    }

    [Fact]
    public void AfterResetThePacketGoesToItsStamp()
    {
        var placer = new PacketPlacer();
        placer.Place(0, 480, false);
        placer.Reset();
        Assert.Equal(490, placer.Place(490, 480, false));
    }
}

public class AudioCutTests
{
    [Fact]
    public void SoundBeforeTheFirstFrameIsCut()
    {
        Assert.True(AudioCut.Fit(position: 0, frames: 4800, origin: 1200, stop: long.MaxValue, out int skip, out int count, out long start, out long duration));
        Assert.Equal((1200, 3600, 0L), (skip, count, start));
        Assert.Equal(AudioTimeline.Ticks(3600), duration);
    }

    [Fact]
    public void SoundAfterTheStopIsCutAndWhollyOutsideIsSkipped()
    {
        Assert.True(AudioCut.Fit(4800, 4800, 0, 6000, out int skip, out int count, out long start, out _));
        Assert.Equal((0, 1200, AudioTimeline.Ticks(4800)), (skip, count, start));
        Assert.False(AudioCut.Fit(0, 1000, 1000, long.MaxValue, out _, out _, out _, out _));
        Assert.False(AudioCut.Fit(6000, 1000, 0, 6000, out _, out _, out _, out _));
    }
}
