using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Ocr2Tran.App;
using Ocr2Tran.Core;

namespace Ocr2Tran.Ocr;

public sealed class OcrImagePreprocessor
{
    private readonly OcrImagePreprocessingSettings _settings;

    public OcrImagePreprocessor(OcrImagePreprocessingSettings settings)
    {
        _settings = settings;
    }

    public CapturedScreen Process(CapturedScreen screen)
    {
        if (!_settings.Enabled)
        {
            return screen;
        }

        var scale = ComputeScale(screen.Image.Size);
        var adjustPixels = _settings.Grayscale ||
                           Math.Abs(_settings.Contrast - 1) > 0.001 ||
                           Math.Abs(_settings.Brightness) > 0.001;
        if (scale <= 1.001 && !adjustPixels)
        {
            return screen;
        }

        var width = Math.Max(1, (int)Math.Round(screen.Image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(screen.Image.Height * scale));
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.DrawImage(screen.Image, new Rectangle(0, 0, width, height));
        }

        if (adjustPixels)
        {
            AdjustPixels(bitmap);
        }

        return new CapturedScreen(bitmap, screen.Origin, screen.Bounds.Size);
    }

    private double ComputeScale(Size size)
    {
        var requested = Math.Max(1, _settings.Scale);
        var maxLongSide = Math.Max(256, _settings.MaxLongSide);
        var longSide = Math.Max(size.Width, size.Height);
        if (longSide <= 0)
        {
            return 1;
        }

        return Math.Max(1, Math.Min(requested, maxLongSide / (double)longSide));
    }

    private void AdjustPixels(Bitmap bitmap)
    {
        BitmapData? data = null;
        try
        {
            data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            var rowBytes = bitmap.Width * 3;
            var row = new byte[rowBytes];
            var contrast = Math.Max(0.1, _settings.Contrast);
            var brightness = Math.Max(-0.5, Math.Min(0.5, _settings.Brightness));

            for (var y = 0; y < bitmap.Height; y++)
            {
                var offset = data.Stride >= 0
                    ? y * data.Stride
                    : (bitmap.Height - 1 - y) * -data.Stride;
                var pointer = IntPtr.Add(data.Scan0, offset);
                Marshal.Copy(pointer, row, 0, rowBytes);

                for (var x = 0; x < rowBytes; x += 3)
                {
                    var b = row[x] / 255d;
                    var g = row[x + 1] / 255d;
                    var r = row[x + 2] / 255d;
                    if (_settings.Grayscale)
                    {
                        var gray = r * 0.299 + g * 0.587 + b * 0.114;
                        r = gray;
                        g = gray;
                        b = gray;
                    }

                    row[x] = ToByte((b - 0.5) * contrast + 0.5 + brightness);
                    row[x + 1] = ToByte((g - 0.5) * contrast + 0.5 + brightness);
                    row[x + 2] = ToByte((r - 0.5) * contrast + 0.5 + brightness);
                }

                Marshal.Copy(row, 0, pointer, rowBytes);
            }
        }
        finally
        {
            if (data is not null)
            {
                bitmap.UnlockBits(data);
            }
        }
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Round(Math.Max(0, Math.Min(1, value)) * 255);
    }
}
