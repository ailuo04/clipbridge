using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ClipBridge.Windows.History;

namespace ClipBridge.Windows;

internal sealed class HistoryEntryControl : UserControl
{
    private const int MetaBand = 22;
    private const int PadX = 13;
    private const int PadY = 9;
    private const float Radius = 8f;
    // The control is at least wide enough for the time/status line; the bubble
    // itself hugs short text and sits against the sender's side.
    private int _bubbleLeft;
    private int _bubbleWidth;
    // Only sent text is tinted. File cards stay white in both directions so the
    // name, size and thumbnail read the same whoever sent them.
    private bool Tinted => _outgoing && _entry.Event.ContentType == "text";
    private int NaturalTextWidth => TextRenderer.MeasureText(string.IsNullOrEmpty(_entry.Event.Text) ? " " : _entry.Event.Text, _text?.Font ?? Font).Width + PadX * 2 + 8;
    private readonly HistoryLocalRecord _entry;
    private readonly bool _outgoing;
    private readonly Panel _content = new() { BackColor = Color.Transparent };
    private readonly Label _meta = new() { AutoSize = false, BackColor = Color.Transparent };
    private readonly TextBox? _text;
    private readonly Label? _files;
    private readonly Label? _fileMeta;
    private readonly Label? _icon;
    private readonly PictureBox? _preview;
    private readonly Label? _more;
    private readonly RoundedContextMenuStrip _menu = new();
    private Point _dragStart;
    private bool _sizing;
    public event Action<HistoryLocalRecord>? DeleteRequested;
    public event Action<HistoryLocalRecord>? CopyRequested;
    public event Action<HistoryLocalRecord>? OpenRequested;
    public HistoryLocalRecord Entry => _entry;
    public int PreferredBubbleWidth => _preview?.Image != null ? Math.Max(148, _preview.Image.Width + 8) : _entry.Event.ContentType == "file" ? 320 : Math.Clamp(NaturalTextWidth, 148, 460);

