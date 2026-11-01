using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// A single buffered surface reveals the new page over the old page. Native
// child controls remain intact underneath and are never independently faded.
internal sealed class ThemeTransitionOverlay : Control
{
	private const int WmNcHitTest = 0x0084;
	private const int HtTransparent = -1;
	private const int SourceCopy = 0x00CC0020;
	private const int RegionAnd = 1;
	private const int RegionDifference = 4;
	private const int FrameIntervalMs = 3;
	private System.Threading.Timer? _timer;
	private Bitmap? _from;
	private Bitmap? _to;
	private NativeFrame? _fromNative;
	private NativeFrame? _toNative;
	private PointF _origin;
	private double _progress;
	private long _start;
	private int _generation;
	private int _frameQueued;
	private bool _highResolutionTimer;

	public bool HasFrame => _from != null;

	public ThemeTransitionOverlay()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
		TabStop = false;
		Visible = false;
		AccessibleRole = AccessibleRole.None;
	}

	public Bitmap CaptureCurrentFrame()
	{
		Bitmap image = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
		using Graphics g = Graphics.FromImage(image);
		DrawFrame(g);
		return image;
	}

	// Takes ownership of frame. Paint it before the underlying controls change.
	public void Freeze(Bitmap frame)
	{
		StopTimer();
		SetBounds(0, 0, frame.Width, frame.Height);
		ReleaseFrames();
		_from = frame;
		_fromNative = new NativeFrame(frame);
		_progress = 0;
		Visible = true;
		BringToFront();
		Invalidate();
		Update();
	}

	// Takes ownership of frame.
	public void Reveal(Bitmap frame, Point origin)
	{
		_to?.Dispose();
		_toNative?.Dispose();
		_to = frame;
		_toNative = new NativeFrame(frame);
		_origin = origin;
		_progress = 0;
		_paintedRadius = 0;
		_start = Stopwatch.GetTimestamp();
		int generation = Interlocked.Increment(ref _generation);
		_highResolutionTimer = TimeBeginPeriod(1) == 0;
		_timer = new System.Threading.Timer(_ => QueueFrame(generation), null, 0, FrameIntervalMs);
	}

	private void QueueFrame(int generation)
	{
		if (generation != Volatile.Read(ref _generation) || Interlocked.Exchange(ref _frameQueued, 1) != 0) return;
		try
		{
			BeginInvoke((Action)(() =>
			{
				Interlocked.Exchange(ref _frameQueued, 0);
				if (generation == _generation && !IsDisposed && !Disposing) Advance();
			}));
		}
		catch (InvalidOperationException)
		{
			Interlocked.Exchange(ref _frameQueued, 0);
		}
	}

	private void Advance()
	{
		double elapsed = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
		_progress = VisualMotion.AnimationsEnabled ? Math.Clamp(elapsed / 360.0, 0.0, 1.0) : 1.0;
		if (_progress >= 1.0)
		{
			EndTransition();
			return;
		}
		// Invalidate only the expanding reveal band instead of re-copying the
		// entire window on every frame, especially important when maximized.
		using Region dirty = RevealBand(_paintedRadius, RadiusForProgress(_progress));
		Invalidate(dirty);
		Update();
	}

	private int _paintedRadius;

	private int RadiusForProgress(double progress)
	{
		double farX = Math.Max(_origin.X, Width - _origin.X);
		double farY = Math.Max(_origin.Y, Height - _origin.Y);
		return (int)Math.Ceiling(Math.Sqrt(farX * farX + farY * farY) * VisualMotion.EaseOutCubic(progress));
	}

	private Region RevealBand(int fromRadius, int toRadius)
	{
		int x = (int)Math.Round(_origin.X);
		int y = (int)Math.Round(_origin.Y);
		int outer = Math.Max(fromRadius, toRadius) + 4;
		int inner = Math.Max(0, Math.Min(fromRadius, toRadius) - 4);
		// WinForms double buffering is deliberately disabled here. Minimize
		// the copied area by describing the circular band as short strips.
		Region band = new Region(Rectangle.Empty);
		int top = Math.Max(0, y - outer);
		int bottom = Math.Min(Height, y + outer);
		for (int row = top; row < bottom; row += 8)
		{
			int end = Math.Min(row + 8, bottom);
			int low = row - y, high = end - y;
			int minDistance = low <= 0 && high >= 0 ? 0 : Math.Min(Math.Abs(low), Math.Abs(high));
			int maxDistance = Math.Max(Math.Abs(low), Math.Abs(high));
			int outerX = (int)Math.Ceiling(Math.Sqrt(Math.Max(0, (double)outer * outer - (double)minDistance * minDistance)));
			int innerX = maxDistance < inner ? (int)Math.Floor(Math.Sqrt((double)inner * inner - (double)maxDistance * maxDistance)) : 0;
			void Add(int left, int right)
			{
				left = Math.Clamp(left, 0, Width);
				right = Math.Clamp(right, 0, Width);
				if (right > left) band.Union(new Rectangle(left, row, right - left, end - row));
			}
			if (innerX == 0) Add(x - outerX, x + outerX);
			else
			{
				Add(x - outerX, x - innerX);
				Add(x + innerX, x + outerX);
			}
		}
		return band;
	}

	private void DrawFrame(Graphics g)
	{
		if (_fromNative == null) return;
		int radius = RadiusForProgress(_progress);
		IntPtr target = g.GetHdc();
		try
		{
			if (_toNative == null || radius <= 0)
			{
				BitBlt(target, 0, 0, Width, Height, _fromNative.Dc, 0, 0, SourceCopy);
			}
			else
			{
				int x = (int)Math.Round(_origin.X);
				int y = (int)Math.Round(_origin.Y);
				IntPtr region = CreateEllipticRgn(x - radius, y - radius, x + radius, y + radius);
				if (region == IntPtr.Zero)
				{
					BitBlt(target, 0, 0, Width, Height, _fromNative.Dc, 0, 0, SourceCopy);
				}
				else
				{
					try
					{
						// The old and new images must never be painted over the same
						// pixel in one frame. Otherwise the uncovered controls can
						// briefly show the old theme before the new one is copied.
						int saved = SaveDC(target);
						if (saved == 0)
						{
							BitBlt(target, 0, 0, Width, Height, _fromNative.Dc, 0, 0, SourceCopy);
						}
						else
						{
							ExtSelectClipRgn(target, region, RegionDifference);
							BitBlt(target, 0, 0, Width, Height, _fromNative.Dc, 0, 0, SourceCopy);
							RestoreDC(target, saved);
							saved = SaveDC(target);
							if (saved != 0)
							{
								ExtSelectClipRgn(target, region, RegionAnd);
								BitBlt(target, 0, 0, Width, Height, _toNative.Dc, 0, 0, SourceCopy);
								RestoreDC(target, saved);
							}
						}
					}
					finally
					{
						DeleteObject(region);
					}
				}
			}
		}
		finally
		{
			g.ReleaseHdc(target);
		}

		if (_toNative != null && radius > 0)
		{
			// A single soft stroke keeps the reveal edge understated without
			// redrawing the full page many times for every frame.
			g.SmoothingMode = SmoothingMode.AntiAlias;
			using Pen edge = new Pen(Color.FromArgb(36, Ui.HeaderIsDark ? Color.Black : Color.White), 2f);
			g.DrawEllipse(edge, _origin.X - radius, _origin.Y - radius, radius * 2f, radius * 2f);
		}
	}

	protected override void OnPaintBackground(PaintEventArgs e) { }
	protected override void OnPaint(PaintEventArgs e)
	{
		DrawFrame(e.Graphics);
		_paintedRadius = RadiusForProgress(_progress);
		base.OnPaint(e);
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

	protected override void OnSizeChanged(EventArgs e)
	{
		base.OnSizeChanged(e);
		if (_from != null && _from.Size != ClientSize) EndTransition();
	}

	private void StopTimer()
	{
		Interlocked.Increment(ref _generation);
		_timer?.Dispose();
		_timer = null;
		if (_highResolutionTimer)
		{
			TimeEndPeriod(1);
			_highResolutionTimer = false;
		}
	}

	private void ReleaseFrames()
	{
		_fromNative?.Dispose();
		_toNative?.Dispose();
		_fromNative = null;
		_toNative = null;
		_from?.Dispose();
		_to?.Dispose();
		_from = null;
		_to = null;
	}

	public void EndTransition()
	{
		StopTimer();
		Visible = false;
		ReleaseFrames();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			StopTimer();
			ReleaseFrames();
		}
		base.Dispose(disposing);
	}

	private sealed class NativeFrame : IDisposable
	{
		private IntPtr _bitmap;
		private IntPtr _previous;
		public IntPtr Dc { get; private set; }

		public NativeFrame(Bitmap image)
		{
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

		public void Dispose()
		{
			if (Dc != IntPtr.Zero && _previous != IntPtr.Zero && _previous != new IntPtr(-1)) SelectObject(Dc, _previous);
			if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap);
			if (Dc != IntPtr.Zero) DeleteDC(Dc);
			_bitmap = _previous = IntPtr.Zero;
			Dc = IntPtr.Zero;
		}
	}

	[DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
	[DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
	[DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
	[DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
	[DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int rasterOperation);
	[DllImport("gdi32.dll")] private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);
	[DllImport("gdi32.dll")] private static extern int ExtSelectClipRgn(IntPtr hdc, IntPtr region, int mode);
	[DllImport("gdi32.dll")] private static extern int SaveDC(IntPtr hdc);
	[DllImport("gdi32.dll")] private static extern bool RestoreDC(IntPtr hdc, int saved);
	[DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] private static extern uint TimeBeginPeriod(uint period);
	[DllImport("winmm.dll", EntryPoint = "timeEndPeriod")] private static extern uint TimeEndPeriod(uint period);
}
