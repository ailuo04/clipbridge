using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class UpdateGlassButton : Button
{
    private readonly bool _primary, _close;
    private readonly Timer _timer = new() { Interval = 16 };
    private bool _over, _down;
    private double _hover;

    public UpdateGlassButton(string text, bool primary = false, bool close = false)
    {
        _primary = primary; _close = close;
        Text = text; Font = Ui.Font(10, primary ? FontStyle.Bold : FontStyle.Regular);
        AutoSize = false; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent; UseVisualStyleBackColor = false; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        _timer.Tick += (_, _) => { _hover = VisualDrawing.MoveTowards(_hover, _over ? 1 : 0, 0.15); Invalidate(); if (_hover == (_over ? 1 : 0)) _timer.Stop(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = DeviceDpi / 96f, inset = _down && Enabled ? 1.2f * s : 0;
        RectangleF rect = new(1 + inset, 1 + inset, Width - 2 - inset * 2, Height - 4 - inset * 2);
        if (rect.Width <= 1 || rect.Height <= 1) return;
        float radius = _close ? rect.Width / 2 : 14 * s;
        if (_primary)
        {
            Color top = Ui.HeaderIsDark ? Color.FromArgb(116, 136, 255) : Color.FromArgb(92, 111, 246);
            Color bottom = Ui.HeaderIsDark ? Color.FromArgb(80, 101, 230) : Color.FromArgb(64, 88, 226);
            if (!Enabled) { top = VisualMotion.Mix(top, Ui.Surface, 0.55); bottom = VisualMotion.Mix(bottom, Ui.Surface, 0.55); }
            top = VisualMotion.Mix(top, Color.White, _hover * 0.10);
            if (Enabled)
            {
                using var shadow = new SolidBrush(Color.FromArgb(38, 77, 90, 220));
                VisualDrawing.FillRoundedRectangle(g, shadow, new RectangleF(rect.X + 2 * s, rect.Y + 3 * s, rect.Width - 4 * s, rect.Height), radius);
            }
            using var brush = new LinearGradientBrush(rect, top, bottom, 80f);
            VisualDrawing.FillRoundedRectangle(g, brush, rect, radius);
            using var border = new Pen(Color.FromArgb(115, Color.White), s);
            VisualDrawing.DrawRoundedRectangle(g, border, rect, radius);
        }
        else
        {
            Color fill = Ui.HeaderIsDark ? Color.FromArgb(110 + (int)(_hover * 50), 57, 68, 92) : Color.FromArgb(160 + (int)(_hover * 60), Color.White);
            if (_close && _over) fill = Color.FromArgb(140, Ui.DangerSoft);
            using var brush = new SolidBrush(fill);
            VisualDrawing.FillRoundedRectangle(g, brush, rect, radius);
            using var edge = new Pen(Ui.HeaderIsDark ? Color.FromArgb(65, 168, 183, 220) : Color.FromArgb(180, Color.White), s);
            VisualDrawing.DrawRoundedRectangle(g, edge, rect, radius);
        }
        Color ink = _primary ? Color.White : Enabled ? Ui.Ink : Ui.Faint;
        if (_close)
        {
            using var pen = new Pen(_over ? Ui.Danger : Ui.Muted, 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float cx = rect.X + rect.Width / 2, cy = rect.Y + rect.Height / 2, d = 3.7f * s;
            g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d); g.DrawLine(pen, cx + d, cy - d, cx - d, cy + d);
        }
        else TextRenderer.DrawText(g, Text, Font, Rectangle.Round(rect), ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(_primary ? Color.White : Ui.Accent, s) { DashStyle = DashStyle.Dot };
            VisualDrawing.DrawRoundedRectangle(g, focus, RectangleF.Inflate(rect, -4 * s, -4 * s), Math.Max(2, radius - 3 * s));
        }
    }
    private void Animate() { if (VisualMotion.AnimationsEnabled) _timer.Start(); else { _hover = _over ? 1 : 0; Invalidate(); } }
    protected override void OnMouseEnter(EventArgs e) { _over = true; Animate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _over = false; _down = false; Animate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { _over = _down = false; Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
