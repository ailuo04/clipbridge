using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class TextBoxShell : Panel
{
	private bool _focused;

	private string _prefix = "";

	private readonly Font _prefixFont = Ui.Font(8.6f, FontStyle.Bold);

	public TextBox Editor { get; }

	// Muted label drawn inside the field before the editor, separated by a hairline.
	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public string Prefix
	{
		get => _prefix;
		set
		{
			_prefix = value;
			PositionEditor();
			Invalidate();
		}
	}

	private int PrefixWidth => string.IsNullOrEmpty(_prefix) ? 0 : TextRenderer.MeasureText(_prefix, _prefixFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 26;

	public TextBoxShell(Font font, bool password = false)
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Height = 40;
		BackColor = Ui.FieldSurface;
		Margin = Padding.Empty;
		Editor = new TextBox
		{
			BorderStyle = BorderStyle.None,
			Font = font,
			BackColor = Ui.FieldSurface,
			ForeColor = Ui.Ink,
			UseSystemPasswordChar = password,
			ShortcutsEnabled = true
		};
		Controls.Add(Editor);
		Editor.Enter += (object? _, EventArgs _) =>
		{
			_focused = true;
			Invalidate();
		};
		Editor.Leave += (object? _, EventArgs _) =>
		{
			_focused = false;
			Invalidate();
		};
		Resize += (object? _, EventArgs _) =>
		{
			PositionEditor();
		};
		PositionEditor();
	}

	private void PositionEditor()
	{
		int y = Math.Max(8, (Height - Editor.PreferredHeight) / 2);
		int left = 13 + PrefixWidth;
		Editor.SetBounds(left, y, Math.Max(10, Width - left - 13), Editor.PreferredHeight);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_prefixFont.Dispose();
		}
		base.Dispose(disposing);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.Clear(Ui.FieldSurface);
		RectangleF bounds = new RectangleF(0.5f, 0.5f, (float)Width - 1.5f, (float)Height - 1.5f);
		using (SolidBrush brush = new SolidBrush(Ui.FieldSurface))
		{
			VisualDrawing.FillRoundedRectangle(e.Graphics, brush, bounds, 10f);
		}
		using Pen pen = new Pen(_focused ? Ui.Accent : Ui.Line, _focused ? 1.5f : 1f);
		VisualDrawing.DrawRoundedRectangle(e.Graphics, pen, bounds, 10f);
		if (!string.IsNullOrEmpty(_prefix))
		{
			int dividerX = 13 + PrefixWidth - 13;
			TextRenderer.DrawText(e.Graphics, _prefix, _prefixFont, new Rectangle(13, 0, dividerX - 13, Height), Ui.Muted, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
			using Pen divider = new Pen(Ui.Line);
			e.Graphics.DrawLine(divider, dividerX, 11, dividerX, Height - 12);
		}
	}
}
