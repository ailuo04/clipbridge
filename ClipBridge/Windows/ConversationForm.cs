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

internal sealed class ConversationForm : Form
{
    private sealed record PeerChoice(PeerSnapshot Snapshot)
    {
        public override string ToString() => Snapshot.Name + (Snapshot.RecentlySeen ? "  ·  在线" : "  ·  离线");
    }

    private readonly HistoryStore? _history;
    private readonly string _localDeviceId;
    private readonly Func<PeerSnapshot[]> _getPeers;
    private readonly Func<PeerSnapshot, string, CancellationToken, Task<bool>> _sendText;
    private readonly Action<string[]> _previewFiles;
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
    private readonly Button _openDirectory = new()
    {
        Name = "OpenReceiveDirectoryButton",
        Text = "打开保存位置",
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(4, 0, 0, 0)
    };
    private readonly Button _manage = new()
    {
        Name = "ManageDevicesButton",
        Text = "设备管理",
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(4, 0, 0, 0)
    };
    private readonly ConversationTimelineControl _timeline = new() { Name = "ConversationTimeline", Dock = DockStyle.Fill };
    private readonly TextBox _composer = new()
    {
        Name = "ConversationComposer",
        Multiline = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None,
        Dock = DockStyle.Fill,
        MinimumSize = new Size(0, 62),
        Margin = Padding.Empty,
        PlaceholderText = "输入文字，Ctrl+V 粘贴；拖入文件可发送"
    };
    private readonly Button _send = new()
    {
        Name = "ConversationSendButton",
        Text = "发送",
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(4, 0, 0, 0)
    };
    private readonly Button _attach = new()
    {
        Name = "ConversationAttachButton",
        Text = "选择文件",
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(4, 0, 0, 0)
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
        Text = "拖入文件到这里，松开后预览并选择设备",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        AutoEllipsis = true,
        Margin = Padding.Empty
    };
    private readonly TableLayoutPanel _root;
    private readonly TableLayoutPanel _composerShell;
    private PeerChoice? _selected;
    private bool _closingForExit;
    private bool _busy;

    public event Action? HiddenToTray;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AllowCloseForExit
    {
        get => _closingForExit;
        set => _closingForExit = value;
    }

    public PeerSnapshot? SelectedPeer => _selected?.Snapshot;
    public IReadOnlyList<HistoryLocalRecord> VisibleEntries => _timeline.Entries;

