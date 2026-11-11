using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Rounded accent tile holding a Segoe Fluent glyph; leads the device panel header.
internal sealed class IconTile : Control
{
	private readonly Font _glyphFont = new Font("Segoe Fluent Icons", 13f, FontStyle.Regular, GraphicsUnit.Point);

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public string Glyph { get; set; } = "";

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public Color SurfaceColor
	{
		get => BackColor;
		set { BackColor = value; Invalidate(); }
	}

	public IconTile()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Ui.Surface;
		TabStop = false;
		Size = new Size(40, 40);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.Clear(BackColor);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
		using (SolidBrush fill = new SolidBrush(Ui.AccentSoft))
		{
			VisualDrawing.FillRoundedRectangle(e.Graphics, fill, bounds, 12f);
		}
		TextRenderer.DrawText(e.Graphics, Glyph, _glyphFont, ClientRectangle, Ui.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_glyphFont.Dispose();
		}
		base.Dispose(disposing);
	}
}

// Small "N 台" badge: accent-tinted when devices exist, neutral when the list is empty.
internal sealed class CountPill : Control
{
	private readonly Font _font = Ui.Font(8.8f, FontStyle.Bold);

	private int _count;

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public int Count
	{
		get => _count;
		set
		{
			_count = value;
			AccessibleName = "设备数量 " + value + " 台";
			Size = GetPreferredSize(Size.Empty);
			Invalidate();
		}
	}

	public CountPill()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Color.Transparent;
		TabStop = false;
		AccessibleName = "设备数量 0 台";
		Size = GetPreferredSize(Size.Empty);
	}

	public override Size GetPreferredSize(Size proposedSize)
	{
		Size text = TextRenderer.MeasureText(_count + " 台", _font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
		return new Size(Math.Max(46, text.Width + 22), 24);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		bool active = _count > 0;
		RectangleF bounds = new RectangleF(0f, 0f, Width - 1f, Height - 1f);
		using (SolidBrush fill = new SolidBrush(active ? Ui.AccentSoft : Ui.SurfaceHover))
		{
			VisualDrawing.FillRoundedRectangle(e.Graphics, fill, bounds, Height / 2f);
		}
		TextRenderer.DrawText(e.Graphics, _count + " 台", _font, ClientRectangle, active ? Ui.Accent : Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_font.Dispose();
		}
		base.Dispose(disposing);
	}
}
