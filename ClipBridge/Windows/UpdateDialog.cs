using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipBridge.Windows;

// A workspace-sized modal overlay: the owner is captured, blurred and dimmed,
// while one compact glass card sits in the exact centre of ClipBridge.
internal sealed class UpdateDialog : Form
{
    private readonly UpdateBackdropSurface _backdrop = new();
    private readonly UpdateCardSurface _card = new();
    private readonly UpdateGlassButton _install, _cancel, _close;
    private readonly bool _applying;
    private readonly bool _isRollback;
    private bool _busy, _canCancel, _cancelSent, _allowClose;
    private readonly Timer _ownerResizeTimer = new() { Interval = 140 };
    private readonly Timer _entranceTimer = new() { Interval = 16 };
    private readonly Stopwatch _entrance = new();
    public event Action? InstallRequested;
    public event Action? CancelRequested;

    public UpdateDialog(string version, string notes, long packageSize, bool applying)
        : this(version, notes, packageSize, applying, false)
    {
    }

    public UpdateDialog(string version, string notes, long packageSize, bool applying = false, bool isRollback = false)
    {
        _applying = applying;
        _isRollback = isRollback;
        Text = "ClipBridge · 软件更新";
        Name = "UpdateDialog";
        Icon = AppIcon.Load();
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        DoubleBuffered = true;
        KeyPreview = true;
        Font = Ui.Font(9f);
        Opacity = VisualMotion.AnimationsEnabled ? 0 : 1;
        _entranceTimer.Tick += (_, _) =>
        {
            double progress = VisualMotion.AnimationsEnabled ? Math.Clamp(_entrance.Elapsed.TotalMilliseconds / 180, 0, 1) : 1;
            Opacity = VisualMotion.EaseOutCubic(progress);
            if (progress >= 1) _entranceTimer.Stop();
        };

        _close = new UpdateGlassButton("", close: true) { Name = "UpdateCloseButton", AccessibleName = "关闭更新窗口", TabIndex = 2 };
        _install = new UpdateGlassButton("下载并安装", primary: true) { Name = "UpdateInstallButton", TabIndex = 0 };
        _cancel = new UpdateGlassButton("稍后再说") { Name = "UpdateCancelButton", TabIndex = 1 };
        _close.Click += (_, _) => RequestClose();
        _install.Click += (_, _) => { if (_applying) Close(); else InstallRequested?.Invoke(); };
        _cancel.Click += (_, _) => { if (_busy) RequestCancellation(); else Close(); };
        _card.TabStop = true;
        Controls.Add(_backdrop);
        Controls.Add(_card);
        _card.Controls.Add(_close);
        _card.Controls.Add(_cancel);
        _card.Controls.Add(_install);

        if (applying)
        {
            _card.ConfigureApplying(isRollback, version);
            _install.Text = "安装中"; _install.Enabled = false;
            _cancel.Text = "请稍候"; _cancel.Enabled = false; _close.Enabled = false;
        }
        else
        {
            if (isRollback)
            {
                _card.ConfigureRollback(version, notes, packageSize);
                _install.Text = "确认回退";
                _cancel.Text = "暂不回退";
            }
            else
            {
                _card.ConfigureAvailable(version, notes, packageSize);
                _install.Text = "下载并安装";
                _cancel.Text = "稍后再说";
            }
        }
        UpdateAccessibility();
        AcceptButton = _install;
        CancelButton = _cancel;
        ApplyTheme();
        Resize += (_, _) => LayoutOverlay();
        DpiChanged += (_, _) => LayoutOverlay();
        _ownerResizeTimer.Tick += (_, _) => { _ownerResizeTimer.Stop(); if (!IsDisposed) CaptureOwner(); };
        Shown += (_, _) => { CaptureOwner(); LayoutOverlay(); _card.Focus(); };
    }

    internal UpdateCardSurface Card => _card;
    internal UpdateBackdropSurface Backdrop => _backdrop;
    internal string BackdropDebug => _backdrop.Builds + " builds, " + _backdrop.Width + "x" + _backdrop.Height + ", owner=" + (Owner?.Width ?? -1) + "x" + (Owner?.Height ?? -1) + ", client=" + ClientSize.Width + "x" + ClientSize.Height;

    internal void BeginDownload()
    {
        _busy = true; _canCancel = true; _cancelSent = false;
        _card.BeginDownload(_isRollback);
        _install.Enabled = false; _install.Text = _isRollback ? "正在回退" : "正在更新";
        _cancel.Enabled = true; _cancel.Text = "取消下载";
        _close.Enabled = true;
        UpdateAccessibility();
        LayoutOverlay();
    }

