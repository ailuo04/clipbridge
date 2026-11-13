using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
    private readonly SidebarSurface _sidebar = new() { Dock = DockStyle.Fill, Padding = new Padding(14, 22, 14, 14) };
    private readonly FlowLayoutPanel _deviceList = new() { Name = "WorkspaceDevices", Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    private readonly Panel _content = new() { Name = "WorkspaceContent", Dock = DockStyle.Fill };
    private readonly StatusStripLabel _status = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Padding = new Padding(16, 1, 12, 0), Font = Ui.Font(8.5f) };
    private readonly ModernButton _folder = new("接收文件夹", ButtonTone.Ghost, "\uE8B7") { Name = "WorkspaceReceiveFolder", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = "打开接收文件夹" };
    private readonly ModernButton _settings = new("设置", ButtonTone.Ghost, "\uE713") { Name = "WorkspaceSettings", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = "应用设置" };
    private readonly ModernButton _update = new("当前为最新版", ButtonTone.Ghost) { Name = "WorkspaceUpdates", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, Margin = new Padding(2, 1, 2, 1), AccessibleName = "检查 ClipBridge 更新" };
    private readonly RoundedSurface _availableUpdate = new() { Name = "WorkspaceAvailableUpdate", Dock = DockStyle.Fill, CornerRadius = 8, FillColor = Ui.AccentSoftSolid, BorderColor = Ui.Accent, Padding = new Padding(4, 2, 2, 2), Margin = Padding.Empty, Visible = false };
    private readonly Label _availableVersion = new() { Name = "WorkspaceAvailableVersion", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = Ui.Font(8.2f, FontStyle.Bold), ForeColor = Ui.Accent, AutoEllipsis = true, Margin = new Padding(4, 0, 0, 0) };
    private readonly ModernButton _updateNow = new("立即更新", ButtonTone.Primary) { Name = "WorkspaceUpdateNowButton", AutoSize = false, Size = new Size(84, 30), Margin = new Padding(0, 0, 2, 0), AccessibleName = "立即安装可用更新" };
    private readonly TableLayoutPanel _sidebarBottom = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
    private readonly Panel _actionsSeparator = new() { Name = "WorkspaceActionsSeparator", Height = 1, Dock = DockStyle.Fill, Margin = new Padding(8, 6, 8, 6), BackColor = Ui.Line };
    private readonly Panel _navSeparator = new() { Name = "WorkspaceNavSeparator", Height = 1, Dock = DockStyle.Fill, Margin = new Padding(8, 5, 8, 5), BackColor = Ui.Line };
    private UpdateSidebarState _updateStatus = UpdateSidebarState.Latest;
    private FlowLayoutPanel? _rollbackPage;
    private IReadOnlyList<GitHubRollbackCandidate>? _rollbackCandidates;
    private bool _rollbackLoading = true;
    private string? _rollbackStatusMessage = "正在读取 GitHub 发布版本…";
    private Action? _openAvailableUpdate;
    private readonly Dictionary<string, long> _readThrough = new();
    private readonly Dictionary<string, WorkspaceDeviceRow> _deviceButtons = new();
    private readonly string? _statePath;
    private readonly Control? _devicesPage;
    private Control? _utilityPage;
    private string _page = "chat";
    private string? _selectedId;
    private bool _refreshing;
    private string _signature = "";
    private bool _exit;
    private readonly PageTransitionOverlay _pageTransition = new() { Name = "WorkspacePageTransition" };
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 800 };
    public ConversationView Conversation { get; }
    public PeerSnapshot? SelectedPeer => Conversation.SelectedPeer;
    public event Action? HiddenToTray;
    public event Action? RollbackRequested;
    public event Action<AvailableUpdate>? RollbackTargetRequested;
    public WorkspaceForm(HistoryStore? history, string localId, Func<PeerSnapshot[]> getPeers, Func<PeerSnapshot, string, CancellationToken, Task<bool>> sendText, Func<PeerSnapshot, string[], CancellationToken, Task> sendFiles, Action openFolder, Action<string> revoke, Action checkUpdate, Action<UiThemeMode> setTheme, Control? devicesPage, string? statePath)
    {
        _history = history; _localId = localId; _getPeers = getPeers; _openFolder = openFolder; _revoke = revoke; _checkUpdate = checkUpdate; _setTheme = setTheme; _devicesPage = devicesPage; _statePath = statePath;
        Text = "ClipBridge · 互传"; Icon = AppIcon.Load(); StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(1080, 760); MinimumSize = new Size(820, 620); Font = Ui.Font(9.5f); DoubleBuffered = true; AutoScaleMode = AutoScaleMode.Dpi;
        LoadState();
        var grid = new TableLayoutPanel { Name = "WorkspaceRoot", Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty }; grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.Controls.Add(_sidebar, 0, 0); grid.Controls.Add(_content, 1, 0); Controls.Add(grid); Controls.Add(_pageTransition); _pageTransition.BringToFront(); Resize += (_, _) => _pageTransition.Finish();
        var nav = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty }; nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 74)); nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 203)); _sidebar.Controls.Add(nav);
        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(4, 2, 0, 16), Margin = Padding.Empty };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42)); brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        var mark = new BrandMark { Size = new Size(34, 34), Anchor = AnchorStyles.Left, Margin = Padding.Empty }; brand.Controls.Add(mark, 0, 0); brand.SetRowSpan(mark, 2);
        brand.Controls.Add(new Label { Text = "ClipBridge", Font = Ui.Font(12f, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Margin = Padding.Empty }, 1, 0);
        brand.Controls.Add(new Label { Text = Environment.MachineName, Font = Ui.Font(8f), Tag = "muted", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 1, 1); nav.Controls.Add(brand, 0, 0);
        var add = new ModernButton("添加设备", ButtonTone.Secondary, "\uE710") { Name = "WorkspaceAddDevice", AutoSize = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Height = 38, Margin = new Padding(2, 8, 6, 10) }; add.Click += (_, _) => ShowDevices(); nav.Controls.Add(add, 0, 1);
        nav.Controls.Add(new Label { Name = "WorkspaceDevicesHeading", Text = "设备会话", Dock = DockStyle.Fill, Padding = new Padding(10, 6, 0, 0), Tag = "section", Font = Ui.Font(8.25f, FontStyle.Bold) }, 0, 2); nav.Controls.Add(_deviceList, 0, 3);
        _sidebarBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        _sidebarBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        _sidebarBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        _sidebarBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        _sidebarBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        _sidebarBottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _folder.Click += (_, _) => _openFolder();
        _settings.Click += (_, _) => ShowSettings();
        _update.Click += (_, _) => _checkUpdate();
        _update.SizeChanged += (_, _) => ApplyUpdatePillRegion();
        _updateNow.Click += (_, _) => _openAvailableUpdate?.Invoke();
        var availableLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
        availableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        availableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        availableLayout.Controls.Add(_availableVersion, 0, 0);
        availableLayout.Controls.Add(_updateNow, 1, 0);
        _availableUpdate.Controls.Add(availableLayout);
        _sidebarBottom.Controls.Add(_actionsSeparator, 0, 0);
        _sidebarBottom.Controls.Add(_update, 0, 1);
        _sidebarBottom.Controls.Add(_availableUpdate, 0, 1);
        _sidebarBottom.Controls.Add(_folder, 0, 2);
        _sidebarBottom.Controls.Add(_navSeparator, 0, 3);
        _sidebarBottom.Controls.Add(_settings, 0, 4);
        _sidebarBottom.Controls.Add(new Label { Text = "● 局域网直连", Tag = "muted", Dock = DockStyle.Fill, Padding = new Padding(14, 8, 0, 0), Font = Ui.Font(8f) }, 0, 5);
        nav.Controls.Add(_sidebarBottom, 0, 4);
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
        _update.Tone = _updateStatus == UpdateSidebarState.Failed ? ButtonTone.Danger : ButtonTone.Secondary;
        _update.Invalidate();
        foreach (ModernButton button in _sidebar.Controls.Find("WorkspaceSettings", true))
        {
            button.Tone = _page == "settings" ? ButtonTone.Secondary : ButtonTone.Ghost;
            button.Invalidate();
        }
        var peers = _getPeers(); var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); var rows = new List<(PeerSnapshot Peer, string Text, int Unread)>();
        foreach (var peer in peers)
        {
            var entries = Recent(peer.PeerId, now); if (_page == "chat" && Visible && ContainsFocus && Conversation.IsAtLatest && peer.PeerId == _selectedId) { long read = entries.Where(x => x.Event.SenderDeviceId != _localId).Select(x => x.Event.CreatedUtc).DefaultIfEmpty(0).Max(); if (read > _readThrough.GetValueOrDefault(peer.PeerId)) { _readThrough[peer.PeerId] = read; _saveTimer.Stop(); _saveTimer.Start(); } }
            int unread = entries.Count(x => x.Event.SenderDeviceId != _localId && x.Event.CreatedUtc > _readThrough.GetValueOrDefault(peer.PeerId)); var latest = entries.OrderByDescending(x => x.Event.CreatedUtc).FirstOrDefault()?.Event; string draft = Conversation.DraftPreview(peer.PeerId); string preview = draft.Length > 0 ? "[草稿] " + draft : latest?.Text ?? latest?.Files.FirstOrDefault()?.Name ?? (peer.RecentlySeen ? "设备已连接" : "设备离线"); rows.Add((peer, preview.Replace('\r', ' ').Replace('\n', ' '), unread));
        }
        string signature = Ui.CurrentTheme + ":" + _page + ":" + _selectedId + ":" + string.Join("|", rows.Select(x => x.Peer.PeerId + x.Peer.Name + x.Peer.RecentlySeen + x.Text + x.Unread)); if (signature == _signature) return; _signature = signature;
        // Same devices in the same order: update rows in place instead of
        // recreating native controls, which made the sidebar flash on every switch.
        if (rows.Count == _deviceButtons.Count && rows.Select(x => x.Peer.PeerId).SequenceEqual(_deviceList.Controls.Cast<Control>().Select(c => c.Name.Substring("WorkspacePeer-".Length))))
        {
            foreach (var item in rows) _deviceButtons[item.Peer.PeerId].SetState(item.Peer.Name, item.Text, item.Peer.RecentlySeen, item.Unread, _page == "chat" && _selectedId == item.Peer.PeerId);
            return;
        }
        _deviceList.SuspendLayout(); try { foreach (Control child in _deviceList.Controls.Cast<Control>().ToArray()) child.Dispose(); _deviceList.Controls.Clear(); _deviceButtons.Clear(); foreach (var item in rows) { var peer = item.Peer; var row = new WorkspaceDeviceRow(peer.Name, item.Text, peer.RecentlySeen, item.Unread, _page == "chat" && _selectedId == peer.PeerId) { Name = "WorkspacePeer-" + peer.PeerId, Width = Math.Max(150, _deviceList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2), Margin = new Padding(0, 0, 0, 5) }; row.Click += (_, _) => SelectConversation(peer.PeerId); _deviceButtons[peer.PeerId] = row; _deviceList.Controls.Add(row); } } finally { _deviceList.ResumeLayout(); }
    }
    public void SelectConversation(string id) { _selectedId = id; _page = "chat"; ShowContent(Conversation, () => Conversation.RefreshPeers(_getPeers(), id)); RefreshSidebar(); _saveTimer.Stop(); _saveTimer.Start(); }
    public void OpenConversation(string? id = null) { if (id != null || _selectedId != null) SelectConversation(id ?? _selectedId!); else ShowDevices(); ShowControlPanel(); }
    private void ShowContent(Control control, Action? update = null)
    {
        Control? current = _content.Controls.Cast<Control>().FirstOrDefault(child => child != _status && child.Visible);
        Bitmap? from = null;
        if (!ReferenceEquals(current, control) && CanAnimatePages())
            from = _pageTransition.CaptureRunningFrame() ?? CaptureContent();
        if (from != null) _pageTransition.Freeze(from, RectangleToClient(_content.RectangleToScreen(_content.ClientRectangle)), Ui.Canvas);
        update?.Invoke();
        foreach (Control child in _content.Controls)
            if (child != _status) child.Visible = ReferenceEquals(child, control);
        // Dock order follows z-order: the status strip must stay at the back so
        // it claims the bottom edge first and the page fills the space above it.
        // Bringing the strip to the front made it overlap every page's footer.
        control.BringToFront();
        _status.SendToBack();
        if (from == null) { _pageTransition.Finish(); return; }
        _content.PerformLayout();
        Bitmap? to = CaptureContent();
        if (to == null) _pageTransition.Finish();
        else _pageTransition.Play(to, _content);
    }

    private bool CanAnimatePages() => IsHandleCreated && Visible && WindowState != FormWindowState.Minimized
        && _content.Width > 0 && _content.Height > 0 && VisualMotion.AnimationsEnabled;

    // Snapshot the content area offscreen; the transition overlay is a form
    // child, so it is never included in this capture.
    private Bitmap? CaptureContent()
    {
        try
        {
            var frame = new Bitmap(_content.Width, _content.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _content.DrawToBitmap(frame, new Rectangle(Point.Empty, _content.Size));
            return frame;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
    public void ShowDevices()
    {
        _page = "devices";
        if (_devicesPage != null) ShowContent(_devicesPage);
        else FinishUtility(ShowUtility("添加设备", "连接同一局域网后，附近设备会出现在这里。"));
        RefreshSidebar();
    }

    private FlowLayoutPanel ShowUtility(string title, string subtitle)
    {
        var page = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, Padding = new Padding(32, 30, 28, 24), BackColor = Ui.Canvas, Visible = false
        };
        page.Controls.Add(new Label { Text = title, AutoSize = true, Font = Ui.Font(19f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8), ForeColor = Ui.Ink });
        page.Controls.Add(new Label { Text = subtitle, AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, 0, 0, 26), ForeColor = Ui.Muted });
        page.SizeChanged += (_, _) =>
        {
            int width = Math.Max(180, Math.Min(620, page.ClientSize.Width - page.Padding.Horizontal - 8));
            foreach (Control child in page.Controls)
            {
                if (child is RoundedSurface section)
                {
                    section.Width = width;
                    foreach (Label text in section.Controls.OfType<Label>().Where(label => label.Tag as string == "wrap"))
                        text.MaximumSize = new Size(Math.Max(80, width - 52), 0);
                }
                else if (child is Label label) label.MaximumSize = new Size(width, 0);
            }
        };
        _content.Controls.Add(page);
        return page;
    }

    private void FinishUtility(FlowLayoutPanel page)
    {
        Control? previous = _utilityPage;
        ShowContent(page);
        _utilityPage = page;
        if (previous != null) { _content.Controls.Remove(previous); previous.Dispose(); }
        RefreshSidebar();
    }

    private RoundedSurface UtilitySection(FlowLayoutPanel page, string name, int height)
    {
        var section = new RoundedSurface
        {
            Name = name, Width = Math.Max(180, Math.Min(620, page.ClientSize.Width - page.Padding.Horizontal - 8)),
            Height = height, CornerRadius = 14, FillColor = Ui.Surface, BorderColor = Ui.Line,
            Padding = new Padding(22, 20, 22, 20), Margin = new Padding(0, 0, 0, 16)
        };
        page.Controls.Add(section);
        return section;
    }

    private void ShowSettings()
    {
        _page = "settings";
        var page = ShowUtility("设置", "调整外观，查看记录保留方式。");
        var section = UtilitySection(page, "WorkspaceSettingsAppearance", 214);
        var theme = new ThemeSelector { Name = "WorkspaceTheme", Location = new Point(22, 58), Size = new Size(230, 40) };
        theme.SetMode(_themeMode);
        theme.ModeChanged += mode => _setTheme(mode);
        section.Controls.Add(new Label { Text = "外观主题", AutoSize = true, Location = new Point(24, 18), Font = Ui.Font(10f, FontStyle.Bold), ForeColor = Ui.Ink });
        section.Controls.Add(theme);
        var check = new ModernButton("检查更新", ButtonTone.Secondary, "\uE895") { Name = "WorkspaceCheckUpdate", AutoSize = false, Size = new Size(144, 38), Location = new Point(22, 112) };
        check.Click += (_, _) => _checkUpdate();
        section.Controls.Add(check);
        var rollback = new ModernButton("回退到历史版本", ButtonTone.Ghost, "\uE72B") { Name = "WorkspaceRollbackHistory", AutoSize = false, Size = new Size(190, 38), Location = new Point(176, 112) };
        rollback.Click += (_, _) => { ShowRollbackHistory(); RollbackRequested?.Invoke(); };
        section.Controls.Add(rollback);
        var history = UtilitySection(page, "WorkspaceSettingsHistory", 112);
        history.Controls.Add(new Label { Text = "聊天记录", AutoSize = true, Location = new Point(24, 22), Font = Ui.Font(10f, FontStyle.Bold), ForeColor = Ui.Ink });
        history.Controls.Add(new Label { Text = "记录保留 7 天，过期后已接收的文件仍会保留。", AutoSize = true, Tag = "wrap", MaximumSize = new Size(history.Width - 48, 0), Location = new Point(24, 57), Font = Ui.Font(9f), ForeColor = Ui.Muted });
        FinishUtility(page);
    }
    private void ShowUpdates()
    {
        _page = "updates";
        var page = ShowUtility("应用更新", "获取新版 ClipBridge，让设备间的传输保持顺畅。");
        var summary = UtilitySection(page, "WorkspaceUpdateSummary", 224);
        summary.Controls.Add(new BrandMark { Location = new Point(24, 24), Size = new Size(46, 46) });
        summary.Controls.Add(new Label { Text = "ClipBridge", AutoSize = true, Location = new Point(84, 23), Font = Ui.Font(13f, FontStyle.Bold), ForeColor = Ui.Ink });
        summary.Controls.Add(new Label { Text = "当前版本  " + UpdateProtocol.CurrentVersion.ToString(3), AutoSize = true, Location = new Point(85, 53), Font = Ui.Font(9f), ForeColor = Ui.Muted });
        summary.Controls.Add(new Label { Text = "更新前请先完成正在进行的传输。", AutoSize = true, Tag = "wrap", MaximumSize = new Size(summary.Width - 48, 0), Location = new Point(26, 102), Font = Ui.Font(9f), ForeColor = Ui.Muted });
        var check = new ModernButton(_update.Text, ButtonTone.Primary, "\uE895")
        {
            Name = "WorkspaceCheckUpdate", AutoSize = false, Size = new Size(144, 40),
            Location = new Point(24, 151), Enabled = _update.Enabled
        };
        check.Click += (_, _) => _checkUpdate();
        summary.Controls.Add(check);
        page.Controls.Add(new Label { Text = "更新包将经过签名和完整性校验。", AutoSize = true, Font = Ui.Font(8.5f), ForeColor = Ui.Muted, Margin = new Padding(4, 0, 0, 0) });
        FinishUtility(page);
    }
    private void ShowRollbackHistory()
    {
        _page = "rollback";
        var page = ShowUtility("回退到历史版本", "从 GitHub 已发布版本中选择需要恢复的版本。回退前请先完成正在进行的传输。");
        var section = UtilitySection(page, "WorkspaceRollbackList", 260);
        section.Controls.Add(new Label { Name = "WorkspaceRollbackStatus", Text = "正在读取 GitHub 发布版本…", AutoSize = true, Location = new Point(24, 24), Font = Ui.Font(9f), ForeColor = Ui.Muted });
        var back = new ModernButton("返回设置", ButtonTone.Ghost) { Name = "WorkspaceRollbackBack", AutoSize = false, Size = new Size(112, 36), Location = new Point(24, 208) };
        back.Click += (_, _) => ShowSettings();
        section.Controls.Add(back);
        FinishUtility(page);
        _rollbackPage = page;
        RenderRollbackCandidates();
    }

    internal void SetRollbackLoading(bool loading, string? message = null)
    {
        _rollbackLoading = loading;
        _rollbackStatusMessage = message ?? (loading ? "正在读取 GitHub 发布版本…" : "没有可回退的历史版本。");
        RenderRollbackCandidates();
    }

    internal void SetRollbackCandidates(IReadOnlyList<GitHubRollbackCandidate> candidates)
    {
        _rollbackCandidates = candidates;
        _rollbackLoading = false;
        _rollbackStatusMessage = null;
        RenderRollbackCandidates();
    }

    private void RenderRollbackCandidates()
    {
        if (_rollbackPage == null || _rollbackPage.IsDisposed) return;
        var section = _rollbackPage.Controls.Find("WorkspaceRollbackList", true).OfType<RoundedSurface>().FirstOrDefault();
        if (section == null) return;
        foreach (Control child in section.Controls.Cast<Control>().Where(control => control.Name.StartsWith("WorkspaceRollbackItem", StringComparison.Ordinal) || control.Name == "WorkspaceRollbackStatus" || control.Name == "WorkspaceRollbackItems").ToArray())
        {
            section.Controls.Remove(child);
            child.Dispose();
        }
        if (_rollbackCandidates == null || _rollbackCandidates.Count == 0)
        {
            string message = _rollbackStatusMessage ?? (_rollbackLoading ? "正在读取 GitHub 发布版本…" : "GitHub 暂无可回退的历史版本。");
            section.Controls.Add(new Label { Name = "WorkspaceRollbackStatus", Text = message, AutoSize = true, Location = new Point(24, 24), Font = Ui.Font(9f), ForeColor = Ui.Muted });
            return;
        }
        var list = new FlowLayoutPanel { Name = "WorkspaceRollbackItems", AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new Point(20, 18), Size = new Size(section.Width - 40, 172), BackColor = Color.Transparent, Margin = Padding.Empty };
        foreach (GitHubRollbackCandidate candidate in _rollbackCandidates)
        {
            string label = candidate.TagName + (candidate.IsPrerelease ? "  ·  预发布" : "") + (candidate.PublishedAt is { } published ? "  ·  " + published.ToLocalTime().ToString("yyyy-MM-dd") : "") + (candidate.Update == null && !string.IsNullOrWhiteSpace(candidate.UnavailableReason) ? "  ·  不可用：" + candidate.UnavailableReason : "");
            var item = new ModernButton(label, candidate.Update == null ? ButtonTone.Ghost : ButtonTone.Secondary)
            {
                Name = "WorkspaceRollbackItem-" + candidate.TagName,
                AutoSize = false,
                Size = new Size(Math.Max(160, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4), 38),
                Enabled = candidate.Update != null,
                AccessibleDescription = candidate.UnavailableReason ?? "回退到 " + candidate.TagName
            };
            item.Click += (_, _) => { if (candidate.Update is { IsRollback: true } update) RollbackTargetRequested?.Invoke(update); };
            list.Controls.Add(item);
        }
        section.Controls.Add(list);
    }

    private void ShowPeerMenu() { var peer = SelectedPeer; if (peer == null) return; var menu = new RoundedContextMenuStrip(); menu.Items.Add(peer.Name).Enabled = false; menu.Items.Add("删除设备", null, (_, _) => { if (MessageBox.Show(this, "删除“" + peer.Name + "”并取消配对？\n已接收的本机文件会保留。", "删除设备", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return; Conversation.ForgetPeer(peer.PeerId); _revoke(peer.PeerId); _readThrough.Remove(peer.PeerId); RefreshWorkspace(); SaveState(); }); menu.Closed += (_, _) => menu.Dispose(); menu.Show(Cursor.Position); }
    private UiThemeMode _themeMode;
    public void ApplyTheme(UiThemeMode mode) { _themeMode = mode; BackColor = Ui.Canvas; ForeColor = Ui.Ink; _sidebar.BackColor = Ui.Sidebar; _content.BackColor = Ui.Canvas; _status.BackColor = Ui.Sidebar; _status.ForeColor = Ui.Muted; _navSeparator.BackColor = Ui.Line; _actionsSeparator.BackColor = Ui.Line; void Colorize(Control control) { if (control is Label label) label.ForeColor = (string?)label.Tag switch { "section" => Ui.Faint, "muted" => Ui.Muted, _ => Ui.Ink }; foreach (Control child in control.Controls) Colorize(child); } Colorize(_sidebar); _folder.GlyphColor = Ui.Accent; _settings.GlyphColor = Ui.Muted; _folder.Invalidate(); _settings.Invalidate(); _update.Tone = _updateStatus == UpdateSidebarState.Failed ? ButtonTone.Danger : ButtonTone.Secondary; _update.Invalidate(); _availableUpdate.FillColor = Ui.AccentSoftSolid; _availableUpdate.BorderColor = Ui.Accent; _availableVersion.ForeColor = Ui.Accent; Conversation.ApplyTheme(); if (_devicesPage is DevicesView devices) devices.ApplyTheme(); if (_page == "settings") ShowSettings(); if (_page == "updates") ShowUpdates(); if (_page == "rollback") ShowRollbackHistory(); _signature = ""; RefreshSidebar(); Invalidate(true); }
    public void ShowControlPanel() { Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); }
    public void AddEvent(string text) { _status.Text = text; System.Diagnostics.Trace.WriteLine(text); }
    public void SetUpdateAvailable(string? version, Action? openUpdate)
    {
        bool available = !string.IsNullOrWhiteSpace(version) && openUpdate != null;
        _openAvailableUpdate = available ? openUpdate : null;
        _availableVersion.Text = available ? "发现新版本 " + version : "";
        SetUpdateStatus(available ? UpdateSidebarState.Available : UpdateSidebarState.Latest, version);
    }

    public void SetUpdateStatus(UpdateSidebarState state, string? detail = null)
    {
        _updateStatus = state;
        bool available = state == UpdateSidebarState.Available && _openAvailableUpdate != null;
        _update.Visible = !available;
        _availableUpdate.Visible = available;
        _sidebarBottom.RowStyles[1].Height = available ? 42 : 38;
        _update.Enabled = state is UpdateSidebarState.Latest or UpdateSidebarState.Failed;
        _update.Tone = state == UpdateSidebarState.Failed ? ButtonTone.Danger : ButtonTone.Secondary;
        _update.Text = state switch
        {
            UpdateSidebarState.Latest => "当前为最新版",
            UpdateSidebarState.Checking => "检查中…",
            UpdateSidebarState.Failed => "检查失败 · 点击重试",
            UpdateSidebarState.Available => "发现新版本",
            UpdateSidebarState.Installing => "正在更新…",
            _ => "检查更新"
        };
        _update.AccessibleDescription = state switch
        {
            UpdateSidebarState.Latest => "当前为最新版。点击检查更新。",
            UpdateSidebarState.Checking => "正在检查更新，请稍候。",
            UpdateSidebarState.Failed => "检查更新失败。" + (string.IsNullOrWhiteSpace(detail) ? "点击重试。" : detail + " 点击重试。"),
            UpdateSidebarState.Available => "发现新版本 " + detail,
            UpdateSidebarState.Installing => string.IsNullOrWhiteSpace(detail) ? "正在更新。" : detail,
            _ => detail ?? ""
        };
        foreach (Control control in _content.Controls.Find("WorkspaceCheckUpdate", true))
            control.Enabled = state is not (UpdateSidebarState.Checking or UpdateSidebarState.Installing);
        foreach (Control control in _content.Controls.Find("WorkspaceRollbackHistory", true))
            control.Enabled = state is not (UpdateSidebarState.Checking or UpdateSidebarState.Installing);
        _sidebarBottom.PerformLayout();
        _update.Invalidate();
    }

    public void SetUpdateCheckState(bool enabled, string? text = null)
    {
        if (!enabled)
        {
            UpdateSidebarState state = text?.Contains("正在更新", StringComparison.Ordinal) == true ? UpdateSidebarState.Installing : UpdateSidebarState.Checking;
            SetUpdateStatus(state, text);
        }
        else if (_updateStatus is UpdateSidebarState.Checking or UpdateSidebarState.Installing)
        {
            SetUpdateStatus(_openAvailableUpdate != null ? UpdateSidebarState.Available : UpdateSidebarState.Latest);
        }
        foreach (Control button in _content.Controls.Find("WorkspaceCheckUpdate", true)) button.Enabled = enabled;
        foreach (Control button in _content.Controls.Find("WorkspaceRollbackHistory", true)) button.Enabled = enabled;
    }

    private void ApplyUpdatePillRegion()
    {
        if (_update.Width <= 0 || _update.Height <= 0) return;
        using GraphicsPath path = new();
        path.AddEllipse(0, 0, _update.Width - 1, _update.Height - 1);
        Region? previous = _update.Region;
        _update.Region = new Region(path);
        previous?.Dispose();
    }
    public void AllowExit() { _exit = true; }
    protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing && !_exit) { e.Cancel = true; Hide(); SaveState(); HiddenToTray?.Invoke(); return; } base.OnFormClosing(e); }
    private sealed record SavedState(string? Selected, Dictionary<string, long> ReadThrough);
    private void LoadState() { if (_statePath == null) return; try { var state = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(_statePath)); _selectedId = state?.Selected; if (state != null) foreach (var pair in state.ReadThrough) _readThrough[pair.Key] = pair.Value; } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { } }
    private void SaveState() { if (_statePath == null) return; try { Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!); string temporary = _statePath + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(new SavedState(_selectedId, _readThrough))); File.Move(temporary, _statePath, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(e.GetType().Name); } }
    protected override void Dispose(bool disposing) { if (disposing) { _saveTimer.Stop(); _saveTimer.Dispose(); SaveState(); } base.Dispose(disposing); }
}

