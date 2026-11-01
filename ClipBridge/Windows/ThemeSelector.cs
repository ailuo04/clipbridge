using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// A single, draggable three-position selector: dark, light, then system.
internal sealed class ThemeSelector : Control
{
	private static readonly UiThemeMode[] Modes = { UiThemeMode.Dark, UiThemeMode.Light, UiThemeMode.System };
	private static readonly string[] Names = { "深色", "浅色", "跟随电脑" };
	private readonly Timer _timer = new Timer { Interval = 15 };
	private readonly ToolTip _toolTip = new ToolTip();
	private int _selected = 1;
	private int _notified = 1;
	private int _target = 1;
	private int _hovered = -1;
	private bool _dragging;
	private double _position = 1;
	private double _startPosition = 1;
	private long _slideStart;

	public event Action<UiThemeMode>? ModeChanged;

	public ThemeSelector()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
		BackColor = Color.Transparent;
		Size = new Size(126, 38);
		MinimumSize = new Size(96, 32);
		Cursor = Cursors.Hand;
		TabStop = true;
		AccessibleName = "主题";
		AccessibleDescription = "使用左右方向键切换深色、浅色或跟随电脑主题";
		AccessibleRole = AccessibleRole.Grouping;
		_timer.Tick += (_, _) => TickSlide();
	}

	public void SetMode(UiThemeMode mode)
	{
		int index = Array.IndexOf(Modes, mode);
		_selected = _notified = _target = index < 0 ? 1 : index;
		_position = _selected;
		_timer.Stop();
		Invalidate();
	}

	private void SlideTo(int index)
	{
		index = Math.Clamp(index, 0, 2);
		if (_target != index)
		{
			_startPosition = _position;
			_target = index;
			_slideStart = Stopwatch.GetTimestamp();
		}
		if (VisualMotion.AnimationsEnabled && Math.Abs(_position - _target) > 0.001)
		{
			_timer.Start();
		}
		else
		{
			_position = _target;
			_timer.Stop();
			Invalidate();
			NotifyIfSettled();
		}
	}

	private void TickSlide()
	{
		double elapsed = (Stopwatch.GetTimestamp() - _slideStart) * 1000.0 / Stopwatch.Frequency;
		double progress = VisualMotion.AnimationsEnabled ? Math.Clamp(elapsed / 180.0, 0.0, 1.0) : 1.0;
		_position = _startPosition + (_target - _startPosition) * VisualMotion.EaseOutCubic(progress);
		Invalidate();
		if (progress >= 1.0)
		{
			_position = _target;
			_timer.Stop();
			NotifyIfSettled();
		}
	}

	private void NotifyIfSettled()
	{
		if (!_dragging && _selected != _notified && Math.Abs(_position - _selected) < 0.001)
		{
			_notified = _selected;
			AccessibilityNotifyClients(AccessibleEvents.Selection, _selected + 1);
			ModeChanged?.Invoke(Modes[_selected]);
		}
	}

	private void SelectIndex(int index)
	{
		_selected = Math.Clamp(index, 0, 2);
		SlideTo(_selected);
		NotifyIfSettled();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaintBackground(e);
		Graphics g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF track = new RectangleF(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
		using (SolidBrush fill = new SolidBrush(VisualMotion.Mix(Ui.Canvas, Ui.SurfaceHover, 0.4)))
		{
			VisualDrawing.FillRoundedRectangle(g, fill, track, 10);
		}
		using (Pen border = new Pen(Ui.Line))
		{
			VisualDrawing.DrawRoundedRectangle(g, border, track, 10);
		}
		float segment = (Width - 6f) / 3f;
		RectangleF thumb = new RectangleF(3 + segment * (float)_position, 3, segment, Height - 7);
		RectangleF shadow = thumb;
		shadow.Offset(0, 1);
		using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(Ui.HeaderIsDark ? 35 : 18, Color.Black)))
		{
			VisualDrawing.FillRoundedRectangle(g, shadowBrush, shadow, 8);
		}
		using (SolidBrush thumbBrush = new SolidBrush(Ui.Surface))
		{
			VisualDrawing.FillRoundedRectangle(g, thumbBrush, thumb, 8);
		}
		using (Pen thumbBorder = new Pen(VisualMotion.Mix(Ui.Line, Ui.Surface, 0.25)))
		{
			VisualDrawing.DrawRoundedRectangle(g, thumbBorder, thumb, 8);
		}
		for (int i = 0; i < 3; i++)
		{
			float cx = 3 + segment * (i + 0.5f);
			float cy = Height / 2f;
			double closeness = Math.Max(0, 1 - Math.Abs(_position - i));
			Color color = VisualMotion.Mix(Ui.Muted, Ui.Accent, closeness);
			GraphicsState iconState = g.Save();
			g.TranslateTransform(cx, cy);
			float scale = DeviceDpi / 96f;
			g.ScaleTransform(scale, scale);
			DrawIcon(g, i, 0, 0, color);
			g.Restore(iconState);
		}
		if (Focused && ShowFocusCues)
		{
			using Pen focus = new Pen(Ui.Accent) { DashStyle = DashStyle.Dot };
			VisualDrawing.DrawRoundedRectangle(g, focus, RectangleF.Inflate(track, -2, -2), 8);
		}
	}

	private static void DrawIcon(Graphics g, int index, float x, float y, Color color)
	{
		using Pen pen = new Pen(color, 1.35f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
		if (index == 0)
		{
			using GraphicsPath moon = new GraphicsPath();
			moon.AddBezier(x + 1.5f, y - 6, x - 4, y - 6, x - 6, y - 1, x - 3, y + 4);
			moon.AddBezier(x - 3, y + 4, x, y + 7, x + 5, y + 5, x + 6, y + 1);
			moon.AddBezier(x + 6, y + 1, x + 1, y + 3, x - 1, y - 1, x + 1.5f, y - 6);
			g.DrawPath(pen, moon);
		}
		else if (index == 1)
		{
			g.DrawEllipse(pen, x - 3, y - 3, 6, 6);
			for (int i = 0; i < 8; i++)
			{
				double angle = Math.PI * i / 4;
				g.DrawLine(pen, x + (float)Math.Cos(angle) * 5, y + (float)Math.Sin(angle) * 5,
					x + (float)Math.Cos(angle) * 7, y + (float)Math.Sin(angle) * 7);
			}
		}
		else
		{
			g.DrawRectangle(pen, x - 6, y - 5, 12, 9);
			g.DrawLine(pen, x, y + 4, x, y + 6);
			g.DrawLine(pen, x - 3, y + 6, x + 3, y + 6);
		}
	}

	private int HitIndex(int x) => Math.Clamp((int)((x - 3) / Math.Max(1f, (Width - 6f) / 3f)), 0, 2);

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		if (e.Button == MouseButtons.Left)
		{
			Focus();
			_dragging = true;
			Capture = true;
			SlideTo(HitIndex(e.X));
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		int index = HitIndex(e.X);
		if (_hovered != index)
		{
			_hovered = index;
			_toolTip.SetToolTip(this, Names[index]);
		}
		if (_dragging)
		{
			SlideTo(index);
		}
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);
		if (_dragging && e.Button == MouseButtons.Left)
		{
			_dragging = false;
			Capture = false;
			SelectIndex(HitIndex(e.X));
		}
	}

	protected override void OnMouseCaptureChanged(EventArgs e)
	{
		base.OnMouseCaptureChanged(e);
		if (_dragging && !Capture)
		{
			_dragging = false;
			SlideTo(_selected);
		}
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hovered = -1;
		base.OnMouseLeave(e);
	}

	protected override bool IsInputKey(Keys keyData)
	{
		Keys key = keyData & Keys.KeyCode;
		return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		int index = e.KeyCode switch
		{
			Keys.Left => Math.Max(0, _selected - 1),
			Keys.Right => Math.Min(2, _selected + 1),
			Keys.Home => 0,
			Keys.End => 2,
			_ => -1
		};
		if (index >= 0)
		{
			e.Handled = true;
			SelectIndex(index);
		}
	}

	protected override AccessibleObject CreateAccessibilityInstance() => new SelectorAccessibleObject(this);

	private sealed class SelectorAccessibleObject : ControlAccessibleObject
	{
		private readonly ThemeSelector _owner;
		public SelectorAccessibleObject(ThemeSelector owner) : base(owner) => _owner = owner;
		public override int GetChildCount() => 3;
		public override AccessibleObject GetChild(int index) => index >= 0 && index < 3 ? new OptionAccessibleObject(_owner, index) : null!;
	}

	private sealed class OptionAccessibleObject : AccessibleObject
	{
		private readonly ThemeSelector _owner;
		private readonly int _index;
		public OptionAccessibleObject(ThemeSelector owner, int index) { _owner = owner; _index = index; }
		public override string? Name { get => Names[_index]; set { } }
		public override AccessibleRole Role => AccessibleRole.RadioButton;
		public override AccessibleObject? Parent => _owner.AccessibilityObject;
		public override string? DefaultAction => "选择";
		public override AccessibleStates State => AccessibleStates.Focusable | AccessibleStates.Selectable
			| (_owner._selected == _index ? AccessibleStates.Checked | AccessibleStates.Selected : AccessibleStates.None)
			| (_owner._selected == _index && _owner.Focused ? AccessibleStates.Focused : AccessibleStates.None)
			| (!_owner.Enabled ? AccessibleStates.Unavailable : AccessibleStates.None);
		public override Rectangle Bounds
		{
			get
			{
				float segment = (_owner.Width - 6f) / 3f;
				return _owner.RectangleToScreen(Rectangle.Round(new RectangleF(3 + segment * _index, 3, segment, _owner.Height - 7)));
			}
		}
		public override void DoDefaultAction() => _owner.SelectIndex(_index);
		public override void Select(AccessibleSelection flags)
		{
			if ((flags & AccessibleSelection.TakeFocus) != 0) _owner.Focus();
			if ((flags & AccessibleSelection.TakeSelection) != 0) _owner.SelectIndex(_index);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_timer.Dispose();
			_toolTip.Dispose();
		}
		base.Dispose(disposing);
	}
}
