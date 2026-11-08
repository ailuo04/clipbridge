using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal enum UpdateCardMode { Available, Applying, Notice }

// One owner-drawn surface: glass, typography, notes and progress share a
// single buffered bitmap, so a progress tick never repaints nested cards.
internal sealed class UpdateCardSurface : Control
{
    internal const int LogicalWidth = 432;
    internal const int LogicalHeight = 472;
    internal const int NoticeLogicalHeight = 338;

    private readonly Timer _progressTimer = new() { Interval = 33 };
    private readonly List<(string Text, int Height)> _noteLines = new();
    private readonly List<string> _noteItems = new();

    private Bitmap? _backdrop;
    private Bitmap? _chrome;
    private readonly Bitmap? _icon;
    private float _scale = 1f;
    private int _chromeBuilds;
    private double _displayPercent, _targetPercent;
    private double _noteOffset;
    private int _noteContentHeight, _notesViewportHeight;
    private bool _notesDragging;
    private int _dragY;
    private double _dragOffset;
    private UpdateStage _stage = UpdateStage.Downloading;

    private Font _brandFont = Ui.Font(9.4f, FontStyle.Bold);
    private Font _eyebrowFont = Ui.Font(8.2f, FontStyle.Bold);
    private Font _titleFont = Ui.Font(21.5f, FontStyle.Bold);
    private Font _subtitleFont = Ui.Font(8.8f);
    private Font _notesTitleFont = Ui.Font(8.8f, FontStyle.Bold);
    private Font _noteFont = Ui.Font(9.05f);
    private Font _metaFont = Ui.Font(8.4f);
    private Font _percentFont = Ui.Font(20f, FontStyle.Bold);
    private Font _stepFont = Ui.Font(8f);
    private Font _footerFont = Ui.Font(7.7f);
    private Font _securityFont = Ui.Font(8.2f);
    private Font _errorFont = Ui.Font(8.8f);

    internal UpdateCardMode Mode { get; private set; } = UpdateCardMode.Available;
    internal string Version { get; private set; } = "";
    internal string NotesText { get; private set; } = "";
    internal string EyebrowText { get; private set; } = "";
    internal string TitleText { get; private set; } = "";
    internal string SubtitleText { get; private set; } = "";
    internal string ProgressLabel { get; private set; } = "";
    internal string DetailsText { get; private set; } = "";
    internal string FooterText { get; private set; } = "RSA 签名清单 · SHA-256 校验 · 完成后自动打开";
    internal string SecurityText { get; private set; } = "更新前会校验 RSA 签名与 SHA-256，校验失败不会安装。";
    internal bool ProgressVisible { get; private set; }
    internal bool Failed { get; private set; }
    internal bool Completed { get; private set; }
    internal bool SecurityVisible { get; private set; } = true;
    internal string PercentText => ProgressVisible ? $"{Math.Floor(_displayPercent):0}%" : "";
    internal double TargetPercent => _targetPercent;
    internal bool ProgressAnimating => _progressTimer.Enabled;
    internal int ChromeBuilds => _chromeBuilds;
    internal int AnimationInterval => _progressTimer.Interval;
    internal float LayoutScale => _scale;
    internal int ActionTopPx => (int)Math.Round((Mode == UpdateCardMode.Notice ? 254 : 386) * _scale);
    internal int ActionHeightPx => (int)Math.Round(36 * _scale);
    internal int ActionRightPx => Width - (int)Math.Round(24 * _scale);
    internal int CloseTopPx => (int)Math.Round(18 * _scale);
    internal int CloseSizePx => (int)Math.Round(28 * _scale);
    internal Size PercentSize
    {
        get
        {
            using var graphics = CreateGraphics();
            Size size = TextRenderer.MeasureText(graphics, "100%", _percentFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return new Size(size.Width + 4, Math.Max(size.Height, (int)Math.Round(28 * _scale)));
        }
    }

    internal UpdateCardSurface()
    {
        Name = "UpdateCard";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque | ControlStyles.Selectable, true);
        BackColor = Color.Black;
        TabStop = true;
        AccessibleName = "更新说明";
        AccessibleRole = AccessibleRole.Grouping;
        _progressTimer.Tick += (_, _) =>
        {
            _displayPercent = VisualDrawing.MoveTowards(_displayPercent, _targetPercent, Math.Max(1.0, Math.Abs(_targetPercent - _displayPercent) * 0.25));
            InvalidateProgress();
            if (_displayPercent == _targetPercent) _progressTimer.Stop();
        };
        _icon = BrandMark.LoadImage();
    }

    internal void ConfigureAvailable(string version, string notes, long packageSize)
    {
        Mode = UpdateCardMode.Available;
        Version = version ?? "";
        NotesText = string.IsNullOrWhiteSpace(notes) ? "更流畅的使用体验与细节改进。" : notes;
        EyebrowText = "发现可用更新";
        TitleText = "让互传更顺手";
        SubtitleText = "v" + UpdateProtocol.CurrentVersion.ToString(3) + "  →  v" + Version + "   ·   " + SizeText(packageSize);
        ProgressVisible = false; Failed = false; Completed = false; SecurityVisible = true;
        ProgressLabel = ""; DetailsText = ""; _targetPercent = _displayPercent = 0;
        FooterText = "RSA 签名清单 · SHA-256 校验 · 完成后自动打开";
        RebuildNoteLayout();
        RebuildChrome();
        AccessibleName = "软件更新 v" + Version;
        AccessibleDescription = "发现可用更新 v" + Version + "，更新包 " + SizeText(packageSize) + "。本次更新：" + NotesText;
        Invalidate();
    }

