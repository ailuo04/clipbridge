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

internal sealed record PairingNetworkAddress(string Adapter, IPAddress Address, bool HasGateway)
{
    public override string ToString() => $"{Address} · {Adapter}";
}

// Owns the address selection and QR bitmap so device-list refreshes keep them intact.
internal sealed class PairingQrPanel : Panel
{
    private readonly string _deviceId;
    private readonly Func<PairingNetworkAddress[]> _getAddresses;
    private readonly List<Font> _fonts = new();
    private readonly Label _title;
    private readonly Label _instructions;
    private readonly Label _addressLabel;
    private readonly Label _status;
    private readonly Label _unavailable;
    private readonly ComboBox _addresses = new()
    {
        Name = "PairingQrAddresses", DropDownStyle = ComboBoxStyle.DropDownList,
        AccessibleName = "电脑局域网地址", Margin = Padding.Empty, IntegralHeight = false
    };
    private readonly PictureBox _image = new()
    {
        Name = "PairingQrImage", BackColor = Color.White, SizeMode = PictureBoxSizeMode.Zoom,
        AccessibleName = "电脑配对二维码", Margin = Padding.Empty, TabStop = false
    };
    private string? _addressSignature;
    private string _invitation = "";
    private bool _refreshing;

    public PairingQrPanel(string deviceId, Func<PairingNetworkAddress[]>? getAddresses = null)
    {
        _deviceId = deviceId;
        _getAddresses = getAddresses ?? ReadAddresses;
        Name = "InlinePairingQr";
        Margin = Padding.Empty;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _title = Label("用手机扫码配对", 12f, FontStyle.Bold);
        _instructions = Label("手机和电脑连接同一 Wi-Fi，\n在手机互传中点击“扫码配对”。", 9f);
        _addressLabel = Label("电脑网络地址", 8.3f);
        _status = Label("扫描后，请在两端核对验证码。", 8.3f);
        _status.Name = "PairingQrStatus";
        _unavailable = Label("暂无可用网络\n连接 Wi-Fi 后显示二维码", 9f);
        _unavailable.Name = "PairingQrUnavailable";
        _unavailable.TextAlign = ContentAlignment.MiddleCenter;
        _unavailable.Dock = DockStyle.Fill;
        _unavailable.BackColor = Color.White;
        _image.Controls.Add(_unavailable);
        _addresses.Font = OwnedFont(8.7f);
        _addresses.SelectedIndexChanged += (_, _) => { if (!_refreshing) UpdateCode(); };
        Controls.AddRange(new Control[] { _title, _instructions, _addressLabel, _addresses, _status, _image });
        ApplyTheme();
    }

    private Font OwnedFont(float size, FontStyle style = FontStyle.Regular)
    {
        var font = Ui.Font(size, style);
        _fonts.Add(font);
        return font;
    }

    private Label Label(string text, float size, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text, Font = OwnedFont(size, style), AutoEllipsis = true,
        TextAlign = ContentAlignment.TopLeft, BackColor = Color.Transparent, Margin = Padding.Empty
    };