    public void Report(UpdateProgress value)
    {
        if (IsDisposed || _allowClose) return;
        bool completed = value.Stage == UpdateStage.Completed;
        bool installing = value.Stage >= UpdateStage.Waiting && !completed;
        _busy = !completed; _canCancel = !installing && !completed;
        _card.Report(value, _isRollback);
        _install.Enabled = false;
        _install.Text = completed ? "已完成" : installing ? "安装中" : "正在更新";
        _cancel.Enabled = !installing && (!_cancelSent || completed);
        _cancel.Text = completed ? "关闭" : _cancelSent ? "正在取消…" : installing ? "请稍候" : "取消下载";
        _close.Enabled = !installing;
        UpdateAccessibility();
        LayoutOverlay(relayoutCard: false);
    }

    private void RequestCancellation()
    {
        if (!_canCancel || _cancelSent) return;
        _cancelSent = true; _cancel.Enabled = false; _cancel.Text = "正在取消…";
        CancelRequested?.Invoke();
    }

    public void ShowFailure(string message)
    {
        _busy = false; _canCancel = false;
        _card.ShowFailure(message, _isRollback);
        _install.Text = _applying ? "关闭" : _isRollback ? "重试回退" : "重试更新";
        _install.Enabled = true; _install.Visible = true;
        _cancel.Enabled = true; _cancel.Text = "关闭";
        _close.Enabled = true;
        UpdateAccessibility();
        LayoutOverlay();
    }

    public void Finish() { _allowClose = true; Close(); }

    public void ApplyTheme()
    {
        _card.ApplyTheme();
        _close.ApplyTheme(); _install.ApplyTheme(); _cancel.ApplyTheme();
        BackColor = Ui.HeaderIsDark ? Color.FromArgb(12, 17, 27) : Color.FromArgb(238, 243, 252);
        if (IsHandleCreated) ApplyTitleBarTheme();
        Invalidate(true);
    }