    internal void ConfigureApplying()
    {
        Mode = UpdateCardMode.Applying;
        Version = "";
        NotesText = "更新已完成下载和安全校验。\n现在正在替换文件并准备打开新版，请稍候。";
        EyebrowText = "更新文件已验证";
        TitleText = "正在安装更新";
        SubtitleText = "请勿关闭窗口，完成后会自动打开 ClipBridge";
        ProgressVisible = true; Failed = false; Completed = false; SecurityVisible = false;
        ProgressLabel = "准备安装"; DetailsText = "正在等待当前版本安全退出…";
        _targetPercent = _displayPercent = 90;
        FooterText = "安装过程中会保留原版本备份";
        RebuildNoteLayout(); RebuildChrome();
        AccessibleName = "软件更新安装程序";
        AccessibleDescription = "正在安装已验证的更新，请稍候";
        Invalidate();
    }

    internal void ConfigureNotice(string title, string message, bool error, string context, string badge)
    {
        Mode = UpdateCardMode.Notice;
        Version = badge ?? "";
        NotesText = message ?? "";
        EyebrowText = error ? context : context + " · 检查完成";
        TitleText = title ?? "";
        SubtitleText = string.IsNullOrWhiteSpace(badge) ? "" : badge;
        ProgressVisible = false; Failed = error; Completed = !error;
        SecurityVisible = false;
        ProgressLabel = ""; DetailsText = error ? message : ""; _targetPercent = _displayPercent = 0;
        FooterText = error ? "当前版本仍可使用，可稍后重试" : "后续更新会继续在这里提供";
        RebuildNoteLayout(); RebuildChrome();
        AccessibleName = context;
        AccessibleDescription = string.Join("，", new[] { context, title, message }.Where(value => !string.IsNullOrWhiteSpace(value)));
        Invalidate();
    }

    internal void BeginDownload()
    {
        Failed = false; Completed = false; SecurityVisible = false; ProgressVisible = true;
        EyebrowText = "正在更新"; TitleText = "正在下载更新"; SubtitleText = "下载会继续在后台进行，界面保持可操作";
        ProgressLabel = "下载进度"; DetailsText = "正在连接下载服务器…";
        _stage = UpdateStage.Downloading; _targetPercent = _displayPercent = 0;
        FooterText = "下载完成后会校验签名，再自动安装";
        RebuildChrome();
        StartAnimation();
        Invalidate();
    }

    internal void Report(UpdateProgress value)
    {
        Failed = false;
        Completed = value.Stage == UpdateStage.Completed;
        bool installing = value.Stage >= UpdateStage.Waiting && !Completed;
        SecurityVisible = false; ProgressVisible = true;
        _stage = value.Stage;
        EyebrowText = Completed ? "更新完成" : installing ? "更新文件已验证" : "正在更新";
        TitleText = value.Stage switch
        {
            UpdateStage.Downloading => "正在下载更新",
            UpdateStage.Verifying => "正在校验安全性",
            UpdateStage.Extracting => "正在准备安装文件",
            UpdateStage.Waiting => "正在等待主程序退出",
            UpdateStage.Installing => "正在安装更新",
            UpdateStage.Restarting => "正在打开新版",
            _ => "更新完成"
        };
        SubtitleText = Completed ? "新版已经打开，当前已是最新版本" : installing ? "请勿关闭窗口，完成后会自动打开" : "下载会继续在后台进行，界面保持可操作";
        ProgressLabel = value.Stage switch
        {
            UpdateStage.Downloading => "下载进度",
            UpdateStage.Verifying => "签名与文件校验",
            UpdateStage.Extracting => "准备安装文件",
            UpdateStage.Waiting => "等待当前程序退出",
            UpdateStage.Installing => "替换应用文件",
            UpdateStage.Restarting => "等待主界面就绪",
            _ => "更新完成"
        };
        DetailsText = value.Stage switch
        {
            UpdateStage.Downloading when value.TotalBytes == 0 => "正在连接下载服务器…",
            UpdateStage.Downloading => SizeText(value.BytesReceived) + " / " + SizeText(value.TotalBytes) + (value.BytesPerSecond > 0 ? "   ·   " + SizeText(value.BytesPerSecond) + "/s   ·   " + Remaining(value) : "   ·   正在测量下载速度…"),
            UpdateStage.Verifying => "正在核对 RSA 签名、文件大小与 SHA-256，确保下载完整、安全。",
            UpdateStage.Extracting => "正在安全解压更新文件，当前版本仍然可用。",
            UpdateStage.Waiting => "已准备好安装，正在安全关闭当前版本。",
            UpdateStage.Installing => "已替换 " + value.BytesReceived + " / " + value.TotalBytes + " 个文件，请勿关闭。",
            UpdateStage.Restarting => "正在等待新版主界面就绪，请稍候…",
            _ => "新版已启动，主界面已自动打开。"
        };
        FooterText = Completed ? "更新完成 · 当前版本 v" + UpdateProtocol.CurrentVersion.ToString(3) : installing ? "安装过程中不会中断当前窗口" : "下载完成后会校验签名，再自动安装";
        _targetPercent = Math.Clamp(value.OverallPercent, 0, 100);
        if (Completed) _displayPercent = 100;
        AccessibleDescription = string.Join("，", new[] { EyebrowText, TitleText, SubtitleText, ProgressLabel, $"{Math.Floor(_targetPercent):0}%", DetailsText }.Where(text => !string.IsNullOrWhiteSpace(text)));
        StartAnimation();
        Invalidate();
    }

