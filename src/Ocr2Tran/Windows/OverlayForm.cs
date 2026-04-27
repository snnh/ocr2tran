using System.Runtime.InteropServices;
using Ocr2Tran.App;
using Ocr2Tran.Core;

namespace Ocr2Tran.Windows;

public sealed class OverlayForm : Form
{
    private const int WsExToolWindow = 0x80;
    private const int WsExTransparent = 0x20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExLayered = 0x00080000;
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private const byte AcSrcOver = 0;
    private const byte AcSrcAlpha = 1;
    private const int UlwAlpha = 0x00000002;
    private OverlaySettings _settings;
    private IReadOnlyList<TextRegion> _regions = Array.Empty<TextRegion>();

    public OverlayForm(OverlaySettings settings)
    {
        _settings = settings;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExTransparent | WsExNoActivate | WsExLayered;
            return cp;
        }
    }

    public void SetRegions(IReadOnlyList<TextRegion> regions)
    {
        Bounds = SystemInformation.VirtualScreen;
        _regions = regions;
        if (!Visible)
        {
            Show();
        }

        RenderLayeredOverlay();
    }

    public void ClearRegions()
    {
        _regions = Array.Empty<TextRegion>();
        RenderLayeredOverlay();
    }

    public void ApplySettings(OverlaySettings settings)
    {
        _settings = settings;
        RenderLayeredOverlay();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        SetWindowPos(Handle, new IntPtr(-1), Left, Top, Width, Height, 0x0010 | 0x0040);
        RenderLayeredOverlay();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest)
        {
            m.Result = new IntPtr(HtTransparent);
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
    }

    private void RenderLayeredOverlay()
    {
        if (!IsHandleCreated || !Visible || Width <= 0 || Height <= 0)
        {
            return;
        }

        using var bitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            DrawRegions(graphics);
        }

        UpdateLayeredBitmap(bitmap);
    }

    private void DrawRegions(Graphics graphics)
    {
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var font = new Font(_settings.FontName, _settings.FontSize, FontStyle.Bold, GraphicsUnit.Point);
        using var foreground = new SolidBrush(ParseColor(_settings.Foreground, Color.White));
        using var background = new SolidBrush(Color.FromArgb((int)(Clamp(_settings.Opacity) * 255), ParseColor(_settings.Background, Color.Black)));
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.NoClip };

        foreach (var region in _regions)
        {
            var text = region.Translation;
            if (string.IsNullOrWhiteSpace(text) && _settings.ShowOriginalWhenNoTranslation)
            {
                text = region.Text;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var bounds = region.Bounds;
            bounds.Offset(-Left, -Top);
            var padded = Rectangle.Inflate(bounds, 6, 4);
            var minHeight = (int)Math.Ceiling(font.GetHeight(graphics) + 8);
            if (padded.Height < minHeight)
            {
                padded.Height = minHeight;
            }

            graphics.FillRectangle(background, padded);
            graphics.DrawString(text, font, foreground, padded, format);
        }
    }

    private void UpdateLayeredBitmap(Bitmap bitmap)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
        var oldBitmap = SelectObject(memoryDc, bitmapHandle);

        try
        {
            var top = new Point(Left, Top);
            var size = new Size(bitmap.Width, bitmap.Height);
            var source = new Point(0, 0);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };

            UpdateLayeredWindow(Handle, screenDc, ref top, ref size, memoryDc, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            SelectObject(memoryDc, oldBitmap);
            DeleteObject(bitmapHandle);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static double Clamp(double value)
    {
        return Math.Max(0, Math.Min(1, value));
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return ColorTranslator.FromHtml(value);
        }
        catch
        {
            return fallback;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref Point pptDst,
        ref Size psize,
        IntPtr hdcSrc,
        ref Point pptSrc,
        int crKey,
        ref BlendFunction pblend,
        int dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }
}
