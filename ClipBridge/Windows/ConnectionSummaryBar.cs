using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class ConnectionSummaryBar : Control
{
	private readonly record struct SummaryMetrics(float Scale, float GlyphWidth, float GlyphGap, float HorizontalInset, float SeparatorInset, float SeparatorVerticalInset);

	private readonly Font _glyphFont = new Font("Segoe Fluent Icons", 10f, FontStyle.Regular, GraphicsUnit.Point);

	private readonly Font _textFont = Ui.Font(8.8f);

	private bool _networkAvailable;

	private int _onlineDeviceCount;

	public int StatusCount => 3;

	public bool NetworkAvailable => _networkAvailable;

	public int OnlineDeviceCount => _onlineDeviceCount;

	public ConnectionSummaryBar()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Ui.Surface;
		AccessibleName = "连接状态概览";
		UpdateAccessibleDescription();
	}

	public void SetNetworkAvailable(bool available)
	{
		if (_networkAvailable != available)
		{
			_networkAvailable = available;
			UpdateAccessibleDescription();
			Invalidate();
		}
	}

	public void SetOnlineDeviceCount(int count)
	{
		if (_onlineDeviceCount != count)
		{
			_onlineDeviceCount = count;
			UpdateAccessibleDescription();
			Invalidate();
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.Clear(Ui.Surface);
		using (Pen pen = new Pen(Ui.Line))
		{
			e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
		}
		SummaryMetrics metrics = CalculateRenderMetrics(DeviceDpi);
		DrawStatus(e.Graphics, metrics, 0, "\ue701", _networkAvailable ? "本机网络可用" : "未检测到可用网络", _networkAvailable ? Ui.Success : Ui.Warning);
		DrawStatus(e.Graphics, metrics, 1, "\ue72e", "AES-256-GCM 加密", Ui.Success);
		DrawStatus(e.Graphics, metrics, 2, "\ue772", _onlineDeviceCount + " 台设备在线", (_onlineDeviceCount > 0) ? Ui.Accent : Ui.Muted);
	}

	private void UpdateAccessibleDescription()
	{
		AccessibleDescription = (_networkAvailable ? "本机网络可用" : "本机网络不可用") + "，AES-256-GCM 已启用，" + _onlineDeviceCount + " 台设备在线";
	}

	private static SummaryMetrics CalculateRenderMetrics(int dpi)
	{
		float num = Math.Max(1f, (float)dpi / 96f);
		return new SummaryMetrics(num, 20f * num, 9f * num, 18f * num, 15f * num, 16f * num);
	}

	private void DrawStatus(Graphics graphics, SummaryMetrics metrics, int index, string glyph, string text, Color accent)
	{
		float num = (float)Width / 3f;
		float num2 = num * (float)index;
		if (index > 0)
		{
			using Pen pen = new Pen(Ui.Line);
			graphics.DrawLine(pen, num2, metrics.SeparatorVerticalInset, num2, (float)Height - metrics.SeparatorVerticalInset);
		}
		Size size = TextRenderer.MeasureText(text, _textFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
		float num3 = metrics.GlyphWidth + metrics.GlyphGap + (float)size.Width;
		float num4 = Math.Max(num2 + metrics.HorizontalInset, num2 + (num - num3) / 2f);
		float badge = 26f * metrics.Scale;
		graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
		using (SolidBrush badgeBrush = new SolidBrush(Color.FromArgb(Ui.HeaderIsDark ? 52 : 30, accent)))
		{
			graphics.FillEllipse(badgeBrush, num4 + metrics.GlyphWidth / 2f - badge / 2f, ((float)Height - badge) / 2f, badge, badge);
		}
		TextRenderer.DrawText(graphics, glyph, _glyphFont, new Rectangle((int)num4, 0, (int)Math.Ceiling(metrics.GlyphWidth), Height), accent, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		TextRenderer.DrawText(graphics, text, _textFont, new Rectangle((int)(num4 + metrics.GlyphWidth + metrics.GlyphGap), 0, Math.Max(1, (int)(num2 + num - num4 - metrics.GlyphWidth - metrics.GlyphGap - metrics.HorizontalInset / 2f)), Height), Ui.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_glyphFont.Dispose();
			_textFont.Dispose();
		}
		base.Dispose(disposing);
	}
}
