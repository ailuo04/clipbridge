using System.Drawing;
using System.Windows.Forms;
namespace ClipBridge.Windows;

internal sealed class DevicesForm : Form
{
    public DevicesView View { get; }
    public DevicesForm(DeviceHub hub) { Text = "ClipBridge · 设备与配对"; Icon = AppIcon.Load(); ClientSize = new Size(640, 640); MinimumSize = new Size(500, 500); StartPosition = FormStartPosition.CenterParent; View = new DevicesView(hub) { Dock = DockStyle.Fill }; Controls.Add(View); }
    public void ApplyTheme() { BackColor = Ui.Canvas; View.ApplyTheme(); }
    private void RefreshRows() => View.RefreshRows();
}
