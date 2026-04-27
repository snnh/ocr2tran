namespace Ocr2Tran.Core;

public sealed class CapturedScreen : IDisposable
{
    public CapturedScreen(Bitmap image, Point origin)
        : this(image, origin, image.Size)
    {
    }

    public CapturedScreen(Bitmap image, Point origin, Size sourceSize)
    {
        Image = image;
        Origin = origin;
        SourceSize = sourceSize;
    }

    public Bitmap Image { get; }
    public Point Origin { get; }
    public Size SourceSize { get; }
    public float ScaleX => SourceSize.Width <= 0 ? 1 : Image.Width / (float)SourceSize.Width;
    public float ScaleY => SourceSize.Height <= 0 ? 1 : Image.Height / (float)SourceSize.Height;
    public Rectangle Bounds => new(Origin, SourceSize);

    public Rectangle ImageBoundsToScreen(Rectangle imageBounds)
    {
        var x = Origin.X + (int)Math.Round(imageBounds.X / Math.Max(ScaleX, 0.0001f));
        var y = Origin.Y + (int)Math.Round(imageBounds.Y / Math.Max(ScaleY, 0.0001f));
        var width = Math.Max(1, (int)Math.Round(imageBounds.Width / Math.Max(ScaleX, 0.0001f)));
        var height = Math.Max(1, (int)Math.Round(imageBounds.Height / Math.Max(ScaleY, 0.0001f)));
        var screenBounds = new Rectangle(x, y, width, height);
        screenBounds.Intersect(Bounds);
        return screenBounds.Width > 0 && screenBounds.Height > 0 ? screenBounds : new Rectangle(x, y, 1, 1);
    }

    public void Dispose()
    {
        Image.Dispose();
    }
}
