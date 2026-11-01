using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class WindowCaptionButton : Control
{
	private readonly Font _glyphFont = new Font("Segoe Fluent Icons", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

	private double _hover;

	private double _pressed;

	private Point _pressPoint;

	private bool _hovering;

	private readonly Timer _motionTimer = new Timer
	{
		Interval = 15
	};

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public string Glyph { get; set; } = string.Empty;

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public bool CloseAction { get; set; }

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public bool DarkBackground { get; set; }

	public double MotionProgress => Math.Max(_hover, _pressed);

	public WindowCaptionButton()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Size = new Size(44, 38);
		BackColor = Color.Transparent;
		Cursor = Cursors.Hand;
		TabStop = false;
		AccessibleRole = AccessibleRole.PushButton;
		_motionTimer.Tick += (object? _, EventArgs _) =>
		{
			_hover = VisualDrawing.MoveTowards(_hover, _hovering ? 1.0 : 0.0, 0.24);
			_pressed = VisualDrawing.MoveTowards(_pressed, 0.0, 0.2);
			double hover = _hover;
			bool flag = ((hover == 0.0 || hover == 1.0) ? true : false);
			if (flag && _pressed == 0.0)
			{
				_motionTimer.Stop();
			}
			Invalidate();
		};
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		float num = (float)(_pressed * 1.2);
		RectangleF bounds = new RectangleF(num, num, (float)Width - num * 2f, (float)Height - num * 2f);
		if (_hover > 0.0 || _pressed > 0.0)
		{
			Color baseColor;
			if (CloseAction)
			{
				baseColor = Ui.DangerSoft;
			}
			else
			{
				baseColor = (DarkBackground ? Ui.HeaderRaised : Ui.SurfaceHover);
			}
			using SolidBrush brush = new SolidBrush(Color.FromArgb((int)(34.0 + 72.0 * Math.Max(_hover, _pressed)), baseColor));
			VisualDrawing.FillRoundedRectangle(e.Graphics, brush, bounds, 10f);
		}
		if (_pressed > 0.0)
		{
			float num2 = (float)Math.Max(Width, Height) * 1.5f;
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb((int)(55.0 * _pressed), Color.White));
			e.Graphics.FillEllipse(brush2, (float)_pressPoint.X - num2 / 2f, (float)_pressPoint.Y - num2 / 2f, num2, num2);
		}
		Color color = (DarkBackground ? Ui.HeaderMuted : Ui.ChromeMuted);
		Color to;
		if (CloseAction)
		{
			to = Ui.Danger;
		}
		else
		{
			to = (DarkBackground ? Ui.HeaderText : Ui.AccentDark);
		}
		Color foreColor = VisualMotion.Mix(color, to, _hover);
		TextRenderer.DrawText(e.Graphics, Glyph, _glyphFont, ClientRectangle, foreColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		_hovering = true;
		AnimateOrSetTarget();
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hovering = false;
		AnimateOrSetTarget();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			_pressed = 1.0;
			_pressPoint = e.Location;
			AnimateOrSetTarget();
			Invalidate();
		}
		base.OnMouseDown(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		_pressed = (VisualMotion.AnimationsEnabled ? 0.65 : 0.0);
		AnimateOrSetTarget();
		base.OnMouseUp(e);
	}

	private void AnimateOrSetTarget()
	{
		if (VisualMotion.AnimationsEnabled)
		{
			_motionTimer.Start();
			return;
		}
		_hover = (_hovering ? 1.0 : 0.0);
		_pressed = 0.0;
		Invalidate();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_glyphFont.Dispose();
			_motionTimer.Dispose();
		}
		base.Dispose(disposing);
	}
}