    internal void ShowFailure(string message)
    {
        Failed = true; Completed = false; ProgressVisible = false; SecurityVisible = false;
        DetailsText = message ?? "";
        EyebrowText = Mode == UpdateCardMode.Applying ? "安装未完成" : "更新未完成";
        TitleText = Mode == UpdateCardMode.Applying ? "请重新打开应用" : "可以再试一次";
        SubtitleText = Mode == UpdateCardMode.Applying ? "原版本已尝试恢复" : "当前版本仍可使用，没有任何改动";
        FooterText = Mode == UpdateCardMode.Applying ? "重新打开 ClipBridge 即可继续使用" : "可以关闭窗口，稍后再试";
        _progressTimer.Stop();
        AccessibleDescription = string.Join("，", new[] { EyebrowText, TitleText, SubtitleText, message }.Where(text => !string.IsNullOrWhiteSpace(text)));
        RebuildChrome();
        Invalidate();
    }

    internal void Complete()
    {
        Failed = false; Completed = true; ProgressVisible = true;
        EyebrowText = "更新完成"; TitleText = "更新完成"; SubtitleText = "新版已经打开，当前已是最新版本";
        ProgressLabel = "更新完成"; DetailsText = "新版已启动，主界面已自动打开。";
        _targetPercent = _displayPercent = 100; _progressTimer.Stop();
        AccessibleDescription = "更新完成，新版已经打开";
        Invalidate();
    }

    internal void ApplyTheme()
    {
        RebuildFonts();
        RebuildNoteLayout();
        RebuildChrome();
        Invalidate();
    }

    internal void SetBackdrop(Bitmap? source)
    {
        Bitmap? previous = _backdrop;
        _backdrop = null;
        try
        {
            if (source != null && source.Width > 0 && source.Height > 0)
            {
                _backdrop = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
                using Graphics graphics = Graphics.FromImage(_backdrop);
                graphics.DrawImageUnscaled(source, 0, 0);
            }
        }
        catch (ArgumentException)
        {
            _backdrop = null;
        }
        finally
        {
            previous?.Dispose();
        }
        RebuildChrome();
        Invalidate();
    }

    // Called once after the owner backdrop arrives; the card's chrome is baked
    // again here, but later progress and note changes reuse the result.
    internal void RefreshBackdrop() => RebuildChrome();

    private static string SizeText(double value) => value >= 1024 * 1024 ? $"{value / (1024 * 1024):0.0} MB" : $"{value / 1024:0.0} KB";
    private static string Remaining(UpdateProgress value)
    {
        double seconds = Math.Max(0, (value.TotalBytes - value.BytesReceived) / Math.Max(1, value.BytesPerSecond));
        return seconds < 1 ? "即将完成" : seconds < 60 ? $"约 {Math.Ceiling(seconds):0} 秒" : $"约 {Math.Ceiling(seconds / 60):0} 分钟";
    }

    private void StartAnimation()
    {
        if (VisualMotion.AnimationsEnabled && Visible) _progressTimer.Start();
        else { _displayPercent = _targetPercent; _progressTimer.Stop(); InvalidateProgress(); }
    }

    private void InvalidateProgress()
    {
        if (!IsHandleCreated || IsDisposed) return;
        int top = (int)Math.Round(298 * _scale);
        Invalidate(new Rectangle(0, Math.Max(0, top - 8), Width, Math.Max(0, Math.Min(Height - top + 16, (int)Math.Round(100 * _scale)))));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        _scale = Math.Max(0.65f, Math.Min(Width / (float)LogicalWidth, Height / (float)(Mode == UpdateCardMode.Notice ? NoticeLogicalHeight : LogicalHeight)));
        RebuildNoteLayout();
        RebuildChrome();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        RebuildFonts(); RebuildNoteLayout(); RebuildChrome(); Invalidate();
    }

    private void RebuildFonts()
    {
        Font oldBrand = _brandFont, oldEyebrow = _eyebrowFont, oldTitle = _titleFont, oldSub = _subtitleFont;
        Font oldNotesTitle = _notesTitleFont, oldNote = _noteFont, oldMeta = _metaFont, oldPercent = _percentFont;
        Font oldStep = _stepFont, oldFooter = _footerFont, oldSecurity = _securityFont, oldError = _errorFont;
        _brandFont = Ui.Font(9.4f, FontStyle.Bold);
        _eyebrowFont = Ui.Font(8.2f, FontStyle.Bold);
        _titleFont = Ui.Font(Mode == UpdateCardMode.Notice ? 20f : 21.5f, FontStyle.Bold);
        _subtitleFont = Ui.Font(8.8f);
        _notesTitleFont = Ui.Font(8.8f, FontStyle.Bold);
        _noteFont = Ui.Font(9.05f);
        _metaFont = Ui.Font(8.4f);
        _percentFont = Ui.Font(20f, FontStyle.Bold);
        _stepFont = Ui.Font(8f);
        _footerFont = Ui.Font(7.7f);
        _securityFont = Ui.Font(8.2f);
        _errorFont = Ui.Font(8.8f);
        oldBrand.Dispose(); oldEyebrow.Dispose(); oldTitle.Dispose(); oldSub.Dispose();
        oldNotesTitle.Dispose(); oldNote.Dispose(); oldMeta.Dispose(); oldPercent.Dispose();
        oldStep.Dispose(); oldFooter.Dispose(); oldSecurity.Dispose(); oldError.Dispose();
    }

