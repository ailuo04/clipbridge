using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Owner-drawn list of discovered peers: one rounded card per device with an avatar,
// name, address, source, status pill and last-seen time.
internal sealed class DeviceListView : Control
{
	private const TextFormatFlags LineFlags = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;

	private readonly Font _nameFont = Ui.Font(10f, FontStyle.Bold);

	private readonly Font _detailFont = Ui.Font(8.6f);

	private readonly Font _pillFont = Ui.Font(8.2f, FontStyle.Bold);

	private readonly Font _timeFont = Ui.Font(8.2f);

	private List<PeerSnapshot> _peers = new List<PeerSnapshot>();

	private int _selected = -1;

	private int _hover = -1;

	private int _scroll;

	public event EventHandler? SelectedPeerChanged;

	public PeerSnapshot? SelectedPeer => (_selected >= 0 && _selected < _peers.Count) ? _peers[_selected] : null;

	private float Dpi => Math.Clamp((float)DeviceDpi / 96f, 1f, 3f);

	private int RowHeight => (int)Math.Round(68f * Dpi);

	private int RowGap => (int)Math.Round(8f * Dpi);

	private int Pitch => RowHeight + RowGap;

	private int ContentHeight => (_peers.Count == 0) ? 0 : (_peers.Count * Pitch - RowGap);

	private int MaxScroll => Math.Max(0, ContentHeight - Height);

	private int RightReserve => (MaxScroll > 0) ? (int)Math.Round(10f * Dpi) : 0;

