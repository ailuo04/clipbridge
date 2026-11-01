using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class WindowsClipboardListener : NativeWindow, IDisposable
{
	private const int WmClipboardUpdate = 797;

	private readonly Action<string> _onTextChanged;

	private readonly Func<bool> _canRead;

	private readonly System.Windows.Forms.Timer _readTimer = new System.Windows.Forms.Timer
	{
		Interval = 50
	};

	private string? _suppressedLocalText;

	private int _readAttempts;

	private bool _disposed;

	public event Action? ReadFailed;

	public WindowsClipboardListener(Action<string> onTextChanged, Func<bool> canRead)
	{
		_onTextChanged = onTextChanged;
		_canRead = canRead;
		_readTimer.Tick += (object? _, EventArgs _) =>
		{
			ReadClipboard();
		};
		CreateHandle(new CreateParams
		{
			Caption = "ClipBridge.ClipboardListener",
			Parent = new IntPtr(-3)
		});
		if (!AddClipboardFormatListener(Handle))
		{
			DestroyHandle();
			throw new InvalidOperationException("Could not register the Windows clipboard listener.");
		}
	}

	protected override void WndProc(ref Message message)
	{
		if (message.Msg == 797 && _canRead())
		{
			_readAttempts = 0;
			_readTimer.Stop();
			_readTimer.Start();
		}
		base.WndProc(ref message);
	}

	private void ReadClipboard()
	{
		_readTimer.Stop();
		if (_disposed || !_canRead())
		{
			return;
		}
		try
		{
			if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
			{
				string text = Clipboard.GetText(TextDataFormat.UnicodeText);
				if (!string.IsNullOrEmpty(text) && !string.Equals(Interlocked.Exchange(ref _suppressedLocalText, null), text, StringComparison.Ordinal))
				{
					_onTextChanged(text);
				}
			}
			else
			{
				_onTextChanged(string.Empty);
			}
		}
		catch (ExternalException)
		{
			if (++_readAttempts < 6)
			{
				_readTimer.Start();
			}
			else
			{
				ReadFailed?.Invoke();
			}
		}
	}

	public void SuppressNextText(string text)
	{
		Interlocked.Exchange(ref _suppressedLocalText, text);
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_readTimer.Dispose();
			RemoveClipboardFormatListener(Handle);
			DestroyHandle();
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool AddClipboardFormatListener(nint hwnd);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool RemoveClipboardFormatListener(nint hwnd);
}
