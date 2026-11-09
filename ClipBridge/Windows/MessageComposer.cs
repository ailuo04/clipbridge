using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class MessageComposer : TextBox
{
    public bool IsComposing { get; private set; }
    public event Func<IDataObject, bool>? PasteAttachment;
    public event Action<string>? PasteFailed;
    public void PasteData(IDataObject data)
    {
        if (PasteAttachment?.Invoke(data) == true) return;
        if (data.GetData(DataFormats.UnicodeText) is string text) SelectedText = text;
        else if (data.GetData(DataFormats.Text) is string plain) SelectedText = plain;
    }
    private void PasteClipboard()
    {
        try { if (Clipboard.GetDataObject() is { } data) PasteData(data); }
        catch (Exception e) when (e is ExternalException or System.IO.IOException or ArgumentException) { PasteFailed?.Invoke("暂时无法粘贴，请重试"); }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is (Keys.Control | Keys.V) or (Keys.Shift | Keys.Insert)) { PasteClipboard(); return true; }
        return base.ProcessCmdKey(ref msg,keyData);
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x10D) IsComposing=true;
        if(m.Msg==0x10E) IsComposing=false;
        if(m.Msg == 0x302) { PasteClipboard(); return; }
        base.WndProc(ref m);
    }
}
