using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Painted glass treatment, without capturing the desktop or relying on DWM blur.
internal sealed class GlassEventSurface : Panel
{
	private readonly Timer _timer = new Timer { Interval = 20 };
	private double _flash;

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public int CornerRadius { get; set; } = 12;
	public double MotionProgress => _flash;
	public static Color BodyColor => VisualMotion.Mix(Ui.Surface, Ui.Canvas, Ui.HeaderIsDark ? 0.24 : 0.32);

	public GlassEventSurface()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
		BackColor = Color.Transparent;
		_timer.Tick += (_, _) =>
		{
			_flash = VisualDrawing.MoveTowards(_flash, 0, 0.055);
			Invalidate();
			if (_flash <= 0) _timer.Stop();
		};
	}

	public void Flash()
	{
		if (!VisualMotion.AnimationsEnabled) return;
		_flash = 1;
		_timer.Start();
		Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		if (Width < 5 || Height < 5) return;
		Graphics g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		// Same bounds as RoundedSurface so the log card lines up with its neighbours.
		RectangleF rect = new RectangleF(2, 1, Width - 4, Height - 7);
		using (SolidBrush fill = new SolidBrush(BodyColor))
		{
			VisualDrawing.FillRoundedRectangle(g, fill, rect, CornerRadius);
		}
		GraphicsState state = g.Save();
		using (GraphicsPath clip = VisualDrawing.RoundedRectangle(rect, CornerRadius))
		{
			g.SetClip(clip);
			// The gloss fades out above the opaque native list, so its background
			// blends seamlessly with the rest of the frosted card.
			RectangleF gloss = new RectangleF(rect.Left, rect.Top, rect.Width, Math.Min(40, rect.Height));
			using (LinearGradientBrush sheen = new LinearGradientBrush(gloss,
				Color.FromArgb(Ui.HeaderIsDark ? 24 : 125, Color.White), Color.FromArgb(0, Color.White), 90f))
			{
				g.FillRectangle(sheen, gloss);
			}
			if (_flash > 0)
			{
				float x = (float)((1 - _flash) * Width);
				using GraphicsPath glowPath = new GraphicsPath();
				glowPath.AddEllipse(x - Width * 0.3f, -30, Width * 0.6f, 70);
				using PathGradientBrush glow = new PathGradientBrush(glowPath)
				{
					CenterColor = Color.FromArgb((int)(50 * _flash), Color.White),
					SurroundColors = new[] { Color.FromArgb(0, Color.White) }
				};
				g.FillPath(glow, glowPath);
			}
		}
		g.Restore(state);
		using (Pen border = new Pen(VisualMotion.Mix(Ui.Line, Ui.Accent, _flash * 0.22)))
		{
			VisualDrawing.DrawRoundedRectangle(g, border, rect, CornerRadius);
		}
		using Pen light = new Pen(Color.FromArgb(Ui.HeaderIsDark ? 35 : 180, Color.White));
		g.DrawLine(light, 14, 2, Width - 15, 2);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing) _timer.Dispose();
		base.Dispose(disposing);
	}
}
