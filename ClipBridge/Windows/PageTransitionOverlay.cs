using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Covers the workspace content area while pages switch. The old page stays
// frozen on screen, the new page is laid out and painted underneath, and the
// overlay cross-fades between two snapshots before handing over in one redraw.
internal sealed class PageTransitionOverlay : Control
{
	private const int WmNcHitTest = 0x0084;
	private const int HtTransparent = -1;
	private const int SourceCopy = 0x00CC0020;
	private const int WmSetRedraw = 0x000B;
	private const uint RdwInvalidate = 0x0001, RdwErase = 0x0004, RdwAllChildren = 0x0080, RdwUpdateNow = 0x0100, RdwFrame = 0x0400;
	private const double DurationMs = 240.0;
	private const double FadeOutShare = 0.4;
	private const double FadeInStart = 0.3;
	private const float SlideDistance = 10f;
	private readonly System.Windows.Forms.Timer _timer = new() { Interval = 8 };
	private NativeFrame? _canvas;
	private NativeFrame? _from;
	private NativeFrame? _to;
	private NativeFrame? _compose;
	private long _start;
	private double _progress;
	private Control? _revealTarget;

	public bool Running => _from != null;

	public PageTransitionOverlay()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
		TabStop = false;
		Visible = false;
		AccessibleRole = AccessibleRole.None;
		_timer.Tick += (_, _) => Advance();
	}

	// Returns what is currently visible in the covered area: the in-flight
	// composite when a transition is running, otherwise the supplied capture.
	public Bitmap? CaptureRunningFrame()
	{
		if (_compose == null || _from == null) return null;
		ComposeFrame();
		return _compose.ToBitmap();
	}

	// Takes ownership of frame and paints it immediately so the page below can
	// change without any intermediate state reaching the screen.
	public void Freeze(Bitmap frame, Rectangle bounds, Color background)
	{
		_timer.Stop();
		ResumeTarget();
		ReleaseFrames();
		Bounds = bounds;
		_from = new NativeFrame(frame);
		_compose = new NativeFrame(frame);
		_canvas = SolidFrame(background);
		frame.Dispose();
		_progress = 0;
		Visible = true;
		BringToFront();
		Update();
	}

	// Takes ownership of frame.
	public void Play(Bitmap frame, Control revealTarget)
	{
		_to?.Dispose();
		_to = new NativeFrame(frame);
		frame.Dispose();
		_progress = 0;
		_start = Stopwatch.GetTimestamp();
		if (!VisualMotion.AnimationsEnabled) { Finish(); return; }
		// The live page sits under the overlay for the whole animation. Stop it
		// from repainting until hand-off so frames only cost the overlay blit.
		if (revealTarget is { IsHandleCreated: true, IsDisposed: false })
		{
			SendMessage(revealTarget.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
			_revealTarget = revealTarget;
		}
		_timer.Start();
	}

	private void Advance()
	{
		double elapsed = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
		_progress = Math.Clamp(elapsed / DurationMs, 0.0, 1.0);
		if (_progress >= 1.0) { Finish(); return; }
		Invalidate();
		Update();
	}

	// Re-enables painting of the page under the overlay. Nothing reaches the
	// screen yet because the opaque overlay still covers that area.
	private Control? ResumeTarget()
	{
		Control? target = _revealTarget;
		_revealTarget = null;
		if (target is not { IsHandleCreated: true, IsDisposed: false }) return null;
		SendMessage(target.Handle, WmSetRedraw, (IntPtr)1, IntPtr.Zero);
		return target;
	}

	// Ends any running transition immediately and shows the real page. The
	// owning form paints composited, so the page appears in a single frame.
	public void Finish()
	{
		_timer.Stop();
		Control? target = ResumeTarget();
		if (!Visible && _from == null) return;
		Visible = false;
		ReleaseFrames();
		if (target != null)
			RedrawWindow(target.Handle, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwFrame | RdwAllChildren | RdwUpdateNow);
		Finished?.Invoke();
	}

	// Raised after the overlay has handed over to the live page.
	public event Action? Finished;

	private void ComposeFrame()
	{
		if (_from == null || _compose == null) return;
		BitBlt(_compose.Dc, 0, 0, _compose.Width, _compose.Height, _from.Dc, 0, 0, SourceCopy);
		if (_to == null || _canvas == null) return;
		// Fade through the page background: the old page dissolves first, then
		// the new one rises in, so two layouts never overlap mid-transition.
		double fadeOut = Math.Clamp(_progress / FadeOutShare, 0.0, 1.0);
		if (fadeOut > 0)
			AlphaBlend(_compose.Dc, 0, 0, _compose.Width, _compose.Height, _canvas.Dc, 0, 0, 1, 1, Blend(VisualMotion.EaseOutCubic(fadeOut)));
		double fadeIn = Math.Clamp((_progress - FadeInStart) / (1.0 - FadeInStart), 0.0, 1.0);
		if (fadeIn <= 0) return;
		double eased = VisualMotion.EaseOutCubic(fadeIn);
		int offset = (int)Math.Round(SlideDistance * (1.0 - eased));
		int height = Math.Min(_to.Height, _compose.Height - offset);
		if (height > 0)
			AlphaBlend(_compose.Dc, 0, offset, _to.Width, height, _to.Dc, 0, 0, _to.Width, height, Blend(eased));
	}

	private static BlendFunction Blend(double opacity) => new()
	{
		BlendOp = 0, BlendFlags = 0, AlphaFormat = 0,
		SourceConstantAlpha = (byte)Math.Round(255 * Math.Clamp(opacity, 0.0, 1.0))
	};

	private static NativeFrame SolidFrame(Color color)
	{
		using var pixel = new Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
		pixel.SetPixel(0, 0, Color.FromArgb(255, color));
		return new NativeFrame(pixel);
	}

	protected override void OnPaintBackground(PaintEventArgs e) { }

	protected override void OnPaint(PaintEventArgs e)
	{
		if (_compose == null) return;
		ComposeFrame();
		IntPtr target = e.Graphics.GetHdc();
		try { BitBlt(target, 0, 0, _compose.Width, _compose.Height, _compose.Dc, 0, 0, SourceCopy); }
		finally { e.Graphics.ReleaseHdc(target); }
	}

	protected override void WndProc(ref Message message)
	{
		if (message.Msg == WmNcHitTest)
		{
			message.Result = (IntPtr)HtTransparent;
			return;
		}
		base.WndProc(ref message);
	}

	private void ReleaseFrames()
	{
		_from?.Dispose();
		_to?.Dispose();
		_compose?.Dispose();
		_canvas?.Dispose();
		_from = _to = _compose = _canvas = null;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_timer.Stop();
			_timer.Dispose();
			ResumeTarget();
			ReleaseFrames();
		}
		base.Dispose(disposing);
	}

	private sealed class NativeFrame : IDisposable
	{
		private IntPtr _bitmap;
		private IntPtr _previous;
		public IntPtr Dc { get; private set; }
		public int Width { get; }
		public int Height { get; }

		public NativeFrame(Bitmap image)
		{
			Width = image.Width;
			Height = image.Height;
			Dc = CreateCompatibleDC(IntPtr.Zero);
			if (Dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
			try
			{
				_bitmap = image.GetHbitmap();
				_previous = SelectObject(Dc, _bitmap);
				if (_previous == IntPtr.Zero || _previous == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		public Bitmap ToBitmap() => Image.FromHbitmap(_bitmap);

		public void Dispose()
		{
			if (Dc != IntPtr.Zero && _previous != IntPtr.Zero && _previous != new IntPtr(-1)) SelectObject(Dc, _previous);
			if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap);
			if (Dc != IntPtr.Zero) DeleteDC(Dc);
			_bitmap = _previous = IntPtr.Zero;
			Dc = IntPtr.Zero;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct BlendFunction
	{
		public byte BlendOp;
		public byte BlendFlags;
		public byte SourceConstantAlpha;
		public byte AlphaFormat;
	}

	[DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
	[DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
	[DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
	[DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
	[DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int rasterOperation);
	[DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
	[DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr update, IntPtr region, uint flags);
	[DllImport("msimg32.dll")] private static extern bool AlphaBlend(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, BlendFunction blend);
}
