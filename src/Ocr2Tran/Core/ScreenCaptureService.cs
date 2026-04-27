namespace Ocr2Tran.Core;

public sealed class ScreenCaptureService
{
    public CapturedScreen CaptureVirtualScreen()
    {
        var bounds = SystemInformation.VirtualScreen;
        return Capture(bounds);
    }

    public CapturedScreen Capture(Rectangle requestedBounds)
    {
        var bounds = requestedBounds;
        bounds.Intersect(SystemInformation.VirtualScreen);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedBounds), "Capture region is outside the virtual screen.");
        }

        var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return new CapturedScreen(bitmap, new Point(bounds.Left, bounds.Top));
    }
}
