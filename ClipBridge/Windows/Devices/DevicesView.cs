using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class DevicesView : UserControl
{
    private readonly DeviceHub _hub;
    private readonly PairingQrPanel _pairingQr;
    private DateTimeOffset _nextAddressRefresh;
    private readonly AppHeaderSurface _header = new() { Dock = DockStyle.Top, Height = 96, Padding = new Padding(24, 18, 24, 18) };
    private readonly Panel _rows = new()
    {
        Name = "DevicesScroll",
        Dock = DockStyle.Fill,
        AutoScroll = true
    };
    private readonly Panel _content = new()
    {
        Name = "DevicesContent",
        Dock = DockStyle.Top,
        Padding = new Padding(22, 18, 22, 16)
    };
    private readonly Panel _footer = new() { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(24, 9, 24, 9) };
    private readonly Label _status = new()
    {
        Name = "DevicesStatus",
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        Text = "同一 Wi-Fi 或热点内自动发现 · 配对时请核对两端验证码"
    };
    private readonly RoundedSurface _connectCard = new() { Name = "ConnectionCard", Height = 138, Padding = new Padding(18, 14, 18, 20), Margin = new Padding(0, 0, 0, 4) };
    private readonly TableLayoutPanel _connectContent = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Padding = Padding.Empty };
    private readonly TableLayoutPanel _manualArea = new() { ColumnCount = 2, RowCount = 2, Dock = DockStyle.Fill, Visible = false, Margin = Padding.Empty };
    private readonly TextBoxShell _manualInput = new(Ui.Font(9.2f)) { Dock = DockStyle.Fill, Prefix = "IPv4" };
    private readonly FlowLayoutPanel _requests = Section("PairingRequests");
    private readonly FlowLayoutPanel _trusted = Section("TrustedDevices");
    private readonly FlowLayoutPanel _nearby = Section("NearbyDevices");
    private readonly Timer _timer = new() { Interval = 1000 };
    private readonly List<Font> _fonts = new();
    private readonly ToolTip _tips = new();
    private string _signature = "";
    private bool _manualExpanded;
    private bool _layingOut;
    private UiColorTheme _renderedTheme = Ui.CurrentTheme;

    public DevicesView(DeviceHub hub)
    {
        SuspendLayout();
        _hub = hub;
        _pairingQr = new PairingQrPanel(hub.DeviceId) { Dock = DockStyle.Fill };
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "ClipBridge · 设备与配对";

        ClientSize = new Size(640, 640);
        MinimumSize = Size.Empty;

        Font = OwnedFont(9.5f);
        DoubleBuffered = true;
        _status.Font = OwnedFont(8.5f);
        _fonts.Add(_manualInput.Editor.Font);
        _tips.SetToolTip(_status, _status.Text);
        _footer.Controls.Add(_status);
        _footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Ui.Line);
            e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
        };
        BuildHeader();
        BuildConnectionCard();
        _content.Controls.AddRange(new Control[] { _requests, _connectCard, _trusted, _nearby });
        _rows.Controls.Add(_content);
        var workspace = new Panel { Name = "DevicesWorkspace", Dock = DockStyle.Fill };
        workspace.Controls.Add(_rows);
        workspace.Controls.Add(_footer);
        workspace.Controls.Add(_header);
        Controls.Add(workspace);
        _rows.ClientSizeChanged += (_, _) => LayoutRows();
        _timer.Tick += (_, _) =>
        {
            if (!Visible) return;
            if (_renderedTheme != Ui.CurrentTheme) ApplyTheme();
            if (DateTimeOffset.UtcNow >= _nextAddressRefresh) RefreshQrAddresses();
            RefreshRows();
        };
        _hub.Status += OnHubStatus;
        Load += (_, _) => { _hub.RefreshDiscovery(); RefreshQrAddresses(); RefreshRows(); _timer.Start(); };
        ApplyTheme();
        AutoScaleDimensions = new SizeF(96, 96);
        ResumeLayout(true);
    }

    private Font OwnedFont(float size, FontStyle style = FontStyle.Regular)
    {
        var font = Ui.Font(size, style);
        _fonts.Add(font);
        return font;
    }

    private int Pixels(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

    private static FlowLayoutPanel Section(string name) => new()
    {
        Name = name,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = false,
        Margin = Padding.Empty,
        Padding = Padding.Empty
    };

    private Label TextLabel(string text, Font font, bool muted = false) => new()
    {
        Text = text,
        Font = font,
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = Padding.Empty,
        BackColor = Color.Transparent,
        ForeColor = muted ? Ui.Muted : Ui.Ink,
        Tag = muted ? "muted" : null
    };

    private ModernButton ActionButton(string text, string name, Action callback, ButtonTone tone = ButtonTone.Secondary, string glyph = "")
    {
        var button = new ModernButton(text, tone, glyph)
        {
            Name = name,
            AutoSize = false,
            Size = new Size(116, 38),
            Margin = Padding.Empty,
            Anchor = AnchorStyles.Right,
            AccessibleName = text
        };
        button.Click += (_, _) =>
        {
            try { callback(); }
            catch (Exception error) { SetStatus(error.Message); }
        };
        return button;
    }

    private void BuildHeader()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        var tile = new IconTile { Glyph = "", Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        var title = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        title.Controls.Add(TextLabel("设备与配对", OwnedFont(18f, FontStyle.Bold)), 0, 0);
        title.Controls.Add(TextLabel("连接设备，轻松互传", OwnedFont(9f), true), 0, 1);
        var search = ActionButton("重新搜索", "RefreshDiscoveryButton", () =>
        {
            _hub.RefreshDiscovery();
            RefreshQrAddresses();
            SetStatus("正在搜索附近设备，请保持两台设备连接同一 Wi-Fi 或热点");
        }, glyph: "");
        _tips.SetToolTip(search, "切换网络后可立即重新搜索，正在进行的传输会继续。");
        layout.Controls.Add(tile, 0, 0);
        layout.Controls.Add(title, 1, 0);
        layout.Controls.Add(search, 2, 0);
        _header.Controls.Add(layout);
    }

    private void BuildConnectionCard()
    {
        _connectContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 216));
        _connectContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _connectContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _connectContent.Controls.Add(_pairingQr, 0, 0);
        var toggle = ActionButton("通过 IP 连接（备用）", "ManualPairingToggle", () =>
        {
            _manualExpanded = !_manualExpanded;
            _manualArea.Visible = _manualExpanded;
            LayoutRows();
            ((Button)_connectContent.GetControlFromPosition(0, 1)!).Text = _manualExpanded ? "收起 IP 备用连接" : "通过 IP 连接（备用）";
            if (_manualExpanded) _manualInput.Editor.Focus();
        }, ButtonTone.Ghost, "");
        toggle.Width = 206;
        toggle.Height = 28;
        toggle.MinimumSize = new Size(72, 28);
        toggle.Anchor = AnchorStyles.Left;
        toggle.Margin = Padding.Empty;
        _connectContent.Controls.Add(toggle, 0, 1);
        _manualArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _manualArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        _manualArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        _manualArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _manualInput.Margin = new Padding(0, 4, 10, 0);
        _manualInput.Editor.Name = "ManualPairingAddress";
        _manualInput.Editor.PlaceholderText = "例如 192.168.1.8";
        _manualInput.Editor.AccessibleName = "设备局域网 IPv4 地址";
        _manualInput.Editor.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            StartManualPairing();
        };
        var connect = ActionButton("连接此地址", "ManualPairingButton", StartManualPairing, ButtonTone.Primary);
        _manualArea.Controls.Add(_manualInput, 0, 0);
        _manualArea.Controls.Add(connect, 1, 0);
        var hint = TextLabel("无法扫码或搜索不到设备时使用", OwnedFont(8.2f), true);
        _manualArea.Controls.Add(hint, 0, 1);
        _manualArea.SetColumnSpan(hint, 2);
        _connectContent.Controls.Add(_manualArea, 0, 2);
        _connectCard.Controls.Add(_connectContent);
    }

    private void RefreshQrAddresses()
    {
        _pairingQr.RefreshAddresses();
        _nextAddressRefresh = DateTimeOffset.UtcNow.AddSeconds(5);
    }

    private void StartManualPairing()
    {
        string value = _manualInput.Editor.Text.Trim();
        if (!IPAddress.TryParse(value, out var address) || !SharedProtocol.IsLocalAddress(address))
        {
            SetStatus("请输入局域网 IPv4 地址，例如 192.168.1.8");
            _manualInput.Editor.Focus();
            return;
        }
        BeginPairing(new NearbyDevice("", value, "android", address.ToString(), 0, false));
    }

    private void BeginPairing(NearbyDevice peer)
    {
        SetStatus("正在建立配对，请在两端核对验证码");
        _ = _hub.PairAsync(peer);
    }

    private void AddHeading(FlowLayoutPanel section, string title, int count)
    {
        var heading = new FlowLayoutPanel
        {
            Height = Pixels(44),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(2, Pixels(12), 0, 0)
        };
        heading.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = _sectionFont,
            ForeColor = Ui.Ink,
            Margin = new Padding(0, 0, Pixels(10), 0)
        });
        heading.Controls.Add(new CountPill { Count = count, Margin = Padding.Empty });
        section.Controls.Add(heading);
    }

    private Font? _sectionFont;
    private Font? _deviceFont;
    private Font? _detailFont;
    private Font? _codeFont;

    private RoundedSurface Card(int height) => new()
    {
        Height = Pixels(height),
        Padding = new Padding(Pixels(16), Pixels(12), Pixels(16), Pixels(18)),
        Margin = new Padding(0, 0, 0, Pixels(8)),
        CornerRadius = Pixels(14)
    };

    private static string PlatformName(string platform) => platform switch
    {
        "android" => "Android",
        "windows" => "Windows",
        _ => platform
    };

    private void AddDevice(FlowLayoutPanel section, string id, string name, string platform, string address, string state, string action, Action callback, ButtonTone tone)
    {
        var card = Card(88);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Pixels(52)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Pixels(112)));
        layout.Controls.Add(new IconTile
        {
            Glyph = platform == "android" ? "" : "",
            Size = new Size(Pixels(40), Pixels(40)),
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty
        }, 0, 0);
        var labels = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 0, Pixels(10), 0) };
        labels.RowStyles.Add(new RowStyle(SizeType.Percent, 53));
        labels.RowStyles.Add(new RowStyle(SizeType.Percent, 47));
        var title = TextLabel(name, _deviceFont!);
        _tips.SetToolTip(title, name);
        labels.Controls.Add(title, 0, 0);
        var detail = TextLabel(state + "  ·  " + PlatformName(platform) + (string.IsNullOrEmpty(address) ? "" : "  ·  " + address), _detailFont!, true);
        if (state == "已连接") { detail.Tag = "success"; detail.ForeColor = Ui.SuccessText; }
        _tips.SetToolTip(detail, detail.Text);
        labels.Controls.Add(detail, 0, 1);
        layout.Controls.Add(labels, 1, 0);
        var button = ActionButton(action, (tone == ButtonTone.Primary ? "PairDevice-" : "RevokeDevice-") + id, callback, tone);
        button.Size = new Size(Pixels(104), Pixels(34));
        button.AccessibleName = action + " " + name;
        layout.Controls.Add(button, 2, 0);
        card.Controls.Add(layout);
        section.Controls.Add(card);
    }

    private void AddEmpty(FlowLayoutPanel section, string title, string detail, string glyph)
    {
        var card = Card(96);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Pixels(52)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new IconTile { Glyph = glyph, Size = new Size(Pixels(40), Pixels(40)), Anchor = AnchorStyles.Left, Margin = Padding.Empty }, 0, 0);
        var labels = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        labels.RowStyles.Add(new RowStyle(SizeType.Absolute, Pixels(26)));
        labels.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        labels.Controls.Add(TextLabel(title, _deviceFont!), 0, 0);
        labels.Controls.Add(TextLabel(detail, _detailFont!, true), 0, 1);
        layout.Controls.Add(labels, 1, 0);
        card.Controls.Add(layout);
        section.Controls.Add(card);
    }

    private void AddRequest(PairingRequest request, DateTimeOffset now)
    {
        bool expired = request.Expires <= now;
        bool decided = request.Decision.Task.IsCompleted;
        bool accepted = decided && request.Decision.Task.IsCompletedSuccessfully && request.Decision.Task.Result;
        string state = !decided ? expired ? "请求已过期" : "待确认" : accepted ? "本机已确认" : "配对已拒绝";
        string headline = accepted ? "等待另一台设备确认" : request.Name;
        var card = Card(226);
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, Pixels(64)));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, Pixels(24)));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, Pixels(40)));
        var status = new GlassStatusCapsule { Name = "PairingRequestStatusCapsule", Dock = DockStyle.Fill, Margin = Padding.Empty };
        status.SetContent(accepted ? GlassStatusKind.Info : GlassStatusKind.Warning, state, headline);
        content.Controls.Add(status, 0, 0);
        content.Controls.Add(TextLabel("请核对两端数字一致 · " + request.Address + " · " + (expired ? "已过期，请重新发起" : "有效至 " + request.Expires.ToLocalTime().ToString("HH:mm:ss")), _detailFont!, true), 0, 1);
        var code = TextLabel(request.Code.Length == 6 ? request.Code.Insert(3, " ") : request.Code, _codeFont!);
        code.TextAlign = ContentAlignment.MiddleCenter;
        code.ForeColor = Ui.Accent;
        code.Tag = "accent";
        content.Controls.Add(code, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, Pixels(2), 0, 0) };
        if (!decided)
        {
            var approve = ActionButton("确认一致", "ConfirmPairing-" + request.Session, () => { _hub.Decide(request, true); RefreshRows(); }, ButtonTone.Primary);
            var reject = ActionButton("拒绝配对", "RejectPairing-" + request.Session, () => { _hub.Decide(request, false); RefreshRows(); }, ButtonTone.Danger);
            approve.Enabled = reject.Enabled = !expired;
            approve.Margin = new Padding(0, 0, Pixels(8), 0);
            actions.Controls.Add(approve);
            actions.Controls.Add(reject);
        }
        else
        {
            var message = TextLabel(accepted ? "等待对方完成确认…" : "可重新发起配对", _detailFont!, true);
            message.AutoSize = true;
            message.Dock = DockStyle.None;
            message.Margin = new Padding(0, Pixels(8), 0, 0);
            actions.Controls.Add(message);
        }
        content.Controls.Add(actions, 0, 3);
        card.Controls.Add(content);
        _requests.Controls.Add(card);
    }
    internal void RefreshRows()
    {
        var nearby = _hub.Devices();
        var requests = _hub.Pending.Values.OrderBy(request => request.Session).ToArray();
        var trusted = _hub.Trusted();
        var now = DateTimeOffset.UtcNow;
        string signature = JsonSerializer.Serialize(new
        {
            Nearby = nearby.Select(peer => new { peer.Id, peer.Name, peer.Platform, peer.Address, peer.Authenticated }),
            Trusted = trusted.Select(device => new { device.Id, device.Name, device.Platform, device.Address, device.Active }),
            Requests = requests.Select(request => new
            {
                request.Session,
                request.Id,
                request.Name,
                request.Address,
                request.Code,
                request.Expires,
                Expired = request.Expires <= now,
                Decided = request.Decision.Task.IsCompleted
            })
        });
        if (signature == _signature) return;
        _sectionFont ??= OwnedFont(11f, FontStyle.Bold);
        _deviceFont ??= OwnedFont(10.5f, FontStyle.Bold);
        _detailFont ??= OwnedFont(8.5f);
        _codeFont ??= OwnedFont(26f, FontStyle.Bold);
        int scroll = -_rows.AutoScrollPosition.Y;
        _rows.SuspendLayout();
        try
        {
            foreach (var section in new[] { _requests, _trusted, _nearby })
            {
                section.SuspendLayout();
                foreach (Control row in section.Controls.Cast<Control>().ToArray()) row.Dispose();
            }
            _requests.Visible = requests.Length > 0;
            if (requests.Length > 0) AddHeading(_requests, "等待配对确认", requests.Length);
            foreach (var request in requests) AddRequest(request, now);
            AddHeading(_trusted, "我的设备", trusted.Length);
            foreach (var device in trusted)
            {
                var online = nearby.FirstOrDefault(peer => peer.Id == device.Id && peer.Authenticated);
                string state = online != null ? "已连接" : device.Active ? "离线" : "配对恢复中";
                AddDevice(_trusted, device.Id, device.Name, device.Platform, online?.Address ?? "", state, "取消配对", () =>
                {
                    _hub.Revoke(device.Id);
                    SetStatus("已取消与 " + device.Name + " 的配对");
                    RefreshRows();
                }, ButtonTone.Ghost);
            }
            if (trusted.Length == 0) AddEmpty(_trusted, "还没有配对设备", "用手机扫码，或在附近设备中选择一台开始配对。", "");
            var unpaired = nearby.Where(peer => trusted.All(device => device.Id != peer.Id)).ToArray();
            AddHeading(_nearby, "附近设备", unpaired.Length);
            foreach (var peer in unpaired)
                AddDevice(_nearby, peer.Id, peer.Name, peer.Platform, peer.Address, "未配对", "开始配对", () => BeginPairing(peer), ButtonTone.Primary);
            if (unpaired.Length == 0) AddEmpty(_nearby, "正在搜索附近设备…", "连接同一 Wi-Fi 或热点，并在另一台设备上打开互传。", "");
            _signature = signature;
        }
        finally
        {
            foreach (var section in new[] { _requests, _trusted, _nearby }) section.ResumeLayout(true);
            _rows.ResumeLayout(true);
            LayoutRows();
            _rows.AutoScrollPosition = new Point(0, scroll);
        }
    }

    private void LayoutRows()
    {
        if (_layingOut || IsDisposed) return;
        _layingOut = true;
        try
        {
            // The second pass accounts for a vertical scrollbar appearing or disappearing.
            for (int pass = 0; pass < 2; pass++)
            {
                _rows.SuspendLayout();
                int width = Math.Max(Pixels(100), _rows.ClientSize.Width - _content.Padding.Horizontal);
                try
                {
                    _connectCard.Width = width;
                    int qrHeight = _pairingQr.GetPreferredSize(new Size(Math.Max(1, width - _connectCard.Padding.Horizontal), 0)).Height;
                    _connectContent.RowStyles[0].Height = qrHeight;
                    _connectContent.RowStyles[2].Height = _manualExpanded ? Pixels(64) : 0;
                    _connectCard.Height = qrHeight + Pixels(30 + (_manualExpanded ? 64 : 0)) + _connectCard.Padding.Vertical;
                    foreach (var section in new[] { _requests, _trusted, _nearby })
                    {
                        section.Width = width;
                        foreach (Control row in section.Controls) row.Width = Math.Max(1, width - row.Margin.Horizontal);
                        section.Height = section.Controls.Cast<Control>().Sum(row => row.Height + row.Margin.Vertical);
                    }
                    int y = _content.Padding.Top;
                    foreach (Control row in _content.Controls)
                    {
                        if (row == _requests && row.Controls.Count == 0) continue;
                        row.Location = new Point(_content.Padding.Left, y);
                        y += row.Height + row.Margin.Bottom;
                    }
                    _content.Height = y + _content.Padding.Bottom;
                }
                finally { _rows.ResumeLayout(true); }
                if (width == Math.Max(Pixels(100), _rows.ClientSize.Width - _content.Padding.Horizontal)) break;
            }
        }
        finally { _layingOut = false; }
    }

    private void SetStatus(string message)
    {
        _status.Text = message;
        _tips.SetToolTip(_status, message);
    }

    private void OnHubStatus(string message)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try
        {
            if (InvokeRequired) BeginInvoke((Action)(() => { if (!IsDisposed && !Disposing) SetStatus(message); }));
            else SetStatus(message);
        }
        catch (InvalidOperationException) { }
    }

    public void ApplyTheme()
    {
        _renderedTheme = Ui.CurrentTheme;
        BackColor = Ui.Canvas;
        ForeColor = Ui.Ink;
        void Recolor(Control control, Color background)
        {
            if (control is PairingQrPanel qrPanel) { qrPanel.ApplyTheme(); return; }
            Color childBackground = background;
            if (control is RoundedSurface surface)
            {
                surface.FillColor = Ui.Surface;
                surface.BorderColor = Ui.Line;
                childBackground = Ui.Surface;
            }
            else if (control is TextBoxShell || control is TextBox) control.BackColor = childBackground = Ui.FieldSurface;
            else if (control is IconTile icon) icon.SurfaceColor = childBackground;
            else if (control is not ModernButton && control is not IconTile && control is not CountPill)
                control.BackColor = background;
            control.ForeColor = (control.Tag as string) switch
            {
                "muted" => Ui.Muted,
                "accent" => Ui.Accent,
                "success" => Ui.SuccessText,
                _ => Ui.Ink
            };
            foreach (Control child in control.Controls) Recolor(child, childBackground);
            control.Invalidate();
        }
        foreach (Control control in Controls) Recolor(control, Ui.Canvas);
        Recolor(_header, Ui.Header);
        Recolor(_footer, Ui.Surface);
        LayoutRows();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _hub.Status -= OnHubStatus;
            _tips.Dispose();
        }
        base.Dispose(disposing);
        if (disposing) foreach (var font in _fonts) font.Dispose();
    }
}