    private RectangleF L(float x, float y, float w, float h) => new(x * _scale, y * _scale, w * _scale, h * _scale);
    private Rectangle Li(float x, float y, float w, float h) => Rectangle.Round(L(x, y, w, h));

    private void RebuildNoteLayout()
    {
        _noteItems.Clear();
        foreach (string raw in (NotesText ?? "").Replace("\r", "").Split('\n'))
        {
            string text = raw.Trim().TrimStart('•', '-', '*', ' ').Trim();
            if (text.Length > 0) _noteItems.Add(text);
        }
        _noteLines.Clear();
        _noteContentHeight = 0;
        if (Width <= 0 || _noteItems.Count == 0) return;
        int textWidth = Math.Max(40, (int)Math.Round((LogicalWidth - 48 - 34 - 10) * _scale));
        using Graphics graphics = CreateGraphics();
        foreach (string item in _noteItems)
        {
            Size size = TextRenderer.MeasureText(graphics, item, _noteFont, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            _noteLines.Add((item, size.Height));
            _noteContentHeight += size.Height + (int)Math.Round(6.5 * _scale);
        }
        _noteContentHeight = Math.Max(0, _noteContentHeight - (int)Math.Round(6.5 * _scale));
        _notesViewportHeight = (int)Math.Round((Mode == UpdateCardMode.Notice ? 50 : 86) * _scale);
        _noteOffset = Math.Clamp(_noteOffset, 0, Math.Max(0, _noteContentHeight - _notesViewportHeight));
    }

    private void RebuildChrome()
    {
        if (Width <= 2 || Height <= 2) return;
        Bitmap? old = _chrome;
        Bitmap next = new(Width, Height, PixelFormat.Format32bppPArgb);
        try
        {
            using (Graphics g = Graphics.FromImage(next))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                DrawGlass(g);
            }
            _chrome = next;
            _chromeBuilds++;
            old?.Dispose();
        }
        catch
        {
            next.Dispose();
            throw;
        }
    }

