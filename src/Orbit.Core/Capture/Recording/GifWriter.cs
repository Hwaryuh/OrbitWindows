namespace Orbit.Core.Capture.Recording;

/// <summary>
/// A looping GIF89a written as it goes, so a long clip doesn't wait in memory. Each frame has its
/// own palette of up to 256 colors from a median cut over a 5-bit-per-channel histogram, without
/// dithering: screen recordings are flat colors and text, and dither would make them bigger.
/// </summary>
public sealed class GifWriter
{
    private const int Bins = 1 << 15;
    private const int MaxColors = 256;
    private const int MaxCode = 4095;

    private readonly Stream _output;
    private readonly int _width, _height;
    private readonly int[] _histogram = new int[Bins];
    private readonly byte[] _indexOfBin = new byte[Bins];
    private readonly byte[] _block = new byte[255];
    private int _blockLength;
    private bool _finished;

    public GifWriter(Stream output, int width, int height)
    {
        if (width is < 1 or > 65535 || height is < 1 or > 65535) throw new ArgumentException("A GIF's sides are 1 to 65535");
        _output = output;
        _width = width;
        _height = height;
        output.Write("GIF89a"u8);
        WriteShort(width);
        WriteShort(height);
        // No global palette: each frame has its own.
        output.WriteByte(0);
        output.WriteByte(0);
        output.WriteByte(0);
        // Netscape's extension: loop for ever.
        output.Write([0x21, 0xFF, 11]);
        output.Write("NETSCAPE2.0"u8);
        output.Write([3, 1, 0, 0, 0]);
    }

    /// <param name="pixels">Blue, green, red, unused; top row first.</param>
    /// <param name="delay">How long it shows, in hundredths of a second.</param>
    public void AddFrame(byte[] pixels, int delay)
    {
        if (pixels.Length < _width * _height * 4) throw new ArgumentException("The picture is smaller than the GIF", nameof(pixels));
        var palette = BuildPalette(pixels);
        int bits = 1;
        while (1 << bits < palette.Count) bits++;

        // How long it shows, replacing the previous frame.
        _output.Write([0x21, 0xF9, 4, 0x04]);
        WriteShort(Math.Clamp(delay, 0, 65535));
        _output.Write([0, 0]);

        _output.WriteByte(0x2C);
        WriteShort(0);
        WriteShort(0);
        WriteShort(_width);
        WriteShort(_height);
        _output.WriteByte((byte)(0x80 | (bits - 1)));
        foreach (var (red, green, blue) in palette)
        {
            _output.WriteByte(red);
            _output.WriteByte(green);
            _output.WriteByte(blue);
        }
        for (int i = palette.Count; i < 1 << bits; i++) _output.Write([0, 0, 0]);

        int minimumCode = Math.Max(2, bits);
        _output.WriteByte((byte)minimumCode);
        Compress(pixels, minimumCode);
        _output.WriteByte(0);
    }

    public void Finish()
    {
        if (_finished) return;
        _finished = true;
        _output.WriteByte(0x3B);
        _output.Flush();
    }

    private static int BinOf(byte[] pixels, int at) =>
        ((pixels[at + 2] >> 3) << 10) | ((pixels[at + 1] >> 3) << 5) | (pixels[at] >> 3);

