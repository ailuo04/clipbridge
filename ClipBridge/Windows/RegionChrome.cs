using System.Drawing;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Sidebar chrome: its own surface tone plus a hard edge against the content
// well, so the two regions never read as one continuous sheet.
internal sealed class SidebarSurface : Panel
{
	public SidebarSurface()
	{
		SetStyle(ControlStyles.ResizeRedraw, true);
		BackColor = Ui.Sidebar;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		if (Width < 2 || Height < 1) return;
		using var edge = new Pen(Ui.Line);
		e.Graphics.DrawLine(edge, Width - 1, 0, Width - 1, Height);
	}
}

// Conversation title bar. A raised surface with a bottom rule separates the
// peer identity from the scrolling message well below it.
internal sealed class ConversationHeaderSurface : Panel
{
	public ConversationHeaderSurface()
	{
		SetStyle(ControlStyles.ResizeRedraw, true);
		BackColor = Ui.Surface;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		if (Width < 1 || Height < 2) return;
		using var edge = new Pen(Ui.Line);
		e.Graphics.DrawLine(edge, 0, Height - 1, Width, Height - 1);
	}
}

// Bottom status strip. Stays a Label so the workspace can still find it as the
// single status child of the content area, but paints its own chrome.
internal sealed class StatusStripLabel : Label
{
	public StatusStripLabel()
	{
		SetStyle(ControlStyles.ResizeRedraw, true);
		BackColor = Ui.Sidebar;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		if (Width > 0 && Height > 0)
		{
			using var edge = new Pen(Ui.Line);
			e.Graphics.DrawLine(edge, 0, 0, Width, 0);
		}
		base.OnPaint(e);
	}
}