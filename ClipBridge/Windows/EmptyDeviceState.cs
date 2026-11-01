using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Empty state of the device panel: a radar-like illustration (bridge disc, soft rings,
// two dashed satellites for Windows and Android), a short title and three requirement chips.
internal sealed class EmptyDeviceState : Control
{
	private readonly record struct PanelLayout(PointF Center, RectangleF Title, RectangleF Description, RectangleF Steps, bool ShowDescription, bool ShowSteps, float Scale);

	private static readonly string[] StepMarks = { "1", "2", "+" };

	private static readonly string[] StepTexts = { "同一局域网", "相同安全密钥", "手动添加 IP" };

	private const TextFormatFlags LineFlags = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;

	private readonly Font _titleFont = Ui.Font(13f, FontStyle.Bold);

	private readonly Font _descriptionFont = Ui.Font(9f);

	private readonly Font _labelFont = Ui.Font(8.2f);

	private readonly Font _stepFont = Ui.Font(8.6f);

	private readonly Font _markFont = Ui.Font(7.6f, FontStyle.Bold);

	private readonly Bitmap _appIcon = BrandMark.LoadImage();

	public EmptyDeviceState()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Color.Transparent;
		Font = _labelFont;
		AccessibleName = "设备连接提示";
		AccessibleDescription = "暂无设备。请确认设备在同一局域网、使用相同安全密钥，或手动添加 IP";
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		float dpi = Math.Clamp((float)DeviceDpi / 96f, 1f, 3f);
		if (Width < 300f * dpi || Height < 140f * dpi)
		{
			return;
		}
		PanelLayout layout = CalculateLayout(Width, Height, dpi, _titleFont.GetHeight(e.Graphics), _descriptionFont.GetHeight(e.Graphics), _stepFont.GetHeight(e.Graphics));
		DrawIllustration(e.Graphics, layout.Center, layout.Scale);
		TextRenderer.DrawText(e.Graphics, "等待设备连接", _titleFont, Rectangle.Round(layout.Title), Ui.Ink, TextFormatFlags.HorizontalCenter | LineFlags);
		if (layout.ShowDescription)
		{
			TextRenderer.DrawText(e.Graphics, "同一局域网内的设备会自动出现在这里", _descriptionFont, Rectangle.Round(layout.Description), Ui.Muted, TextFormatFlags.HorizontalCenter | LineFlags);
		}
		if (layout.ShowSteps)
		{
			DrawSteps(e.Graphics, layout.Steps, layout.Scale);
		}
	}

	private static PanelLayout CalculateLayout(float width, float height, float dpi, float titleLine, float descLine, float stepLine)
	{
		// Design units are 1 px at 96 dpi; k shrinks the whole group on small panels.
		float k = Math.Clamp(Math.Min((width - 40f * dpi) / (400f * dpi), height / (330f * dpi)), 0.62f, 1.15f);
		float u = dpi * k;
		float art = 160f * u;
		float titleH = Math.Max(26f * dpi, titleLine + 6f * dpi);
		float descH = Math.Max(20f * dpi, descLine + 4f * dpi);
		float stepsH = Math.Max(28f * dpi, stepLine + 12f * dpi);
		float artGap = 6f * u, descGap = 2f * dpi, stepsGap = 14f * u;
		float baseH = art + artGap + titleH;
		bool showDesc = height >= baseH + descGap + descH + 8f * dpi;
		bool showSteps = showDesc && width >= 400f * dpi && height >= baseH + descGap + descH + stepsGap + stepsH + 8f * dpi;
		float groupH = baseH + (showDesc ? descGap + descH : 0f) + (showSteps ? stepsGap + stepsH : 0f);
		float top = Math.Max(2f * dpi, (height - groupH) * 0.5f);
		PointF center = new PointF(width / 2f, top + art / 2f);
		RectangleF title = new RectangleF(12f * dpi, top + art + artGap, width - 24f * dpi, titleH);
		RectangleF desc = new RectangleF(12f * dpi, title.Bottom + descGap, width - 24f * dpi, descH);
		RectangleF steps = new RectangleF(12f * dpi, desc.Bottom + stepsGap, width - 24f * dpi, stepsH);
		return new PanelLayout(center, title, desc, steps, showDesc, showSteps, u);
	}

	private void DrawIllustration(Graphics g, PointF c, float u)
	{
		bool dark = Ui.HeaderIsDark;
		float[] radii = { 80f * u, 58f * u, 38f * u };
		int[] alphas = { dark ? 14 : 12, dark ? 24 : 20, dark ? 40 : 34 };
		for (int i = 0; i < radii.Length; i++)
		{
			RectangleF ring = new RectangleF(c.X - radii[i], c.Y - radii[i], radii[i] * 2f, radii[i] * 2f);
			using SolidBrush fill = new SolidBrush(Color.FromArgb(alphas[i], Ui.Accent));
			g.FillEllipse(fill, ring);
			if (i < 2)
			{
				using Pen edge = new Pen(Color.FromArgb(dark ? 60 : 52, Ui.Accent), 1f);
				g.DrawEllipse(edge, ring);
			}
		}
		float sat = 24f * u;
		float reach = 104f * u;
		DrawSatellite(g, new PointF(c.X - reach, c.Y), sat, "Windows", phone: false, u);
		DrawSatellite(g, new PointF(c.X + reach, c.Y), sat, "Android", phone: true, u);
		// App icon on a rounded tile at the center of the rings, like the header brand mark.
		float half = 33f * u;
		RectangleF tile = new RectangleF(c.X - half, c.Y - half, half * 2f, half * 2f);
		float radius = tile.Width * 0.24f;
		for (int i = 3; i >= 1; i--)
		{
			using SolidBrush glow = new SolidBrush(Color.FromArgb((dark ? 26 : 20) / i, Ui.Accent));
			VisualDrawing.FillRoundedRectangle(g, glow, RectangleF.Inflate(tile, 3f * i * u, 3f * i * u), radius + 3f * i * u);
		}
		using (SolidBrush fill = new SolidBrush(Ui.HeaderIsDark ? Ui.HeaderRaised : Ui.Surface))
		{
			VisualDrawing.FillRoundedRectangle(g, fill, tile, radius);
		}
		using (Pen border = new Pen(Ui.ChromeLine))
		{
			VisualDrawing.DrawRoundedRectangle(g, border, tile, radius);
		}
		float side = tile.Width - 6f * u;
		g.InterpolationMode = InterpolationMode.HighQualityBicubic;
		g.PixelOffsetMode = PixelOffsetMode.HighQuality;
		g.DrawImage(_appIcon, new RectangleF(c.X - side / 2f, c.Y - side / 2f, side, side));
	}

	private void DrawSatellite(Graphics g, PointF center, float radius, string label, bool phone, float u)
	{
		RectangleF bounds = new RectangleF(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
		using (SolidBrush fill = new SolidBrush(Ui.Surface))
		{
			g.FillEllipse(fill, bounds);
		}
		using (Pen border = new Pen(Color.FromArgb(170, Ui.Faint), 1.3f) { DashStyle = DashStyle.Dash })
		{
			g.DrawEllipse(border, bounds);
		}
		float inset = radius * 0.5f;
		VisualDrawing.DrawDeviceGlyph(g, RectangleF.Inflate(bounds, -inset, -inset), Ui.Muted, phone, u);
		Rectangle caption = Rectangle.Round(new RectangleF(center.X - 44f * u, bounds.Bottom + 4f * u, 88f * u, _labelFont.Height + 2f));
		TextRenderer.DrawText(g, label, _labelFont, caption, Ui.Muted, TextFormatFlags.HorizontalCenter | LineFlags);
	}

	// Three short hints as pills: same LAN, same key, or add an IP by hand.
	private void DrawSteps(Graphics graphics, RectangleF area, float u)
	{
		float chipH = area.Height;
		float markD = chipH * 0.58f;
		float gap = 10f * u;
		float[] widths = new float[StepTexts.Length];
		float total = gap * (StepTexts.Length - 1);
		for (int i = 0; i < StepTexts.Length; i++)
		{
			int textW = TextRenderer.MeasureText(StepTexts[i], _stepFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
			widths[i] = 7f * u + markD + 7f * u + textW + 13f * u;
			total += widths[i];
		}
		if (total > area.Width)
		{
			return;
		}
		float x = area.X + (area.Width - total) / 2f;
		for (int i = 0; i < StepTexts.Length; i++)
		{
			RectangleF chip = new RectangleF(x, area.Y, widths[i], chipH);
			using (SolidBrush fill = new SolidBrush(Ui.FieldSurface))
			{
				VisualDrawing.FillRoundedRectangle(graphics, fill, chip, chipH / 2f);
			}
			using (Pen border = new Pen(Ui.Line))
			{
				VisualDrawing.DrawRoundedRectangle(graphics, border, chip, chipH / 2f);
			}
			RectangleF mark = new RectangleF(chip.X + 7f * u, chip.Y + (chipH - markD) / 2f, markD, markD);
			using (SolidBrush markFill = new SolidBrush(Ui.AccentSoft))
			{
				graphics.FillEllipse(markFill, mark);
			}
			TextRenderer.DrawText(graphics, StepMarks[i], _markFont, Rectangle.Round(mark), Ui.Accent, TextFormatFlags.HorizontalCenter | LineFlags);
			float textX = mark.Right + 7f * u;
			TextRenderer.DrawText(graphics, StepTexts[i], _stepFont, Rectangle.Round(new RectangleF(textX, chip.Y, Math.Max(1f, chip.Right - textX - 6f * u), chipH)), Ui.Muted, LineFlags);
			x += widths[i] + gap;
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_titleFont.Dispose();
			_descriptionFont.Dispose();
			_labelFont.Dispose();
			_stepFont.Dispose();
			_markFont.Dispose();
			_appIcon.Dispose();
		}
		base.Dispose(disposing);
	}
}
