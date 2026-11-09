using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ClipBridge.Windows.History;

namespace ClipBridge.Windows;

internal sealed class ConversationTimelineControl : UserControl
{
    private readonly Panel _list = new()
    {
        Dock = DockStyle.Fill,
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
    private sealed class MessageRow : Panel
    {
        public HistoryEntryControl Bubble { get; }
        private readonly bool _outgoing;

        public MessageRow(HistoryEntryControl bubble, bool outgoing)
        {
            Bubble = bubble;
            _outgoing = outgoing;
            Margin = new Padding(0, 0, 0, 8);
            Height = bubble.Height;
            Controls.Add(bubble);
            LayoutBubble();
            Resize += (_, _) => LayoutBubble();
        }

        private void LayoutBubble()
        {
            int width = Math.Max(120, (int)(ClientSize.Width * 0.78));
            Bubble.Width = Math.Min(Bubble.PreferredBubbleWidth, width);
            Bubble.Left = HistoryPresentation.Alignment(_outgoing) == HorizontalAlignment.Right
                ? Math.Max(0, ClientSize.Width - Bubble.Width)
                : 0;
            Bubble.Top = 0;
            Height = Bubble.Height;
        }
    }

    private readonly List<MessageRow> _rows = new();
    private HistoryLocalRecord[] _entries=Array.Empty<HistoryLocalRecord>();
    private string _signature = string.Empty;
    private string? _partition;
    private readonly Dictionary<string,(int Y,bool Follow)> _positions=new();
    public bool IsAtLatest => _rows.Count==0 || -_list.AutoScrollPosition.Y+_list.ClientSize.Height>=_list.DisplayRectangle.Height-40;
    private (int Y,bool Follow)? _restorePosition;
    public void SelectPartition(string peerId) { if(_partition==peerId)return;if(_partition!=null)_positions[_partition]=(-_list.AutoScrollPosition.Y,IsAtLatest);_partition=peerId;_restorePosition=_positions.TryGetValue(peerId,out var saved)?saved:(0,true);_signature=""; }

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

    public IReadOnlyList<HistoryLocalRecord> Entries => _entries;

    public void SetEntries(IEnumerable<HistoryLocalRecord> entries, string localDeviceId)
    {
        HistoryLocalRecord[] values = entries.OrderBy(e => e.Event.CreatedUtc).ThenBy(e => e.Event.EventId, StringComparer.Ordinal).ToArray();
        bool follow = _rows.Count == 0 || -_list.AutoScrollPosition.Y + _list.ClientSize.Height >= _list.DisplayRectangle.Height - 40;
        int savedY = -_list.AutoScrollPosition.Y;
        if(_restorePosition is { } restore){savedY=restore.Y;follow=restore.Follow;_restorePosition=null;}
        string signature = localDeviceId + ":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / (24L * 60 * 60 * 1000) + ":" + string.Join("\\n", values.Select(entry => entry.Event.PeerId + ":" + entry.Event.EventId + ":" + entry.Event.CreatedUtc + ":" + entry.Event.State + ":" + entry.LocalFileReferences.Count));
        if (signature == _signature) return;
        _signature = signature;
        _entries=values;
        _list.AutoScrollPosition = Point.Empty;
        _list.SuspendLayout();
        try
        {
            foreach (MessageRow row in _rows) row.Dispose();
            _rows.Clear();
            _list.Controls.Clear();
            foreach (HistoryLocalRecord entry in values.SelectMany(PresentationRecords))
            {
                var bubble = new HistoryEntryControl(entry, localDeviceId);
                bool outgoing = string.Equals(entry.Event.SenderDeviceId, localDeviceId, StringComparison.Ordinal);
                var row = new MessageRow(bubble, outgoing)
                {
                    Width = Math.Max(260, _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8),
                    MinimumSize = new Size(260, 0)
                };
                bubble.DeleteRequested += value => DeleteRequested?.Invoke(value);
                bubble.CopyRequested += value => CopyRequested?.Invoke(value);
                bubble.OpenRequested += value => OpenRequested?.Invoke(value);
                _rows.Add(row);
                _list.Controls.Add(row);
            }
            _empty.Visible = _rows.Count == 0;
            _list.Visible = _rows.Count > 0;
        }
        finally { _list.ResumeLayout(true); }
        LayoutRows();
        _list.AutoScrollPosition = new Point(0, follow ? _list.DisplayRectangle.Height : savedY);
    }

    private static IEnumerable<HistoryLocalRecord> PresentationRecords(HistoryLocalRecord record)
    {
        var files=record.Event.Files;
        if(record.Event.ContentType!="file" || files.Length<2 || record.Event.State.Contains('%')) {yield return record;yield break;}
        foreach(var root in files.Select((file,index)=>(file,index)).Where(x=>x.file.RelativePath==null||!x.file.RelativePath.Contains('/')))
        {
            var references=new Dictionary<string,string>();
            if(record.LocalFileReferences.TryGetValue(root.index.ToString(System.Globalization.CultureInfo.InvariantCulture),out string? path)) references["0"]=path;
            var items=root.file.IsDirectory?files.Where(f=>f.RelativePath==root.file.RelativePath||f.RelativePath?.StartsWith(root.file.RelativePath+"/",StringComparison.Ordinal)==true).ToArray():new[]{root.file};
            yield return new HistoryLocalRecord(record.Event with { Files=items },references);
        }
    }
    public void SetMessage(string message)
    {
        _signature = string.Empty;
        _entries=Array.Empty<HistoryLocalRecord>();
        foreach (MessageRow row in _rows) row.Dispose();
        _rows.Clear();
        _list.Controls.Clear();
        _list.AutoScrollMinSize = Size.Empty;
        _list.Visible = false;
        _empty.Text = message;
        _empty.Visible = true;
    }

    public void ApplyTheme()
    {
        BackColor = Ui.Canvas;
        _list.BackColor = Ui.Canvas;
        _empty.ForeColor = Ui.Muted;
        foreach (MessageRow row in _rows) row.Bubble.ApplyTheme();
        Invalidate(true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutRows();
    }

    private void LayoutRows()
    {
        int width = Math.Max(260, _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
        int top = _list.AutoScrollPosition.Y;
        foreach (MessageRow row in _rows)
        {
            row.Width = width;
            row.Location = new Point(0, top);
            top += row.Height + row.Margin.Bottom;
        }
        _list.AutoScrollMinSize = new Size(0, top - _list.AutoScrollPosition.Y);
    }
}
