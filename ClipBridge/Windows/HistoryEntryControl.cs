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
    private readonly HistoryLocalRecord _entry;
    private readonly bool _outgoing;
    private readonly Panel _content = new() { BackColor = Color.Transparent };
    private readonly Label _meta = new() { AutoSize = false, BackColor = Color.Transparent };
    private readonly TextBox? _text;
    private readonly Label? _files;
    private readonly Label? _icon;
    private readonly PictureBox? _preview;
    private readonly RoundedContextMenuStrip _menu = new();
    private Point _dragStart;
    private bool _sizing;
    public event Action<HistoryLocalRecord>? DeleteRequested;
    public event Action<HistoryLocalRecord>? CopyRequested;
    public event Action<HistoryLocalRecord>? OpenRequested;
    public HistoryLocalRecord Entry => _entry;
    public int PreferredBubbleWidth => _entry.Event.ContentType == "file" ? 350 : Math.Clamp(TextRenderer.MeasureText(_entry.Event.Text ?? "", _text?.Font ?? Font).Width + 36, 150, 460);

    public HistoryEntryControl(HistoryLocalRecord entry, string localDeviceId)
    {
        _entry = entry;
        _outgoing = entry.Event.SenderDeviceId == localDeviceId;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
        AccessibleName = (_outgoing ? "我发送的" : "对方发来的") + HistoryPresentation.ContentKind(entry.Event);
        _meta.Font = Ui.Font(8f);
        _meta.TextAlign = _outgoing ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
        _meta.Text = DateTimeOffset.FromUnixTimeMilliseconds(entry.Event.CreatedUtc).ToLocalTime().ToString("MM-dd HH:mm") + "  ·  " + HistoryPresentation.State(entry.Event.State);
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
            Image? image=path == null ? null : MediaPreview.Load(path);
            if(image!=null) {
                _preview=new PictureBox { Image=image, SizeMode=PictureBoxSizeMode.Zoom, Cursor=Cursors.Hand, BackColor=Color.Transparent };
                _preview.Click+=(_,_)=>OpenRequested?.Invoke(_entry);
                _content.Controls.Add(_preview);
            }
            _icon = new Label { Text = visibleFiles.Any(f=>f.IsDirectory)?"▣":visibleFiles.Any(f=>MediaPreview.IsVideo(f.Name)) ? "▶" : "▤", Font = Ui.Font(24f), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent };
            _files = new Label { Text = string.Join("\n", visibleFiles.Select(f => f.Name + "\n" + (f.IsDirectory?"文件夹 · 点击打开":FormatBytes(f.Size)))), Font = Ui.Font(10f), BackColor = Color.Transparent, AutoEllipsis = true, Cursor = Cursors.Hand };
            _files.Click += (_, _) => OpenRequested?.Invoke(_entry);
            _icon.Click += (_,_)=>OpenRequested?.Invoke(_entry);_icon.Cursor=Cursors.Hand;
            _content.Click+=(_,_)=>OpenRequested?.Invoke(_entry);_content.Cursor=Cursors.Hand;
            Click+=(_,_)=>OpenRequested?.Invoke(_entry);Cursor=Cursors.Hand;
            _content.Controls.Add(_icon); _content.Controls.Add(_files);
        }
        foreach (string action in entry.Event.State.Contains('%') ? Array.Empty<string>() : HistoryPresentation.Actions(entry))
            _menu.Items.Add(action, null, (_, _) => { if (action == "复制") CopyRequested?.Invoke(_entry); else if (action == "打开本地文件") OpenRequested?.Invoke(_entry); else DeleteRequested?.Invoke(_entry); });
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
            int inner = Math.Max(60, Width-32);
            int contentHeight;
            if (_text != null) {
                int measured = TextRenderer.MeasureText(_text.Text.Length == 0 ? " " : _text.Text, _text.Font, new Size(inner, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height + 8;
                contentHeight = Math.Clamp(measured, 28, 240);
                _text.SetBounds(0, 0, inner, contentHeight);
                _text.ScrollBars = measured > 240 ? ScrollBars.Vertical : ScrollBars.None;
            } else {
                contentHeight = _preview != null ? 210 : Math.Clamp((_entry.Event.Files.Count(f=>f.RelativePath==null||!f.RelativePath.Contains('/')) * 48), 56, 240);
                _icon?.SetBounds(0, 0, 42, 50);
                _files?.SetBounds(54, _preview != null ? 170 : 0, Math.Max(30, inner-54), _preview != null ? 40 : contentHeight);
                if(_preview != null) { _preview.SetBounds(0,0,inner,160); _icon?.SetBounds(0,170,42,40); }
            }
            _content.SetBounds(16, 14, inner, contentHeight);
            Height = contentHeight + 54;
            _meta.SetBounds(4, Height-24, Width-8, 22);
        } finally { _sizing=false; }
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(_outgoing ? Ui.AccentSoftSolid : Ui.Surface);
        VisualDrawing.FillRoundedRectangle(e.Graphics, fill, new RectangleF(0, 0, Width-1, Height-27), 16);
        if (_entry.Event.State.Contains('%') && double.TryParse(_entry.Event.State.Split(" · ").Last().TrimEnd('%'), out double percent))
        {
            using var track = new SolidBrush(Ui.Line); using var progress = new SolidBrush(Ui.Accent);
            var bounds = new RectangleF(16,Height-35,Width-32,3);
            e.Graphics.FillRectangle(track,bounds); bounds.Width *= (float)Math.Clamp(percent/100,0,1); e.Graphics.FillRectangle(progress,bounds);
        }
    }
    public void ApplyTheme()
    {
        ForeColor=Ui.Ink; _meta.ForeColor=Ui.Muted;
        if(_text != null) { _text.BackColor=_outgoing ? Ui.AccentSoftSolid : Ui.Surface; _text.ForeColor=Ui.Ink; }
        if(_files != null) _files.ForeColor=Ui.Ink;
        if(_icon != null) _icon.ForeColor=Ui.Accent;
        _menu.BackColor=Ui.Surface; _menu.ForeColor=Ui.Ink; Invalidate(true);
    }
    protected override void Dispose(bool disposing) { if(disposing) { _preview?.Image?.Dispose(); _menu.Dispose(); } base.Dispose(disposing); }
    private static string FormatBytes(long size) => size < 1024 ? size+" B" : size < 1048576 ? $"{size/1024d:0.#} KB" : $"{size/1048576d:0.#} MB";
}
