using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Two small rounded tags showing the discovery and transfer ports.
internal sealed class PortTags : Control
{
	private readonly Font _font = new Font("Consolas", 8.4f);

	public PortTags()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Ui.Surface;
		AccessibleName = "端口";
		AccessibleDescription = "发现 UDP 45821，传输 TCP 45822";
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.Clear(BackColor);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		float x = 0f;
		x = DrawTag(e.Graphics, x, "发现 UDP 45821") + 8f;
		DrawTag(e.Graphics, x, "传输 TCP 45822");
	}

	private float DrawTag(Graphics graphics, float x, string text)
	{
		Size size = TextRenderer.MeasureText(text, _font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
		RectangleF tag = new RectangleF(x, (Height - 22f) / 2f, size.Width + 18f, 22f);
		using (SolidBrush fill = new SolidBrush(Ui.AccentSoftSolid))
		{
			VisualDrawing.FillRoundedRectangle(graphics, fill, tag, 11f);
		}
		TextRenderer.DrawText(graphics, text, _font, Rectangle.Round(tag), Ui.AccentDark, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
		return tag.Right;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_font.Dispose();
		}
		base.Dispose(disposing);
	}
}
