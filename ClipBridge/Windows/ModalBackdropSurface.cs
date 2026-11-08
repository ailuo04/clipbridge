using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// The owner workspace is captured once and blurred in a small bitmap. The
// overlay then paints a still, soft backdrop; it never blurs on every frame.
internal sealed class ModalBackdropSurface : Control
{
    private Bitmap? _blurred;
    private readonly System.Collections.Generic.List<Bitmap> _retired = new();
    private int _builds;

    internal int Builds => _builds;
    internal Bitmap? Image => _blurred;

    internal ModalBackdropSurface()
    {
        Name = "ModalBackdrop";
        Dock = DockStyle.Fill;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        BackColor = Color.Black;
        TabStop = false;
        AccessibleRole = AccessibleRole.Pane;
        AccessibleName = "模糊窗口背景";
    }

    internal void CaptureOwner(Control? owner)
    {
        Bitmap? next = Build(owner, Width, Height);
        if (next == null) { Invalidate(); return; }
        Bitmap? old = _blurred;
        _blurred = next;
        _builds++;
        // A repaint may already be reading the previous frame. Retire it
        // instead of disposing it underneath the paint callback.
        if (old != null && !ReferenceEquals(old, next))
        {
            _retired.Add(old);
            while (_retired.Count > 4) { _retired[0].Dispose(); _retired.RemoveAt(0); }
        }
        Invalidate();
    }

    internal void Release()
    {
        foreach (Bitmap bitmap in _retired) bitmap.Dispose();
        _retired.Clear();
        _blurred?.Dispose();
        _blurred = null;
    }

    private Bitmap? Build(Control? owner, int width, int height)
    {
        if (width < 4 || height < 4) return null;
        Bitmap? source = null;
        try
        {
            if (owner != null && owner.Width > 4 && owner.Height > 4)
            {
                source = new Bitmap(owner.Width, owner.Height, PixelFormat.Format32bppPArgb);
                owner.DrawToBitmap(source, new Rectangle(Point.Empty, source.Size));
            }
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is ExternalException || ex is OutOfMemoryException)
        {
            source?.Dispose();
            source = null;
        }

        int smallWidth = Math.Max(12, width / 10);
        int smallHeight = Math.Max(12, height / 10);
        int midWidth = Math.Max(24, width / 3);
        int midHeight = Math.Max(24, height / 3);
        Color matte = Ui.HeaderIsDark ? Color.FromArgb(18, 23, 35) : Color.FromArgb(237, 242, 250);
        Bitmap small = new(smallWidth, smallHeight, PixelFormat.Format32bppPArgb);
        Bitmap output = new(width, height, PixelFormat.Format32bppPArgb);
        try
        {
            using (Graphics g = Graphics.FromImage(small))
            {
                g.Clear(matte);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                if (source != null) g.DrawImage(source, new Rectangle(0, 0, smallWidth, smallHeight));
                else
                {
                    using var gradient = new LinearGradientBrush(new Rectangle(0, 0, smallWidth, smallHeight), Color.FromArgb(233, 241, 253), Color.FromArgb(222, 230, 250), 120f);
                    g.FillRectangle(gradient, 0, 0, smallWidth, smallHeight);
                    using var glow = new SolidBrush(Color.FromArgb(70, 210, 223, 255));
                    g.FillEllipse(glow, -smallWidth / 4, -smallHeight / 4, smallWidth, smallHeight);
                }
                using var frost = new SolidBrush(Ui.HeaderIsDark ? Color.FromArgb(20, 8, 12, 22) : Color.FromArgb(10, 247, 250, 255));
                g.FillRectangle(frost, 0, 0, smallWidth, smallHeight);
            }
            Bitmap mid = new(midWidth, midHeight, PixelFormat.Format32bppPArgb);
            try
            {
                using (Graphics gm = Graphics.FromImage(mid))
                {
                    gm.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    gm.DrawImage(small, new Rectangle(0, 0, midWidth, midHeight));
                }
                using (Graphics g = Graphics.FromImage(output))
                {
                    g.Clear(matte);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.DrawImage(mid, new Rectangle(0, 0, width, height));
                    using var veil = new SolidBrush(Ui.HeaderIsDark ? Color.FromArgb(40, 6, 10, 20) : Color.FromArgb(16, 250, 252, 255));
                    g.FillRectangle(veil, 0, 0, width, height);
                }
            }
            finally { mid.Dispose(); }
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
        finally
        {
            small.Dispose();
            source?.Dispose();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        Bitmap? frame = _blurred;
        try
        {
            if (frame != null && frame.Size == Size) e.Graphics.DrawImageUnscaled(frame, 0, 0);
            else if (frame != null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(frame, ClientRectangle);
            }
            else PaintFallback(e.Graphics);
        }
        catch (ArgumentException)
        {
            PaintFallback(e.Graphics);
        }
    }

    private void PaintFallback(Graphics graphics)
    {
        {
            using var gradient = new LinearGradientBrush(ClientRectangle, Ui.HeaderIsDark ? Color.FromArgb(15, 20, 31) : Color.FromArgb(238, 243, 252), Ui.HeaderIsDark ? Color.FromArgb(9, 13, 23) : Color.FromArgb(227, 235, 250), 120f);
            graphics.FillRectangle(gradient, ClientRectangle);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (Bitmap bitmap in _retired) bitmap.Dispose();
            _retired.Clear();
            _blurred?.Dispose();
        }
        base.Dispose(disposing);
    }
}
