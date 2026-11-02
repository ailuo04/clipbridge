using System;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class ManualSendForm : Form
{
    private sealed record Target(PeerSnapshot Peer) { public override string ToString() => Peer.Name + " · " + Peer.Address; }
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ComboBox _targets = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _send = new() { Text = "确认发送", AutoSize = true };
    private bool _busy;
    public ManualSendForm(PeerSnapshot[] peers, string title, string preview, Func<IPAddress, CancellationToken, Task> send)
    {
        Text = title; Font = Ui.Font(10f); Size = new Size(600, 440); MinimumSize = new Size(460, 340);
        StartPosition = FormStartPosition.CenterParent; BackColor = Ui.Canvas; ForeColor = Ui.Ink;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        foreach (var peer in peers) _targets.Items.Add(new Target(peer));
        if (peers.Length > 0) _targets.SelectedIndex = 0;
        var text = new TextBox { Text = preview, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Ui.FieldSurface, ForeColor = Ui.Ink };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "取消 / 关闭", AutoSize = true }; cancel.Click += (_, _) => Close();
        buttons.Controls.Add(cancel); buttons.Controls.Add(_send);
        layout.Controls.Add(_targets, 0, 0); layout.Controls.Add(text, 0, 1); layout.Controls.Add(_status, 0, 2); layout.Controls.Add(buttons, 0, 3); Controls.Add(layout);
        _send.Enabled = peers.Length > 0; _status.Text = peers.Length == 0 ? "请先在控制面板中添加设备 IP 或等待设备发现" : "仅向选中的设备发送；可以关闭窗口取消当前操作";
        _send.Click += async (_, _) =>
        {
            if (_busy || _targets.SelectedItem is not Target target) return;
            _busy = true; _send.Enabled = false; _targets.Enabled = false; _status.Text = "正在发送…";
            try { await send(IPAddress.Parse(target.Peer.Address), _lifetime.Token); if (!IsDisposed) _status.Text = "已发送，请在目标设备查看接收结果"; }
            catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "已取消"; }
            catch (Exception ex) { if (!IsDisposed) _status.Text = ex.Message; }
            finally { _busy = false; if (!IsDisposed) { _send.Enabled = true; _targets.Enabled = true; } }
        };
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { _lifetime.Cancel(); base.OnFormClosed(e); }
}
