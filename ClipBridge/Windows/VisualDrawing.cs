using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClipBridge.Windows;

internal static class VisualDrawing
{
	internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
	{
		float num = Math.Max(0f, Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height)));
		GraphicsPath graphicsPath = new GraphicsPath();
		if (num <= 0f)
		{
			graphicsPath.AddRectangle(bounds);
			return graphicsPath;
		}
		RectangleF rect = new RectangleF(bounds.X, bounds.Y, num, num);
		graphicsPath.AddArc(rect, 180f, 90f);
		rect.X = bounds.Right - num;
		graphicsPath.AddArc(rect, 270f, 90f);
		rect.Y = bounds.Bottom - num;
		graphicsPath.AddArc(rect, 0f, 90f);
		rect.X = bounds.X;
		graphicsPath.AddArc(rect, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	internal static void FillRoundedRectangle(Graphics graphics, Brush brush, RectangleF bounds, float radius)
	{
		using GraphicsPath path = RoundedRectangle(bounds, radius);
		graphics.FillPath(brush, path);
	}

	internal static void DrawRoundedRectangle(Graphics graphics, Pen pen, RectangleF bounds, float radius)
	{
		using GraphicsPath path = RoundedRectangle(bounds, radius);
		graphics.DrawPath(pen, path);
	}

	internal static void DrawBridgeGlyph(Graphics graphics, RectangleF bounds, Color color, float width)
	{
		using Pen pen = new Pen(color, width)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round
		};
		float num = bounds.Y + bounds.Height * 0.58f;
		graphics.DrawLine(pen, bounds.X, num, bounds.Right, num);
		graphics.DrawLine(pen, bounds.X + bounds.Width * 0.24f, bounds.Y + bounds.Height * 0.22f, bounds.X + bounds.Width * 0.24f, bounds.Bottom);
		graphics.DrawLine(pen, bounds.Right - bounds.Width * 0.24f, bounds.Y + bounds.Height * 0.22f, bounds.Right - bounds.Width * 0.24f, bounds.Bottom);
		float width2 = bounds.Width * 0.46f;
		graphics.DrawArc(pen, bounds.X + bounds.Width * 0.04f, bounds.Y + bounds.Height * 0.23f, width2, bounds.Height * 0.55f, 180f, 180f);
		graphics.DrawArc(pen, bounds.X + bounds.Width * 0.5f, bounds.Y + bounds.Height * 0.23f, width2, bounds.Height * 0.55f, 180f, 180f);
	}

	internal static void DrawDeviceGlyph(Graphics graphics, RectangleF bounds, Color color, bool phone, float scale)
	{
		using Pen pen = new Pen(color, 1.5f * scale)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round
		};
		if (phone)
		{
			RectangleF body = new RectangleF(bounds.X + bounds.Width * 0.28f, bounds.Y + 1f, bounds.Width * 0.44f, bounds.Height - 2f);
			DrawRoundedRectangle(graphics, pen, body, 3f * scale);
			graphics.DrawLine(pen, body.X + body.Width * 0.35f, body.Bottom - 4f, body.Right - body.Width * 0.35f, body.Bottom - 4f);
		}
		else
		{
			RectangleF screen = new RectangleF(bounds.X + 1f, bounds.Y + 2f, bounds.Width - 2f, bounds.Height * 0.7f);
			DrawRoundedRectangle(graphics, pen, screen, 2f * scale);
			graphics.DrawLine(pen, bounds.X + bounds.Width / 2f, screen.Bottom, bounds.X + bounds.Width / 2f, bounds.Bottom - 2f);
			graphics.DrawLine(pen, bounds.X + bounds.Width * 0.32f, bounds.Bottom - 2f, bounds.Right - bounds.Width * 0.32f, bounds.Bottom - 2f);
		}
	}

	internal static double MoveTowards(double current, double target, double amount)
	{
		if (Math.Abs(target - current) <= amount)
		{
			return target;
		}
		return current + (double)Math.Sign(target - current) * amount;
	}
}
