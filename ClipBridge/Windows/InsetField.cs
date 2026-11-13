using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Rounded read-only field backdrop for the local address list. The flat
// variant has no outline and hosts the borderless message editor.
internal sealed class InsetField : Panel
{
	private readonly bool _showAccentRail;
	private readonly bool _flat;
	private bool _highlighted;

	// Accent outline while the hosted editor has focus.
	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public bool Highlighted
	{
		get => _highlighted;
		set { if (_highlighted == value) return; _highlighted = value; Invalidate(); }
	}

	public InsetField(bool showAccentRail = true, bool flat = false)
	{
		_showAccentRail = showAccentRail;
		_flat = flat;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Ui.Surface;
		Padding = new Padding(14, 10, 12, 8);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.Clear(BackColor);
		if (_flat) return;
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		RectangleF bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
		using (SolidBrush fill = new SolidBrush(Ui.FieldSurface))
		{
			VisualDrawing.FillRoundedRectangle(e.Graphics, fill, bounds, 8f);
		}
		using (Pen border = new Pen(_highlighted ? Ui.Accent : Ui.ChromeRaised, _highlighted ? 1.4f : 1f))
		{
			VisualDrawing.DrawRoundedRectangle(e.Graphics, border, bounds, 8f);
		}
		if (_showAccentRail)
		{
			using SolidBrush rail = new SolidBrush(Color.FromArgb(200, Ui.Accent));
			VisualDrawing.FillRoundedRectangle(e.Graphics, rail, new RectangleF(6f, 12f, 3f, 16f), 1.5f);
		}
	}
}
