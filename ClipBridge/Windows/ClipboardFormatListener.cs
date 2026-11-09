using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class ClipboardFormatListener : NativeWindow, IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private readonly Action _changed;
    private bool _registered;
    public ClipboardFormatListener(Action changed)
    {
        _changed = changed;
        CreateHandle(new CreateParams { Caption = "ClipBridge clipboard change listener" });
        _registered = AddClipboardFormatListener(Handle);
        if (!_registered) { DestroyHandle(); throw new InvalidOperationException("无法开启剪贴板监听"); }
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmClipboardUpdate) _changed();
        base.WndProc(ref message);
    }
    public void Dispose()
    {
        if (_registered) { RemoveClipboardFormatListener(Handle); _registered = false; }
        DestroyHandle();
    }
    [DllImport("user32.dll", SetLastError=true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
