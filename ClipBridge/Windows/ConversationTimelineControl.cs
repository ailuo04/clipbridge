using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ClipBridge.Windows.History;

namespace ClipBridge.Windows;

internal sealed class ConversationTimelineControl : UserControl
{
    private readonly FlowLayoutPanel _list = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(2),
        Margin = Padding.Empty
    };
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = Ui.Font(10f),
        ForeColor = Ui.Muted,
        Text = "选择设备后，这里会显示最近 7 天的收发记录。"
    };
    private readonly List<HistoryEntryControl> _rows = new();
    private string _signature = string.Empty;

    public event Action<HistoryLocalRecord>? DeleteRequested;
    public event Action<HistoryLocalRecord>? CopyRequested;
    public event Action<HistoryLocalRecord>? OpenRequested;

    public ConversationTimelineControl()
    {
        SetStyle(ControlStyles.Selectable, true);
        BackColor = Ui.Canvas;
        Controls.Add(_empty);
        Controls.Add(_list);
        AccessibleName = "历史记录时间线";
        ApplyTheme();
    }

    public IReadOnlyList<HistoryLocalRecord> Entries => _rows.Select(row => row.Entry).ToArray();

    public void SetEntries(IEnumerable<HistoryLocalRecord> entries, string localDeviceId)
    {
        HistoryLocalRecord[] values = entries.ToArray();
        string signature = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / (24L * 60 * 60 * 1000) + ":" + string.Join("\\n", values.Select(entry => entry.Event.EventId + ":" + entry.Event.CreatedUtc + ":" + entry.Event.State + ":" + entry.LocalFileReferences.Count));
        if (signature == _signature) return;
        _signature = signature;
        _list.SuspendLayout();
        try
        {
            foreach (HistoryEntryControl row in _rows) row.Dispose();
            _rows.Clear();
            _list.Controls.Clear();
            foreach (HistoryLocalRecord entry in values)
            {
                var row = new HistoryEntryControl(entry, localDeviceId)
                {
                    Width = Math.Max(260, _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8),
                    Anchor = AnchorStyles.Left | AnchorStyles.Right
                };
                row.DeleteRequested += entry => DeleteRequested?.Invoke(entry);
                row.CopyRequested += entry => CopyRequested?.Invoke(entry);
                row.OpenRequested += entry => OpenRequested?.Invoke(entry);
                _rows.Add(row);
                _list.Controls.Add(row);
            }
            _empty.Visible = _rows.Count == 0;
            _list.Visible = _rows.Count > 0;
        }
        finally { _list.ResumeLayout(true); }
    }

    public void SetMessage(string message)
    {
        foreach (HistoryEntryControl row in _rows) row.Dispose();
        _rows.Clear();
        _list.Controls.Clear();
        _list.Visible = false;
        _empty.Text = message;
        _empty.Visible = true;
    }

    public void ApplyTheme()
    {
        BackColor = Ui.Canvas;
        _list.BackColor = Ui.Canvas;
        _empty.ForeColor = Ui.Muted;
        foreach (HistoryEntryControl row in _rows) row.ApplyTheme();
        Invalidate(true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        int width = Math.Max(260, _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
        foreach (Control row in _list.Controls) row.Width = width;
    }
}
