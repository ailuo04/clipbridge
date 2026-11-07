using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class UpdateEmblem : Control
{
    public UpdateEmblem()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent; AccessibleName = "软件更新";
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        float size = System.Math.Min(Width, Height), s = size / 100f;
        GraphicsState outer = g.Save(); g.TranslateTransform((Width - size) / 2, (Height - size) / 2); g.ScaleTransform(s, s);
        using (var ellipse = new GraphicsPath())
        {
            ellipse.AddEllipse(0, 0, 100, 100);
            using var glow = new PathGradientBrush(ellipse) { CenterColor = Color.FromArgb(Ui.HeaderIsDark ? 70 : 60, 118, 129, 255), SurroundColors = new[] { Color.Transparent } };
            g.FillPath(glow, ellipse);
        }
        GraphicsState backing = g.Save(); g.TranslateTransform(50, 47); g.RotateTransform(-14);
        RectangleF tile = new(-33, -33, 66, 66);
        using (var brush = new LinearGradientBrush(tile, Color.FromArgb(Ui.HeaderIsDark ? 55 : 90, 150, 151, 248), Color.FromArgb(30, 93, 151, 230), 100)) VisualDrawing.FillRoundedRectangle(g, brush, tile, 20);
        using (var edge = new Pen(Color.FromArgb(100, Color.White))) VisualDrawing.DrawRoundedRectangle(g, edge, tile, 20);
        g.Restore(backing);
        RectangleF front = new(18, 14, 68, 68);
        using (var shadow = new SolidBrush(Color.FromArgb(22, 68, 77, 158))) VisualDrawing.FillRoundedRectangle(g, shadow, new RectangleF(19, 20, 68, 68), 21);
        Color top = Ui.HeaderIsDark ? Color.FromArgb(88, 107, 171) : Color.FromArgb(251, 253, 255);
        Color bottom = Ui.HeaderIsDark ? Color.FromArgb(43, 58, 107) : Color.FromArgb(220, 229, 255);
        using (var glass = new LinearGradientBrush(front, top, bottom, 100)) VisualDrawing.FillRoundedRectangle(g, glass, front, 21);
        using (var edge = new Pen(Color.FromArgb(Ui.HeaderIsDark ? 150 : 240, Color.White), 1.3f)) VisualDrawing.DrawRoundedRectangle(g, edge, front, 21);
        using (var clip = VisualDrawing.RoundedRectangle(front, 21))
        {
            GraphicsState state = g.Save(); g.SetClip(clip);
            using var shine = new LinearGradientBrush(front, Color.FromArgb(100, Color.White), Color.FromArgb(0, Color.White), 90f);
            g.FillEllipse(shine, 3, -22, 112, 75); g.Restore(state);
        }
        using (var pen = new Pen(Ui.HeaderIsDark ? Color.FromArgb(233, 239, 255) : Color.FromArgb(87, 108, 217), 3.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            g.DrawLine(pen, 52, 30, 52, 54); g.DrawLines(pen, new[] { new PointF(43, 46), new PointF(52, 55), new PointF(61, 46) });
            using var tray = new GraphicsPath(); tray.AddLine(36, 58, 36, 64); tray.AddBezier(36, 64, 36, 67, 39, 68, 42, 68); tray.AddLine(42, 68, 62, 68); tray.AddBezier(62, 68, 66, 68, 68, 66, 68, 63); tray.AddLine(68, 63, 68, 58); g.DrawPath(pen, tray);
        }
        using (var brush = new SolidBrush(Ui.HeaderIsDark ? Color.FromArgb(70, 92, 166) : Color.FromArgb(244, 249, 255))) g.FillEllipse(brush, 69, 66, 23, 23);
        using (var pen = new Pen(Color.FromArgb(180, Color.White), 1.1f)) g.DrawEllipse(pen, 69, 66, 23, 23);
        using (var pen = new Pen(Ui.Success, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLines(pen, new[] { new PointF(76, 77), new PointF(79, 80), new PointF(85, 74) });
        g.Restore(outer);
    }
}
