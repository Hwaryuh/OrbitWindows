using System.Windows.Media.Imaging;
using Orbit.Core.Capture.Recording;

namespace Orbit.Tests;

/// <summary>
/// Real Media Foundation on synthetic files: no window, no sound device, nothing played. Frames carry
/// their number in the red channel; the top half is bright and the bottom half blue, to catch a
/// picture written upside down.
/// </summary>
public sealed class VideoExportTests : IDisposable
{
    private const int Width = 320, Height = 180, Fps = 30;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "orbit-video-" + Guid.NewGuid().ToString("N"));

    public VideoExportTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
            // A decoder still closing; the system cleans its temp folder.
        }
    }

    private string File(string name) => Path.Combine(_folder, name);

    private static KeptPart Part(double start, double end) => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));

    private static byte RedOf(int frame) => (byte)Math.Min(255, frame * 2);

    /// <param name="videoSeconds">How long the picture runs.</param>
    /// <param name="soundFrom">Where the sound starts; null for no sound track.</param>
    /// <param name="soundTo">Where the sound stops.</param>
    private string MakeVideo(string name, double videoSeconds = 3, double? soundFrom = 0, double soundTo = 3) => OnMta(() =>
    {
        string path = File(name);
        using var writer = Mp4Writer.CreateInMemory(path, Width, Height, Fps, soundFrom is not null);
        var pixels = new byte[Width * Height * 4];
        long frameTicks = TimeSpan.TicksPerSecond / Fps;
        var silence = new short[AudioTimeline.Rate / 10 * AudioTimeline.Channels];
        double soundAt = soundFrom ?? double.MaxValue;
        int total = (int)(videoSeconds * Fps);
        for (int frame = 0; frame < total; frame++)
        {
            for (int y = 0, at = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++, at += 4)
                {
                    bool top = y < Height / 2;
                    pixels[at] = top ? (byte)0 : (byte)220;
                    pixels[at + 1] = top ? (byte)160 : (byte)0;
                    pixels[at + 2] = top ? RedOf(frame) : (byte)0;
                    pixels[at + 3] = 255;
                }
            }
            writer.WriteBgra(pixels, Width * 4, frame * frameTicks, frameTicks);
            while (soundAt < Math.Min(soundTo, (frame + 1.0) / Fps))
            {
                writer.WriteAudio(silence, 0, AudioTimeline.Rate / 10, (long)(soundAt * TimeSpan.TicksPerSecond), TimeSpan.TicksPerSecond / 10);
                soundAt += 0.1;
            }
        }
        while (soundAt < soundTo)
        {
            writer.WriteAudio(silence, 0, AudioTimeline.Rate / 10, (long)(soundAt * TimeSpan.TicksPerSecond), TimeSpan.TicksPerSecond / 10);
            soundAt += 0.1;
        }
        writer.Finish();
        return path;
    });

    private static T OnMta<T>(Func<T> work) => Task.Run(work).GetAwaiter().GetResult();

    private static void AssertNear(double expected, TimeSpan actual, double tolerance = 0.1) =>
        Assert.InRange(actual.TotalSeconds, expected - tolerance, expected + tolerance);

    /// <summary>The first frame's picture and the first sound sample's time, read through the export's own reader.</summary>
    private static (byte[] Pixels, long? SoundAt) ReadStart(string path) => OnMta(() =>
    {
        using var reader = new VideoReader(path, sound: true);
        byte[] pixels;
        using (var first = reader.NextVideo()!) pixels = (byte[])reader.CopyFrame(first).Clone();
        long? soundAt = null;
        if (reader.HasSound)
        {
            using var sound = reader.Next(picture: false, sound: true);
            if (sound.Kind == VideoReader.SampleKind.Sound) soundAt = sound.Time;
        }
        return (pixels, soundAt);
    });

    [Fact]
    public async Task TheWholeVideoComesThrough()
    {
        var source = MakeVideo("source.mp4");
        var output = File("out.mp4");
        await VideoExport.ExportMp4Async(source, output, Part(0, 3), muted: false);
        var info = VideoProbe.Read(output);
        Assert.Equal((Width, Height), (info.Width, info.Height));
        AssertNear(3, info.Length);
        AssertNear(3, info.Sound!.Value, 0.15);
    }

    [Fact]
    public async Task TrimmingKeepsThatLengthInBothStreams()
    {
        var source = MakeVideo("source.mp4");
        var output = File("out.mp4");
        await VideoExport.ExportMp4Async(source, output, Part(1, 2.25), muted: false);
        var info = VideoProbe.Read(output);
        AssertNear(1.25, info.Length);
        AssertNear(1.25, info.Sound!.Value, 0.15);
        Assert.InRange(info.Frames, 35, 40);
    }

    [Fact]
    public async Task TrimmingStartsAtTheRightFrameNotAKeyFrameBeforeIt()
    {
        var source = MakeVideo("source.mp4");
        var output = File("out.mp4");
        await VideoExport.ExportMp4Async(source, output, Part(1.5, 2.5), muted: false);
        var (pixels, _) = ReadStart(output);
        // Frame 45: compression moves colors a little, and never by half a second's worth.
        Assert.InRange(pixels[(10 * Width + 10) * 4 + 2], RedOf(45) - 12, RedOf(45) + 12);
    }

    [Fact]
    public async Task ThePictureStaysTheRightWayUp()
    {
        var source = MakeVideo("source.mp4");
        var output = File("out.mp4");
        await VideoExport.ExportMp4Async(source, output, Part(0.5, 1), muted: true);
        var (pixels, _) = ReadStart(output);
        Assert.True(pixels[(10 * Width + 10) * 4 + 1] > 100, "top row should be green-ish");
        Assert.True(pixels[(170 * Width + 10) * 4] > 150, "bottom row should be blue");
    }

    [Fact]
    public async Task MutingLeavesNoSoundTrack()
    {
        var source = MakeVideo("source.mp4");
        var output = File("out.mp4");
        await VideoExport.ExportMp4Async(source, output, Part(0, 1), muted: true);
        Assert.Null(VideoProbe.Read(output).Sound);
    }

    [Fact]
    public async Task AVideoWithoutSoundExportsWithoutSound()
    {
        var source = MakeVideo("source.mp4", soundFrom: null);
        var output = File("out.mp4");
        await VideoExport.ExportMp4Async(source, output, Part(0, 1), muted: false);
        var info = VideoProbe.Read(output);
        AssertNear(1, info.Length);
        Assert.Null(info.Sound);
    }

    [Fact]
    public async Task SoundThatStartsLateKeepsItsPlaceAfterTheTrim()
    {
        var source = MakeVideo("source.mp4", soundFrom: 1);
        var output = File("out.mp4");
        await VideoExport.ExportMp4Async(source, output, Part(0.5, 2), muted: false);
        var (_, soundAt) = ReadStart(output);
        Assert.NotNull(soundAt);
        Assert.InRange(soundAt!.Value / (double)TimeSpan.TicksPerSecond, 0.4, 0.6);
    }

    [Fact]
    public async Task PictureEndingBeforeTheSoundStillFinishes()
    {
        var source = MakeVideo("source.mp4", videoSeconds: 1, soundTo: 2);
        var output = File("out.mp4");
        await VideoExport.ExportMp4Async(source, output, Part(0, 2), muted: false);
        AssertNear(1, VideoProbe.Read(output).Length);
    }

    [Fact]
    public async Task SoundEndingBeforeThePictureStillFinishes()
    {
        var source = MakeVideo("source.mp4", videoSeconds: 2, soundTo: 0.5);
        var output = File("out.mp4");
        await VideoExport.ExportMp4Async(source, output, Part(0, 2), muted: false);
        AssertNear(2, VideoProbe.Read(output).Length);
    }

    [Fact]
    public async Task CancellingLeavesNoFile()
    {
        var source = MakeVideo("source.mp4");
        var output = File("out.mp4");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VideoExport.ExportMp4Async(source, output, Part(0, 3), false, cancel.Token));
        Assert.False(System.IO.File.Exists(output));
        Assert.False(System.IO.File.Exists(output + ".part"));
    }

    [Fact]
    public async Task APartPastTheEndFailsAndLeavesNoFile()
    {
        var source = MakeVideo("source.mp4");
        var output = File("out.mp4");
        // Media Foundation refuses to seek there (0xC00D36E5); the editor's ends can't get there anyway.
        await Assert.ThrowsAnyAsync<Exception>(() => VideoExport.ExportMp4Async(source, output, Part(10, 11), false));
        Assert.False(System.IO.File.Exists(output));
        Assert.False(System.IO.File.Exists(output + ".part"));
    }

    [Fact]
    public async Task TheOriginalIsNeverChanged()
    {
        var source = MakeVideo("source.mp4");
        var before = await System.IO.File.ReadAllBytesAsync(source);
        await VideoExport.ExportMp4Async(source, File("out.mp4"), Part(1, 2), true);
        await VideoExport.ExportGifAsync(source, File("out.gif"), Part(1, 2));
        Assert.Equal(before, await System.IO.File.ReadAllBytesAsync(source));
    }

    [Fact]
    public async Task AGifHasTheRightFramesAndLengthAndLoops()
    {
        var source = MakeVideo("source.mp4");
        var output = File("out.gif");
        await VideoExport.ExportGifAsync(source, output, Part(1, 2));
        var bytes = await System.IO.File.ReadAllBytesAsync(output);
        Assert.Equal("GIF89a"u8.ToArray(), bytes[..6]);
        Assert.True(bytes.AsSpan().IndexOf("NETSCAPE2.0"u8) > 0);

        // A decoder that is not ours.
        using var stream = new MemoryStream(bytes);
        var gif = new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Equal(15, gif.Frames.Count);
        Assert.Equal(Width, gif.Frames[0].PixelWidth);
        int hundredths = gif.Frames.Sum(f => (int)(ushort)((BitmapMetadata)f.Metadata).GetQuery("/grctlext/Delay"));
        Assert.InRange(hundredths, 99, 101);

        // The right way up, and at the right frame: frame 30's red.
        var top = Pixel(gif.Frames[0], 10, 10);
        var bottom = Pixel(gif.Frames[0], 10, 170);
        Assert.True(top.Green > 100, "top should be green-ish");
        Assert.True(bottom.Blue > 150, "bottom should be blue");
        Assert.InRange(top.Red, RedOf(30) - 20, RedOf(30) + 20);
    }

    private static (byte Blue, byte Green, byte Red) Pixel(BitmapSource frame, int x, int y)
    {
        var converted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[0], pixel[1], pixel[2]);
    }

    [Fact]
    public async Task AGifIsNoWiderThanTheLimit()
    {
        var pixels = new byte[1000 * 500 * 4];
        var (width, height) = VideoExport.FitWithin(1000, 500, VideoExport.GifMaxWidth, int.MaxValue);
        Assert.Equal((800, 400), (width, height));
        Assert.Equal(800 * 400 * 4, VideoExport.Scaled(pixels, 1000, 500, width, height).Length);
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(30, 15)]
    [InlineData(60, 7.5)]
    [InlineData(1000, 3)]
    public void GifFrameRates(double seconds, double fps) => Assert.Equal(fps, VideoExport.GifFps(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(15, 150)]
    [InlineData(7.5, 75)]
    [InlineData(3, 30)]
    public void GifDelaysAddUpToTheLength(double fps, int frames)
    {
        var delays = new GifDelays(fps);
        int total = Enumerable.Range(0, frames).Sum(_ => delays.Next());
        Assert.InRange(total, frames / fps * 100 - 1, frames / fps * 100 + 1);
    }

    /// <summary>The compressor against a real decoder, across code widths and a table reset.</summary>
    [Theory]
    [InlineData(1, 1, "flat")]
    [InlineData(2, 2, "flat")]
    [InlineData(256, 256, "gradient")]
    [InlineData(600, 400, "gradient")]
    [InlineData(800, 600, "noise")]
    [InlineData(16, 16, "two")]
    public void GifPicturesDecode(int width, int height, string kind)
    {
        var random = new Random(7);
        var pixels = new byte[width * height * 4];
        for (int y = 0, at = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++, at += 4)
            {
                (pixels[at], pixels[at + 1], pixels[at + 2]) = kind switch
                {
                    "flat" => ((byte)40, (byte)80, (byte)200),
                    "two" => x < width / 2 ? ((byte)0, (byte)0, (byte)0) : ((byte)255, (byte)255, (byte)255),
                    "gradient" => ((byte)(x * 255 / width), (byte)(y * 255 / height), (byte)((x + y) * 255 / (width + height))),
                    _ => ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)),
                };
            }
        }
        using var stream = new MemoryStream();
        var gif = new GifWriter(stream, width, height);
        gif.AddFrame(pixels, 10);
        gif.AddFrame(pixels, 10);
        gif.Finish();
        stream.Position = 0;
        var decoded = new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Equal(2, decoded.Frames.Count);
        foreach (var (x, y) in new[] { (0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1), (width / 2, height / 2) })
        {
            int at = (y * width + x) * 4;
            var pixel = Pixel(decoded.Frames[1], x, y);
            // Color reduction moves a color by some, but noise has no neighbours to average with.
            int allowed = kind == "noise" ? 128 : 40;
            Assert.InRange(pixel.Blue, pixels[at] - allowed, pixels[at] + allowed);
            Assert.InRange(pixel.Green, pixels[at + 1] - allowed, pixels[at + 1] + allowed);
            Assert.InRange(pixel.Red, pixels[at + 2] - allowed, pixels[at + 2] + allowed);
        }
    }

    [Fact]
    public async Task TheFilmstripHasEachPictureAtItsOwnPlace()
    {
        var source = MakeVideo("source.mp4");
        var pictures = await VideoExport.FilmstripAsync(source, TimeSpan.FromSeconds(3), 6, 120);
        Assert.Equal(6, pictures.Count);
        Assert.All(pictures, p => Assert.Equal((120, 68), (p.Width, p.Height)));
        Assert.All(pictures, p => Assert.Equal(p.Width * p.Height * 4, p.Pixels.Length));
        // Later pictures are later frames, and each kept its own pixels.
        byte Red(VideoPicture p) => p.Pixels[(5 * p.Width + 5) * 4 + 2];
        Assert.True(Red(pictures[5]) > Red(pictures[0]) + 60, $"{Red(pictures[0])} then {Red(pictures[5])}");
    }

    [Fact]
    public async Task AVideoSmallerThanTheFilmstripKeepsSeparatePictures()
    {
        var source = MakeVideo("source.mp4");
        var pictures = await VideoExport.FilmstripAsync(source, TimeSpan.FromSeconds(3), 2, 4000);
        Assert.NotSame(pictures[0].Pixels, pictures[1].Pixels);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(101, 100)]
    [InlineData(100, 0)]
    [InlineData(100, 101)]
    public void TheMemoryWriterRefusesBadSizes(int width, int height) =>
        Assert.Throws<ArgumentException>(() => Mp4Writer.CreateInMemory(File("x.mp4"), width, height, 30, false));
}
