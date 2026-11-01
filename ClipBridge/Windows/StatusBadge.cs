using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class StatusBadge : Control
{
	private readonly Timer _motionTimer = new Timer
	{
		Interval = 30
	};

	private bool _active;

	private double _phase;

	private double _stateProgress;

	public double MotionProgress => _stateProgress;

	private bool ShouldAnimate
	{
		get
		{
			if (Visible && VisualMotion.AnimationsEnabled)
			{
				if (!_active)
				{
					return _stateProgress > 0.0;
				}
				return true;
			}
			return false;
		}
	}

	public StatusBadge()
	{
		AutoSize = false;
		Size = new Size(112, 34);
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Color.Transparent;
		_motionTimer.Tick += (object? _, EventArgs _) =>
		{
			_stateProgress = VisualDrawing.MoveTowards(_stateProgress, _active ? 1.0 : 0.0, 0.14);
			if (_active && Visible && VisualMotion.AnimationsEnabled)
			{
				_phase = (_phase + 0.07) % (Math.PI * 2.0);
			}
			if (!ShouldAnimate)
			{
				_motionTimer.Stop();
			}
			Invalidate();
		};
	}

	public void SetActive(bool active)
	{
		_active = active;
		if (!VisualMotion.AnimationsEnabled)
		{
			_stateProgress = (active ? 1.0 : 0.0);
		}
		UpdateAnimation();
		Invalidate();
	}

	public void SetState(bool active)
	{
		SetActive(active);
	}

	private void UpdateAnimation()
	{
		if (ShouldAnimate)
		{
			_motionTimer.Start();
		}
		else
		{
			_motionTimer.Stop();
		}
	}

	protected override void OnVisibleChanged(EventArgs e)
	{
		base.OnVisibleChanged(e);
		UpdateAnimation();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		base.OnPaintBackground(e);
		Color color = VisualMotion.Mix(Ui.WarningSoft, Ui.SuccessSoft, _stateProgress);
		Color color2 = VisualMotion.Mix(Ui.WarningText, Ui.SuccessText, _stateProgress);
		using (SolidBrush brush = new SolidBrush(color))
		{
			VisualDrawing.FillRoundedRectangle(e.Graphics, brush, new RectangleF(0.5f, 3.5f, (float)Width - 1.5f, (float)Height - 7f), 13f);
		}
		PointF pointF = new PointF(15f, (float)Height / 2f);
		if (_active && VisualMotion.AnimationsEnabled)
		{
			double num = (Math.Sin(_phase) + 1.0) / 2.0;
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb((int)(30.0 + num * 20.0), Ui.Success));
			e.Graphics.FillEllipse(brush2, pointF.X - (5.5f + (float)num * 2f), pointF.Y - (5.5f + (float)num * 2f), 11f + (float)num * 4f, 11f + (float)num * 4f);
		}
		using (SolidBrush brush3 = new SolidBrush(color2))
		{
			e.Graphics.FillEllipse(brush3, pointF.X - 3.5f, pointF.Y - 3.5f, 7f, 7f);
		}
		TextRenderer.DrawText(e.Graphics, _active ? "同步中" : "已暂停", Font, new Rectangle(27, 0, Width - 34, Height), color2, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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
