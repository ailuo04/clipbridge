using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Day marker between message groups. Plain centred caption, the way chat
// clients mark time: it separates days without competing with the bubbles.
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
		Height = 34;
		Font = Ui.Font(8.25f);
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
		TextRenderer.DrawText(e.Graphics, _text, Font, ClientRectangle, Ui.Faint,
			TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
	}
}