	public DeviceListView()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, value: true);
		TabStop = true;
		AccessibleName = "局域网设备列表";
		AccessibleRole = AccessibleRole.List;
	}

	public void SetPeers(IReadOnlyList<PeerSnapshot> peers)
	{
		string? keep = SelectedPeer?.Address;
		_peers = new List<PeerSnapshot>(peers);
		_selected = -1;
		if (keep != null)
		{
			_selected = _peers.FindIndex((PeerSnapshot peer) => peer.Address == keep);
		}
		_hover = -1;
		_scroll = Math.Clamp(_scroll, 0, MaxScroll);
		Invalidate();
	}

	private Rectangle CardBounds(int index)
	{
		return new Rectangle(0, index * Pitch - _scroll, Math.Max(1, Width - RightReserve), RowHeight);
	}

	private int HitTest(Point point)
	{
		if (point.X >= Width - RightReserve)
		{
			return -1;
		}
		int y = point.Y + _scroll;
		if (y < 0 || y % Pitch >= RowHeight)
		{
			return -1;
		}
		int index = y / Pitch;
		return (index < _peers.Count) ? index : -1;
	}

	private void Select(int index)
	{
		if (_peers.Count == 0)
		{
			return;
		}
		index = Math.Clamp(index, 0, _peers.Count - 1);
		EnsureVisible(index);
		if (index != _selected)
		{
			_selected = index;
			SelectedPeerChanged?.Invoke(this, EventArgs.Empty);
		}
		Invalidate();
	}

	private void EnsureVisible(int index)
	{
		int top = index * Pitch;
		if (top < _scroll)
		{
			_scroll = top;
		}
		else if (top + RowHeight > _scroll + Height)
		{
			_scroll = top + RowHeight - Height;
		}
		_scroll = Math.Clamp(_scroll, 0, MaxScroll);
	}

	protected override bool IsInputKey(Keys keyData)
	{
		switch (keyData & Keys.KeyCode)
		{
		case Keys.Up:
		case Keys.Down:
		case Keys.Home:
		case Keys.End:
		case Keys.PageUp:
		case Keys.PageDown:
			return true;
		default:
			return base.IsInputKey(keyData);
		}
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		int page = Math.Max(1, Height / Pitch);
		switch (e.KeyCode)
		{
		case Keys.Up:
			Select((_selected < 0) ? 0 : _selected - 1);
			e.Handled = true;
			break;
		case Keys.Down:
			Select(_selected + 1);
			e.Handled = true;
			break;
		case Keys.Home:
			Select(0);
			e.Handled = true;
			break;
		case Keys.End:
			Select(_peers.Count - 1);
			e.Handled = true;
			break;
		case Keys.PageUp:
			Select((_selected < 0) ? 0 : _selected - page);
			e.Handled = true;
			break;
		case Keys.PageDown:
			Select(_selected + page);
			e.Handled = true;
			break;
		}
		base.OnKeyDown(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		Focus();
		int index = HitTest(e.Location);
		if (index >= 0)
		{
			Select(index);
		}
		base.OnMouseDown(e);
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		int index = HitTest(e.Location);
		if (index != _hover)
		{
			_hover = index;
			Invalidate();
		}
		base.OnMouseMove(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		if (_hover != -1)
		{
			_hover = -1;
			Invalidate();
		}
		base.OnMouseLeave(e);
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		int next = Math.Clamp(_scroll - (int)(e.Delta / 120f * Pitch * 0.8f), 0, MaxScroll);
		if (next != _scroll)
		{
			_scroll = next;
			Invalidate();
		}
		base.OnMouseWheel(e);
	}

	protected override void OnResize(EventArgs e)
	{
		_scroll = Math.Clamp(_scroll, 0, MaxScroll);
		base.OnResize(e);
	}

	protected override void OnGotFocus(EventArgs e)
	{
		Invalidate();
		base.OnGotFocus(e);
	}

	protected override void OnLostFocus(EventArgs e)
	{
		Invalidate();
		base.OnLostFocus(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics g = e.Graphics;
		g.Clear(Ui.SurfaceMintBody);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		float u = Dpi;
		for (int i = 0; i < _peers.Count; i++)
		{
			Rectangle card = CardBounds(i);
			if (card.Bottom < 0 || card.Top > Height)
			{
				continue;
			}
			DrawCard(g, i, card, u);
		}
		DrawScrollbar(g, u);
	}

	private static (string Label, Color Dot, Color PillFill, Color PillInk) Status(PeerSnapshot peer)
	{
		if (peer.RecentlySeen)
		{
			return ("在线", Ui.Success, Ui.SuccessSoft, Ui.SuccessText);
		}
		if (peer.Manual)
		{
			return ("待确认", Ui.Warning, Ui.WarningSoft, Ui.WarningText);
		}
		return ("离线", Ui.Faint, Ui.SurfaceHover, Ui.Muted);
	}

	private static string Source(PeerSnapshot peer)
	{
		if (peer.Manual)
		{
			return peer.RecentlySeen ? "自动 + 手动" : "手动添加";
		}
		return "自动发现";
	}

	private void DrawCard(Graphics g, int index, Rectangle card, float u)
	{
		PeerSnapshot peer = _peers[index];
		bool selected = index == _selected;
		bool hover = index == _hover;
		var (statusLabel, dotColor, pillFill, pillInk) = Status(peer);
		RectangleF bounds = new RectangleF(card.X + 0.5f, card.Y + 0.5f, card.Width - 1f, card.Height - 1f);
		Color fill = selected ? Ui.AccentSoftSolid : (hover ? Ui.SurfaceHover : Ui.FieldSurface);
		Color border = selected ? Color.FromArgb(150, Ui.Accent) : (hover ? Color.FromArgb(200, Ui.Accent) : Ui.Line);
		using (SolidBrush brush = new SolidBrush(fill))
		{
			VisualDrawing.FillRoundedRectangle(g, brush, bounds, 12f * u);
		}
		using (Pen pen = new Pen(border, selected ? 1.4f : 1f))
		{
			VisualDrawing.DrawRoundedRectangle(g, pen, bounds, 12f * u);
		}
		if (selected)
		{
			using SolidBrush bar = new SolidBrush(Ui.Accent);
			VisualDrawing.FillRoundedRectangle(g, bar, new RectangleF(bounds.X + 1f, bounds.Y + 16f * u, 3f * u, bounds.Height - 32f * u), 1.5f * u);
		}
		float size = 40f * u;
		RectangleF avatar = new RectangleF(card.X + 18f * u, card.Y + (card.Height - size) / 2f, size, size);
		Color avatarFill = peer.RecentlySeen && !selected ? Ui.AccentSoft : Ui.Surface;
		using (SolidBrush brush = new SolidBrush(avatarFill))
		{
			g.FillEllipse(brush, avatar);
		}
		if (!peer.RecentlySeen || selected)
		{
			using Pen ring = new Pen(peer.RecentlySeen ? Color.FromArgb(110, Ui.Accent) : Ui.Line);
			g.DrawEllipse(ring, avatar.X + 0.5f, avatar.Y + 0.5f, avatar.Width - 1f, avatar.Height - 1f);
		}
		float inset = avatar.Width * 0.27f;
		VisualDrawing.DrawDeviceGlyph(g, RectangleF.Inflate(avatar, -inset, -inset), peer.RecentlySeen ? Ui.Accent : Ui.Muted, phone: false, u);
		float dot = 11f * u;
		RectangleF dotBounds = new RectangleF(avatar.Right - dot + 1f * u, avatar.Bottom - dot + 1f * u, dot, dot);
		using (SolidBrush halo = new SolidBrush(fill))
		{
			g.FillEllipse(halo, RectangleF.Inflate(dotBounds, 2f * u, 2f * u));
		}
		using (SolidBrush dotBrush = new SolidBrush(dotColor))
		{
			g.FillEllipse(dotBrush, dotBounds);
		}
		string timeText = peer.LastSeen?.ToLocalTime().ToString("HH:mm:ss") is string time ? "最近 " + time : "暂无记录";
		int pillTextW = TextRenderer.MeasureText(statusLabel, _pillFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
		int timeW = TextRenderer.MeasureText(timeText, _timeFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
		float pillW = Math.Max(52f * u, pillTextW + 24f * u);
		float columnW = Math.Max(pillW, timeW);
		float columnRight = card.Right - 18f * u;
		float pillH = 22f * u;
		float timeH = _timeFont.Height;
		float columnTop = card.Y + (card.Height - (pillH + 5f * u + timeH)) / 2f;
		RectangleF pill = new RectangleF(columnRight - pillW, columnTop, pillW, pillH);
		using (SolidBrush brush = new SolidBrush(pillFill))
		{
			VisualDrawing.FillRoundedRectangle(g, brush, pill, pillH / 2f);
		}
		TextRenderer.DrawText(g, statusLabel, _pillFont, Rectangle.Round(pill), pillInk, TextFormatFlags.HorizontalCenter | LineFlags);
		TextRenderer.DrawText(g, timeText, _timeFont, Rectangle.Round(new RectangleF(columnRight - columnW, pill.Bottom + 5f * u, columnW, timeH)), Ui.Faint, TextFormatFlags.Right | LineFlags);
		float textX = avatar.Right + 14f * u;
		float textW = Math.Max(1f, columnRight - columnW - 14f * u - textX);
		float nameH = _nameFont.Height;
		float detailH = _detailFont.Height;
		float y = card.Y + (card.Height - (nameH + 3f * u + detailH)) / 2f;
		TextRenderer.DrawText(g, peer.Name, _nameFont, Rectangle.Round(new RectangleF(textX, y, textW, nameH)), Ui.Ink, LineFlags);
		string detail = peer.Address + ":" + peer.Port + "   ·   " + Source(peer);
		TextRenderer.DrawText(g, detail, _detailFont, Rectangle.Round(new RectangleF(textX, y + nameH + 3f * u, textW, detailH)), Ui.Muted, LineFlags);
		if (selected && Focused && ShowFocusCues)
		{
			using Pen focus = new Pen(Color.FromArgb(150, Ui.Accent), 1.2f) { DashStyle = DashStyle.Dot };
			VisualDrawing.DrawRoundedRectangle(g, focus, RectangleF.Inflate(bounds, -3f * u, -3f * u), 10f * u);
		}
	}

	private void DrawScrollbar(Graphics g, float u)
	{
		int max = MaxScroll;
		if (max <= 0)
		{
			return;
		}
		float thumbH = Math.Max(28f * u, (float)Height * Height / ContentHeight);
		float thumbY = (float)_scroll / max * (Height - thumbH);
		RectangleF thumb = new RectangleF(Width - 6f * u, thumbY, 4f * u, thumbH);
		using SolidBrush brush = new SolidBrush(Color.FromArgb(130, Ui.Faint));
		VisualDrawing.FillRoundedRectangle(g, brush, thumb, 2f * u);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_nameFont.Dispose();
			_detailFont.Dispose();
			_pillFont.Dispose();
			_timeFont.Dispose();
		}
		base.Dispose(disposing);
	}
}
