namespace Ocr2Tran.Windows;

public sealed class RegionSelectionForm : Form
{
    private readonly Rectangle _virtualScreen;
    private Point _start;
    private Point _current;
    private bool _dragging;

    private RegionSelectionForm()
    {
        _virtualScreen = SystemInformation.VirtualScreen;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = _virtualScreen;
        TopMost = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        BackColor = Color.Black;
        Opacity = 0.28;
        SelectedBounds = null;
    }

    public Rectangle? SelectedBounds { get; private set; }

    public static Rectangle? SelectRegion(IWin32Window? owner = null)
    {
        using var form = new RegionSelectionForm();
        return form.ShowDialog(owner) == DialogResult.OK ? form.SelectedBounds : null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _start = e.Location;
        _current = e.Location;
        _dragging = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        _current = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging || e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = false;
        _current = e.Location;
        var selected = NormalizeSelection(_start, _current);
        if (selected.Width < 8 || selected.Height < 8)
        {
            SelectedBounds = null;
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        selected.Offset(_virtualScreen.Left, _virtualScreen.Top);
        SelectedBounds = selected;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_dragging)
        {
            return;
        }

        var selection = NormalizeSelection(_start, _current);
        using var border = new Pen(Color.DeepSkyBlue, 2);
        using var fill = new SolidBrush(Color.FromArgb(70, Color.DeepSkyBlue));
        e.Graphics.FillRectangle(fill, selection);
        e.Graphics.DrawRectangle(border, selection);
    }

    private static Rectangle NormalizeSelection(Point start, Point end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
