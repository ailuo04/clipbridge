using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class PairingQrForm : Form
{
    private sealed record AddressOption(string Adapter, IPAddress Address, bool HasGateway)
    {
        public override string ToString() => $"{Address} · {Adapter}";
    }

    private readonly DeviceHub _hub;
    private readonly ComboBox _addresses = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 405 };
    private readonly PictureBox _image = new() { Width = 320, Height = 320, BackColor = Color.White, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly Timer _timer = new() { Interval = 500 };
    private readonly HashSet<string> _existing;

    public PairingQrForm(DeviceHub hub)
    {
        _hub = hub;
        _existing = hub.Pending.Keys.ToHashSet();
        Text = "ClipBridge · 扫码配对";
        Icon = AppIcon.Load();
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(500, 630);
        MinimumSize = new Size(460, 540);
        Font = Ui.Font(10f);
        BackColor = Ui.Canvas;
        ForeColor = Ui.Ink;
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
        Controls.Add(content);
        content.Controls.Add(new Label { Text = "用手机互传扫描此二维码", AutoSize = true, Font = Ui.Font(14f, FontStyle.Bold), Margin = new Padding(3, 6, 3, 12) });
        content.Controls.Add(new Label { Text = "手机和电脑需要连接可互通的同一网络。多张网卡时，请选择手机能访问的地址。", AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(3, 0, 3, 10) });
        _addresses.AccessibleName = "电脑局域网地址";
        _addresses.SelectedIndexChanged += (_, _) => UpdateCode();
        content.Controls.Add(_addresses);
        var refresh = new Button { Text = "刷新网络地址", AutoSize = true, MinimumSize = new Size(120, 36), Margin = new Padding(3, 8, 3, 8) };
        refresh.Click += (_, _) => RefreshAddresses();
        content.Controls.Add(refresh);
        _image.AccessibleName = "电脑配对二维码";
        content.Controls.Add(_image);
        content.Controls.Add(_status);
        content.Controls.Add(new Label { Text = "二维码不含密钥；连接后仍需在手机和电脑上核对验证码。", AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(3, 10, 3, 0) });
        _timer.Tick += (_, _) => { if (_hub.Pending.Keys.Any(session => !_existing.Contains(session))) Close(); };
        Shown += (_, _) => { RefreshAddresses(); _timer.Start(); };
        FormClosed += (_, _) => { _timer.Dispose(); _image.Image?.Dispose(); };
    }

    private void RefreshAddresses()
    {
        string? previous = (_addresses.SelectedItem as AddressOption)?.Address.ToString();
        var result = new List<AddressOption>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)))
            {
                try
                {
                    var properties = adapter.GetIPProperties();
                    bool gateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    foreach (var unicast in properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                    {
                        try { _ = PairingInvitationCodec.Format(_hub.DeviceId, unicast.Address); result.Add(new AddressOption(adapter.Name, unicast.Address, gateway)); }
                        catch (InvalidDataException) { }
                    }
                }
                catch (NetworkInformationException) { }
            }
        }
        catch (NetworkInformationException) { _status.Text = "无法读取网络地址，请检查网络后重试。"; }
        var items = result.OrderByDescending(x => x.HasGateway).ThenBy(x => x.Adapter).ThenBy(x => x.Address.ToString()).ToArray();
        _addresses.Items.Clear();
        _addresses.Items.AddRange(items);
        int index = Array.FindIndex(items, item => item.Address.ToString() == previous);
        if (items.Length > 0) _addresses.SelectedIndex = index >= 0 ? index : 0;
        else { _image.Image?.Dispose(); _image.Image = null; _status.Text = "没有找到可用的局域网地址，请先连接 Wi-Fi 或热点。"; }
    }

    private void UpdateCode()
    {
        if (_addresses.SelectedItem is not AddressOption selected) return;
        try
        {
            byte[] png = PairingQr.RenderPng(PairingInvitationCodec.Format(_hub.DeviceId, selected.Address));
            using var stream = new MemoryStream(png);
            using var decoded = Image.FromStream(stream);
            var next = new Bitmap(decoded);
            var old = _image.Image;
            _image.Image = next;
            old?.Dispose();
            _status.Text = "等待手机扫描；如扫描后无法连接，请检查网络是否允许设备互访。";
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException)
        { _status.Text = "无法生成二维码，请刷新地址后重试。"; }
    }
}