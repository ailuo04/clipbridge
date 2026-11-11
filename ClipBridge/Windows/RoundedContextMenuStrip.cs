using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class RoundedContextMenuStrip : ContextMenuStrip
{
    private Region? _roundedRegion;

    public RoundedContextMenuStrip()
    {
        Renderer = new RoundedMenuRenderer();
        ShowImageMargin = false;
        BackColor = Ui.Surface;
        ForeColor = Ui.Ink;
        Padding = new Padding(4);
        Font = Ui.Font(9f);
    }

    protected override void OnItemAdded(ToolStripItemEventArgs e)
    {
        e.Item.Margin = new Padding(2, 1, 2, 1);
        if (e.Item is ToolStripMenuItem item) item.Padding = new Padding(12, 6, 12, 6);
        base.OnItemAdded(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateRoundedRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Visible) UpdateRoundedRegion();
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using GraphicsPath path = VisualDrawing.RoundedRectangle(new RectangleF(0, 0, Width, Height), 9f);
        var next = new Region(path);
        Region = next;
        Region? previous = _roundedRegion;
        _roundedRegion = next;
        previous?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Region = null;
            _roundedRegion?.Dispose();
            _roundedRegion = null;
        }
        base.Dispose(disposing);
    }
}

internal sealed class RoundedMenuRenderer : ToolStripProfessionalRenderer
{
    public RoundedMenuRenderer() : base(new ThemedMenuColorTable()) { }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is not ToolStripDropDown)
        {
            base.OnRenderToolStripBackground(e);
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Ui.Surface);
        RectangleF bounds = new(0.5f, 0.5f, e.ToolStrip.Width - 1f, e.ToolStrip.Height - 1f);
        using var fill = new SolidBrush(Ui.Surface);
        using var border = new Pen(Ui.Line);
        using GraphicsPath path = VisualDrawing.RoundedRectangle(bounds, 9f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected || e.Item.Pressed)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = Rectangle.Inflate(e.Item.Bounds, -2, -1);
            using var fill = new SolidBrush(e.Item.Pressed ? Ui.AccentSoftSolid : Ui.SurfaceHover);
            VisualDrawing.FillRoundedRectangle(e.Graphics, fill, bounds, 6f);
            return;
        }

        base.OnRenderMenuItemBackground(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.ContentRectangle.Top + e.Item.ContentRectangle.Height / 2;
        using var pen = new Pen(Ui.Line);
        e.Graphics.DrawLine(pen, e.Item.ContentRectangle.Left + 8, y, e.Item.ContentRectangle.Right - 8, y);
    }
}