    private void DrawGlass(Graphics g)
    {
        float s = _scale;
        RectangleF outer = new(0.5f, 0.5f, Width - 1, Height - 1);
        float radius = 24 * s;
        if (_backdrop != null)
        {
            Rectangle src = Parent == null
                ? new Rectangle(Point.Empty, Size)
                : Parent.RectangleToClient(RectangleToScreen(ClientRectangle));
            src.Intersect(new Rectangle(Point.Empty, _backdrop.Size));
            if (src.Width > 0 && src.Height > 0) g.DrawImage(_backdrop, new Rectangle(0, 0, Width, Height), src, GraphicsUnit.Pixel);
        }
        else g.Clear(Ui.Canvas);

        using GraphicsPath glassPath = VisualDrawing.RoundedRectangle(outer, radius);
        GraphicsState state = g.Save();
        g.SetClip(glassPath);
        Color veilTop = Ui.HeaderIsDark ? Color.FromArgb(236, 25, 34, 50) : Color.FromArgb(228, 250, 252, 255);
        Color veilBottom = Ui.HeaderIsDark ? Color.FromArgb(222, 17, 24, 39) : Color.FromArgb(210, 226, 238, 250);
        using (var veil = new LinearGradientBrush(outer, veilTop, veilBottom, 118f)) g.FillRectangle(veil, outer);
        if (Ui.HeaderIsDark)
        {
            DrawGlow(g, new RectangleF(-Width * .22f, -Height * .12f, Width * .95f, Height * .72f), Color.FromArgb(32, 91, 126, 232));
            DrawGlow(g, new RectangleF(Width * .42f, Height * .48f, Width * .8f, Height * .72f), Color.FromArgb(22, 36, 127, 156));
        }
        else
        {
            DrawGlow(g, new RectangleF(-Width * .28f, -Height * .18f, Width * 1.05f, Height * .76f), Color.FromArgb(36, 137, 158, 255));
            DrawGlow(g, new RectangleF(Width * .36f, Height * .40f, Width * .86f, Height * .76f), Color.FromArgb(25, 116, 205, 241));
            DrawGlow(g, new RectangleF(Width * .08f, Height * .62f, Width * .68f, Height * .62f), Color.FromArgb(18, 184, 165, 255));
        }
        RectangleF sheen = new(0, 0, Width, Height * .52f);
        using (var gloss = new LinearGradientBrush(sheen, Color.FromArgb(Ui.HeaderIsDark ? 28 : 96, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical))
            g.FillRectangle(gloss, sheen);
        RectangleF diagonal = new(-Width * .2f, -Height * .12f, Width * .9f, Height * .5f);
        using (var shine = new LinearGradientBrush(diagonal, Color.FromArgb(Ui.HeaderIsDark ? 20 : 78, Color.White), Color.FromArgb(0, Color.White), 118f))
            g.FillRectangle(shine, diagonal);
        g.Restore(state);

        using (var rim = new Pen(Ui.HeaderIsDark ? Color.FromArgb(100, 214, 228, 255) : Color.FromArgb(232, 255, 255, 255), Math.Max(1f, 1.1f * s)))
            VisualDrawing.DrawRoundedRectangle(g, rim, outer, radius);
        RectangleF innerRim = RectangleF.Inflate(outer, -Math.Max(1f, 1.3f * s), -Math.Max(1f, 1.3f * s));
        using (var rim = new Pen(Ui.HeaderIsDark ? Color.FromArgb(24, Color.White) : Color.FromArgb(58, 105, 129, 196), s))
            VisualDrawing.DrawRoundedRectangle(g, rim, innerRim, Math.Max(0, radius - s));

        // Notes sit directly on the glass; one translucent inset keeps the
        // reading area separate without stacking another opaque card.
        RectangleF notes = L(24, Mode == UpdateCardMode.Notice ? 132 : 148, LogicalWidth - 48, Mode == UpdateCardMode.Notice ? 84 : 130);
        using (var fill = new LinearGradientBrush(notes, Ui.HeaderIsDark ? Color.FromArgb(72, Color.White) : Color.FromArgb(112, Color.White), Ui.HeaderIsDark ? Color.FromArgb(48, Color.White) : Color.FromArgb(78, Color.White), 105f))
            VisualDrawing.FillRoundedRectangle(g, fill, notes, 14 * s);
        using (var edge = new Pen(Ui.HeaderIsDark ? Color.FromArgb(34, Color.White) : Color.FromArgb(110, Color.White), s))
            VisualDrawing.DrawRoundedRectangle(g, edge, notes, 14 * s);

        if (Mode != UpdateCardMode.Notice)
        {
            if (SecurityVisible && !ProgressVisible)
            {
                RectangleF security = L(24, 304, LogicalWidth - 48, 42);
                using var fill = new SolidBrush(Ui.HeaderIsDark ? Color.FromArgb(76, Color.White) : Color.FromArgb(96, Color.White));
                VisualDrawing.FillRoundedRectangle(g, fill, security, 12 * s);
                using var edge = new Pen(Ui.HeaderIsDark ? Color.FromArgb(68, Color.White) : Color.FromArgb(144, Color.White), s);
                VisualDrawing.DrawRoundedRectangle(g, edge, security, 12 * s);
            }
            using var divider = new Pen(Ui.HeaderIsDark ? Color.FromArgb(40, Color.White) : Color.FromArgb(88, 255, 255, 255), s);
            g.DrawLine(divider, 24 * s, 292 * s, (LogicalWidth - 24) * s, 292 * s);
            RectangleF track = L(24, 346, LogicalWidth - 48, 5);
            using var groove = new SolidBrush(Ui.HeaderIsDark ? Color.FromArgb(38, Color.White) : Color.FromArgb(28, 42, 66, 112));
            VisualDrawing.FillRoundedRectangle(g, groove, track, 2.5f * s);
        }
    }

    private static void DrawGlow(Graphics g, RectangleF bounds, Color color)
    {
        using var ellipse = new GraphicsPath();
        ellipse.AddEllipse(bounds);
        using var glow = new PathGradientBrush(ellipse) { CenterColor = color, SurroundColors = new[] { Color.Transparent } };
        g.FillPath(glow, ellipse);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 2 || Height <= 2) return;
        if (_chrome == null || _chrome.Size != Size) RebuildChrome();
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        if (_chrome != null) g.DrawImageUnscaled(_chrome, 0, 0);
        float s = _scale;
        Color ink = Ui.HeaderIsDark ? Color.FromArgb(244, 247, 255) : Color.FromArgb(22, 34, 53);
        Color body = Ui.HeaderIsDark ? Color.FromArgb(185, 198, 219) : Color.FromArgb(70, 88, 112);
        Color muted = Ui.HeaderIsDark ? Color.FromArgb(139, 155, 178) : Color.FromArgb(116, 133, 155);
        Color accent = Ui.HeaderIsDark ? Color.FromArgb(143, 171, 255) : Color.FromArgb(45, 92, 211);
        Color semantic = Failed ? (Ui.HeaderIsDark ? Color.FromArgb(255, 142, 158) : Color.FromArgb(181, 53, 72)) : Completed ? (Ui.HeaderIsDark ? Color.FromArgb(108, 220, 190) : Color.FromArgb(31, 137, 106)) : accent;

        DrawHeader(g, s, body);
        if (Mode == UpdateCardMode.Notice) DrawNoticeBody(g, s, ink, body, muted, semantic);
        else DrawUpdateBody(g, s, ink, body, muted, accent, semantic);
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Color.FromArgb(120, accent)) { DashStyle = DashStyle.Dot };
            VisualDrawing.DrawRoundedRectangle(g, focus, new RectangleF(4 * s, 4 * s, Width - 8 * s, Height - 8 * s), 20 * s);
        }
    }

    private void DrawHeader(Graphics g, float s, Color body)
    {
        RectangleF tile = L(24, 19, 30, 30);
        using (var fill = new LinearGradientBrush(tile, Ui.HeaderIsDark ? Color.FromArgb(92, Color.White) : Color.FromArgb(210, 255, 255, 255), Ui.HeaderIsDark ? Color.FromArgb(38, Color.White) : Color.FromArgb(125, 222, 235, 255), 125f))
            VisualDrawing.FillRoundedRectangle(g, fill, tile, 9.5f * s);
        using (var edge = new Pen(Ui.HeaderIsDark ? Color.FromArgb(60, Color.White) : Color.FromArgb(210, 255, 255, 255), s))
            VisualDrawing.DrawRoundedRectangle(g, edge, tile, 9.5f * s);
        if (_icon != null) g.DrawImage(_icon, Rectangle.Round(L(27, 22, 24, 24)));
        TextRenderer.DrawText(g, "ClipBridge", _brandFont, Li(62, 19, 220, 30), body, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

    }

    private void DrawUpdateBody(Graphics g, float s, Color ink, Color body, Color muted, Color accent, Color semantic)
    {
        DrawEyebrow(g, s, semantic);
        DrawTitleAndSubtitle(g, s, ink, muted);
        DrawNotes(g, s, body, muted);
        if (ProgressVisible) DrawProgress(g, s, body, muted, accent, semantic);
        else if (Failed) DrawErrorBlock(g, s);
        else if (SecurityVisible) DrawSecurity(g, s, body);
        TextRenderer.DrawText(g, FooterText, _footerFont, Li(24, 436, LogicalWidth - 48, 14), muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private void DrawNoticeBody(Graphics g, float s, Color ink, Color body, Color muted, Color semantic)
    {
        DrawEyebrow(g, s, semantic);
        DrawTitleAndSubtitle(g, s, ink, muted);
        if (Failed) DrawErrorBlock(g, s);
        else DrawNotes(g, s, body, muted);
        TextRenderer.DrawText(g, FooterText, _footerFont, Li(24, 302, LogicalWidth - 48, 14), muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private void DrawEyebrow(Graphics g, float s, Color color)
    {
        float y = Mode == UpdateCardMode.Notice ? 62 : 66;
        using var dot = new SolidBrush(color);
        g.FillEllipse(dot, 25 * s, (y + 6) * s, 5 * s, 5 * s);
        using var halo = new Pen(Color.FromArgb(38, color), 4 * s);
        g.DrawEllipse(halo, 22.5f * s, (y + 3.5f) * s, 10 * s, 10 * s);
        TextRenderer.DrawText(g, EyebrowText, _eyebrowFont, Li(39, y - 2, LogicalWidth - 63, 18), color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private void DrawTitleAndSubtitle(Graphics g, float s, Color ink, Color muted)
    {
        float titleY = Mode == UpdateCardMode.Notice ? 80 : 84;
        Font titleFont = _titleFont;
        if (MeasuredWidth(g, TitleText, titleFont) > (LogicalWidth - 48) * s)
        {
            using var smaller = Ui.Font(Mode == UpdateCardMode.Notice ? 17.5f : 18.5f, FontStyle.Bold);
            TextRenderer.DrawText(g, TitleText, smaller, Li(24, titleY, LogicalWidth - 48, 34), ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
        else TextRenderer.DrawText(g, TitleText, titleFont, Li(24, titleY, LogicalWidth - 48, 34), ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, SubtitleText ?? "", _subtitleFont, Li(24, titleY + 36, LogicalWidth - 48, 18), muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private static int MeasuredWidth(Graphics g, string text, Font font) => string.IsNullOrEmpty(text) ? 0 : TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

    private void DrawNotes(Graphics g, float s, Color body, Color muted)
    {
        RectangleF notes = L(24, Mode == UpdateCardMode.Notice ? 132 : 148, LogicalWidth - 48, Mode == UpdateCardMode.Notice ? 84 : 130);
        float titleY = notes.Y + 11 * s;
        TextRenderer.DrawText(g, Failed && Mode == UpdateCardMode.Notice ? "检查详情" : "本次更新", _notesTitleFont, new Rectangle((int)(notes.X + 15 * s), (int)titleY, (int)(notes.Width - 30 * s), (int)(18 * s)), body, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        if (!Failed && Mode != UpdateCardMode.Notice && _noteItems.Count > 0)
        {
            string count = _noteItems.Count + " 项改进";
            int width = MeasuredWidth(g, count, _metaFont);
            TextRenderer.DrawText(g, count, _metaFont, new Rectangle((int)(notes.Right - 15 * s - width), (int)titleY, width + 2, (int)(18 * s)), muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
        RectangleF viewport = new(notes.X + 15 * s, notes.Y + (Mode == UpdateCardMode.Notice ? 29 : 30) * s, notes.Width - 32 * s, Mode == UpdateCardMode.Notice ? 50 * s : 86 * s);
        if (Failed && Mode == UpdateCardMode.Notice)
        {
            TextRenderer.DrawText(g, NotesText, _noteFont, Rectangle.Round(viewport), body, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            return;
        }
        GraphicsState state = g.Save();
        g.SetClip(viewport);
        float y = viewport.Y - (float)_noteOffset;
        foreach ((string text, int height) in _noteLines)
        {
            using (var dot = new SolidBrush(Ui.HeaderIsDark ? Color.FromArgb(146, 167, 255) : Color.FromArgb(98, 122, 233)))
                g.FillEllipse(dot, viewport.X + 1 * s, y + _noteFont.Height * 0.44f, 4.2f * s, 4.2f * s);
            TextRenderer.DrawText(g, text, _noteFont, new Rectangle((int)(viewport.X + 13 * s), (int)y, (int)(viewport.Width - 13 * s), height), body, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            y += height + 6.5f * s;
        }
        g.Restore(state);
        if (_noteContentHeight > _notesViewportHeight)
        {
            float thumb = Math.Max(14 * s, _notesViewportHeight * (float)_notesViewportHeight / _noteContentHeight);
            float top = viewport.Y + (float)((_notesViewportHeight - thumb) * _noteOffset / Math.Max(1, _noteContentHeight - _notesViewportHeight));
            using var scroll = new SolidBrush(Color.FromArgb(80, muted));
            VisualDrawing.FillRoundedRectangle(g, scroll, new RectangleF(notes.Right - 8 * s, top, 2.5f * s, thumb), 1.5f * s);
        }
    }

    private void DrawSecurity(Graphics g, float s, Color body)
    {
        RectangleF row = L(24, 304, LogicalWidth - 48, 42);
        float cx = row.X + 19 * s, cy = row.Y + row.Height / 2;
        using (var pen = new Pen(Ui.HeaderIsDark ? Color.FromArgb(126, 153, 199) : Color.FromArgb(104, 123, 150), 1.25f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            RectangleF bodyRect = new(cx - 5 * s, cy - 2f * s, 10 * s, 8 * s);
            VisualDrawing.DrawRoundedRectangle(g, pen, bodyRect, 2 * s);
            g.DrawArc(pen, cx - 4 * s, cy - 8 * s, 8 * s, 10 * s, 180, 180);
        }
        using var dot = new SolidBrush(Ui.HeaderIsDark ? Color.FromArgb(110, 211, 186) : Color.FromArgb(34, 151, 117));
        g.FillEllipse(dot, row.X + 35 * s, cy - 2 * s, 4 * s, 4 * s);
        TextRenderer.DrawText(g, SecurityText, _securityFont, new Rectangle((int)(row.X + 45 * s), (int)row.Y, (int)(row.Width - 56 * s), (int)row.Height), body, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private void DrawProgress(Graphics g, float s, Color body, Color muted, Color accent, Color semantic)
    {
        float top = 300;
        TextRenderer.DrawText(g, ProgressLabel, _metaFont, Li(24, top, 190, 18), body, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        string percent = PercentText;
        int pw = MeasuredWidth(g, "100%", _percentFont) + 4;
        TextRenderer.DrawText(g, percent, _percentFont, new Rectangle(ActionRightPx - pw, (int)((top - 8) * s), pw + 2, (int)(32 * s)), semantic, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, DetailsText, _metaFont, Li(24, top + 21, LogicalWidth - 48, 18), muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        RectangleF track = L(24, 346, LogicalWidth - 48, 5);
        double shown = Math.Clamp(_displayPercent, 0, 100);
        if (shown > 0)
        {
            RectangleF fill = new(track.X, track.Y, Math.Max(3 * s, (float)(track.Width * shown / 100.0)), track.Height);
            using (var brush = new LinearGradientBrush(fill, Ui.HeaderIsDark ? Color.FromArgb(112, 130, 245) : Color.FromArgb(118, 142, 242), Completed ? (Ui.HeaderIsDark ? Color.FromArgb(91, 211, 177) : Color.FromArgb(42, 166, 134)) : accent, 0f))
                VisualDrawing.FillRoundedRectangle(g, brush, fill, track.Height / 2);
            using (var gloss = new SolidBrush(Color.FromArgb(60, Color.White)))
                g.FillRectangle(gloss, fill.X + 1, fill.Y + 1, Math.Max(0, fill.Width - 2), Math.Max(1, fill.Height * .34f));
        }
        string[] labels = { "下载", "校验", "安装", "完成" };
        int step = _stage switch { UpdateStage.Downloading => 0, UpdateStage.Verifying or UpdateStage.Extracting => 1, UpdateStage.Waiting or UpdateStage.Installing => 2, UpdateStage.Restarting or UpdateStage.Completed => 3, _ => 0 };
        float slot = (LogicalWidth - 48) / 4f;
        for (int i = 0; i < 4; i++)
        {
            float x = 24 + slot * i;
            RectangleF area = new((x + 1) * s, 358 * s, (slot - 4) * s, 18 * s);
            Color color = i < step || Completed ? (Ui.HeaderIsDark ? Color.FromArgb(115, 212, 188) : Color.FromArgb(45, 137, 112)) : i == step ? accent : muted;
            float cy = area.Y + area.Height / 2;
            if (i < step || Completed)
            {
                using var pen = new Pen(color, 1.3f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLines(pen, new[] { new PointF(area.X + 1 * s, cy), new PointF(area.X + 3.5f * s, cy + 2.5f * s), new PointF(area.X + 8 * s, cy - 3 * s) });
            }
            else
            {
                using var pen = new Pen(color, 1.15f * s);
                g.DrawEllipse(pen, area.X + 1 * s, cy - 3 * s, 6 * s, 6 * s);
                if (i == step) { using var dot = new SolidBrush(color); g.FillEllipse(dot, area.X + 2.6f * s, cy - 1.4f * s, 2.8f * s, 2.8f * s); }
            }
            if (i == step && !Completed)
            {
                using var bold = new Font(_stepFont, FontStyle.Bold);
                TextRenderer.DrawText(g, labels[i], bold, new Rectangle((int)(area.X + 12 * s), (int)area.Y, (int)(area.Width - 12 * s), (int)area.Height), color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
            else TextRenderer.DrawText(g, labels[i], _stepFont, new Rectangle((int)(area.X + 12 * s), (int)area.Y, (int)(area.Width - 12 * s), (int)area.Height), color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    private void DrawErrorBlock(Graphics g, float s)
    {
        RectangleF area = L(24, Mode == UpdateCardMode.Notice ? 132 : 300, LogicalWidth - 48, Mode == UpdateCardMode.Notice ? 84 : 72);
        Color border = Ui.HeaderIsDark ? Color.FromArgb(62, 255, 136, 154) : Color.FromArgb(62, 181, 53, 72);
        using (var fill = new SolidBrush(Ui.HeaderIsDark ? Color.FromArgb(30, 255, 92, 112) : Color.FromArgb(24, 196, 61, 82)))
            VisualDrawing.FillRoundedRectangle(g, fill, area, 12 * s);
        using (var edge = new Pen(border, s)) VisualDrawing.DrawRoundedRectangle(g, edge, area, 12 * s);
        using (var pen = new Pen(Failed ? (Ui.HeaderIsDark ? Color.FromArgb(255, 146, 160) : Color.FromArgb(181, 53, 72)) : Ui.Muted, 1.4f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            float cx = area.X + 17 * s, cy = area.Y + 18 * s;
            g.DrawLine(pen, cx - 4 * s, cy - 4 * s, cx + 4 * s, cy + 4 * s);
            g.DrawLine(pen, cx + 4 * s, cy - 4 * s, cx - 4 * s, cy + 4 * s);
        }
        TextRenderer.DrawText(g, DetailsText, _errorFont, new Rectangle((int)(area.X + 32 * s), (int)(area.Y + 10 * s), (int)(area.Width - 44 * s), (int)(area.Height - 18 * s)), Ui.HeaderIsDark ? Color.FromArgb(201, 211, 229) : Color.FromArgb(66, 84, 108), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left && _noteContentHeight > _notesViewportHeight && InNotes(e.Location))
        {
            _notesDragging = true; _dragY = e.Y; _dragOffset = _noteOffset; Capture = true;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_notesDragging)
        {
            _noteOffset = Math.Clamp(_dragOffset + (e.Y - _dragY) * 1.6, 0, Math.Max(0, _noteContentHeight - _notesViewportHeight));
            InvalidateNotes();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e) { _notesDragging = false; Capture = false; base.OnMouseUp(e); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (InNotes(e.Location))
        {
            _noteOffset = Math.Clamp(_noteOffset - Math.Sign(e.Delta) * 28 * _scale, 0, Math.Max(0, _noteContentHeight - _notesViewportHeight));
            InvalidateNotes();
            return;
        }
        base.OnMouseWheel(e);
    }

    private bool InNotes(Point p)
    {
        RectangleF notes = L(24, Mode == UpdateCardMode.Notice ? 132 : 148, LogicalWidth - 48, Mode == UpdateCardMode.Notice ? 84 : 130);
        return notes.Contains(p);
    }

    private void InvalidateNotes()
    {
        RectangleF notes = L(24, Mode == UpdateCardMode.Notice ? 132 : 148, LogicalWidth - 48, Mode == UpdateCardMode.Notice ? 84 : 130);
        Invalidate(Rectangle.Round(notes));
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int step = Height;
        switch (e.KeyCode)
        {
            case Keys.Down: _noteOffset = Math.Clamp(_noteOffset + 26 * _scale, 0, Math.Max(0, _noteContentHeight - _notesViewportHeight)); break;
            case Keys.Up: _noteOffset = Math.Clamp(_noteOffset - 26 * _scale, 0, Math.Max(0, _noteContentHeight - _notesViewportHeight)); break;
            case Keys.PageDown: _noteOffset = Math.Clamp(_noteOffset + step, 0, Math.Max(0, _noteContentHeight - _notesViewportHeight)); break;
            case Keys.PageUp: _noteOffset = Math.Clamp(_noteOffset - step, 0, Math.Max(0, _noteContentHeight - _notesViewportHeight)); break;
            case Keys.Home: _noteOffset = 0; break;
            case Keys.End: _noteOffset = Math.Max(0, _noteContentHeight - _notesViewportHeight); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
        InvalidateNotes();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _progressTimer.Dispose();
            _chrome?.Dispose(); _icon?.Dispose();
            _brandFont.Dispose(); _eyebrowFont.Dispose(); _titleFont.Dispose(); _subtitleFont.Dispose();
            _notesTitleFont.Dispose(); _noteFont.Dispose(); _metaFont.Dispose(); _percentFont.Dispose();
            _stepFont.Dispose(); _footerFont.Dispose(); _securityFont.Dispose(); _errorFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
