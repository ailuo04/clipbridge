using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed record TransferRow(string Id, string Description, bool Pending)
{
    public override string ToString() => Description;
}

internal sealed class TransfersForm : Form
{
    private readonly ListBox _rows = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    public event Action<string, bool>? Decision;
    public event Action<string>? CancelTransfer;
    public TransfersForm(string directory)
    {
        Text = "文件请求与传输记录"; Size = new Size(700, 440); Font = Ui.Font(10f); StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.Controls.Add(new Label { Text = "保存位置：" + directory, Dock = DockStyle.Fill, AutoEllipsis = true }, 0, 0); layout.Controls.Add(_rows, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        foreach (var action in new[] { "接受", "拒绝", "取消传输", "打开目录" })
        {
            var button = new Button { Text = action, AutoSize = true }; buttons.Controls.Add(button);
            button.Click += (_, _) =>
            {
                if (action == "打开目录") { System.IO.Directory.CreateDirectory(directory); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true }); return; }
                if (_rows.SelectedItem is not TransferRow row) return;
                if (action == "取消传输") CancelTransfer?.Invoke(row.Id);
                else if (row.Pending) Decision?.Invoke(row.Id, action == "接受");
            };
        }
        layout.Controls.Add(buttons, 0, 2); Controls.Add(layout);
    }
    public void UpdateRows(IReadOnlyList<TransferRow> rows)
    {
        string selected = (_rows.SelectedItem as TransferRow)?.Id;
        _rows.BeginUpdate(); _rows.Items.Clear();
        foreach (var row in rows) { int i = _rows.Items.Add(row); if (row.Id == selected) _rows.SelectedIndex = i; }
        _rows.EndUpdate();
    }
}
