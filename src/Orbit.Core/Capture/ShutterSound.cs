namespace Orbit.Core.Capture;

/// <summary>
/// The sound a capture makes (Orbit plays macOS's "Screen Capture" sound; Windows ships none it
/// may borrow). Made here: two short bursts of filtered noise, a shutter opening and closing, as a
/// 16-bit mono WAV file in memory. The same every time (fixed seed).
/// </summary>
public static class ShutterSound
{
    public const int SampleRate = 44100;

    private static byte[]? _wave;

    public static byte[] Wave() => _wave ??= Make();

    private static byte[] Make()
    {
        int length = (int)(SampleRate * 0.16);
        var samples = new short[length];
        var random = new Random(0x0B17);
        Burst(samples, random, start: 0, seconds: 0.035, gain: 0.55);
        Burst(samples, random, start: (int)(SampleRate * 0.075), seconds: 0.05, gain: 0.45);
        return ToWave(samples);
    }

    /// <summary>Noise that starts sharp and dies away fast, softened by a one-pole low-pass.</summary>
    private static void Burst(short[] samples, Random random, int start, double seconds, double gain)
    {
        int count = Math.Min((int)(SampleRate * seconds), samples.Length - start);
        double smoothed = 0;
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / count;
            double envelope = Math.Exp(-6 * t) * Math.Min(1, i / (SampleRate * 0.001));
            smoothed += 0.35 * (random.NextDouble() * 2 - 1 - smoothed);
            int value = samples[start + i] + (int)(smoothed * envelope * gain * short.MaxValue);
            samples[start + i] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
        }
    }

    internal static byte[] ToWave(short[] samples)
    {
        int dataBytes = samples.Length * 2;
        using var stream = new MemoryStream(44 + dataBytes);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + dataBytes);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1);              // PCM
            writer.Write((short)1);              // mono
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);        // bytes per second
            writer.Write((short)2);              // block align
            writer.Write((short)16);             // bits per sample
            writer.Write("data"u8);
            writer.Write(dataBytes);
            foreach (var sample in samples) writer.Write(sample);
        }
        return stream.ToArray();
    }
}