    public HistoryEntryControl(HistoryLocalRecord entry, string localDeviceId)
    {
        _entry = entry;
        _outgoing = entry.Event.SenderDeviceId == localDeviceId;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
        AccessibleName = (_outgoing ? "我发送的" : "对方发来的") + HistoryPresentation.ContentKind(entry.Event);
        _meta.Font = Ui.Font(7.8f);
        _meta.TextAlign = _outgoing ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
        // The day marker above the group already carries the date.
        _meta.Text = DateTimeOffset.FromUnixTimeMilliseconds(entry.Event.CreatedUtc).ToLocalTime().ToString("HH:mm") + " · " + HistoryPresentation.State(entry.Event.State);
        Controls.Add(_content); Controls.Add(_meta);
        if (entry.Event.ContentType == "text")
        {
            _text = new TextBox { Text = entry.Event.Text ?? "", Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, Font = Ui.Font(10f), WordWrap = true, AccessibleName = "消息内容" };
            _content.Controls.Add(_text);
            _text.DoubleClick += (_, _) => CopyRequested?.Invoke(_entry);
        }
        else
        {
            var visibleFiles=entry.Event.Files.Where(f=>f.RelativePath==null||!f.RelativePath.Contains('/')).ToArray();
            string? path=visibleFiles.Length==1 ? entry.LocalFileReferences.Values.FirstOrDefault() : null;
            // A single picture is shown as the picture itself, like a chat
            // photo. The stored thumbnail covers originals that have since moved.
            Image? image=null;
            if(visibleFiles.Length==1 && !visibleFiles[0].IsDirectory && MediaPreview.IsImage(visibleFiles[0].Name)) {
                image=path == null ? null : MediaPreview.Load(path,300,220);
                if(image!=null) MediaPreview.Remember(visibleFiles[0].Sha256,image); else image=MediaPreview.Recall(visibleFiles[0].Sha256);
            }
            Click+=(_,_)=>OpenRequested?.Invoke(_entry);Cursor=Cursors.Hand;
            _content.Click+=(_,_)=>OpenRequested?.Invoke(_entry);_content.Cursor=Cursors.Hand;
            if(image!=null) {
                _preview=new PictureBox { Image=image, SizeMode=PictureBoxSizeMode.Zoom, Cursor=Cursors.Hand, BackColor=Color.Transparent, AccessibleName=visibleFiles[0].Name };
                _preview.Click+=(_,_)=>OpenRequested?.Invoke(_entry);
                _content.Controls.Add(_preview);
            } else {
                _icon = new Label { Text = visibleFiles.Any(f=>f.IsDirectory)?"▣":visibleFiles.Any(f=>MediaPreview.IsVideo(f.Name)) ? "▶" : "▤", Font = Ui.Font(13f), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent };
                _files = new Label { Text = string.Join("\n", visibleFiles.Select(f => (f.IsDirectory?"▣ ":"") + f.Name)), Font = Ui.Font(9.5f, FontStyle.Bold), BackColor = Color.Transparent, AutoEllipsis = true, Cursor = Cursors.Hand };
                _fileMeta = new Label { Text = visibleFiles.Length == 1 ? (visibleFiles[0].IsDirectory ? "文件夹 · 点击打开" : FormatBytes(visibleFiles[0].Size)) : visibleFiles.Length + " 个项目 · 点击打开", Font = Ui.Font(8.5f), BackColor = Color.Transparent, AutoEllipsis = true, Cursor = Cursors.Hand };
                _files.Click += (_, _) => OpenRequested?.Invoke(_entry);
                _fileMeta.Click += (_, _) => OpenRequested?.Invoke(_entry);
                _icon.Click += (_,_)=>OpenRequested?.Invoke(_entry);_icon.Cursor=Cursors.Hand;
                _content.Controls.Add(_icon); _content.Controls.Add(_files); _content.Controls.Add(_fileMeta);
            }
        }
        foreach (string action in entry.Event.State.Contains('%') ? Array.Empty<string>() : HistoryPresentation.Actions(entry))
            _menu.Items.Add(action, null, (_, _) => { if (action == "复制") CopyRequested?.Invoke(_entry); else if (action == "打开本地文件") OpenRequested?.Invoke(_entry); else DeleteRequested?.Invoke(_entry); });
        if (_menu.Items.Count > 0)
        {
            // Visible entry point for the same actions as the right-click menu.
            _more = new Label { Name = "MessageMoreButton", Text = "•••", Font = Ui.Font(7f), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent, Cursor = Cursors.Hand, AccessibleName = "更多操作", AccessibleRole = AccessibleRole.ButtonMenu };
            _more.Click += (_, _) => _menu.Show(_more, new Point(0, _more.Height));
            Controls.Add(_more); _more.BringToFront();
        }
        if (entry.Event.Files.Length > 1)
            foreach (var reference in entry.LocalFileReferences)
            {
                string path = reference.Value;
                _menu.Items.Add("打开 " + Path.GetFileName(path), null, (_, _) => OpenRequested?.Invoke(entry with { LocalFileReferences = new System.Collections.Generic.Dictionary<string, string> { [reference.Key] = path } }));
            }
        Wire(this);
        Width = PreferredBubbleWidth;
        ApplyTheme(); LayoutContent();
    }

