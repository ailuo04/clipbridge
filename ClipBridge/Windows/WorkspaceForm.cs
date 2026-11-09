using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClipBridge.Windows.History;

namespace ClipBridge.Windows;

internal sealed class WorkspaceForm : Form
{
    private readonly HistoryStore? _history;
    private readonly string _localId;
    private readonly Func<PeerSnapshot[]> _getPeers;
    private readonly Action _openFolder;
    private readonly Action<string> _revoke;
    private readonly Action _checkUpdate;
    private readonly Action<UiThemeMode> _setTheme;
    private readonly Panel _sidebar = new() { Dock = DockStyle.Fill, Padding = new Padding(14, 22, 14, 14) };
    private readonly FlowLayoutPanel _deviceList = new() { Name = "WorkspaceDevices", Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    private readonly Panel _content = new() { Name = "WorkspaceContent", Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly ModernButton _update = new("检查更新", ButtonTone.Secondary, "\uE895") { Name = "WorkspaceUpdates", Dock = DockStyle.Top, Height = 42 };
    private readonly Dictionary<string, long> _readThrough = new();
    private readonly Dictionary<string, Button> _deviceButtons = new();
    private readonly string? _statePath;
    private readonly Control? _devicesPage;
    private Control? _utilityPage;
    private string _page = "chat";
    private string? _selectedId;
    private bool _refreshing;
    private string _signature = "";
    private bool _exit;
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 800 };
    public ConversationView Conversation { get; }
    public PeerSnapshot? SelectedPeer => Conversation.SelectedPeer;
    public event Action? HiddenToTray;
    public WorkspaceForm(HistoryStore? history, string localId, Func<PeerSnapshot[]> getPeers, Func<PeerSnapshot, string, CancellationToken, Task<bool>> sendText, Func<PeerSnapshot, string[], CancellationToken, Task> sendFiles, Action openFolder, Action<string> revoke, Action checkUpdate, Action<UiThemeMode> setTheme, Control? devicesPage, string? statePath)
    {
        _history = history; _localId = localId; _getPeers = getPeers; _openFolder = openFolder; _revoke = revoke; _checkUpdate = checkUpdate; _setTheme = setTheme; _devicesPage = devicesPage; _statePath = statePath;
        Text = "ClipBridge · 互传"; Icon = AppIcon.Load(); StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(1080, 760); MinimumSize = new Size(820, 620); Font = Ui.Font(9.5f); DoubleBuffered = true; AutoScaleMode = AutoScaleMode.Dpi;
        LoadState();
        var grid = new TableLayoutPanel { Name = "WorkspaceRoot", Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty }; grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 246)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.Controls.Add(_sidebar, 0, 0); grid.Controls.Add(_content, 1, 0); Controls.Add(grid);
        var nav = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty }; nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 74)); nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 166)); _sidebar.Controls.Add(nav);
        var brand = new Label { Text = "⇄  ClipBridge\n    " + Environment.MachineName, Font = Ui.Font(13f, FontStyle.Bold), Dock = DockStyle.Fill, Padding = new Padding(6, 6, 0, 0) }; nav.Controls.Add(brand, 0, 0);
        var add = new ModernButton("添加设备", ButtonTone.Secondary, "\uE710") { Name = "WorkspaceAddDevice", Dock = DockStyle.Top, Height = 42 }; add.Click += (_, _) => ShowDevices(); nav.Controls.Add(add, 0, 1);
        nav.Controls.Add(new Label { Text = "设备会话", Dock = DockStyle.Fill, Padding = new Padding(8, 4, 0, 0), Tag = "muted", Font = Ui.Font(8.5f) }, 0, 2); nav.Controls.Add(_deviceList, 0, 3);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 }; for (int i = 0; i < 3; i++) bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var folder = new ModernButton("接收文件夹", ButtonTone.Secondary, "\uE8B7") { Name = "WorkspaceReceiveFolder", Dock = DockStyle.Fill }; folder.Click += (_, _) => _openFolder(); bottom.Controls.Add(folder, 0, 0);
        var settings = new ModernButton("设置", ButtonTone.Secondary, "\uE713") { Name = "WorkspaceSettings", Dock = DockStyle.Fill }; settings.Click += (_, _) => ShowSettings(); bottom.Controls.Add(settings, 0, 1); bottom.Controls.Add(_update, 0, 2); _update.Click += (_, _) => ShowUpdates(); bottom.Controls.Add(new Label { Text = "● 局域网直连", Tag = "muted", Dock = DockStyle.Fill, Padding = new Padding(8, 8, 0, 0), Font = Ui.Font(8f) }, 0, 3); nav.Controls.Add(bottom, 0, 4);
        Conversation = new(history, localId, getPeers, sendText, sendFiles, openFolder, ShowPeerMenu) { Dock = DockStyle.Fill }; Conversation.SetEmbedded(); _content.Controls.Add(Conversation); _content.Controls.Add(_status);
        if (devicesPage != null) { devicesPage.Dock = DockStyle.Fill; devicesPage.Visible = false; _content.Controls.Add(devicesPage); }
        Conversation.StateChanged += () => { if (!_refreshing) RefreshSidebar(); };
        _deviceList.SizeChanged += (_, _) => { foreach (Control item in _deviceList.Controls) item.Width = Math.Max(150, _deviceList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2); };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveState(); }; Activated += (_, _) => RefreshSidebar(); ApplyTheme(UiThemeMode.System); RefreshWorkspace();
    }
    public void RefreshWorkspace()
    {
        if (IsDisposed || _refreshing) return; _refreshing = true;
        try { var peers = _getPeers(); bool firstConnection = _selectedId == null && peers.Length > 0; if (!peers.Any(x => x.PeerId == _selectedId)) _selectedId = peers.FirstOrDefault()?.PeerId; Conversation.RefreshPeers(peers, _selectedId); if (firstConnection) { _page = "chat"; ShowContent(Conversation); } if (peers.Length == 0 && _page == "chat") ShowDevices(); RefreshSidebar(); }
        finally { _refreshing = false; }
    }
    private IReadOnlyList<HistoryLocalRecord> Recent(string id, long now) { try { return _history?.GetRecent(id, now) ?? Array.Empty<HistoryLocalRecord>(); } catch (Exception e) when (e is IOException or JsonException) { return Array.Empty<HistoryLocalRecord>(); } }
    private void RefreshSidebar()
    {
        var peers = _getPeers(); var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); var rows = new List<(PeerSnapshot Peer, string Text, int Unread)>();
        foreach (var peer in peers)
        {
            var entries = Recent(peer.PeerId, now); if (_page == "chat" && Visible && ContainsFocus && Conversation.IsAtLatest && peer.PeerId == _selectedId) { long read = entries.Where(x => x.Event.SenderDeviceId != _localId).Select(x => x.Event.CreatedUtc).DefaultIfEmpty(0).Max(); if (read > _readThrough.GetValueOrDefault(peer.PeerId)) { _readThrough[peer.PeerId] = read; _saveTimer.Stop(); _saveTimer.Start(); } }
            int unread = entries.Count(x => x.Event.SenderDeviceId != _localId && x.Event.CreatedUtc > _readThrough.GetValueOrDefault(peer.PeerId)); var latest = entries.OrderByDescending(x => x.Event.CreatedUtc).FirstOrDefault()?.Event; string draft = Conversation.DraftPreview(peer.PeerId); string preview = draft.Length > 0 ? "[草稿] " + draft : latest?.Text ?? latest?.Files.FirstOrDefault()?.Name ?? (peer.RecentlySeen ? "设备已连接" : "设备离线"); rows.Add((peer, preview.Replace('\r', ' ').Replace('\n', ' '), unread));
        }
        string signature = Ui.CurrentTheme + ":" + _page + ":" + _selectedId + ":" + string.Join("|", rows.Select(x => x.Peer.PeerId + x.Peer.Name + x.Peer.RecentlySeen + x.Text + x.Unread)); if (signature == _signature) return; _signature = signature;
        _deviceList.SuspendLayout(); try { foreach (Control child in _deviceList.Controls.Cast<Control>().ToArray()) child.Dispose(); _deviceList.Controls.Clear(); _deviceButtons.Clear(); foreach (var row in rows) { var peer = row.Peer; var button = new Button { Name = "WorkspacePeer-" + peer.PeerId, Text = peer.Name + (peer.RecentlySeen ? "  ●" : "  · 离线") + (row.Unread > 0 ? "  (" + row.Unread + ")" : "") + "\n" + row.Text, Height = 76, Width = Math.Max(150, _deviceList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 8, 6, 8), FlatStyle = FlatStyle.Flat, BackColor = _page == "chat" && _selectedId == peer.PeerId ? Ui.AccentSoftSolid : Ui.FieldSurface, ForeColor = Ui.Ink, Margin = new Padding(0, 0, 0, 6), AutoEllipsis = true }; button.FlatAppearance.BorderSize = 0; button.Click += (_, _) => SelectConversation(peer.PeerId); _deviceButtons[peer.PeerId] = button; _deviceList.Controls.Add(button); } } finally { _deviceList.ResumeLayout(); }
    }
    public void SelectConversation(string id) { _selectedId = id; _page = "chat"; ShowContent(Conversation); Conversation.RefreshPeers(_getPeers(), id); RefreshSidebar(); _saveTimer.Stop(); _saveTimer.Start(); }
    public void OpenConversation(string? id = null) { if (id != null || _selectedId != null) SelectConversation(id ?? _selectedId!); else ShowDevices(); ShowControlPanel(); }
    private void ShowContent(Control control) { foreach (Control child in _content.Controls) if (child != _status) child.Visible = ReferenceEquals(child, control); control.BringToFront(); _status.BringToFront(); }
    public void ShowDevices() { _page = "devices"; if (_devicesPage != null) ShowContent(_devicesPage); else ShowUtility("添加设备", "连接同一局域网后，附近设备会出现在这里。"); RefreshSidebar(); }
    private FlowLayoutPanel ShowUtility(string title, string subtitle) { if (_utilityPage != null) { _content.Controls.Remove(_utilityPage); _utilityPage.Dispose(); } var page = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(32), BackColor = Ui.Canvas }; page.Controls.Add(new Label { Text = title, AutoSize = true, Font = Ui.Font(20f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 14), ForeColor = Ui.Ink }); page.Controls.Add(new Label { Text = subtitle, AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(0, 0, 0, 28), ForeColor = Ui.Muted }); _utilityPage = page; _content.Controls.Add(page); ShowContent(page); return page; }
    private void ShowSettings() { _page = "settings"; var page = ShowUtility("设置", "已配对设备自动接收 · 图片、视频与文件夹保持原样"); page.Controls.Add(new Label { Text = "外观主题", AutoSize = true, ForeColor = Ui.Ink }); var theme = new ComboBox { Name = "WorkspaceTheme", DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 }; theme.Items.AddRange(new object[] { "跟随系统", "浅色", "深色" }); theme.SelectedIndex = _themeMode == UiThemeMode.Light ? 1 : _themeMode == UiThemeMode.Dark ? 2 : 0; theme.SelectedIndexChanged += (_, _) => _setTheme(theme.SelectedIndex == 1 ? UiThemeMode.Light : theme.SelectedIndex == 2 ? UiThemeMode.Dark : UiThemeMode.System); page.Controls.Add(theme); page.Controls.Add(new Label { Text = "\n聊天记录保留 7 天\n记录过期不会删除已经接收的本机文件。", AutoSize = true, ForeColor = Ui.Muted }); RefreshSidebar(); }
    private void ShowUpdates() { _page = "updates"; var page = ShowUtility("应用更新", "更新前完成正在进行的传输，保持文件完整。"); page.Controls.Add(new Label { Text = "ClipBridge  " + Application.ProductVersion, AutoSize = true, Font = Ui.Font(13f), ForeColor = Ui.Ink }); var check = new ModernButton("检查更新", ButtonTone.Primary) { Name = "WorkspaceCheckUpdate", Width = 150, Height = 42, Margin = new Padding(0, 24, 0, 0) }; check.Click += (_, _) => _checkUpdate(); page.Controls.Add(check); RefreshSidebar(); }
    private void ShowPeerMenu() { var peer = SelectedPeer; if (peer == null) return; var menu = new ContextMenuStrip(); menu.Items.Add(peer.Name).Enabled = false; menu.Items.Add("删除设备", null, (_, _) => { if (MessageBox.Show(this, "删除“" + peer.Name + "”并取消配对？\n已接收的本机文件会保留。", "删除设备", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return; Conversation.ForgetPeer(peer.PeerId); _revoke(peer.PeerId); _readThrough.Remove(peer.PeerId); RefreshWorkspace(); SaveState(); }); menu.Closed += (_, _) => menu.Dispose(); menu.Show(Cursor.Position); }
    private UiThemeMode _themeMode;
    public void ApplyTheme(UiThemeMode mode) { _themeMode = mode; BackColor = Ui.Canvas; ForeColor = Ui.Ink; _sidebar.BackColor = Ui.FieldSurface; _content.BackColor = Ui.Canvas; _status.ForeColor = Ui.Muted; void Colorize(Control control) { if (control is Label label) label.ForeColor = (string?)label.Tag == "muted" ? Ui.Muted : Ui.Ink; foreach (Control child in control.Controls) Colorize(child); } Colorize(_sidebar); Conversation.ApplyTheme(); if (_devicesPage is DevicesView devices) devices.ApplyTheme(); if (_page == "settings") ShowSettings(); if (_page == "updates") ShowUpdates(); _signature = ""; RefreshSidebar(); Invalidate(true); }
    public void ShowControlPanel() { Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); }
    public void AddEvent(string text) { _status.Text = text; System.Diagnostics.Trace.WriteLine(text); }
    public void SetUpdateCheckState(bool enabled, string? text = null) { _update.Enabled = enabled; _update.Text = text ?? "检查更新"; foreach (var button in _content.Controls.Find("WorkspaceCheckUpdate", true)) button.Enabled = enabled; }
    public void AllowExit() { _exit = true; }
    protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing && !_exit) { e.Cancel = true; Hide(); SaveState(); HiddenToTray?.Invoke(); return; } base.OnFormClosing(e); }
    private sealed record SavedState(string? Selected, Dictionary<string, long> ReadThrough);
    private void LoadState() { if (_statePath == null) return; try { var state = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(_statePath)); _selectedId = state?.Selected; if (state != null) foreach (var pair in state.ReadThrough) _readThrough[pair.Key] = pair.Value; } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { } }
    private void SaveState() { if (_statePath == null) return; try { Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!); string temporary = _statePath + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(new SavedState(_selectedId, _readThrough))); File.Move(temporary, _statePath, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(e.GetType().Name); } }
    protected override void Dispose(bool disposing) { if (disposing) { _saveTimer.Stop(); _saveTimer.Dispose(); SaveState(); } base.Dispose(disposing); }
}