    /// <summary>Splits the colors in use by the widest channel until there are enough boxes; each box's average is a color.</summary>
    private List<(byte Red, byte Green, byte Blue)> BuildPalette(byte[] pixels)
    {
        Array.Clear(_histogram);
        int total = _width * _height;
        for (int i = 0, at = 0; i < total; i++, at += 4) _histogram[BinOf(pixels, at)]++;

        var used = new List<int>();
        for (int bin = 0; bin < Bins; bin++)
            if (_histogram[bin] > 0) used.Add(bin);

        var boxes = new List<List<int>> { used };
        while (boxes.Count < MaxColors)
        {
            int widest = -1, widestRange = 0, channelOfWidest = 0;
            for (int b = 0; b < boxes.Count; b++)
            {
                if (boxes[b].Count < 2) continue;
                for (int channel = 0; channel < 3; channel++)
                {
                    int shift = channel * 5, low = 31, high = 0;
                    foreach (int bin in boxes[b])
                    {
                        int value = (bin >> shift) & 31;
                        low = Math.Min(low, value);
                        high = Math.Max(high, value);
                    }
                    if (high - low > widestRange)
                    {
                        widest = b;
                        widestRange = high - low;
                        channelOfWidest = channel;
                    }
                }
            }
            if (widest < 0) break;

            int split = channelOfWidest * 5;
            var box = boxes[widest];
            box.Sort((x, y) => ((x >> split) & 31).CompareTo((y >> split) & 31));
            long half = box.Sum(bin => (long)_histogram[bin]) / 2, seen = 0;
            int cut = 1;
            for (int i = 0; i < box.Count - 1; i++)
            {
                seen += _histogram[box[i]];
                cut = i + 1;
                if (seen >= half) break;
            }
            boxes[widest] = box.GetRange(0, cut);
            boxes.Add(box.GetRange(cut, box.Count - cut));
        }

        var palette = new List<(byte, byte, byte)>(boxes.Count);
        foreach (var box in boxes)
        {
            long red = 0, green = 0, blue = 0, count = 0;
            foreach (int bin in box)
            {
                long weight = _histogram[bin];
                // The middle of the bin, so a flat color comes back about as it was.
                red += weight * (((bin >> 10) & 31) * 8 + 4);
                green += weight * (((bin >> 5) & 31) * 8 + 4);
                blue += weight * ((bin & 31) * 8 + 4);
                count += weight;
                _indexOfBin[bin] = (byte)palette.Count;
            }
            palette.Add(count == 0 ? ((byte)0, (byte)0, (byte)0) : ((byte)(red / count), (byte)(green / count), (byte)(blue / count)));
        }
        if (palette.Count == 0) palette.Add((0, 0, 0));
        return palette;
    }

    /// <summary>GIF's LZW: codes grow from one bit over the minimum to 12 bits, then the table starts again.</summary>
    private void Compress(byte[] pixels, int minimumCode)
    {
        int clear = 1 << minimumCode, end = clear + 1;
        var table = new Dictionary<int, int>(5003);
        int next = end + 1, size = minimumCode + 1;
        int buffer = 0, bitsHeld = 0;
        _blockLength = 0;

        void Put(int code, int width)
        {
            buffer |= code << bitsHeld;
            bitsHeld += width;
            while (bitsHeld >= 8)
            {
                _block[_blockLength++] = (byte)buffer;
                buffer >>= 8;
                bitsHeld -= 8;
                if (_blockLength == 255) FlushBlock();
            }
        }

        Put(clear, size);
        int total = _width * _height;
        int current = _indexOfBin[BinOf(pixels, 0)];
        for (int i = 1, at = 4; i < total; i++, at += 4)
        {
            int symbol = _indexOfBin[BinOf(pixels, at)];
            int key = (current << 8) | symbol;
            if (table.TryGetValue(key, out int code))
            {
                current = code;
                continue;
            }
            Put(current, size);
            if (next <= MaxCode)
            {
                table[key] = next;
                if (next == 1 << size && size < 12) size++;
                next++;
            }
            else
            {
                Put(clear, size);
                table.Clear();
                next = end + 1;
                size = minimumCode + 1;
            }
            current = symbol;
        }
        Put(current, size);
        // The decoder has grown its table by one more entry by now.
        if (next == 1 << size && size < 12) size++;
        Put(end, size);
        if (bitsHeld > 0) _block[_blockLength++] = (byte)buffer;
        FlushBlock();
    }

    private void FlushBlock()
    {
        if (_blockLength == 0) return;
        _output.WriteByte((byte)_blockLength);
        _output.Write(_block, 0, _blockLength);
        _blockLength = 0;
    }

    private void WriteShort(int value)
    {
        _output.WriteByte((byte)value);
        _output.WriteByte((byte)(value >> 8));
    }
}