    private void Wire(Control control)
    {
        control.ContextMenuStrip = _menu;
        control.MouseDown += (_, e) => { if(e.Button == MouseButtons.Left) _dragStart = e.Location; };
        if (_entry.Event.ContentType == "file") control.MouseMove += (_, e) => {
            if (e.Button != MouseButtons.Left || Math.Abs(e.X-_dragStart.X)+Math.Abs(e.Y-_dragStart.Y)<10) return;
            var paths = _entry.LocalFileReferences.Values.Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();
            if(paths.Length==0) return;
            var collection = new System.Collections.Specialized.StringCollection(); collection.AddRange(paths);
            var data = new DataObject(); data.SetFileDropList(collection); DoDragDrop(data, DragDropEffects.Copy);
        };
        foreach(Control child in control.Controls) Wire(child);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); if (_entry != null) LayoutContent(); }
    private void LayoutContent()
    {
        if (_sizing) return;
        _sizing = true;
        try {
            // Pictures sit almost edge to edge; text and file cards get room.
            int padX = _preview != null ? 4 : PadX, padY = _preview != null ? 4 : PadY;
            _bubbleWidth = _text != null ? Math.Clamp(NaturalTextWidth, 44, Math.Max(44, Width))
                : _preview?.Image != null ? Math.Clamp(_preview.Image.Width + padX * 2, 44, Math.Max(44, Width)) : Width;
            _bubbleLeft = _outgoing ? Width - _bubbleWidth : 0;
            int inner = Math.Max(16, _bubbleWidth - padX * 2);
            int contentHeight;
            if (_text != null) {
                int measured = TextRenderer.MeasureText(_text.Text.Length == 0 ? " " : _text.Text, _text.Font, new Size(inner, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height + 4;
                contentHeight = Math.Clamp(measured, 22, 240);
                _text.SetBounds(0, 0, inner, contentHeight);
                _text.ScrollBars = measured > 240 ? ScrollBars.Vertical : ScrollBars.None;
            } else if (_preview?.Image != null) {
                contentHeight = Math.Max(24, (int)Math.Round(_preview.Image.Height * (inner / (double)Math.Max(1, _preview.Image.Width))));
                _preview.SetBounds(0, 0, inner, contentHeight);
            } else {
                int count = _entry.Event.Files.Count(f=>f.RelativePath==null||!f.RelativePath.Contains('/'));
                bool single = count <= 1;
                contentHeight = single ? 42 : Math.Clamp(count * 24, 56, 240);
                _icon?.SetBounds(0, 1, 40, 40);
                _files?.SetBounds(50, single ? 1 : 0, Math.Max(30, inner-50), single ? 21 : contentHeight);
                _fileMeta?.SetBounds(50, 22, Math.Max(30, inner-50), 18);
                if (_fileMeta != null) _fileMeta.Visible = single;
            }
            _content.SetBounds(_bubbleLeft + padX, padY, inner, contentHeight);
            Height = contentHeight + padY * 2 + MetaBand;
            int more = _more != null ? 28 : 0;
            _meta.SetBounds(_outgoing ? 2 + more : 2, Height - MetaBand + 1, Width - 4 - more, MetaBand - 2);
            _more?.SetBounds(_outgoing ? 0 : Width - more, Height - MetaBand + 1, more, MetaBand - 2);
        } finally { _sizing=false; }
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Flat bubble: fill and a hairline carry it, no drop shadow.
        RectangleF bubble = new(_bubbleLeft + 0.5f, 0.5f, Math.Max(8f, _bubbleWidth - 1.5f), Math.Max(8f, Height - MetaBand - 1.5f));
        using (var fill = new SolidBrush(Tinted ? Ui.BubbleOutgoing : Ui.Surface)) VisualDrawing.FillRoundedRectangle(g, fill, bubble, Radius);
        if (!Tinted)
        {
            using var border = new Pen(Ui.Line, 1f);
            VisualDrawing.DrawRoundedRectangle(g, border, bubble, Radius);
        }
        if (_icon != null)
        {
            RectangleF tile = new(_content.Left + _icon.Left, _content.Top + _icon.Top, _icon.Width, _icon.Height);
            using var tileFill = new SolidBrush(Ui.AccentSoftSolid);
            VisualDrawing.FillRoundedRectangle(g, tileFill, tile, 8);
        }
        if (_entry.Event.State.Contains('%') && double.TryParse(_entry.Event.State.Split(" · ").Last().TrimEnd('%'), out double percent))
        {
            using var track = new SolidBrush(Tinted ? Color.FromArgb(70, Color.White) : Ui.Line);
            using var progress = new SolidBrush(Tinted ? Ui.BubbleOutgoingInk : Ui.Accent);
            RectangleF bounds = new(bubble.Left + PadX, bubble.Bottom - 6, Math.Max(8f, bubble.Width - PadX * 2), 3);
            VisualDrawing.FillRoundedRectangle(g, track, bounds, 1.5f);
            bounds.Width *= (float)Math.Clamp(percent/100,0,1);
            if (bounds.Width > 0) VisualDrawing.FillRoundedRectangle(g, progress, bounds, 1.5f);
        }
    }
    public void ApplyTheme()
    {
        ForeColor=Ui.Ink; _meta.ForeColor=Ui.Faint; if(_more != null) _more.ForeColor=Ui.Muted;
        Color ink = Tinted ? Ui.BubbleOutgoingInk : Ui.Ink;
        if(_text != null) { _text.BackColor=Tinted ? Ui.BubbleOutgoing : Ui.Surface; _text.ForeColor=ink; }
        if(_files != null) _files.ForeColor=ink;
        if(_fileMeta != null) _fileMeta.ForeColor=Ui.Muted;
        if(_icon != null) _icon.ForeColor=Ui.Accent;
        _menu.BackColor=Ui.Surface; _menu.ForeColor=Ui.Ink; Invalidate(true);
    }
    protected override void Dispose(bool disposing) { if(disposing) { _preview?.Image?.Dispose(); _menu.Dispose(); } base.Dispose(disposing); }
    private static string FormatBytes(long size) => size < 1024 ? size+" B" : size < 1048576 ? $"{size/1024d:0.#} KB" : $"{size/1048576d:0.#} MB";
}
