using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class ModernCheckBox : CheckBox
{
	private readonly Timer _motionTimer = new Timer
	{
		Interval = 15
	};

	private double _progress;

	private double _hover;

	private bool _hovering;

	public ModernCheckBox()
	{
		AutoSize = false;
		Size = new Size(96, 30);
		Font = Ui.Font(9f);
		ForeColor = Ui.Muted;
		Cursor = Cursors.Hand;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Color.Transparent;
		_motionTimer.Tick += (object? _, EventArgs _) =>
		{
			_progress = VisualDrawing.MoveTowards(_progress, Checked ? 1.0 : 0.0, 0.18);
			_hover = VisualDrawing.MoveTowards(_hover, _hovering ? 1.0 : 0.0, 0.18);
			bool flag = Math.Abs(_progress - (Checked ? 1.0 : 0.0)) < 0.001;
			if (flag)
			{
				double hover = _hover;
				bool flag2 = ((hover == 0.0 || hover == 1.0) ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				_motionTimer.Stop();
			}
			Invalidate();
		};
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaintBackground(e);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF bounds = new RectangleF(1f, ((float)Height - 18f) / 2f, 18f, 18f);
		Color color = VisualMotion.Mix(VisualMotion.Mix(Ui.Surface, Ui.SurfaceHover, _hover), Ui.Accent, _progress);
		Color color2 = VisualMotion.Mix(Ui.Line, Ui.Accent, _progress);
		using (SolidBrush brush = new SolidBrush(color))
		{
			VisualDrawing.FillRoundedRectangle(e.Graphics, brush, bounds, 5f);
		}
		using (Pen pen = new Pen(color2))
		{
			VisualDrawing.DrawRoundedRectangle(e.Graphics, pen, bounds, 5f);
		}
		if (_progress > 0.03)
		{
			using Pen pen2 = new Pen(Color.FromArgb((int)(255.0 * _progress), Ui.PrimaryInk), 1.8f)
			{
				StartCap = LineCap.Round,
				EndCap = LineCap.Round
			};
			e.Graphics.DrawLines(pen2, new PointF[3]
			{
				new PointF(bounds.X + 4.5f, bounds.Y + 9f),
				new PointF(bounds.X + 7.7f, bounds.Y + 12f),
				new PointF(bounds.X + 13.5f, bounds.Y + 6.2f)
			});
		}
		TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(27, 0, Width - 27, Height), Enabled ? ForeColor : Ui.Faint, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
	}

	protected override void OnCheckedChanged(EventArgs e)
	{
		if (VisualMotion.AnimationsEnabled)
		{
			_motionTimer.Start();
		}
		else
		{
			_progress = (Checked ? 1.0 : 0.0);
		}
		Invalidate();
		base.OnCheckedChanged(e);
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		_hovering = true;
		if (VisualMotion.AnimationsEnabled)
		{
			_motionTimer.Start();
		}
		else
		{
			_hover = 1.0;
		}
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hovering = false;
		if (VisualMotion.AnimationsEnabled)
		{
			_motionTimer.Start();
		}
		else
		{
			_hover = 0.0;
		}
		base.OnMouseLeave(e);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_motionTimer.Dispose();
		}
		base.Dispose(disposing);
	}
}
