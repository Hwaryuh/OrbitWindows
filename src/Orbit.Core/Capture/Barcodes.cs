using ZXing;
using ZXing.Common;

namespace Orbit.Core.Capture;

/// <summary>
/// QR codes and barcodes in a capture (Orbit: OCR.swift <c>barcodes</c> over Vision), through
/// ZXing.Net (Apache 2.0). Text capture copies a code's payload instead of the words around it.
/// </summary>
public static class Barcodes
{
    private static readonly IList<BarcodeFormat> Formats =
    [
        BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC, BarcodeFormat.PDF_417,
        BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A, BarcodeFormat.UPC_E,
        BarcodeFormat.CODE_128, BarcodeFormat.CODE_39, BarcodeFormat.ITF,
    ];

    /// <summary>The payloads of every code found, in the order found; empty when there is none.</summary>
    public static IReadOnlyList<string> Read(ScreenFrame frame)
    {
        if (frame.Width == 0 || frame.Height == 0) return [];
        var pixels = frame.Pixels;
        if (frame.Stride != frame.Width * 4)
        {
            // ZXing reads rows packed one after another.
            pixels = new byte[frame.Width * 4 * frame.Height];
            for (int y = 0; y < frame.Height; y++) Buffer.BlockCopy(frame.Pixels, y * frame.Stride, pixels, y * frame.Width * 4, frame.Width * 4);
        }
        var source = new RGBLuminanceSource(pixels, frame.Width, frame.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, PossibleFormats = Formats },
        };
        var results = reader.DecodeMultiple(source);
        return results is null ? [] : results.Select(r => r.Text).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
    }
}
