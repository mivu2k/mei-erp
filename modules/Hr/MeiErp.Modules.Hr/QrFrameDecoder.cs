using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace MeiErp.Modules.Hr;

public interface IQrFrameDecoder { string? Decode(byte[] image); }

public sealed class QrFrameDecoder : IQrFrameDecoder
{
    public string? Decode(byte[] image)
    {
        SKBitmap? bitmap;
        try { bitmap = SKBitmap.Decode(image); }
        catch { return null; }
        if (bitmap is null) return null;
        using (bitmap)
        {
            var luminance = new byte[bitmap.Width * bitmap.Height];
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                luminance[y * bitmap.Width + x] =
                    (byte)((pixel.Red * 299 + pixel.Green * 587 + pixel.Blue * 114) / 1000);
            }
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions { PossibleFormats = [BarcodeFormat.QR_CODE], TryHarder = true }
            };
            return reader.Decode(new RGBLuminanceSource(
                luminance, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.Gray8))?.Text;
        }
    }
}
