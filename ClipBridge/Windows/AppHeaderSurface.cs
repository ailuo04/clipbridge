using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class AppHeaderSurface : Panel
{
	public double MotionProgress => 0.0;

	public AppHeaderSurface()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Ui.Header;
	}

	public void Flash()
	{
		Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.Clear(Ui.Header);
		using Pen pen = new Pen(Ui.Line);
		e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
		// Short accent fade on the left edge of the hairline, under the brand mark.
		if (Width > 40)
		{
			Rectangle strip = new Rectangle(0, Height - 2, Math.Min(Width, 360), 2);
			using System.Drawing.Drawing2D.LinearGradientBrush fade = new System.Drawing.Drawing2D.LinearGradientBrush(strip, Color.FromArgb(150, Ui.Accent), Color.FromArgb(0, Ui.Accent), 0f);
			e.Graphics.FillRectangle(fade, strip);
		}
	}
}
