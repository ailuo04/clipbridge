using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Custom text keeps the glass background visible; scrolling appears only for overflow.
internal sealed class UpdateReleaseNotes : Control
{
    private int _offset, _contentHeight;
    private bool _dragging;
    private int _dragY, _dragOffset;
    public UpdateReleaseNotes()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent; Font = Ui.Font(9.5f); TabStop = true;
        AccessibleName = "更新说明"; AccessibleRole = AccessibleRole.Text;
    }
    private float UiScale => DeviceDpi / 96f;
    private int ContentHeight(Graphics g)
    {
        int width = Math.Max(30, Width - (int)(32 * UiScale)); int total = 0;
        foreach (string line in Lines()) total += TextRenderer.MeasureText(g, line, Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + (int)(10 * UiScale);
        return Math.Max(0, total - (int)(10 * UiScale));
    }
    private string[] Lines() => Text.Replace("\r", "").Split('\n').Select(line => line.Trim().TrimStart('•', '-', '*', ' ')).Where(line => line.Length != 0).ToArray();
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        _contentHeight = ContentHeight(g); _offset = Math.Clamp(_offset, 0, Math.Max(0, _contentHeight - Height));
        int y = -_offset; int left = (int)(18 * UiScale), width = Math.Max(20, Width - (int)(32 * UiScale));
        foreach (string line in Lines())
        {
            int height = TextRenderer.MeasureText(g, line, Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            using (var dot = new SolidBrush(VisualMotion.Mix(Ui.Accent, Ui.Muted, 0.35))) g.FillEllipse(dot, 1, y + Font.Height / 2 - 2 * UiScale, 4 * UiScale, 4 * UiScale);
            TextRenderer.DrawText(g, line, Font, new Rectangle(left, y, width, height), Ui.Muted, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            y += height + (int)(10 * UiScale);
        }
        if (_contentHeight > Height)
        {
            float thumb = Math.Max(18 * UiScale, Height * (float)Height / _contentHeight);
            float top = (Height - thumb) * _offset / Math.Max(1, _contentHeight - Height);
            using var brush = new SolidBrush(Color.FromArgb(90, Ui.Muted));
            VisualDrawing.FillRoundedRectangle(g, brush, new RectangleF(Width - 4 * UiScale, top, 3 * UiScale, thumb), 2 * UiScale);
        }
        if (Focused && ShowFocusCues) { using var pen = new Pen(Color.FromArgb(100, Ui.Accent)) { DashStyle = DashStyle.Dot }; g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); }
    }
    private void Scroll(int delta) { _offset = Math.Clamp(_offset + delta, 0, Math.Max(0, _contentHeight - Height)); Invalidate(); }
    protected override void OnMouseWheel(MouseEventArgs e) { Scroll(-Math.Sign(e.Delta) * (int)(38 * UiScale)); base.OnMouseWheel(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down: Scroll((int)(24 * UiScale)); break;
            case Keys.Up: Scroll(-(int)(24 * UiScale)); break;
            case Keys.PageDown: Scroll(Height); break;
            case Keys.PageUp: Scroll(-Height); break;
            case Keys.Home: Scroll(-_contentHeight); break;
            case Keys.End: Scroll(_contentHeight); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left && e.X >= Width - 12 * UiScale && _contentHeight > Height) { _dragging = true; _dragY = e.Y; _dragOffset = _offset; Capture = true; }
        base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) { float thumb = Math.Max(18 * UiScale, Height * (float)Height / _contentHeight); _offset = Math.Clamp(_dragOffset + (int)((e.Y - _dragY) * (_contentHeight - Height) / Math.Max(1, Height - thumb)), 0, Math.Max(0, _contentHeight - Height)); Invalidate(); }
        base.OnMouseMove(e);
    }
    protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; Capture = false; base.OnMouseUp(e); }
    protected override void OnTextChanged(EventArgs e) { _offset = 0; AccessibleDescription = Text; Invalidate(); base.OnTextChanged(e); }
}