    private void UpdateAccessibility()
    {
        AccessibleName = _card.Mode == UpdateCardMode.Applying ? "软件更新安装程序" : _card.Mode == UpdateCardMode.Notice ? "软件更新通知" : "软件更新";
        AccessibleDescription = _card.AccessibleDescription;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBarTheme();
        PositionOverOwner();
        LayoutOverlay();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) return;
        PositionOverOwner();
        LayoutOverlay();
    }

    private void PositionOverOwner()
    {
        Form? owner = Owner;
        if (owner == null || owner.IsDisposed)
        {
            Size desired = _card.Mode == UpdateCardMode.Notice ? new Size(720, 430) : new Size(760, 560);
            if (WindowState == FormWindowState.Normal && ClientSize != desired)
            {
                ClientSize = desired;
                Rectangle work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
                Location = new Point(work.X + (work.Width - Width) / 2, work.Y + (work.Height - Height) / 2);
            }
            return;
        }
        Rectangle bounds = owner.RectangleToScreen(owner.ClientRectangle);
        if (Bounds != bounds) Bounds = bounds;
    }

    private void CaptureOwner()
    {
        Control? control = Owner;
        _backdrop.CaptureOwner(control);
        _card.SetBackdrop(_backdrop.Image);
    }

    private void LayoutOverlay() => LayoutOverlay(relayoutCard: true);

    private void LayoutOverlay(bool relayoutCard)
    {
        if (ClientSize.Width < 40 || ClientSize.Height < 40) return;
        _backdrop.Bounds = ClientRectangle;
        float scale = DeviceDpi / 96f;
        int logicalHeight = _card.Mode == UpdateCardMode.Notice ? UpdateCardSurface.NoticeLogicalHeight : UpdateCardSurface.LogicalHeight;
        int cardWidth = Math.Min((int)Math.Round(UpdateCardSurface.LogicalWidth * scale), Math.Max(280, ClientSize.Width - 32));
        int cardHeight = Math.Min((int)Math.Round(logicalHeight * scale), Math.Max(260, ClientSize.Height - 32));
        if (cardWidth < 280) cardWidth = Math.Max(220, ClientSize.Width - 16);
        if (cardHeight < 260) cardHeight = Math.Max(200, ClientSize.Height - 16);
        int x = (ClientSize.Width - cardWidth) / 2;
        int y = (ClientSize.Height - cardHeight) / 2;
        if (relayoutCard || _card.Bounds != new Rectangle(x, y, cardWidth, cardHeight)) _card.Bounds = new Rectangle(x, y, cardWidth, cardHeight);
        _backdrop.CardBounds = _card.Bounds;
        float cardScale = _card.LayoutScale;
        int buttonHeight = _card.ActionHeightPx;
        int buttonBottom = y + _card.ActionTopPx + buttonHeight;
        int right = x + _card.ActionRightPx;
        int primaryWidth = _card.Mode == UpdateCardMode.Notice ? Math.Min(112, cardWidth / 2) : Math.Min(124, cardWidth / 3);
        int secondaryWidth = Math.Min(106, cardWidth / 4);
        _install.SetBounds(right - x - primaryWidth, _card.ActionTopPx, primaryWidth, buttonHeight);
        _cancel.SetBounds(right - x - primaryWidth - 9 - secondaryWidth, _card.ActionTopPx, secondaryWidth, buttonHeight);
        _cancel.Visible = _card.Mode != UpdateCardMode.Notice;
        _close.SetBounds(cardWidth - _card.CloseSizePx - 20, _card.CloseTopPx, _card.CloseSizePx, _card.CloseSizePx);
        _card.SendToBack();
        _backdrop.SendToBack();
        _card.BringToFront(); _close.BringToFront(); _cancel.BringToFront(); _install.BringToFront();
        Invalidate(true);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && _busy && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; RequestCancellation(); }
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            RequestClose();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void RequestClose()
    {
        if (!_close.Enabled || !_allowClose && _busy) { if (_busy) RequestCancellation(); return; }
        Close();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (Opacity < 1) { _entrance.Restart(); _entranceTimer.Start(); }
        if (Owner != null)
        {
            Owner.Move += OwnerMoved;
            Owner.Resize += OwnerResized;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (Owner != null)
        {
            Owner.Move -= OwnerMoved;
            Owner.Resize -= OwnerResized;
        }
        _ownerResizeTimer.Dispose();
        _entranceTimer.Dispose();
        _card.SetBackdrop(null);
        _backdrop.Release();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _entranceTimer.Dispose(); _ownerResizeTimer.Dispose(); }
        base.Dispose(disposing);
    }

    private void OwnerMoved(object? sender, EventArgs e) => PositionOverOwner();
    private void OwnerResized(object? sender, EventArgs e)
    {
        PositionOverOwner();
        LayoutOverlay();
        _ownerResizeTimer.Stop();
        _ownerResizeTimer.Start();
    }

    private void ApplyTitleBarTheme() { int dark = Ui.HeaderIsDark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal static UpdateDialog CreateNotice(string title, string message, bool error, string context = "软件更新")
    {
        bool latest = !error && title.Contains("最新版本", StringComparison.Ordinal);
        var dialog = new UpdateDialog("", message, 0);
        dialog._card.ConfigureNotice(title, message, error, context, latest ? "v" + UpdateProtocol.CurrentVersion.ToString(3) : "");
        dialog._install.Text = "知道了"; dialog._install.Enabled = true;
        dialog._cancel.Visible = false;
        dialog._install.Click += (_, _) => { dialog._allowClose = true; dialog.Close(); };
        dialog.AcceptButton = dialog._install;
        dialog.UpdateAccessibility();
        dialog.LayoutOverlay();
        return dialog;
    }

    internal static void ShowNotice(IWin32Window? owner, string title, string message, bool error = false, string context = "软件更新")
    {
        using var dialog = CreateNotice(title, message, error, context);
        if (owner is null) dialog.ShowDialog();
        else dialog.ShowDialog(owner);
    }

    internal static int RunInstaller(string[] args)
    {
        _ = new UiThemeController(new UiThemePreferences());
        int result = 1;
        bool rollback = args.Length >= 2 && Array.IndexOf(args, "--rollback") is int rollbackIndex && rollbackIndex >= 0 && rollbackIndex + 1 < args.Length && bool.TryParse(args[rollbackIndex + 1], out bool rollbackValue) && rollbackValue;
        int versionIndex = Array.IndexOf(args, "--rollback-version");
        string rollbackVersion = rollback && versionIndex >= 0 && versionIndex + 1 < args.Length ? args[versionIndex + 1] : "";
        using var dialog = new UpdateDialog(rollbackVersion, rollback ? "更新已完成下载和安全校验。\n现在正在恢复所选历史版本，请稍候。" : "更新已完成下载和安全校验。\n现在正在替换文件并准备打开新版，请稍候。", 0, applying: true, isRollback: rollback);
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
