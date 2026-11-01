using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Status footer painted as a rounded card so it shares the silhouette, border
// and soft shadow of the network, device and log cards above it.
internal sealed class FooterBarSurface : Panel
{
	private const int CornerRadius = 14;

	private bool _active;

	public double MotionProgress => 0.0;

	public FooterBarSurface()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Ui.Canvas;
	}

	// Leading status dot: green while syncing, amber while paused.
	public void SetActive(bool active)
	{
		_active = active;
		Invalidate();
	}

	public void Flash()
	{
		Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		if (Width < 5 || Height < 8) return;
		Graphics g = e.Graphics;
		g.Clear(Ui.Canvas);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF card = new RectangleF(2f, 0f, Width - 4f, Height - 6f);
		int shadowAlpha = Ui.HeaderIsDark ? 34 : 14;
		for (int layer = 3; layer >= 1; layer--)
		{
			using SolidBrush shadow = new SolidBrush(Color.FromArgb(shadowAlpha / layer, 24, 40, 72));
			RectangleF bounds = RectangleF.Inflate(card, layer - 1, layer - 1);
			bounds.Offset(0f, layer * 1.2f);
			VisualDrawing.FillRoundedRectangle(g, shadow, bounds, CornerRadius + layer - 1);
		}
		using (SolidBrush fill = new SolidBrush(Ui.Surface))
		{
			VisualDrawing.FillRoundedRectangle(g, fill, card, CornerRadius);
		}
		using (Pen border = new Pen(Ui.Line))
		{
			VisualDrawing.DrawRoundedRectangle(g, border, card, CornerRadius);
		}
		Color dot = _active ? Ui.Success : Ui.Warning;
		float cy = card.Top + card.Height / 2f;
		using (SolidBrush halo = new SolidBrush(Color.FromArgb(40, dot)))
		{
			g.FillEllipse(halo, 16f, cy - 7f, 14f, 14f);
		}
		using SolidBrush core = new SolidBrush(dot);
		g.FillEllipse(core, 20f, cy - 3f, 6f, 6f);
	}
}