    public ConversationForm(
        HistoryStore? history,
        string localDeviceId,
        Func<PeerSnapshot[]> getPeers,
        Func<PeerSnapshot, string, CancellationToken, Task<bool>> sendText,
        Action<string[]> previewFiles,
        Action openReceiveDirectory,
        Action manageDevices)
    {
        _history = history;
        _localDeviceId = localDeviceId;
        _getPeers = getPeers;
        _sendText = sendText;
        _previewFiles = previewFiles;
        _openReceiveDirectory = openReceiveDirectory;
        _manageDevices = manageDevices;

        Text = "互传";
        AccessibleName = "互传会话窗口";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(780, 720);
        MinimumSize = new Size(540, 500);
        Font = Ui.Font(9.5f);
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = Ui.Canvas;
        ForeColor = Ui.Ink;
        AllowDrop = true;

        _root = new TableLayoutPanel
        {
            Name = "ConversationRoot",
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Ui.Canvas
        };
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(_root);

        _root.Controls.Add(BuildHeader(), 0, 0);
        _root.Controls.Add(_timeline, 0, 1);
        _composerShell = BuildComposer();
        _root.Controls.Add(_composerShell, 0, 2);
        _root.Controls.Add(BuildFooter(), 0, 3);

        _peers.SelectedIndexChanged += (_, _) => SelectPeerFromControl();
        _send.Click += async (_, _) => await SendTextAsync();
        _attach.Click += (_, _) => ChooseFiles();
        _openDirectory.Click += (_, _) => _openReceiveDirectory();
        _manage.Click += (_, _) => _manageDevices();
        _composer.KeyDown += ComposerKeyDown;
        _timeline.DeleteRequested += DeleteRecord;
        _timeline.CopyRequested += CopyRecord;
        _timeline.OpenRequested += OpenRecord;
        WireDrop(_dropZone);
        WireDrop(_composer);
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
        _status.ForeColor = Ui.Muted;
        _subtitle.ForeColor = Ui.Muted;
        _peers.BackColor = Ui.FieldSurface;
        _peers.ForeColor = Ui.Ink;
        _composer.BackColor = Ui.FieldSurface;
        _composer.ForeColor = Ui.Ink;
        _dropZone.BackColor = Ui.FieldSurface;
        _dropHint.ForeColor = Ui.Muted;
        foreach (Button button in new[] { _openDirectory, _manage, _send, _attach }) StyleButton(button);
        foreach (RoundedSurface surface in AllControls(this).OfType<RoundedSurface>())
        {
            surface.FillColor = Ui.Surface;
            surface.BorderColor = Ui.Line;
            surface.Invalidate();
        }
        _timeline.ApplyTheme();
        Invalidate(true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshPeers();
        _composer.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_closingForExit)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
            return;
        }
        _lifetime.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _lifetime.Dispose();
        base.Dispose(disposing);
    }

    private Control BuildHeader()
    {
        var surface = new RoundedSurface
        {
            Name = "ConversationHeader",
            Dock = DockStyle.Fill,
            CornerRadius = 16,
            FillColor = Ui.Surface,
            BorderColor = Ui.Line,
            Padding = new Padding(16, 7, 16, 7),
            Margin = new Padding(0, 0, 0, 8)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        var title = new Label { Text = "互传", Dock = DockStyle.Fill, Font = Ui.Font(15f, FontStyle.Bold), ForeColor = Ui.Ink, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
        layout.Controls.Add(title, 0, 0); layout.SetRowSpan(title, 2);
        layout.Controls.Add(_subtitle, 1, 0); layout.SetColumnSpan(_subtitle, 3);
        layout.Controls.Add(_peers, 1, 1);
        layout.Controls.Add(_status, 2, 1);
        var actions = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty };
        actions.Controls.Add(_manage);
        actions.Controls.Add(_openDirectory);
        layout.Controls.Add(actions, 3, 1);
        surface.Controls.Add(layout);
        return surface;
    }

    private TableLayoutPanel BuildComposer()
    {
        var shell = new TableLayoutPanel
        {
            Name = "ConversationComposerShell",
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 10, 0, 0),
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        var inputSurface = new RoundedSurface
        {
            Name = "ConversationInputSurface",
            Dock = DockStyle.Fill,
            CornerRadius = 15,
            FillColor = Ui.Surface,
            BorderColor = Ui.Line,
            Padding = new Padding(12),
            Margin = Padding.Empty
        };
        var inputLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent };
        inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        _dropZone.Controls.Add(_dropHint);
        _dropZone.Controls.Add(_composer);
        inputLayout.Controls.Add(_dropZone, 0, 0);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty };
        buttons.Controls.Add(_send);
        buttons.Controls.Add(_attach);
        inputLayout.Controls.Add(buttons, 1, 0);
        inputSurface.Controls.Add(inputLayout);
        shell.Controls.Add(inputSurface, 0, 0);
        var hint = new Label { Text = "只在点击发送后传输；历史记录保留 7 天", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ui.Muted, Font = Ui.Font(8.5f), Margin = new Padding(4, 3, 0, 0) };
        shell.Controls.Add(hint, 0, 1);
        return shell;
    }

    private Control BuildFooter()
    {
        return new Label
        {
            Name = "ConversationFooter",
            Text = "文本可复制、粘贴和拖动；文件可拖到其他软件。删除历史记录不会删除本地文件。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            ForeColor = Ui.Muted,
            Font = Ui.Font(8.5f),
            Margin = Padding.Empty
        };
    }

    private void SelectPeerFromControl()
    {
        _selected = _peers.SelectedItem as PeerChoice;
        if (_selected is null)
        {
            _status.Text = "未连接";
            _subtitle.Text = "暂无设备 · 请先完成配对";
            _timeline.SetMessage("连接设备后，这里会显示最近 7 天的收发记录。\r\n历史记录只保留 7 天，删除记录不会删除本地文件。\r\n\r\n请从设备管理完成配对，然后回到这里开始发送和接收。");
            return;
        }
        _status.Text = _selected.Snapshot.RecentlySeen ? "在线" : "离线";
        _subtitle.Text = $"{_selected.Snapshot.Name} · {_status.Text} · {_selected.Snapshot.Address}";
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
            _timeline.SetEntries(_history.GetRecent(_selected.Snapshot.PeerId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), _localDeviceId);
        }
        catch (Exception error)
        {
            _timeline.SetMessage("历史记录暂时不可用：" + error.Message);
        }
    }

    private async Task SendTextAsync()
    {
        if (_busy) return;
        PeerSnapshot? target = SelectedPeer;
        string text = _composer.Text;
        if (target is null) { _status.Text = "请先选择设备"; return; }
        if (string.IsNullOrWhiteSpace(text)) { _status.Text = "请输入要发送的文字"; return; }
        _busy = true;
        _send.Enabled = false;
        _status.Text = "正在发送…";
        try
        {
            bool sent = await _sendText(target, text, _lifetime.Token);
            _status.Text = sent ? "已发送" : "发送失败，请检查连接";
            if (sent) { _composer.Clear(); RefreshSelectedHistory(); }
        }
        catch (OperationCanceledException) { _status.Text = "已取消"; }
        catch (Exception error) { _status.Text = error.Message; }
        finally { _busy = false; if (!IsDisposed) _send.Enabled = SelectedPeer?.RecentlySeen == true; }
    }

    private void ComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && e.Control)
        {
            e.SuppressKeyPress = true;
            _ = SendTextAsync();
        }
    }

    private void ChooseFiles()
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Title = "选择要发送的文件" };
        if (dialog.ShowDialog(this) == DialogResult.OK) _previewFiles(dialog.FileNames);
    }

    private void WireDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        control.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            {
                string[] files = paths.Where(File.Exists).Take(256).ToArray();
                if (files.Length > 0) _previewFiles(files);
            }
        };
    }

    private void DeleteRecord(HistoryLocalRecord entry)
    {
        if (_history is null) return;
        try
        {
            _history.DeleteRecord(entry.Event.PeerId, entry.Event.EventId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _status.Text = "已删除历史记录，本地文件保留";
            RefreshSelectedHistory();
        }
        catch (Exception error) { _status.Text = "删除记录失败：" + error.Message; }
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
        string? path = entry.LocalFileReferences.Values.FirstOrDefault(File.Exists);
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
        button.BackColor = Ui.SurfaceHover;
        button.ForeColor = Ui.Ink;
        button.FlatAppearance.BorderColor = Ui.Line;
        button.FlatAppearance.MouseOverBackColor = Ui.AccentSoft;
        button.FlatAppearance.MouseDownBackColor = Ui.AccentSoftSolid;
    }
}


