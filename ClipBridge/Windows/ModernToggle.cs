using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class ModernToggle : CheckBox
{
	private readonly record struct ToggleMetrics(float Scale, float TrackWidth, float TrackHeight, float KnobSize, float KnobInset);

	private readonly Timer _motionTimer = new Timer
	{
		Interval = 15
	};

	private double _hover;

	private double _progress;

	private bool _hovering;

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public bool DarkBackground { get; set; }

	public double AnimatedValue => _progress;

	public ModernToggle()
	{
		AutoSize = false;
		Size = new Size(126, 34);
		Font = Ui.Font(9f);
		ForeColor = Ui.Ink;
		Cursor = Cursors.Hand;
		Text = "自动同步";
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Color.Transparent;
		_motionTimer.Tick += (object? _, EventArgs _) =>
		{
			_hover = VisualDrawing.MoveTowards(_hover, _hovering ? 1.0 : 0.0, 0.17);
			_progress = VisualDrawing.MoveTowards(_progress, Checked ? 1.0 : 0.0, 0.15);
			double hover = _hover;
			bool flag = ((hover == 0.0 || hover == 1.0) ? true : false);
			if (flag && Math.Abs(_progress - (Checked ? 1.0 : 0.0)) < 0.001)
			{
				_motionTimer.Stop();
			}
			Invalidate();
		};
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		base.OnPaintBackground(e);
		ToggleMetrics toggleMetrics = CalculateRenderMetrics(DeviceDpi);
		Color foreColor;
		if (Enabled)
		{
			foreColor = (DarkBackground ? Ui.HeaderText : Ui.Ink);
		}
		else
		{
			foreColor = Ui.Faint;
		}
		int width = Math.Max(1, Width - (int)Math.Ceiling(toggleMetrics.TrackWidth + 9f * toggleMetrics.Scale));
		TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, width, Height), foreColor, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		RectangleF rectangleF = new RectangleF((float)Width - toggleMetrics.TrackWidth - 4f * toggleMetrics.Scale, ((float)Height - toggleMetrics.TrackHeight) / 2f, toggleMetrics.TrackWidth, toggleMetrics.TrackHeight);
		Color color = VisualMotion.Mix(DarkBackground ? Ui.HeaderRaised : Ui.ChromeRaised, Ui.Accent, _progress);
		if (!Enabled)
		{
			color = Ui.ChromeRaised;
		}
		using (SolidBrush brush = new SolidBrush(color))
		{
			VisualDrawing.FillRoundedRectangle(e.Graphics, brush, rectangleF, toggleMetrics.TrackHeight / 2f);
		}
		if (Focused && ShowFocusCues)
		{
			using Pen pen = new Pen(Color.FromArgb(150, Ui.Accent), 1.2f)
			{
				DashStyle = DashStyle.Dot
			};
			VisualDrawing.DrawRoundedRectangle(e.Graphics, pen, RectangleF.Inflate(rectangleF, 2f * toggleMetrics.Scale, 2f * toggleMetrics.Scale), toggleMetrics.TrackHeight / 2f + 2f * toggleMetrics.Scale);
		}
		float x = VisualMotion.Lerp(rectangleF.X + toggleMetrics.KnobInset, rectangleF.Right - toggleMetrics.KnobInset - toggleMetrics.KnobSize, _progress);
		RectangleF rect = new RectangleF(x, rectangleF.Y + toggleMetrics.KnobInset, toggleMetrics.KnobSize, toggleMetrics.KnobSize);
		using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(38, 48, 81, 115)))
		{
			e.Graphics.FillEllipse(brush2, rect.X, rect.Y + toggleMetrics.Scale, rect.Width, rect.Height);
		}
		using SolidBrush brush3 = new SolidBrush(Enabled ? Color.White : Ui.SurfaceMutedSolid);
		e.Graphics.FillEllipse(brush3, rect);
	}

	private static ToggleMetrics CalculateRenderMetrics(int dpi)
	{
		float num = Math.Max(1f, (float)dpi / 96f);
		return new ToggleMetrics(num, 42f * num, 24f * num, 18f * num, 3f * num);
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

	protected override void OnEnabledChanged(EventArgs e)
	{
		Cursor = (Enabled ? Cursors.Hand : Cursors.Default);
		Invalidate();
		base.OnEnabledChanged(e);
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
