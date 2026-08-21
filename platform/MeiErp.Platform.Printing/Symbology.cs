using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace MeiErp.Platform.Printing;

/// <summary>
/// Draws Code 128 barcodes and QR codes as PNG bytes for embedding in a
/// document.
///
/// Both carry the bare document number, so a bench scanner reading the bars and
/// a phone reading the QR land on exactly the same record.
///
/// The bitmap is drawn from ZXing's own matrix rather than through its SkiaSharp
/// binding package: the drawing is a dozen lines, and it keeps the dependency
/// list to the encoder we already use for the kiosk.
/// </summary>
public static class Symbology
{
    /// <summary>
    /// A Code 128 barcode, stretched to the width asked for.
    ///
    /// The caller says how wide, not how many pixels per module. A fixed module
    /// width overflows a narrow header, and QuestPDF only reports that as a
    /// layout exception at render time - long after the mistake was made.
    /// </summary>
    public static byte[] Barcode(string value, int width = 300, int height = 70)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var matrix = new MultiFormatWriter().encode(
            value, BarcodeFormat.CODE_128, width, height,
            new Dictionary<EncodeHintType, object>
            {
                [EncodeHintType.MARGIN] = 2,

                // The number is printed separately by the layout, in a font that
                // matches the rest of the document.
                [EncodeHintType.PURE_BARCODE] = true
            });

        return Render(matrix);
    }

    /// <summary>
    /// A QR code carrying the same payload as the barcode beside it.
    ///
    /// Error correction M: enough to survive a thumbprint on a delivery note
    /// without pushing the symbol larger than a 62mm label can hold.
    /// </summary>
    public static byte[] QrCode(string value, int size = 160)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var matrix = new MultiFormatWriter().encode(
            value, BarcodeFormat.QR_CODE, size, size,
            new Dictionary<EncodeHintType, object>
            {
                [EncodeHintType.MARGIN] = 1,
                [EncodeHintType.ERROR_CORRECTION] = ZXing.QrCode.Internal.ErrorCorrectionLevel.M,
                [EncodeHintType.CHARACTER_SET] = "UTF-8"
            });

        return Render(matrix);
    }

    private static byte[] Render(BitMatrix matrix)
    {
        using var bitmap = new SKBitmap(matrix.Width, matrix.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.White);

        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = false };

        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                if (matrix[x, y]) canvas.DrawPoint(x, y, ink);
            }
        }

        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
