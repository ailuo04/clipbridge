using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Layered, locally painted glass: no desktop capture and no opacity applied to text.
internal sealed class UpdateGlassSurface : Panel
{
    public UpdateGlassSurface()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 8 || Height < 8) return;
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        RectangleF rect = new(scale, scale, Width - 2 * scale, Height - 3 * scale);
        Color top = Ui.HeaderIsDark ? Color.FromArgb(115, 48, 61, 91) : Color.FromArgb(190, Color.White);
        Color bottom = Ui.HeaderIsDark ? Color.FromArgb(120, 34, 44, 68) : Color.FromArgb(115, Color.White);
        using (var glass = new LinearGradientBrush(rect, top, bottom, 100f)) VisualDrawing.FillRoundedRectangle(g, glass, rect, 20 * scale);
        using (var edge = new Pen(Ui.HeaderIsDark ? Color.FromArgb(70, 156, 176, 215) : Color.FromArgb(235, Color.White), scale)) VisualDrawing.DrawRoundedRectangle(g, edge, rect, 20 * scale);
        RectangleF inner = RectangleF.Inflate(rect, -2 * scale, -2 * scale);
        using (var edge = new Pen(Ui.HeaderIsDark ? Color.FromArgb(12, Color.White) : Color.FromArgb(22, 104, 127, 182), scale)) VisualDrawing.DrawRoundedRectangle(g, edge, inner, 18 * scale);
        base.OnPaint(e);
    }

}

internal sealed class UpdateProgressBar : Control
{
    private readonly Timer _timer = new() { Interval = 20 };
    private double _target, _display, _shine;
    private bool _active;
    public UpdateProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        AccessibleRole = AccessibleRole.ProgressBar; AccessibleName = "更新总进度";
        _timer.Tick += (_, _) =>
        {
            _display = VisualDrawing.MoveTowards(_display, _target, Math.Max(0.5, Math.Abs(_target - _display) * 0.18));
            _shine = (_shine + 0.014) % 1;
            Invalidate();
            if ((!_active && _display == _target) || !Visible) _timer.Stop();
        };
    }
    public void SetProgress(double percent, bool active)
    {
        _target = Math.Clamp(percent, 0, 100); _active = active;
        AccessibleDescription = $"更新总进度 {Math.Floor(_target):0}%";
        if (VisualMotion.AnimationsEnabled && Visible && active) _timer.Start();
        else { _timer.Stop(); _display = _target; }
        if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        Invalidate();
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new ProgressAccessibility(this);
    private sealed class ProgressAccessibility(UpdateProgressBar owner) : ControlAccessibleObject(owner)
    {
        public override string? Value { get => $"{Math.Floor(owner._target):0}%"; set { } }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 4 || Height < 4) return;
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF track = new(1, Height * 0.25f, Width - 2, Height * 0.5f);
        using (var background = new SolidBrush(VisualMotion.Mix(Ui.FieldSurface, Ui.Line, 0.22)))
            VisualDrawing.FillRoundedRectangle(g, background, track, track.Height / 2);
        if (_display <= 0) return;
        RectangleF fill = new(track.X, track.Y, Math.Max(2, (float)(track.Width * _display / 100)), track.Height);
        using (var brush = new LinearGradientBrush(fill, Color.FromArgb(132, 144, 250), Ui.Accent, 0f))
            VisualDrawing.FillRoundedRectangle(g, brush, fill, track.Height / 2);
        using (var clip = VisualDrawing.RoundedRectangle(fill, track.Height / 2))
        {
            GraphicsState state = g.Save(); g.SetClip(clip);
            RectangleF light = new(fill.X, fill.Y, fill.Width, fill.Height / 2);
            using (var gloss = new SolidBrush(Color.FromArgb(65, Color.White))) g.FillRectangle(gloss, light);
            if (_active && VisualMotion.AnimationsEnabled)
            {
                using var glint = new LinearGradientBrush(new RectangleF((float)(_shine * (Width + 120)) - 120, track.Y, 90, track.Height), Color.Transparent, Color.FromArgb(120, Color.White), 0f);
                g.FillRectangle(glint, (float)(_shine * (Width + 120)) - 120, track.Y, 90, track.Height);
            }
            g.Restore(state);
        }
    }
    protected override void OnVisibleChanged(EventArgs e) { if (!Visible) _timer.Stop(); else if (_active) SetProgress(_target, true); base.OnVisibleChanged(e); }
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}

internal sealed class UpdateStepStrip : Control
{
    private int _step = -1;
    public UpdateStepStrip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent; AccessibleName = "下载、校验、安装、打开";
    }
    public void SetStage(UpdateStage stage)
    {
        _step = stage switch { UpdateStage.Downloading => 0, UpdateStage.Verifying or UpdateStage.Extracting => 1, UpdateStage.Waiting or UpdateStage.Installing => 2, UpdateStage.Restarting => 3, _ => 4 };
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        string[] labels = { "下载", "校验", "安装", "打开" };
        float s = DeviceDpi / 96f, slot = Width / 4f;
        using var font = Ui.Font(8.7f);
        for (int i = 0; i < 4; i++)
        {
            RectangleF pill = new(i * slot + 3 * s, 3 * s, slot - 6 * s, Height - 6 * s);
            bool active = i == _step, done = i < _step;
            Color color = done ? Ui.SuccessText : active ? Ui.Accent : Ui.Faint;
            if (active)
            {
                using var brush = new SolidBrush(Color.FromArgb(Ui.HeaderIsDark ? 80 : 22, Ui.Accent));
                VisualDrawing.FillRoundedRectangle(g, brush, pill, pill.Height / 2);
                using var edge = new Pen(Color.FromArgb(Ui.HeaderIsDark ? 55 : 30, Ui.Accent), s);
                VisualDrawing.DrawRoundedRectangle(g, edge, pill, pill.Height / 2);
            }
            int textWidth = TextRenderer.MeasureText(g, labels[i], font, Size.Empty, TextFormatFlags.NoPadding).Width;
            float x = pill.X + (pill.Width - textWidth - 15 * s) / 2, cy = pill.Y + pill.Height / 2;
            if (done)
            {
                using var pen = new Pen(color, 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLines(pen, new[] { new PointF(x, cy), new PointF(x + 3 * s, cy + 3 * s), new PointF(x + 8 * s, cy - 3 * s) });
            }
            else
            {
                using var pen = new Pen(color, 1.2f * s); g.DrawEllipse(pen, x, cy - 3.5f * s, 7 * s, 7 * s);
                if (active) { using var dot = new SolidBrush(color); g.FillEllipse(dot, x + 2 * s, cy - 1.5f * s, 3 * s, 3 * s); }
            }
            TextRenderer.DrawText(g, labels[i], font, new Rectangle((int)(x + 14 * s), (int)pill.Y, textWidth + (int)(3 * s), (int)pill.Height), color, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }
}
