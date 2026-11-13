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
    // One message with its sender's avatar: the peer's device on the left, this
    // computer on the right, the way chat clients lay out a conversation.
    private sealed class MessageRow : Panel
    {
        private const int Avatar = 34;
        private const int Gutter = Avatar + 10;
        public HistoryEntryControl Bubble { get; }
        private readonly bool _outgoing;
        private readonly bool _peerIsComputer;

        public MessageRow(HistoryEntryControl bubble, bool outgoing, bool peerIsComputer)
        {
            Bubble = bubble;
            _outgoing = outgoing;
            _peerIsComputer = peerIsComputer;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Margin = new Padding(0, 0, 0, 6);
            Height = Math.Max(Avatar, bubble.Height);
            Controls.Add(bubble);
            LayoutBubble();
            Resize += (_, _) => LayoutBubble();
        }

        private void LayoutBubble()
        {
            int lane = Math.Max(80, ClientSize.Width - Gutter * 2);
            int width = Math.Max(120, (int)(lane * 0.82));
            Bubble.Width = Math.Min(Bubble.PreferredBubbleWidth, Math.Min(width, Math.Max(60, ClientSize.Width - Gutter)));
            Bubble.Left = HistoryPresentation.Alignment(_outgoing) == HorizontalAlignment.Right
                ? Math.Max(0, ClientSize.Width - Gutter - Bubble.Width)
                : Gutter;
            Bubble.Top = 0;
            Height = Math.Max(Avatar, Bubble.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            RectangleF tile = new(_outgoing ? ClientSize.Width - Avatar - 1 : 0, 0, Avatar, Avatar);
            // This computer is neutral; the other device carries the accent.
            VisualDrawing.DrawDeviceAvatar(e.Graphics, tile, _outgoing ? false : !_peerIsComputer, _outgoing ? Ui.Faint : Ui.PrimarySurface, Ui.PrimaryInk);
        }
    }

    private readonly List<MessageRow> _rows = new();
    private readonly List<DaySeparator> _separators = new();
    private readonly List<Control> _order = new();
    private HistoryLocalRecord[] _entries=Array.Empty<HistoryLocalRecord>();
    private string _signature = string.Empty;
    private string? _partition;
    private readonly Dictionary<string,(int Y,bool Follow)> _positions=new();
    public bool IsAtLatest => _rows.Count==0 || -_list.AutoScrollPosition.Y+_list.ClientSize.Height>=_list.DisplayRectangle.Height-40;
    private (int Y,bool Follow)? _restorePosition;
    public void SelectPartition(string peerId) { if(_partition==peerId)return;if(_partition!=null)_positions[_partition]=(-_list.AutoScrollPosition.Y,IsAtLatest);_partition=peerId;_restorePosition=_positions.TryGetValue(peerId,out var saved)?saved:(0,true);_signature=""; }

    // Picks the avatar drawn beside the peer's messages.
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool PeerIsComputer { get; set; }

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
        string signature = localDeviceId + ":" + PeerIsComputer + ":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / (24L * 60 * 60 * 1000) + ":" + string.Join("\\n", values.Select(entry => entry.Event.PeerId + ":" + entry.Event.EventId + ":" + entry.Event.CreatedUtc + ":" + entry.Event.State + ":" + entry.LocalFileReferences.Count));
        if (signature == _signature) return;
        _signature = signature;
        _entries=values;
        _list.AutoScrollPosition = Point.Empty;
        _list.SuspendLayout();
        try
        {
            foreach (MessageRow row in _rows) row.Dispose();
            _rows.Clear();
            foreach (DaySeparator marker in _separators) marker.Dispose();
            _separators.Clear();
            _order.Clear();
            _list.Controls.Clear();
            DateTime? currentDay = null;
            foreach (HistoryLocalRecord entry in values.SelectMany(PresentationRecords))
            {
                DateTime day = DateTimeOffset.FromUnixTimeMilliseconds(entry.Event.CreatedUtc).ToLocalTime().Date;
                if (currentDay != day)
                {
                    currentDay = day;
                    var marker = new DaySeparator(day)
                    {
                        Width = Math.Max(260, _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8),
                        Margin = new Padding(0, 0, 0, 2)
                    };
                    _separators.Add(marker);
                    _order.Add(marker);
                    _list.Controls.Add(marker);
                }
                var bubble = new HistoryEntryControl(entry, localDeviceId);
                bool outgoing = string.Equals(entry.Event.SenderDeviceId, localDeviceId, StringComparison.Ordinal);
                var row = new MessageRow(bubble, outgoing, PeerIsComputer)
                {
                    Width = Math.Max(260, _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8),
                    MinimumSize = new Size(260, 0)
                };
                bubble.DeleteRequested += value => DeleteRequested?.Invoke(value);
                bubble.CopyRequested += value => CopyRequested?.Invoke(value);
                bubble.OpenRequested += value => OpenRequested?.Invoke(value);
                _rows.Add(row);
                _order.Add(row);
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
        foreach (DaySeparator marker in _separators) marker.Dispose();
        _separators.Clear();
        _order.Clear();
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
        foreach (DaySeparator marker in _separators) marker.Invalidate();
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
        foreach (Control item in _order)
        {
            item.Width = width;
            item.Location = new Point(0, top);
            top += item.Height + item.Margin.Bottom;
        }
        _list.AutoScrollMinSize = new Size(0, top - _list.AutoScrollPosition.Y);
    }
}
