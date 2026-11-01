using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Rounded read-only field backdrop for the local address list.
internal sealed class InsetField : Panel
{
	public InsetField()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Ui.Surface;
		Padding = new Padding(14, 10, 12, 8);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.Clear(BackColor);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
		using (SolidBrush fill = new SolidBrush(Ui.FieldSurface))
		{
			VisualDrawing.FillRoundedRectangle(e.Graphics, fill, bounds, 10f);
		}
		using (Pen border = new Pen(Ui.Line))
		{
			VisualDrawing.DrawRoundedRectangle(e.Graphics, border, bounds, 10f);
		}
		// Accent rail on the left edge marks this as the "active" address.
		using SolidBrush rail = new SolidBrush(Color.FromArgb(200, Ui.Accent));
		VisualDrawing.FillRoundedRectangle(e.Graphics, rail, new RectangleF(6f, 12f, 3f, 16f), 1.5f);
	}
}
