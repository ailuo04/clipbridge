using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Compact online/offline chip for the conversation title bar. Carries the
// connection state so the status line below it is free for transient results.
internal sealed class ConnectionPill : Control
{
	private bool _online;
	private string _text = "";

	public ConnectionPill()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
			| ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
		BackColor = Color.Transparent;
		TabStop = false;
		Font = Ui.Font(8.25f, FontStyle.Bold);
		AccessibleRole = AccessibleRole.StatusBar;
		SetState(false);
	}

	public void SetState(bool online)
	{
		string text = online ? "在线" : "离线";
		if (_online == online && _text == text) return;
		_online = online;
		_text = text;
		AccessibleName = online ? "设备在线" : "设备离线";
		Measure();
		Invalidate();
	}

	private void Measure()
	{
		Size preferred = TextRenderer.MeasureText(_text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
		Size = new Size(preferred.Width + 34, 24);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		Graphics g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF bounds = new(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
		Color fill = _online ? Ui.SuccessSoft : Ui.SurfaceMutedSolid;
		Color ink = _online ? Ui.SuccessText : Ui.Muted;
		Color dot = _online ? Ui.Success : Ui.Faint;
		using (var brush = new SolidBrush(fill)) VisualDrawing.FillRoundedRectangle(g, brush, bounds, Height / 2f);
		using (var pen = new Pen(Color.FromArgb(_online ? 90 : 150, dot), 1f)) VisualDrawing.DrawRoundedRectangle(g, pen, bounds, Height / 2f);
		float centerY = Height / 2f;
		using (var halo = new SolidBrush(Color.FromArgb(46, dot))) g.FillEllipse(halo, 9f, centerY - 5.5f, 11f, 11f);
		using (var core = new SolidBrush(dot)) g.FillEllipse(core, 11.5f, centerY - 3f, 6f, 6f);
		TextRenderer.DrawText(g, _text, Font, new Rectangle(22, 0, Width - 28, Height), ink,
			TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
	}
}