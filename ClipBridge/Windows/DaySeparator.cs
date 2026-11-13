using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Day marker between message groups. Without it a week of transfers reads as
// one undifferentiated column of bubbles.
internal sealed class DaySeparator : Control
{
	private readonly string _text;

	public DaySeparator(DateTime day)
	{
		_text = Label(day);
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
			| ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
		BackColor = Color.Transparent;
		TabStop = false;
		Height = 38;
		Font = Ui.Font(8.25f, FontStyle.Bold);
		AccessibleRole = AccessibleRole.Separator;
		AccessibleName = _text;
	}

	private static string Label(DateTime day)
	{
		DateTime today = DateTime.Today;
		if (day.Date == today) return "今天";
		if (day.Date == today.AddDays(-1)) return "昨天";
		return day.Year == today.Year ? day.ToString("M月d日") : day.ToString("yyyy年M月d日");
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		if (Width < 40) return;
		Graphics g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		Size measured = TextRenderer.MeasureText(_text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
		int chipWidth = measured.Width + 26;
		RectangleF chip = new((Width - chipWidth) / 2f, (Height - 22) / 2f, chipWidth, 22);
		using (var rule = new Pen(Ui.Line, 1f))
		{
			float centerY = chip.Top + chip.Height / 2f;
			g.DrawLine(rule, 0, centerY, chip.Left - 12, centerY);
			g.DrawLine(rule, chip.Right + 12, centerY, Width, centerY);
		}
		using (var fill = new SolidBrush(Ui.SurfaceMutedSolid)) VisualDrawing.FillRoundedRectangle(g, fill, chip, 11f);
		using (var border = new Pen(Ui.Line, 1f)) VisualDrawing.DrawRoundedRectangle(g, border, chip, 11f);
		TextRenderer.DrawText(g, _text, Font, Rectangle.Round(chip), Ui.Faint,
			TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
	}
}