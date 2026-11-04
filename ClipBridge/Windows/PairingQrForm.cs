using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// Retained for callers that host pairing in a standalone window.
internal sealed class PairingQrForm : Form
{
    private readonly PairingQrPanel _qr;
    private readonly Timer _timer = new() { Interval = 1000 };

    public PairingQrForm(DeviceHub hub)
    {
        Text = "ClipBridge · 扫码配对";
        Icon = AppIcon.Load();
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(680, 340);
        MinimumSize = new Size(500, 390);
        BackColor = Ui.Canvas;
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), AutoScroll = true };
        _qr = new PairingQrPanel(hub.DeviceId) { Dock = DockStyle.Top };
        void LayoutQr() => _qr.Height = _qr.GetPreferredSize(new Size(content.ClientSize.Width - content.Padding.Horizontal, 0)).Height;
        content.Controls.Add(_qr);
        Controls.Add(content);
        content.ClientSizeChanged += (_, _) => LayoutQr();
        var existing = hub.Pending.Keys.ToHashSet();
        _timer.Tick += (_, _) => { if (hub.Pending.Keys.Any(session => !existing.Contains(session))) Close(); };
        Shown += (_, _) => { LayoutQr(); _qr.RefreshAddresses(); _timer.Start(); };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