    private int Pixels(int value) => (int)Math.Round(value * DeviceDpi / 96.0);
    private bool IsWide(int width) => width >= Pixels(460);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, Pixels(IsWide(width) ? 216 : 346));
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_title == null) return;
        int width = ClientSize.Width;
        if (IsWide(width))
        {
            int side = Pixels(208);
            int textWidth = Math.Max(1, width - side - Pixels(24));
            _title.SetBounds(0, Pixels(6), textWidth, Pixels(32));
            _instructions.SetBounds(0, Pixels(48), textWidth, Pixels(52));
            _addressLabel.Visible = true;
            _addressLabel.SetBounds(0, Pixels(113), textWidth, Pixels(20));
            _addresses.SetBounds(0, Pixels(137), textWidth, _addresses.PreferredHeight);
            _status.SetBounds(0, Pixels(178), textWidth, Pixels(36));
            _image.SetBounds(width - side, Pixels(4), side, side);
        }
        else
        {
            int side = Pixels(192);
            _title.SetBounds(0, 0, width, Pixels(28));
            _instructions.SetBounds(0, Pixels(32), width, Pixels(40));
            _image.SetBounds(Math.Max(0, (width - side) / 2), Pixels(76), side, side);
            _addressLabel.Visible = false;
            _addresses.SetBounds(0, Pixels(278), width, _addresses.PreferredHeight);
            _status.SetBounds(0, Pixels(312), width, Pixels(32));
        }
    }

    private static PairingNetworkAddress[] ReadAddresses()
    {
        var result = new List<PairingNetworkAddress>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(adapter =>
            adapter.OperationalStatus == OperationalStatus.Up &&
            adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)))
        {
            try
            {
                var properties = adapter.GetIPProperties();
                bool gateway = properties.GatewayAddresses.Any(item =>
                    item.Address.AddressFamily == AddressFamily.InterNetwork && !item.Address.Equals(IPAddress.Any));
                foreach (var address in properties.UnicastAddresses.Where(item =>
                    item.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(item.Address) && SharedProtocol.IsLocalAddress(item.Address)))
                    result.Add(new PairingNetworkAddress(adapter.Name, address.Address, gateway));
            }
            catch (NetworkInformationException) { }
        }
        return result.ToArray();
    }

    public void RefreshAddresses()
    {
        try
        {
            var options = _getAddresses().Where(option =>
                option.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(option.Address) && SharedProtocol.IsLocalAddress(option.Address))
                .OrderByDescending(option => option.HasGateway).ThenBy(option => option.Adapter)
                .ThenBy(option => option.Address.ToString()).DistinctBy(option => option.Address).ToArray();
            string signature = string.Join("|", options.Select(option => $"{option.Address}:{option.Adapter}:{option.HasGateway}"));
            if (signature == _addressSignature && (_image.Image != null || options.Length == 0)) return;
            string? previous = (_addresses.SelectedItem as PairingNetworkAddress)?.Address.ToString();
            _refreshing = true;
            _addresses.BeginUpdate();
            try
            {
                _addresses.Items.Clear();
                _addresses.Items.AddRange(options);
                int index = Array.FindIndex(options, option => option.Address.ToString() == previous);
                _addresses.SelectedIndex = options.Length == 0 ? -1 : Math.Max(0, index);
                _addresses.Enabled = options.Length > 0;
                _addressSignature = signature;
            }
            finally { _addresses.EndUpdate(); _refreshing = false; }
            UpdateCode();
        }
        catch (Exception error) when (error is NetworkInformationException or SocketException)
        {
            _addressSignature = null;
            _addresses.Items.Clear();
            _addresses.Enabled = false;
            ClearCode("无法读取网络地址，请检查 Wi-Fi 后重新搜索。");
        }
    }

    private void UpdateCode()
    {
        if (_addresses.SelectedItem is not PairingNetworkAddress selected)
        {
            ClearCode("请先连接 Wi-Fi 或热点，二维码会自动显示。");
            return;
        }
        try
        {
            string invitation = PairingInvitationCodec.Format(_deviceId, selected.Address);
            if (_invitation == invitation && _image.Image != null) return;
            using var stream = new MemoryStream(PairingQr.RenderPng(invitation));
            using var decoded = Image.FromStream(stream);
            var next = new Bitmap(decoded);
            var old = _image.Image;
            _image.Image = next;
            _invitation = invitation;
            _unavailable.Visible = false;
            old?.Dispose();
            _status.Text = "扫描后，请在两端核对验证码。";
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException)
        {
            ClearCode("无法生成二维码，请重新搜索后重试。");
        }
    }

    private void ClearCode(string message)
    {
        var old = _image.Image;
        _image.Image = null;
        _invitation = "";
        _unavailable.Visible = true;
        _status.Text = message;
        old?.Dispose();
    }

    public void ApplyTheme()
    {
        BackColor = Ui.Surface;
        _title.ForeColor = Ui.Ink;
        _instructions.ForeColor = _addressLabel.ForeColor = _status.ForeColor = Ui.Muted;
        _addresses.BackColor = Ui.FieldSurface;
        _addresses.ForeColor = Ui.Ink;
        _image.BackColor = _unavailable.BackColor = Color.White;
        _unavailable.ForeColor = Color.FromArgb(88, 101, 120);
        Invalidate(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { var image = _image.Image; _image.Image = null; image?.Dispose(); }
        base.Dispose(disposing);
        if (disposing) foreach (var font in _fonts) font.Dispose();
    }
}
