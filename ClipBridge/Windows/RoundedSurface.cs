using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class RoundedSurface : Panel
{
	private readonly Timer _flashTimer = new Timer
	{
		Interval = 20
	};

	private double _flash;

	private double _hover;

	private bool _hovering;

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public int CornerRadius { get; set; } = 14;

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public Color FillColor { get; set; } = Ui.Surface;

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public Color BorderColor { get; set; } = Ui.Line;

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public bool Flat { get; set; }

	public double MotionProgress => Math.Max(_flash, _hover);

	public RoundedSurface()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Color.Transparent;
		_flashTimer.Tick += (object? _, EventArgs _) =>
		{
			_flash = VisualDrawing.MoveTowards(_flash, 0.0, 0.08);
			_hover = VisualDrawing.MoveTowards(_hover, (_hovering && VisualMotion.AnimationsEnabled) ? 1.0 : 0.0, 0.11);
			bool flag = _flash <= 0.0;
			if (flag)
			{
				double hover = _hover;
				bool flag2 = ((hover == 0.0 || hover == 1.0) ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				_flashTimer.Stop();
			}
			Invalidate();
		};
	}

	public void Flash()
	{
		if (!VisualMotion.AnimationsEnabled)
		{
			_flash = 0.0;
			Invalidate();
		}
		else
		{
			_flash = 1.0;
			_flashTimer.Start();
			Invalidate();
		}
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		base.OnPaintBackground(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		if (Flat)
		{
			// Edge-to-edge band: one rule along the top is the only chrome.
			e.Graphics.Clear(FillColor);
			using Pen rule = new Pen(BorderColor);
			e.Graphics.SmoothingMode = SmoothingMode.None;
			e.Graphics.DrawLine(rule, 0, 0, Width, 0);
			return;
		}
		// Cards separate from the canvas by fill and a hairline, not a shadow.
		RectangleF rectangleF = new RectangleF(2f, 1f, (float)Width - 4f, (float)Height - 7f);
		using (SolidBrush brush2 = new SolidBrush(VisualMotion.Mix(FillColor, Ui.SurfaceHover, _hover * 0.18)))
		{
			VisualDrawing.FillRoundedRectangle(e.Graphics, brush2, rectangleF, CornerRadius);
		}
		using Pen pen = new Pen(VisualMotion.Mix(BorderColor, Ui.Accent, _flash * 0.9), (_flash > 0.0) ? 1.5f : 1f);
		VisualDrawing.DrawRoundedRectangle(e.Graphics, pen, rectangleF, CornerRadius);
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		_hovering = true;
		if (VisualMotion.AnimationsEnabled)
		{
			_flashTimer.Start();
		}
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hovering = false;
		if (VisualMotion.AnimationsEnabled)
		{
			_flashTimer.Start();
		}
		base.OnMouseLeave(e);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_flashTimer.Dispose();
		}
		base.Dispose(disposing);
	}
}
