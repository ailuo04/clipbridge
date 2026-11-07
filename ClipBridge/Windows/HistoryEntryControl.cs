using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ClipBridge.Windows.History;

namespace ClipBridge.Windows;

internal sealed class HistoryEntryControl : UserControl
{
    private readonly HistoryLocalRecord _entry;
    private readonly string _localDeviceId;
    private readonly TextBox? _text;
    private readonly Label _title;
    private readonly Label _meta;
    private readonly FlowLayoutPanel _actions;
    private Point _dragStart;

    public event Action<HistoryLocalRecord>? DeleteRequested;
    public event Action<HistoryLocalRecord>? CopyRequested;
    public event Action<HistoryLocalRecord>? OpenRequested;

    public HistoryLocalRecord Entry => _entry;

    public HistoryEntryControl(HistoryLocalRecord entry, string localDeviceId)
    {
        _entry = entry;
        _localDeviceId = localDeviceId;
        AutoSize = false;
        Height = entry.Event.ContentType == "text" ? 142 : 116;
        Margin = new Padding(0, 0, 0, 10);
        Padding = new Padding(14, 12, 14, 12);
        BackColor = Ui.Surface;
        ForeColor = Ui.Ink;
        BorderStyle = BorderStyle.FixedSingle;
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = HistoryPresentation.ContentKind(entry.Event) + "历史记录";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(root);

        bool outgoing = string.Equals(entry.Event.SenderDeviceId, localDeviceId, StringComparison.Ordinal);
        _title = new Label
        {
            Text = $"{HistoryPresentation.Direction(outgoing)} · {HistoryPresentation.ContentKind(entry.Event)}",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = Ui.Font(10f, FontStyle.Bold),
            ForeColor = Ui.Ink,
            Margin = Padding.Empty
        };
        _meta = new Label
        {
            Text = BuildMetaText(),
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleRight,
            Font = Ui.Font(8.5f),
            ForeColor = Ui.Muted,
            Margin = Padding.Empty
        };
        root.Controls.Add(_title, 0, 0);
        root.Controls.Add(_meta, 1, 0);

        if (entry.Event.ContentType == "text")
        {
            _text = new TextBox
            {
                Text = entry.Event.Text ?? string.Empty,
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Ui.FieldSurface,
                ForeColor = Ui.Ink,
                Font = Ui.Font(9.5f),
                Margin = new Padding(0, 4, 8, 4),
                AccessibleName = "文本内容"
            };
            _text.MouseDown += BeginDrag;
            _text.MouseMove += ContinueDrag;
            _text.DoubleClick += (_, _) => CopyRequested?.Invoke(_entry);
            root.Controls.Add(_text, 0, 1);
            root.SetColumnSpan(_text, 2);
        }
        else
        {
            var files = new Label
            {
                Text = string.Join(Environment.NewLine, entry.Event.Files.Select(file => $"{file.Name}  ·  {FormatBytes(file.Size)}")),
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Ui.Font(9.5f),
                ForeColor = Ui.Ink,
                Margin = new Padding(0, 4, 8, 4),
                AccessibleName = "文件列表"
            };
            files.MouseDown += BeginDrag;
            files.MouseMove += ContinueDrag;
            root.Controls.Add(files, 0, 1);
            root.SetColumnSpan(files, 2);
        }

        _actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        foreach (string action in HistoryPresentation.Actions(entry))
        {
            var button = new Button
            {
                Text = action,
                AutoSize = true,
                Height = 24,
                FlatStyle = FlatStyle.Flat,
                BackColor = action == "删除记录" ? Ui.DangerSoft : Ui.SurfaceHover,
                ForeColor = action == "删除记录" ? Ui.Danger : Ui.Ink,
                Margin = new Padding(4, 0, 0, 0),
                Padding = new Padding(7, 0, 7, 0),
                AccessibleName = action
            };
            button.FlatAppearance.BorderColor = Ui.Line;
            button.Click += (_, _) =>
            {
                if (action == "复制") CopyRequested?.Invoke(_entry);
                else if (action == "打开本地文件") OpenRequested?.Invoke(_entry);
                else if (action == "删除记录") DeleteRequested?.Invoke(_entry);
            };
            _actions.Controls.Add(button);
        }
        root.Controls.Add(_actions, 0, 2);
        root.SetColumnSpan(_actions, 2);
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        BackColor = Ui.Surface;
        ForeColor = Ui.Ink;
        _title.ForeColor = Ui.Ink;
        _meta.ForeColor = Ui.Muted;
        if (_text is not null)
        {
            _text.BackColor = Ui.FieldSurface;
            _text.ForeColor = Ui.Ink;
        }
        foreach (Button button in _actions.Controls.OfType<Button>())
        {
            button.BackColor = button.Text == "删除记录" ? Ui.DangerSoft : Ui.SurfaceHover;
            button.ForeColor = button.Text == "删除记录" ? Ui.Danger : Ui.Ink;
            button.FlatAppearance.BorderColor = Ui.Line;
        }
        Invalidate();
    }

    private string BuildMetaText()
    {
        DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeMilliseconds(_entry.Event.CreatedUtc).ToLocalTime();
        return $"{timestamp:MM-dd HH:mm} · {HistoryPresentation.State(_entry.Event.State)} · {HistoryPresentation.ExpiryLabel(_entry.Event.CreatedUtc, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())}";
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) _dragStart = e.Location;
    }

    private void ContinueDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || Math.Abs(e.X - _dragStart.X) < SystemInformation.DoubleClickSize.Width / 2 && Math.Abs(e.Y - _dragStart.Y) < SystemInformation.DoubleClickSize.Height / 2) return;
        DataObject data = new DataObject();
        if (_entry.Event.ContentType == "text")
        {
            data.SetData(DataFormats.UnicodeText, _entry.Event.Text ?? string.Empty);
        }
        else
        {
            string[] paths = _entry.LocalFileReferences.Values.Where(File.Exists).ToArray();
            if (paths.Length == 0) return;
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { });
            var list = new System.Collections.Specialized.StringCollection();
            list.AddRange(paths);
            data.SetFileDropList(list);
        }
        DoDragDrop(data, DragDropEffects.Copy);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):0.#} MB";
        return $"{bytes / (1024d * 1024 * 1024):0.#} GB";
    }
}
