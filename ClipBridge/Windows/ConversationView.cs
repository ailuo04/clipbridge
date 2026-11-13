using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClipBridge.Windows.History;

namespace ClipBridge.Windows;

internal sealed class ConversationView : UserControl
{
    private sealed record PeerChoice(PeerSnapshot Snapshot)
    {
        public override string ToString() => Snapshot.Name + (Snapshot.RecentlySeen ? "  ·  在线" : "  ·  离线");
    }

    private readonly HistoryStore? _history;
    private readonly string _localDeviceId;
    private readonly Func<PeerSnapshot[]> _getPeers;
    private readonly Func<PeerSnapshot, string, CancellationToken, Task<bool>> _sendText;
    private readonly Func<PeerSnapshot, string[], CancellationToken, Task> _sendFiles;
    private readonly ModernButton _paste = new("粘贴", ButtonTone.Secondary, "\uE8C8") { Name = "ConversationPasteButton" };
    private readonly FlowLayoutPanel _attachmentStrip = new() { Name = "PendingAttachments", Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, Margin = Padding.Empty };
    private readonly FlowLayoutPanel _composerTools = new() { Dock = DockStyle.Fill, WrapContents = false, BackColor = Ui.Surface, Margin = Padding.Empty };
    private List<string> _attachments = new();
    private readonly HashSet<string> _ownedImages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Text, List<string> Files)> _drafts = new();
    private string? _draftPeer;
    public string[] PendingAttachments => _attachments.ToArray();
    private readonly Action _openReceiveDirectory;
    private readonly Action _manageDevices;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ComboBox _peers = new()
    {
        Name = "ConversationPeerSelector",
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        MaxDropDownItems = 12,
        Margin = Padding.Empty
    };
    private readonly Label _status = new()
    {
        Name = "ConversationStatus",
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = Padding.Empty
    };
    private readonly Label _subtitle = new()
    {
        Name = "ConversationSubtitle",
        Text = "连接后，发送和接收都在这里完成",
        Dock = DockStyle.Fill,
        Font = Ui.Font(8.5f),
        ForeColor = Ui.Muted,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = Padding.Empty
    };
    private readonly ModernButton _openDirectory = new("", ButtonTone.Secondary)
    {
        Name = "OpenReceiveDirectoryButton",
        Text = "打开保存位置",
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(4, 0, 0, 0)
    };
    private readonly ModernButton _manage = new("", ButtonTone.Secondary)
    {
        Name = "ManageDevicesButton",
        Text = "设备管理",
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(4, 0, 0, 0)
    };
    private readonly ConversationTimelineControl _timeline = new() { Name = "ConversationTimeline", Dock = DockStyle.Fill, Padding = new Padding(16, 6, 12, 2) };
    private readonly MessageComposer _composer = new()
    {
        Name = "ConversationComposer",
        Multiline = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.None,
        BorderStyle = BorderStyle.None,
        Dock = DockStyle.Fill,
        MinimumSize = new Size(0, 62),
        Margin = Padding.Empty,
        PlaceholderText = "输入文字，Ctrl+V 粘贴；拖入文件可发送"
    };
    private readonly InsetField _composerField = new(false)
    {
        Name = "ConversationEditorField",
        Dock = DockStyle.Fill,
        Padding = new Padding(11, 9, 9, 5),
        Margin = new Padding(0, 0, 0, 5)
    };
    private readonly ModernButton _send = new("", ButtonTone.Primary)
    {
        Name = "ConversationSendButton",
        Text = "发送",
        Glyph = "\uE724",
        AutoSize = false,
        Size = new Size(88, 32),
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(4, 0, 0, 0)
    };
    private readonly ModernButton _attach = new("", ButtonTone.Ghost)
    {
        Name = "ConversationAttachButton",
        Text = "文件",
        Glyph = "\uE8A5",
        AutoSize = false,
        Size = new Size(74, 30),
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(0, 2, 0, 0)
    };
    private readonly Panel _dropZone = new()
    {
        Name = "ConversationDropZone",
        Dock = DockStyle.Fill,
        AllowDrop = true,
        Padding = new Padding(12),
        Margin = Padding.Empty
    };
    private readonly Label _dropHint = new()
    {
        Name = "ConversationDropHint",
        Text = "拖入文件，点击发送",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        AutoEllipsis = true,
        Margin = Padding.Empty
    };
    private readonly TableLayoutPanel _root;
    private readonly TableLayoutPanel _composerShell;
    private PeerChoice? _selected;

    private readonly HashSet<string> _sending = new();
    private readonly Dictionary<string, CancellationTokenSource> _sendCancellation = new();
    private bool _busy => _draftPeer != null && _sending.Contains(_draftPeer);
    public bool IsAtLatest => _timeline.IsAtLatest;
    public bool HasActiveSends => _sending.Count > 0;
    public event Action? StateChanged;
    private readonly Label _peerTitle = new() { AutoSize = true, Anchor = AnchorStyles.Left, Font = Ui.Font(12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
    private readonly ConnectionPill _connection = new() { Name = "ConversationConnectionPill", Anchor = AnchorStyles.Left, Margin = new Padding(12, 0, 0, 0) };
    private readonly ConversationHeaderSurface _header = new() { Name = "ConversationHeader", Dock = DockStyle.Fill, Padding = new Padding(20, 9, 12, 7), Margin = Padding.Empty };
    public void SetEmbedded() { _peers.Parent!.Controls.Remove(_peers); ((TableLayoutPanel)_status.Parent!).Controls.Add(_peerTitle, 0, 0); _manage.Text = ""; _manage.Glyph = "\uE712"; _manage.AutoSize = false; _manage.MinimumSize = new Size(36, 36); _manage.Size = new Size(36, 36); _manage.Tone = ButtonTone.Ghost; _manage.Margin = new Padding(4, 6, 0, 0); _manage.AccessibleName = "设备选项"; _manage.AccessibleDescription = "管理当前设备"; _openDirectory.Visible = false; }
    public void ForgetPeer(string id) { if (_sendCancellation.TryGetValue(id, out var cancel)) cancel.Cancel(); _drafts.Remove(id); if (_draftPeer == id) { _draftPeer = null; _attachments = new(); _composer.Clear(); _selected = null; RenderAttachments(); } }
    private bool _resourcesDisposed;
    private TransferProgress[] _transfers = Array.Empty<TransferProgress>();
    public void SetTransfers(TransferProgress[] values) { _transfers = values; RefreshSelectedHistory(); }

    public PeerSnapshot? SelectedPeer => _selected?.Snapshot;
    public IReadOnlyList<HistoryLocalRecord> VisibleEntries => _timeline.Entries;

    public ConversationView(
        HistoryStore? history,
        string localDeviceId,
        Func<PeerSnapshot[]> getPeers,
        Func<PeerSnapshot, string, CancellationToken, Task<bool>> sendText,
        Func<PeerSnapshot, string[], CancellationToken, Task> sendFiles,
        Action openReceiveDirectory,
        Action manageDevices)
    {
        _history = history;
        _localDeviceId = localDeviceId;
        _getPeers = getPeers;
        _sendText = sendText;
        _sendFiles = sendFiles;
        _openReceiveDirectory = openReceiveDirectory;
        _manageDevices = manageDevices;

        Text = "互传";
        AccessibleName = "互传会话窗口";

        Size = new Size(860, 760);
        MinimumSize = Size.Empty;
        Font = Ui.Font(9.5f);

        BackColor = Ui.Canvas;
        ForeColor = Ui.Ink;
        AllowDrop = true;

        _root = new TableLayoutPanel
        {
            Name = "ConversationRoot",
            Dock = DockStyle.Fill,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Ui.Canvas
        };
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 172));
        Controls.Add(_root);

        _root.Controls.Add(BuildHeader(), 0, 0);
        _root.Controls.Add(_timeline, 0, 1);
        _composerShell = BuildComposer();
        _root.Controls.Add(_composerShell, 0, 2);

        _peers.SelectedIndexChanged += (_, _) => SelectPeerFromControl();
        _send.Click += async (_, _) => await SendTextAsync();
        _attach.Click += (_, _) => ShowAttachmentMenu();
        _paste.Click += (_, _) => { try { if (Clipboard.GetDataObject() is { } data) _composer.PasteData(data); } catch (ExternalException) { _status.Text = "剪贴板正忙，请重试"; } };
        _composer.PasteAttachment += PasteAttachment;
        _composer.PasteFailed += message => _status.Text = message;
        _openDirectory.Click += (_, _) => _openReceiveDirectory();
        _manage.Click += (_, _) => _manageDevices();
        _composer.KeyDown += ComposerKeyDown;
        _composer.TextChanged += (_, _) => StateChanged?.Invoke();
        _timeline.DeleteRequested += RequestDelete;
        _composer.Enter += (_, _) => _composerField.Highlighted = true;
        _composer.Leave += (_, _) => _composerField.Highlighted = false;
        _timeline.CopyRequested += CopyRecord;
        _timeline.OpenRequested += OpenRecord;
        WireDrop(this);
        ApplyTheme();
        RefreshPeers();
    }

    public void RefreshPeers(IReadOnlyList<PeerSnapshot>? peers = null, string? preferredPeerId = null)
    {
        PeerSnapshot[] values = peers?.ToArray() ?? _getPeers();
        string? prior = preferredPeerId ?? _selected?.Snapshot.PeerId ?? _selected?.Snapshot.Address;
        bool samePeers = _peers.Items.Count == values.Length && values.Select((peer, index) => (peer, index)).All(item =>
            _peers.Items[item.index] is PeerChoice choice &&
            string.Equals(choice.Snapshot.Name, item.peer.Name, StringComparison.Ordinal) &&
            string.Equals(choice.Snapshot.Address, item.peer.Address, StringComparison.Ordinal) &&
            choice.Snapshot.RecentlySeen == item.peer.RecentlySeen &&
            string.Equals(choice.Snapshot.PeerId, item.peer.PeerId, StringComparison.Ordinal));
        if (samePeers && values.Length > 0 && string.IsNullOrWhiteSpace(preferredPeerId))
        {
            RefreshSelectedHistory();
            return;
        }
        _peers.BeginUpdate();
        try
        {
            _peers.Items.Clear();
            foreach (PeerSnapshot peer in values) _peers.Items.Add(new PeerChoice(peer));
            if (_peers.Items.Count == 0)
            {
                _selected = null;
                _send.Enabled = false; _composer.ReadOnly = true;
                _connection.SetState(false);
                _status.Text = "未连接";
                _subtitle.Text = "暂无设备 · 请先完成配对";
                _timeline.SetMessage("连接设备后，这里会显示最近 7 天的收发记录。\r\n历史记录只保留 7 天，删除记录不会删除本地文件。");
                return;
            }
            int index = -1;
            if (!string.IsNullOrWhiteSpace(prior))
            {
                index = values.Select((peer, i) => (peer, i)).FirstOrDefault(item => item.peer.PeerId == prior || item.peer.Address == prior).i;
                if (!values.Any(peer => peer.PeerId == prior || peer.Address == prior)) index = -1;
            }
            _peers.SelectedIndex = index >= 0 ? index : 0;
        }
        finally { _peers.EndUpdate(); }
    }

    public void RefreshHistory() => RefreshSelectedHistory();

    public void ApplyTheme()
    {
        BackColor = Ui.Canvas;
        ForeColor = Ui.Ink;
        _root.BackColor = Ui.Canvas;
        _header.BackColor = Ui.Surface;
        _status.ForeColor = Ui.Muted;
        _peerTitle.ForeColor = Ui.Ink;
        _subtitle.ForeColor = Ui.Muted;
        _peers.BackColor = Ui.FieldSurface;
        _peers.ForeColor = Ui.Ink;
        _composer.BackColor = Ui.FieldSurface;
        _composer.ForeColor = Ui.Ink;
        _composerField.BackColor = Ui.Surface;
        _composerTools.BackColor = Ui.Surface;
        _dropZone.BackColor = Ui.Surface;
        _dropHint.ForeColor = Ui.Muted;
        foreach (Button button in new[] { _openDirectory, _manage, _send, _attach, _paste }) StyleButton(button);
        foreach (RoundedSurface surface in AllControls(this).OfType<RoundedSurface>())
        {
            surface.FillColor = Ui.Surface;
            surface.BorderColor = Ui.Line;
            surface.Invalidate();
        }
        _connection.Invalidate();
        _header.Invalidate();
        _timeline.ApplyTheme();
        Invalidate(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed) { _resourcesDisposed = true; _lifetime.Cancel(); _lifetime.Dispose(); if (!HasActiveSends) foreach (var path in _ownedImages) { try { File.Delete(path); } catch (IOException) { } } }
        base.Dispose(disposing);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _peers.Font = Ui.Font(12f, FontStyle.Bold); _peers.FlatStyle = FlatStyle.Flat; _peers.MinimumSize = new Size(240, 34); _peers.Anchor = AnchorStyles.Left;
        header.Controls.Add(_peers, 0, 0);
        header.Controls.Add(_connection, 1, 0);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
        actions.Controls.Add(_openDirectory); actions.Controls.Add(_manage); header.Controls.Add(actions, 2, 0); header.SetRowSpan(actions, 2);
        _status.Text = "已配对设备直接接收"; _status.Font = Ui.Font(8.5f); header.Controls.Add(_status, 0, 1); header.SetColumnSpan(_status, 2);
        _header.Controls.Add(header);
        return _header;
    }
    private TableLayoutPanel BuildComposer()
    {
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, Margin = Padding.Empty };
        var surface = new RoundedSurface { Name = "ConversationInputSurface", Dock = DockStyle.Fill, Flat = true, CornerRadius = 0, Padding = new Padding(14, 7, 14, 6), Margin = Padding.Empty, FillColor = Ui.Surface, BorderColor = Ui.Line };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _composerTools.Controls.Add(_attach);
        layout.Controls.Add(_composerTools, 0, 0); layout.Controls.Add(_attachmentStrip, 0, 1);
        _composerField.Controls.Add(_composer);
        layout.Controls.Add(_composerField, 0, 2);
        _composer.MinimumSize = Size.Empty; _composer.PlaceholderText = "输入或粘贴文字、图片，也可以拖入视频和文件";
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_send, 1, 0); layout.Controls.Add(bottom, 0, 3);
        surface.Controls.Add(layout); shell.Controls.Add(surface); return shell;
    }

    private void SelectPeerFromControl()
    {
        var choice = _peers.SelectedItem as PeerChoice;
        if (choice == null) return;
        string key = string.IsNullOrWhiteSpace(choice.Snapshot.PeerId) ? choice.Snapshot.Address : choice.Snapshot.PeerId;
        if (_draftPeer != key)
        {
            if (_draftPeer != null) _drafts[_draftPeer] = (_composer.Text, _attachments);
            var draft = _drafts.GetValueOrDefault(key);
            _composer.Text = draft.Text ?? ""; _attachments = draft.Files ?? new List<string>(); _draftPeer = key;
            RenderAttachments();
        }
        _selected = choice;
        _peerTitle.Text = choice.Snapshot.Name;
        _connection.SetState(choice.Snapshot.RecentlySeen);
        _send.Enabled = !_busy && choice.Snapshot.RecentlySeen; _composer.ReadOnly = _busy;
        StateChanged?.Invoke();
        if (_selected is null)
        {
            _status.Text = "未连接";
            _subtitle.Text = "暂无设备 · 请先完成配对";
            _timeline.SetMessage("连接设备后，这里会显示最近 7 天的收发记录。\r\n历史记录只保留 7 天，删除记录不会删除本地文件。\r\n\r\n请从设备管理完成配对，然后回到这里开始发送和接收。");
            return;
        }
        _status.Text = _selected.Snapshot.RecentlySeen ? "已配对设备直接接收 · " + _selected.Snapshot.Address : "设备离线，连接后可继续发送 · " + _selected.Snapshot.Address;
        _subtitle.Text = $"{_selected.Snapshot.Name} · {(_selected.Snapshot.RecentlySeen ? "在线" : "离线")} · {_selected.Snapshot.Address}";
        RefreshSelectedHistory();
    }

    private void RefreshSelectedHistory()
    {
        if (_selected is null)
        {
            _status.Text = "未连接";
            _subtitle.Text = "暂无设备 · 请先完成配对";
            _timeline.SetMessage("连接设备后，这里会显示最近 7 天的收发记录。\r\n历史记录只保留 7 天，删除记录不会删除本地文件。\r\n\r\n请从设备管理完成配对，然后回到这里开始发送和接收。");
            return;
        }
        if (_history is null || string.IsNullOrWhiteSpace(_selected.Snapshot.PeerId))
        {
            _timeline.SetMessage("该设备尚未建立可同步的历史分区。连接后发送或接收内容即可开始记录。");
            return;
        }
        try
        {
            _timeline.SelectPartition(_selected.Snapshot.PeerId);
            _timeline.PeerIsComputer = _selected.Snapshot.IsComputer;
            var history = _history.GetRecent(_selected.Snapshot.PeerId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var active = _transfers.Where(p => p.PeerId == _selected.Snapshot.PeerId && !history.Any(h => h.Event.EventId == p.TransferId) && p.State is "排队中" or "准备文件" or "连接接收设备" or "传输中")
                .Select(p => new HistoryLocalRecord(new HistorySyncEvent(1, p.TransferId, p.PeerId!, p.Direction == "发送" ? _localDeviceId : p.PeerId!, p.CreatedUtc, "file", null,
                    new[] { new HistoryFileMetadata(p.Name, p.Total, new string('0', 64)) }, p.State + (p.Total > 0 ? $" · {Math.Clamp(p.Completed * 100d / p.Total, 0, 100):0}%" : "")), new Dictionary<string, string>()));
            _timeline.SetEntries(history.Concat(active), _localDeviceId);
        }
        catch (Exception error)
        {
            _timeline.SetMessage("历史记录暂时不可用：" + error.Message);
        }
    }

    private async Task SendTextAsync()
    {
        if (_busy) return;
        var target = SelectedPeer;
        if (target is not { RecentlySeen: true }) { _status.Text = "请先连接目标设备"; return; }
        string key = string.IsNullOrWhiteSpace(target.PeerId) ? target.Address : target.PeerId;
        string text = _composer.Text; var attachments = _attachments; string[] paths = attachments.ToArray();
        if (string.IsNullOrWhiteSpace(text) && paths.Length == 0) return;
        _drafts[key] = (text, attachments); _sending.Add(key);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _sendCancellation[key] = cancel;
        _send.Enabled = false; _composer.ReadOnly = true; StateChanged?.Invoke();
        try
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (!await _sendText(target, text, cancel.Token)) throw new IOException("消息未发出，请重试");
                _drafts[key] = ("", attachments); if (_draftPeer == key && !IsDisposed) _composer.Clear();
            }
            if (paths.Length > 0)
            {
                await _sendFiles(target, paths, cancel.Token);
                foreach (string path in paths) { attachments.Remove(path); _ownedImages.Remove(path); }
            }
            if (!IsDisposed && _draftPeer == key) _status.Text = "已发送 · 原文件完整保留";
        }
        catch (OperationCanceledException) { if (!IsDisposed && _draftPeer == key) _status.Text = "已取消，未发送附件保留"; }
        catch (Exception error) { if (!IsDisposed && _draftPeer == key) _status.Text = "发送失败，附件保留：" + error.Message; }
        finally
        {
            _sending.Remove(key); _sendCancellation.Remove(key);
            if (!IsDisposed) { _send.Enabled = !_busy && SelectedPeer?.RecentlySeen == true; _composer.ReadOnly = _busy; RenderAttachments(); RefreshSelectedHistory(); StateChanged?.Invoke(); }
        }
    }
    public string DraftPreview(string peerId) => _draftPeer == peerId ? (_composer.Text.Length > 0 ? _composer.Text : _attachments.Count > 0 ? "附件 " + _attachments.Count : "") : _drafts.TryGetValue(peerId, out var draft) ? (draft.Text.Length > 0 ? draft.Text : draft.Files.Count > 0 ? "附件 " + draft.Files.Count : "") : "";
    private bool PasteAttachment(IDataObject data)
    {
        if (_busy) return true;
        if (data.GetData(DataFormats.FileDrop) is string[] files) { AddAttachments(files); return true; }
        if (_attachments.Count >= 256 && (data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent("PNG"))) { _status.Text = "最多添加 256 个附件"; return true; }
        if (data.GetData("PNG") is MemoryStream png)
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBridge", "SentImages");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "粘贴图片-" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(path, png.ToArray()); _ownedImages.Add(path); AddAttachments(new[] { path }); return true;
        }
        if (data.GetData(DataFormats.Bitmap) is Image image)
        {
            if (_attachments.Count >= 256) { _status.Text = "最多添加 256 个附件"; return true; }
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBridge", "SentImages");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "粘贴图片-" + Guid.NewGuid().ToString("N") + ".png");
            try { image.Save(path, System.Drawing.Imaging.ImageFormat.Png); _ownedImages.Add(path); AddAttachments(new[] { path }); }
            catch { if (File.Exists(path)) File.Delete(path); throw; }
            return true;
        }
        return false;
    }

    public void AddAttachments(string[] paths)
    {
        if (_busy) { _status.Text = "正在发送，请稍后添加"; return; }
        if (paths.Length == 0 || paths.Any(p => !File.Exists(p) && !Directory.Exists(p))) { _status.Text = "文件或文件夹已不存在"; return; }
        string[] added = paths.Where(p => !_attachments.Contains(p, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (_attachments.Count + added.Length > 256) { _status.Text = "最多添加 256 个附件"; return; }
        _attachments.AddRange(added); RenderAttachments(); StateChanged?.Invoke(); _composer.Focus();
        _status.Text = "已添加附件，点击发送 · 图片视频按原文件传输";
    }
    private void RenderAttachments()
    {
        foreach (Control c in _attachmentStrip.Controls.Cast<Control>().ToArray()) c.Dispose();
        _attachmentStrip.Controls.Clear();
        foreach (string path in _attachments.ToArray())
        {
            var tile = new FlowLayoutPanel { Width = 190, Height = 64, WrapContents = false, BackColor = Ui.FieldSurface, Margin = new Padding(0, 2, 6, 0) };
            var picture = new PictureBox { Width = 54, Height = 54, SizeMode = PictureBoxSizeMode.Zoom, Image = MediaPreview.Load(path, 54, 54) };
            picture.Disposed += (_, _) => picture.Image?.Dispose(); tile.Controls.Add(picture);
            tile.Controls.Add(new Label { Text = (Directory.Exists(path) ? "▣ " : "") + Path.GetFileName(path), Width = 92, Height = 54, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ui.Ink });
            var remove = new Button { Text = "×", Width = 24, Height = 28, FlatStyle = FlatStyle.Flat, AccessibleName = "移除附件 " + Path.GetFileName(path) };
            remove.Click += (_, _) => { if (_busy) return; _attachments.Remove(path); if (_ownedImages.Remove(path)) { try { File.Delete(path); } catch (IOException) { } } RenderAttachments(); };
            tile.Controls.Add(remove); _attachmentStrip.Controls.Add(tile);
        }
        if (_attachmentStrip.Parent is TableLayoutPanel layout) layout.RowStyles[1].Height = _attachments.Count > 0 ? 80 : 0;
        _root.RowStyles[2].Height = _attachments.Count > 0 ? 252 : 172;
    }

    private void ComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift && !_composer.IsComposing)
        {
            e.SuppressKeyPress = true;
            _ = SendTextAsync();
        }
    }

    private void ShowAttachmentMenu()
    {
        var menu = new RoundedContextMenuStrip();
        menu.Items.Add("选择文件（图片、视频及其他文件）", null, (_, _) => ChooseFiles());
        menu.Items.Add("选择文件夹", null, (_, _) => { using var dialog = new FolderBrowserDialog { Description = "选择要发送的文件夹", UseDescriptionForTitle = true }; if (dialog.ShowDialog(this) == DialogResult.OK) AddAttachments(new[] { dialog.SelectedPath }); });
        menu.Closed += (_, _) => menu.Dispose(); menu.Show(_attach, new Point(0, _attach.Height));
    }
    private void ChooseFiles()
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Title = "选择要发送的文件" };
        if (dialog.ShowDialog(this) == DialogResult.OK) AddAttachments(dialog.FileNames);
    }

    public async Task SendFilesAsync(string[] paths)
    {
        PeerSnapshot? target = SelectedPeer;
        if (target is not { RecentlySeen: true }) { _status.Text = "设备离线，请连接后再发送"; return; }
        _status.Text = "正在发送文件…";
        try { await _sendFiles(target, paths, _lifetime.Token); if (!IsDisposed) { _status.Text = "文件已送达"; RefreshSelectedHistory(); } }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "传输已取消"; }
        catch (Exception error) { if (!IsDisposed) _status.Text = "发送失败：" + error.Message; }
    }

    private void WireDrop(Control control)
    {
        foreach (Control child in control.Controls) WireDrop(child);
        control.ControlAdded += (_, e) => { if (e.Control != null) WireDrop(e.Control); };
        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        control.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            {
                string[] files = paths;
                if (files.Length > 0) AddAttachments(files);
            }
        };
    }

    // Ask first: deleting also removes the copy ClipBridge saved on this PC.
    private void RequestDelete(HistoryLocalRecord entry)
    {
        string[] owned = LocalFiles.OwnedExisting(entry.LocalFileReferences.Values);
        bool keepsOriginal = entry.LocalFileReferences.Values.Any(path => !LocalFiles.IsAppOwned(path));
        string message = owned.Length == 0 ? "删除这条记录？"
            : "删除这条记录，并删除本机保存的文件？\n\n" + string.Join("\n", owned.Take(4).Select(Path.GetFileName)) + (owned.Length > 4 ? "\n等 " + owned.Length + " 项" : "");
        if (keepsOriginal) message += "\n\n你发送的原文件不会被删除。";
        message += "\n\n对方设备上的这条记录会同步移除。";
        if (MessageBox.Show(FindForm(), message, "删除", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
        DeleteRecord(entry);
    }

    private void DeleteRecord(HistoryLocalRecord entry)
    {
        if (_history is null) return;
        try
        {
            int failed = DeleteEntry(entry);
            _status.Text = failed > 0 ? "记录已删除，" + failed + " 个文件正在使用，未能删除" : "已删除";
            RefreshSelectedHistory();
        }
        catch (Exception error) { _status.Text = "删除记录失败：" + error.Message; }
    }

    // Removes the record, the files ClipBridge saved for it, and its thumbnail.
    private int DeleteEntry(HistoryLocalRecord entry)
    {
        _history!.DeleteRecord(entry.Event.PeerId, entry.Event.EventId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        foreach (var file in entry.Event.Files) MediaPreview.Forget(file.Sha256);
        return LocalFiles.Delete(LocalFiles.OwnedExisting(entry.LocalFileReferences.Values)).Length;
    }

    public int HistoryCount => _selected is null || _history is null || string.IsNullOrWhiteSpace(_selected.Snapshot.PeerId) ? 0
        : _history.GetRecent(_selected.Snapshot.PeerId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Count;

    public void ClearHistory()
    {
        if (_selected is null || _history is null || string.IsNullOrWhiteSpace(_selected.Snapshot.PeerId)) return;
        try
        {
            int failed = 0;
            foreach (var entry in _history.GetRecent(_selected.Snapshot.PeerId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())) failed += DeleteEntry(entry);
            _status.Text = failed > 0 ? "聊天记录已清空，" + failed + " 个文件正在使用，未能删除" : "聊天记录已清空";
            RefreshSelectedHistory(); StateChanged?.Invoke();
        }
        catch (Exception error) { _status.Text = "清空记录失败：" + error.Message; }
    }

    private void CopyRecord(HistoryLocalRecord entry)
    {
        if (entry.Event.Text is null) return;
        try
        {
            Clipboard.SetText(entry.Event.Text, TextDataFormat.UnicodeText);
            _status.Text = "已复制文本";
        }
        catch (ExternalException) { _status.Text = "剪贴板正忙，请稍后重试"; }
    }

    private void OpenRecord(HistoryLocalRecord entry)
    {
        string? path = entry.LocalFileReferences.Values.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));
        if (path is null) { _status.Text = "本地文件已不存在"; return; }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            _status.Text = "已打开本地文件";
        }
        catch (Exception error) { _status.Text = "打开文件失败：" + error.Message; }
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in AllControls(child)) yield return nested;
        }
    }

    private static void StyleButton(Button button)
    {
        button.BackColor = Color.Transparent;
        button.ForeColor = Ui.Ink;
        button.FlatAppearance.BorderColor = Ui.Line;
        button.FlatAppearance.MouseOverBackColor = Ui.AccentSoft;
        button.FlatAppearance.MouseDownBackColor = Ui.AccentSoftSolid;
    }
}


