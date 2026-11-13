using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class ModernButton : Button
{
	private readonly Timer _motionTimer = new Timer
	{
		Interval = 15
	};

	private readonly Font _glyphFont = new Font("Segoe Fluent Icons", 10.5f, FontStyle.Regular, GraphicsUnit.Point);

	private double _hover;

	private double _pressed;

	private double _rippleProgress = 1.0;

	private Point _rippleOrigin;

	private bool _hovering;

	private bool _pressing;

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public ButtonTone Tone { get; set; }

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public string Glyph { get; set; }

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public Color GlyphColor { get; set; }

	public double MotionProgress => Math.Max(_hover, Math.Max(_pressed, 1.0 - _rippleProgress));

	public ModernButton(string text, ButtonTone tone = ButtonTone.Secondary, string glyph = "")
	{
		Text = text;
		Tone = tone;
		Glyph = glyph;
		GlyphColor = Color.Empty;
		Font = Ui.Font(9f);
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		MinimumSize = new Size(72, 34);
		Margin = new Padding(8, 0, 0, 0);
		Cursor = Cursors.Hand;
		FlatStyle = FlatStyle.Flat;
		FlatAppearance.BorderSize = 0;
		UseVisualStyleBackColor = false;
		TabStop = true;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Color.Transparent;
		_motionTimer.Tick += (object? _, EventArgs _) =>
		{
			_hover = VisualDrawing.MoveTowards(_hover, _hovering ? 1.0 : 0.0, 0.16);
			_pressed = VisualDrawing.MoveTowards(_pressed, _pressing ? 1.0 : 0.0, 0.24);
			_rippleProgress = Math.Min(1.0, _rippleProgress + 0.1);
			double hover = _hover;
			bool flag = ((hover == 0.0 || hover == 1.0) ? true : false);
			bool flag2 = flag;
			if (flag2)
			{
				double pressed = _pressed;
				bool flag3 = ((pressed == 0.0 || pressed == 1.0) ? true : false);
				flag2 = flag3;
			}
			if (flag2 && _rippleProgress >= 1.0)
			{
				_motionTimer.Stop();
			}
			Invalidate();
		};
	}

	public override Size GetPreferredSize(Size proposedSize)
	{
		if (string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(Glyph)) return new Size(36, 36);
		Size size = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine);
		int num = ((!string.IsNullOrEmpty(Glyph)) ? 23 : 0);
		return new Size(Math.Max(72, size.Width + num + 26), 34);
	}

	private const float Radius = 7f;

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaintBackground(e);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		float num = (float)(_pressed * 1.2);
		RectangleF rectangleF = new RectangleF(1f + num, 1f + num, (float)Width - 2f - num * 2f, (float)Height - 2f - num * 2f);
		Color color = Ui.Surface;
		Color color2 = Color.Transparent;
		Color foreColor = Ui.Ink;
		switch (Tone)
		{
		case ButtonTone.Primary:
			color = VisualMotion.Mix(VisualMotion.Mix(Ui.PrimarySurface, Ui.PrimaryHover, _hover), Ui.PrimaryPressed, _pressed);
			color2 = Color.FromArgb(160, Ui.PrimaryHover);
			foreColor = Ui.PrimaryInk;
			break;
		case ButtonTone.Secondary:
			color = VisualMotion.Mix(Ui.Surface, Ui.SurfaceHover, _hover);
			color2 = VisualMotion.Mix(Ui.Line, Ui.Accent, _hover * 0.45 + _pressed * 0.3);
			break;
		case ButtonTone.Ghost:
			color = VisualMotion.Mix(Parent?.BackColor ?? Ui.Canvas, Ui.SurfaceHover, _hover);
			foreColor = Ui.Muted;
			break;
		case ButtonTone.Danger:
			color = VisualMotion.Mix(Ui.Surface, Ui.DangerSoft, _hover);
			color2 = Ui.Line;
			foreColor = Ui.Danger;
			break;
		}
		if (!Enabled)
		{
			color = Ui.SurfaceMutedSolid;
			color2 = Ui.Line;
			foreColor = Ui.Faint;
		}
		// Flat fills throughout: the primary action stands out by colour alone.
		using (SolidBrush brush = new SolidBrush(color))
		{
			VisualDrawing.FillRoundedRectangle(e.Graphics, brush, rectangleF, Radius);
		}
		if (color2.A > 0 && !(Tone == ButtonTone.Primary && Enabled))
		{
			using Pen pen = new Pen(color2, 1f);
			VisualDrawing.DrawRoundedRectangle(e.Graphics, pen, rectangleF, Radius);
		}
		if (_rippleProgress < 1.0 && Enabled)
		{
			GraphicsState gstate = e.Graphics.Save();
			using GraphicsPath clip = VisualDrawing.RoundedRectangle(rectangleF, Radius);
			e.Graphics.SetClip(clip);
			float num2 = (float)(Math.Sqrt(Width * Width + Height * Height) * _rippleProgress);
			using (SolidBrush brush2 = new SolidBrush(Color.FromArgb((int)(42.0 * (1.0 - _rippleProgress)), Color.White)))
			{
				e.Graphics.FillEllipse(brush2, (float)_rippleOrigin.X - num2 / 2f, (float)_rippleOrigin.Y - num2 / 2f, num2, num2);
			}
			e.Graphics.Restore(gstate);
		}
		if (Focused && ShowFocusCues)
		{
			RectangleF bounds = RectangleF.Inflate(rectangleF, -2.5f, -2.5f);
			using Pen pen2 = new Pen(Color.FromArgb(150, Ui.Accent), 1.2f)
			{
				DashStyle = DashStyle.Dot
			};
			VisualDrawing.DrawRoundedRectangle(e.Graphics, pen2, bounds, Radius);
		}
		Size size = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
		int num3 = ((!string.IsNullOrEmpty(Glyph)) ? 22 : 0);
		int num4 = size.Width + num3;
		int num5 = TextAlign == ContentAlignment.MiddleLeft ? 14 : Math.Max(8, (Width - num4) / 2);
		if (string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(Glyph)) num5 = (Width - 18) / 2;
		if (!string.IsNullOrEmpty(Glyph))
		{
			Color glyphColor = Enabled && !GlyphColor.IsEmpty ? GlyphColor : foreColor;
			TextRenderer.DrawText(e.Graphics, Glyph, _glyphFont, new Rectangle(num5, 0, 18, Height), glyphColor, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
			num5 += 22;
		}
		TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(num5, 0, Math.Max(1, Width - num5 - 9), Height), foreColor, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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
		_pressing = false;
		AnimateOrSetTarget();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs mevent)
	{
		if (mevent.Button == MouseButtons.Left)
		{
			_pressing = true;
			_rippleOrigin = mevent.Location;
			_rippleProgress = 0.0;
		}
		AnimateOrSetTarget();
		base.OnMouseDown(mevent);
	}

	protected override void OnMouseUp(MouseEventArgs mevent)
	{
		_pressing = false;
		AnimateOrSetTarget();
		base.OnMouseUp(mevent);
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		Cursor = (Enabled ? Cursors.Hand : Cursors.Default);
		Invalidate();
		base.OnEnabledChanged(e);
	}

	private void AnimateOrSetTarget()
	{
		if (VisualMotion.AnimationsEnabled)
		{
			_motionTimer.Start();
			return;
		}
		_hover = (_hovering ? 1.0 : 0.0);
		_pressed = (_pressing ? 1.0 : 0.0);
		_rippleProgress = 1.0;
		Invalidate();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_motionTimer.Dispose();
			_glyphFont.Dispose();
		}
		base.Dispose(disposing);
	}
}
