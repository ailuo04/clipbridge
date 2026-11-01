using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class BrandMark : Control
{
	private readonly Bitmap _image = LoadImage();

	public BrandMark()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Size = new Size(48, 48);
		Margin = Padding.Empty;
		BackColor = Color.Transparent;
		AccessibleName = "ClipBridge";
	}

	internal static Bitmap LoadImage()
	{
		using Stream? stream = typeof(BrandMark).Assembly.GetManifestResourceStream("ClipBridge.AppIcon.ico");
		using Icon icon = stream != null ? new Icon(stream, 128, 128) : AppIcon.Load();
		return icon.ToBitmap();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		float side = Math.Min(ClientSize.Width, ClientSize.Height) - 2f;
		if (side <= 4f)
		{
			return;
		}

		RectangleF tile = new RectangleF((ClientSize.Width - side) / 2f, (ClientSize.Height - side) / 2f, side, side);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using (SolidBrush fill = new SolidBrush(Ui.HeaderRaised))
		using (Pen border = new Pen(Ui.ChromeLine))
		{
			VisualDrawing.FillRoundedRectangle(e.Graphics, fill, tile, side * 0.24f);
			VisualDrawing.DrawRoundedRectangle(e.Graphics, border, tile, side * 0.24f);
		}

		int size = Math.Max(1, (int)Math.Floor(side - 4f));
		e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		e.Graphics.DrawImage(_image, new Rectangle((ClientSize.Width - size) / 2, (ClientSize.Height - size) / 2, size, size));
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_image.Dispose();
		}
		base.Dispose(disposing);
	}
}
