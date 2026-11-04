using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class UpdateDialog : Form
{
    private readonly Label _title, _metadata, _phase, _details, _percent, _hint;
    private readonly UpdateReleaseNotes _notes;
    private readonly UpdateGlassButton _install, _cancel, _close;
    private readonly UpdateProgressBar _bar = new() { Name = "UpdateProgressBar", Dock = DockStyle.Fill };
    private readonly UpdateStepStrip _steps = new() { Name = "UpdateSteps", Dock = DockStyle.Fill };
    private readonly bool _applying;
    private bool _busy, _canCancel, _cancelSent, _allowClose, _failed, _completed;
    public event Action? InstallRequested;
    public event Action? CancelRequested;

    public UpdateDialog(string version, string notes, long packageSize, bool applying = false)
    {
        _applying = applying;
        Text = "ClipBridge · 软件更新"; Icon = AppIcon.Load();
        StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(600, 642); FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false; MinimizeBox = false; Font = Ui.Font(9);
        DoubleBuffered = true;
        var root = new TableLayoutPanel { Name = "UpdateLayout", Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(28, 22, 28, 20), ColumnCount = 1, RowCount = 10, Margin = Padding.Empty };
        foreach (RowStyle row in new[]
        {
            new RowStyle(SizeType.Absolute, 30), new RowStyle(SizeType.Absolute, 14),
            new RowStyle(SizeType.Absolute, 112), new RowStyle(SizeType.Absolute, 18),
            new RowStyle(SizeType.Percent, 100), new RowStyle(SizeType.Absolute, 16),
            new RowStyle(SizeType.Absolute, 174), new RowStyle(SizeType.Absolute, 20),
            new RowStyle(SizeType.Absolute, 50), new RowStyle(SizeType.Absolute, 32)
        }) root.RowStyles.Add(row);

        var chrome = new TableLayoutPanel { Name = "UpdateChrome", Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        chrome.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28)); chrome.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); chrome.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        chrome.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var brand = new BrandMark { Name = "UpdateBrand", Size = new Size(22, 22), Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        var brandName = Label("UpdateBrandName", "ClipBridge  /  软件更新", 9, FontStyle.Regular);
        _close = new UpdateGlassButton("", close: true) { Name = "UpdateCloseButton", AccessibleName = "关闭更新窗口", Dock = DockStyle.Fill, Margin = Padding.Empty, TabIndex = 3 };
        _close.Click += (_, _) => Close();
        chrome.Controls.Add(brand, 0, 0); chrome.Controls.Add(brandName, 1, 0); chrome.Controls.Add(_close, 2, 0); root.Controls.Add(chrome, 0, 0);

        var hero = new TableLayoutPanel { Name = "UpdateHero", Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        hero.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112)); hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); hero.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var emblem = new UpdateEmblem { Name = "UpdateEmblem", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 10, 0, 6), Margin = Padding.Empty };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _title = Label("UpdateTitle", applying ? "即将焕然一新" : "让互传更好用", 18, FontStyle.Bold);
        _metadata = Label("UpdateMetadata", applying ? "正在安装已验证的更新" : $"v{UpdateProtocol.CurrentVersion.ToString(3)}  →  v{version}   ·   {SizeText(packageSize)}", 9.5f);
        _hint = Label("UpdateHint", "新版本已就绪，安装后自动为你打开。", 9);
        heading.Controls.Add(_title, 0, 0); heading.Controls.Add(_metadata, 0, 1); heading.Controls.Add(_hint, 0, 2);
        hero.Controls.Add(emblem, 0, 0); hero.Controls.Add(heading, 1, 0); root.Controls.Add(hero, 0, 2);

        var notesSurface = new UpdateGlassSurface { Name = "UpdateNotesSurface", Dock = DockStyle.Fill, Padding = new Padding(20, 14, 20, 14), Margin = Padding.Empty };
        var notesLayout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        notesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); notesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var notesTitle = Label("UpdateNotesTitle", "本次更新", 9.5f, FontStyle.Bold);
        _notes = new UpdateReleaseNotes { Name = "UpdateNotes", Dock = DockStyle.Fill, Margin = Padding.Empty, Text = string.IsNullOrWhiteSpace(notes) ? "更流畅的使用体验与细节改进。" : notes, TabIndex = 2 };
        notesLayout.Controls.Add(notesTitle, 0, 0); notesLayout.Controls.Add(_notes, 0, 1); notesSurface.Controls.Add(notesLayout); root.Controls.Add(notesSurface, 0, 4);

        var progressSurface = new UpdateGlassSurface { Name = "UpdateProgressSurface", Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 12), Margin = Padding.Empty };
        var progress = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 4, Margin = Padding.Empty };
        progress.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); progress.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
        progress.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); progress.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); progress.RowStyles.Add(new RowStyle(SizeType.Absolute, 16)); progress.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _phase = Label("UpdatePhase", "准备迎接新版本", 11, FontStyle.Bold);
        _percent = Label("UpdatePercent", "0%", 22, FontStyle.Bold); _percent.TextAlign = ContentAlignment.MiddleRight;
        _details = Label("UpdateDetails", "下载、安装和重新打开，会在这里为你完成。", 9);
        progress.Controls.Add(_phase, 0, 0); progress.Controls.Add(_percent, 1, 0); progress.Controls.Add(_details, 0, 1); progress.SetColumnSpan(_details, 2);
        progress.Controls.Add(_bar, 0, 2); progress.SetColumnSpan(_bar, 2); progress.Controls.Add(_steps, 0, 3); progress.SetColumnSpan(_steps, 2);
        progressSurface.Controls.Add(progress); root.Controls.Add(progressSurface, 0, 6);

        var actions = new TableLayoutPanel { Name = "UpdateActions", Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty, ColumnCount = 2, RowCount = 1 };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _install = new UpdateGlassButton("下载并安装", primary: true) { Name = "UpdateInstallButton", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0), TabIndex = 0 };
        _cancel = new UpdateGlassButton("稍后再说") { Name = "UpdateCancelButton", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0), TabIndex = 1 };
        _install.Click += (_, _) => InstallRequested?.Invoke();
        _cancel.Click += (_, _) => { if (_busy) RequestCancellation(); else Close(); };
        actions.Controls.Add(_cancel, 0, 0); actions.Controls.Add(_install, 1, 0); root.Controls.Add(actions, 0, 8);
        var footer = Label("UpdateFooter", "安全校验  ·  完成后自动打开", 8.3f); footer.TextAlign = ContentAlignment.MiddleCenter; root.Controls.Add(footer, 0, 9);
        Controls.Add(root); AcceptButton = _install; CancelButton = _cancel;
        foreach (Control draggable in new Control[] { chrome, brand, brandName, hero, emblem, heading, _title, _metadata, _hint }) draggable.MouseDown += DragWindow;
        ApplyTheme(); UpdateWindowShape();
        if (applying) Report(new UpdateProgress(UpdateStage.Waiting));
    }
    private static Label Label(string name, string text, float size, FontStyle style = FontStyle.Regular) => new() { Name = name, Text = text, Font = Ui.Font(size, style), BackColor = Color.Transparent, Dock = DockStyle.Fill, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private static string SizeText(double value) => value >= 1024 * 1024 ? $"{value / (1024 * 1024):0.0} MB" : $"{value / 1024:0.0} KB";
    public void BeginDownload()
    {
        _failed = _completed = false;
        _busy = true; _canCancel = true; _cancelSent = false;
        _install.Enabled = false; _install.Text = "正在更新"; _cancel.Enabled = true; _cancel.Text = "取消下载";
        _title.Text = "正在为你更新"; _phase.ForeColor = Ui.Ink;
        Report(new UpdateProgress(UpdateStage.Downloading));
    }
    public void Report(UpdateProgress value)
    {
        if (IsDisposed || _allowClose) return;
        _failed = false;
        _completed = value.Stage == UpdateStage.Completed;
        bool installing = value.Stage >= UpdateStage.Waiting && !_completed;
        _busy = !_completed;
        _canCancel = !installing && !_completed;
        _install.Enabled = false;
        _install.Text = value.Stage == UpdateStage.Completed ? "已完成" : installing ? "安装中" : "正在更新";
        _cancel.Enabled = !installing && (!_cancelSent || _completed);
        _phase.ForeColor = _completed ? Ui.SuccessText : Ui.Ink;
        _percent.ForeColor = _completed ? Ui.SuccessText : Ui.Accent;
        if (_completed) { _title.Text = "更新完成"; _cancel.Text = "关闭"; }
        _phase.Text = value.Stage switch { UpdateStage.Downloading => "正在下载更新", UpdateStage.Verifying => "正在校验安全性", UpdateStage.Extracting => "正在准备安装文件", UpdateStage.Waiting => "正在等待主程序退出", UpdateStage.Installing => "正在安装更新", UpdateStage.Restarting => "正在打开新版", _ => "更新完成" };
        _percent.Text = $"{Math.Floor(value.OverallPercent):0}%";
        _close.Enabled = !installing;
        _details.Text = value.Stage switch
        {
            UpdateStage.Downloading when value.TotalBytes == 0 => "正在连接下载服务器…",
            UpdateStage.Downloading => $"{SizeText(value.BytesReceived)} / {SizeText(value.TotalBytes)}  ·  下载 {value.Fraction:P0}" + (value.BytesPerSecond > 0 ? $"\n{SizeText(value.BytesPerSecond)}/s  ·  {Remaining(value)}" : "\n正在测量下载速度…"),
            UpdateStage.Verifying => "正在核对更新文件，确保下载完整、安全。",
            UpdateStage.Extracting => "正在安全解压更新文件，当前版本仍然可用。",
            UpdateStage.Waiting => "已准备好安装，正在安全关闭当前版本。",
            UpdateStage.Installing => $"已替换 {value.BytesReceived} / {value.TotalBytes} 个文件，请勿关闭。",
            UpdateStage.Restarting => "正在等待新版主界面就绪，请稍候…",
            _ => "新版已启动，主界面已自动打开。"
        };
        _hint.Text = _completed ? "新版已经打开，可以继续使用了。" : installing ? "完成后自动打开，请让窗口保持运行。" : "安心等一会儿，完成后自动为你打开。";
        _bar.SetProgress(value.OverallPercent, _busy); _steps.SetStage(value.Stage);
    }
    private static string Remaining(UpdateProgress value)
    {
        double seconds = Math.Max(0, (value.TotalBytes - value.BytesReceived) / value.BytesPerSecond);
        return seconds < 1 ? "即将完成下载" : seconds < 60 ? $"预计剩余 {Math.Ceiling(seconds):0} 秒" : $"预计剩余 {Math.Ceiling(seconds / 60):0} 分钟";
    }
    private void RequestCancellation()
    {
        if (!_canCancel || _cancelSent) return;
        _cancelSent = true; _cancel.Enabled = false; _cancel.Text = "正在取消…";
        CancelRequested?.Invoke();
    }
    public void ShowFailure(string message)
    {
        _failed = true; _completed = false;
        _busy = false; _canCancel = false; _bar.SetProgress(0, false);
        _title.Text = "这次更新未完成"; _phase.Text = _applying ? "请重新打开应用" : "可以再试一次"; _percent.Text = "";
        _close.Enabled = true;
        _details.Text = message; _details.AccessibleDescription = message;
        _install.Text = "重试更新"; _install.Enabled = !_applying; _install.Visible = !_applying;
        _cancel.Enabled = true; _cancel.Text = "关闭";
        _hint.Text = _applying ? "请查看错误详情后重新打开应用。" : "当前版本可继续使用，稍后也可以重试。";
        _phase.ForeColor = Ui.Danger;
    }
    public void Finish() { _allowClose = true; Close(); }
    public void ApplyTheme()
    {
        BackColor = Ui.Canvas; ForeColor = Ui.Ink;
        void ThemeLabels(Control parent)
        {
            foreach (Control child in parent.Controls) { if (child is Label label) label.ForeColor = Ui.Muted; ThemeLabels(child); }
        }
        ThemeLabels(this);
        _title.ForeColor = Ui.Ink; _phase.ForeColor = _failed ? Ui.Danger : _completed ? Ui.SuccessText : Ui.Ink; _percent.ForeColor = _completed ? Ui.SuccessText : Ui.Accent;
        _notes.ForeColor = Ui.Muted;
        if (IsHandleCreated) ApplyTitleBarTheme();
        Invalidate(true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        Color top = Ui.HeaderIsDark ? Color.FromArgb(25, 31, 48) : Color.FromArgb(246, 248, 255);
        Color bottom = Ui.HeaderIsDark ? Color.FromArgb(19, 25, 39) : Color.FromArgb(235, 241, 251);
        using (var background = new LinearGradientBrush(ClientRectangle, top, bottom, 105f)) g.FillRectangle(background, ClientRectangle);
        DrawGlow(g, new RectangleF(Width * 0.25f, -Height * 0.26f, Width, Height * 0.82f), Color.FromArgb(Ui.HeaderIsDark ? 38 : 62, 153, 140, 255));
        DrawGlow(g, new RectangleF(-Width * 0.50f, Height * 0.30f, Width * 1.2f, Height * 0.9f), Color.FromArgb(Ui.HeaderIsDark ? 24 : 40, 116, 200, 219));
        DrawGlow(g, new RectangleF(Width * 0.25f, Height * 0.55f, Width, Height * 0.8f), Color.FromArgb(Ui.HeaderIsDark ? 20 : 30, 127, 163, 255));
        float s = DeviceDpi / 96f;
        using var edge = new Pen(Ui.HeaderIsDark ? Color.FromArgb(90, 162, 179, 212) : Color.FromArgb(235, Color.White), s);
        VisualDrawing.DrawRoundedRectangle(g, edge, new RectangleF(s / 2, s / 2, Width - s, Height - s), 26 * s);
    }
    private static void DrawGlow(Graphics graphics, RectangleF bounds, Color color)
    {
        using var ellipse = new GraphicsPath(); ellipse.AddEllipse(bounds);
        using var glow = new PathGradientBrush(ellipse) { CenterColor = color, SurroundColors = new[] { Color.Transparent } };
        graphics.FillPath(glow, ellipse);
    }
    private void UpdateWindowShape()
    {
        if (Width < 2 || Height < 2) return;
        using var rounded = VisualDrawing.RoundedRectangle(new RectangleF(0, 0, Width, Height), 26 * DeviceDpi / 96f);
        Region? previous = Region; Region = new Region(rounded); previous?.Dispose();
    }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateWindowShape(); }
    protected override CreateParams CreateParams { get { CreateParams parameters = base.CreateParams; parameters.ClassStyle |= 0x00020000; return parameters; } }
    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture(); SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && _busy && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; RequestCancellation(); }
        base.OnFormClosing(e);
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyTitleBarTheme(); UpdateWindowShape(); }
    private void ApplyTitleBarTheme() { int dark = Ui.HeaderIsDark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal static UpdateDialog CreateNotice(string title, string message, bool error)
    {
        var dialog = new UpdateDialog("", message, 0);
        dialog.SuspendLayout();
        dialog.ClientSize = new Size(540, 414);
        dialog._title.Text = title;
        dialog._metadata.Text = "ClipBridge · 软件更新";
        dialog._hint.Text = error ? "网络恢复后，可以再次检查。" : "当前版本无需更新。";
        var layout = (TableLayoutPanel)dialog.Controls.Find("UpdateLayout", true)[0];
        foreach (int row in new[] { 5, 6 }) { layout.RowStyles[row].SizeType = SizeType.Absolute; layout.RowStyles[row].Height = 0; }
        layout.RowStyles[7].Height = 18;
        dialog.Controls.Find("UpdateProgressSurface", true)[0].Visible = false;
        dialog.Controls.Find("UpdateNotesTitle", true)[0].Text = error ? "检查详情" : "检查结果";
        dialog.Controls.Find("UpdateFooter", true)[0].Text = error ? "检查失败不影响当前版本使用" : "后续更新会继续在这里提供";
        var actions = (TableLayoutPanel)dialog.Controls.Find("UpdateActions", true)[0];
        actions.ColumnStyles[0].Width = 0;
        dialog._install.Margin = Padding.Empty;
        dialog._install.Text = "知道了";
        dialog._cancel.Visible = false;
        dialog.InstallRequested += dialog.Close;
        dialog.ResumeLayout(performLayout: true);
        return dialog;
    }

    internal static void ShowNotice(IWin32Window owner, string title, string message, bool error = false)
    {
        using var dialog = CreateNotice(title, message, error);
        dialog.ShowDialog(owner);
    }
    internal static int RunInstaller(string[] args)
    {
        _ = new UiThemeController(new UiThemePreferences());
        int result = 1;
        using var dialog = new UpdateDialog("", "更新已完成下载和安全校验。\n现在正在替换文件并准备打开新版，请稍候。", 0, applying: true);
        dialog.Shown += async (_, _) =>
        {
            var progress = new Progress<UpdateProgress>(value => { if (!dialog.IsDisposed) dialog.Report(value); });
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBridge");
            result = await Task.Run(() => SelfUpdate.Apply(args, appData, progress));
            if (result == 0) { await Task.Delay(450); dialog.Finish(); }
            else
            {
                string message = "更新未完成，请重新打开 ClipBridge。";
                try { message = File.ReadLines(Path.Combine(appData, "update.log")).LastOrDefault() ?? message; } catch (IOException) { }
                dialog.ShowFailure(message);
            }
        };
        Application.Run(dialog);
        return result;
    }
}