internal sealed class WorkspaceDeviceRow : Control
{
    private string _deviceName = "";
    private string _preview = "";
    private bool _online;
    private int _unread;
    private bool _selected;
    private bool _hovering;

    internal WorkspaceDeviceRow(string deviceName, string preview, bool online, int unread, bool selected)
    {
        Height = 68; MinimumSize = new Size(140, 68); Cursor = Cursors.Hand; TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetState(deviceName, preview, online, unread, selected);
    }

    internal void SetState(string deviceName, string preview, bool online, int unread, bool selected)
    {
        if (_deviceName == deviceName && _preview == preview && _online == online && _unread == unread && _selected == selected) return;
        _deviceName = deviceName; _preview = preview; _online = online; _unread = unread; _selected = selected;
        AccessibleName = deviceName + (online ? "，在线" : "，离线"); AccessibleDescription = preview;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF bounds = new(1, 1, Width - 3, Height - 7);
        Color fill = _selected ? Ui.AccentSoftSolid : _hovering ? Ui.SurfaceHover : Ui.Surface;
        for (int layer = 2; layer >= 1; layer--)
        {
            using var shadow = new SolidBrush(Color.FromArgb((Ui.HeaderIsDark ? 30 : 13) / layer, 24, 40, 72));
            RectangleF halo = RectangleF.Inflate(bounds, layer - 1, layer - 1);
            halo.Offset(0f, layer * 1.1f);
            VisualDrawing.FillRoundedRectangle(g, shadow, halo, 9 + layer - 1);
        }
        using (var brush = new SolidBrush(fill)) VisualDrawing.FillRoundedRectangle(g, brush, bounds, 9);
        using (var pen = new Pen(_selected ? Ui.Accent : Ui.Line, _selected ? 1.4f : 1f)) VisualDrawing.DrawRoundedRectangle(g, pen, bounds, 9);
        if (_selected)
        {
            using var marker = new SolidBrush(Ui.Accent);
            VisualDrawing.FillRoundedRectangle(g, marker, new RectangleF(bounds.X, bounds.Y + 14, 3, bounds.Height - 28), 2);
        }
        Color onlineColor = _online ? Ui.Success : Ui.Faint;
        float dotY = bounds.Y + 15;
        using (var halo = new SolidBrush(Color.FromArgb(_online ? 46 : 28, onlineColor))) g.FillEllipse(halo, Width - 27, dotY - 3, 12, 12);
        using (var dot = new SolidBrush(onlineColor)) g.FillEllipse(dot, Width - 24, dotY, 6, 6);
        int badgeWidth = 0;
        if (_unread > 0)
        {
            string count = _unread > 99 ? "99+" : _unread.ToString();
            using var badgeFont = Ui.Font(8f, FontStyle.Bold);
            badgeWidth = Math.Max(20, TextRenderer.MeasureText(g, count, badgeFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 12);
            RectangleF badge = new(Width - 16 - badgeWidth, bounds.Y + 34, badgeWidth, 18);
            using (var badgeFill = new SolidBrush(Ui.Accent)) VisualDrawing.FillRoundedRectangle(g, badgeFill, badge, 9);
            TextRenderer.DrawText(g, count, badgeFont, Rectangle.Round(badge), Ui.PrimaryInk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
        using var titleFont = Ui.Font(9.5f, FontStyle.Bold);
        using var previewFont = Ui.Font(8.2f);
        int titleWidth = Math.Max(36, Width - 48);
        int previewWidth = Math.Max(36, Width - 30 - (badgeWidth > 0 ? badgeWidth + 8 : 0));
        TextRenderer.DrawText(g, _deviceName, titleFont, new Rectangle(14, 8, titleWidth, 23), _selected ? Ui.AccentDark : Ui.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, _preview, previewFont, new Rectangle(14, 34, previewWidth, 20), Ui.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Ui.Accent, 1) { DashStyle = DashStyle.Dot };
            VisualDrawing.DrawRoundedRectangle(g, focus, RectangleF.Inflate(bounds, -3, -3), 7);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovering = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
