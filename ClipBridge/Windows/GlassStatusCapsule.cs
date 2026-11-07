using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal enum GlassStatusKind
{
    Info,
    Success,
    Warning,
    Error,
    Progress
}

internal sealed class GlassStatusCapsule : Control
{
    private GlassStatusKind _kind;
    private string _context = "";
    private string _title = "";
    private string _badge = "";

    public GlassStatusCapsule()
    {
        Name = "GlassStatusCapsule";
        Height = 76;
        AccessibleName = "状态提示";
        AccessibleRole = AccessibleRole.StatusBar;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public void SetContent(GlassStatusKind kind, string context, string title, string badge = "")
    {
        _kind = kind;
        _context = context ?? "";
        _title = title ?? "";
        _badge = badge ?? "";
        AccessibleDescription = string.Join("，", new[] { _context, _title, _badge }.Where(value => !string.IsNullOrWhiteSpace(value)));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 20 || Height < 20) return;
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float scale = DeviceDpi / 96f;
        RectangleF bounds = new(scale, scale, Width - 2 * scale, Height - 2 * scale);
        float radius = bounds.Height / 2;
        Color semantic = SemanticColor();
        Color soft = SemanticSoftColor();
        Color top = Ui.HeaderIsDark ? VisualMotion.Mix(Ui.Surface, soft, 0.34) : VisualMotion.Mix(Color.White, soft, 0.64);
        Color bottom = Ui.HeaderIsDark ? VisualMotion.Mix(Ui.Surface, soft, 0.50) : VisualMotion.Mix(Ui.Surface, soft, 0.58);

        using (var background = new LinearGradientBrush(bounds, top, bottom, 100f))
            VisualDrawing.FillRoundedRectangle(graphics, background, bounds, radius);
        using (var clip = VisualDrawing.RoundedRectangle(bounds, radius))
        {
            GraphicsState state = graphics.Save();
            graphics.SetClip(clip);
            RectangleF sheen = new(bounds.X, bounds.Y, bounds.Width, bounds.Height * 0.56f);
            using (var gloss = new LinearGradientBrush(sheen,
                Color.FromArgb(Ui.HeaderIsDark ? 30 : 150, Color.White),
                Color.FromArgb(0, Color.White), LinearGradientMode.Vertical))
                graphics.FillRectangle(gloss, sheen);
            graphics.Restore(state);
        }
        using (var rim = new Pen(Color.FromArgb(Ui.HeaderIsDark ? 74 : 226, Ui.HeaderIsDark ? semantic : Color.White), scale))
            VisualDrawing.DrawRoundedRectangle(graphics, rim, bounds, radius);
        RectangleF inner = RectangleF.Inflate(bounds, -1.5f * scale, -1.5f * scale);
        using (var innerRim = new Pen(Color.FromArgb(Ui.HeaderIsDark ? 18 : 44, semantic), scale))
            VisualDrawing.DrawRoundedRectangle(graphics, innerRim, inner, Math.Max(0, radius - scale));

        float iconSize = Math.Min(40 * scale, bounds.Height - 16 * scale);
        RectangleF icon = new(bounds.X + 13 * scale, bounds.Y + (bounds.Height - iconSize) / 2, iconSize, iconSize);
        using (var orb = new LinearGradientBrush(icon,
            Ui.HeaderIsDark ? VisualMotion.Mix(Ui.Surface, soft, 0.78) : Color.White,
            VisualMotion.Mix(Color.White, soft, Ui.HeaderIsDark ? 0.18 : 0.48), 110f))
            graphics.FillEllipse(orb, icon);
        using (var orbRim = new Pen(Color.FromArgb(Ui.HeaderIsDark ? 65 : 235, Ui.HeaderIsDark ? semantic : Color.White), scale))
            graphics.DrawEllipse(orbRim, icon);
        using (Font glyphFont = new Font("Segoe UI Symbol", 15 * scale, FontStyle.Regular, GraphicsUnit.Point))
        using (var glyphBrush = new SolidBrush(semantic))
        {
            string glyph = _kind switch
            {
                GlassStatusKind.Success => "✓",
                GlassStatusKind.Warning => "!",
                GlassStatusKind.Error => "×",
                GlassStatusKind.Progress => "•",
                _ => "i"
            };
            SizeF glyphSize = graphics.MeasureString(glyph, glyphFont);
            graphics.DrawString(glyph, glyphFont, glyphBrush, icon.X + (icon.Width - glyphSize.Width) / 2, icon.Y + (icon.Height - glyphSize.Height) / 2 - scale);
        }

        float left = icon.Right + 12 * scale;
        float right = bounds.Right - 13 * scale;
        using Font contextFont = Ui.Font(8.2f);
        using Font titleFont = Ui.Font(12.5f, FontStyle.Bold);
        float badgeWidth = 0;
        if (!string.IsNullOrWhiteSpace(_badge))
        {
            using Font badgeFont = Ui.Font(8f);
            badgeWidth = Math.Min(116 * scale, TextRenderer.MeasureText(_badge, badgeFont, Size.Empty, TextFormatFlags.NoPadding).Width + 18 * scale);
            RectangleF badge = new(right - badgeWidth, bounds.Y + (bounds.Height - 22 * scale) / 2, badgeWidth, 22 * scale);
            using var badgeFill = new SolidBrush(Color.FromArgb(Ui.HeaderIsDark ? 48 : 142, Ui.Surface));
            graphics.FillEllipse(badgeFill, badge);
            using var badgeEdge = new Pen(Color.FromArgb(Ui.HeaderIsDark ? 46 : 196, Color.White), scale);
            graphics.DrawEllipse(badgeEdge, badge);
            TextRenderer.DrawText(graphics, _badge, badgeFont, Rectangle.Round(badge), Ui.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
        int textRight = Math.Max((int)left, (int)(right - badgeWidth - (badgeWidth > 0 ? 10 * scale : 0)));
        Rectangle contextRect = Rectangle.Round(new RectangleF(left, bounds.Y + 12 * scale, textRight - left, 14 * scale));
        Rectangle titleRect = Rectangle.Round(new RectangleF(left, bounds.Y + 29 * scale, textRight - left, 23 * scale));
        TextRenderer.DrawText(graphics, _context, contextFont, contextRect, Ui.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, _title, titleFont, titleRect, Ui.Ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        base.OnPaint(e);
    }

    private Color SemanticColor() => _kind switch
    {
        GlassStatusKind.Success => Ui.SuccessText,
        GlassStatusKind.Warning => Ui.WarningText,
        GlassStatusKind.Error => Ui.Danger,
        GlassStatusKind.Progress => Ui.Accent,
        _ => Ui.Accent
    };

    private Color SemanticSoftColor() => _kind switch
    {
        GlassStatusKind.Success => Ui.SuccessSoft,
        GlassStatusKind.Warning => Ui.WarningSoft,
        GlassStatusKind.Error => Ui.DangerSoft,
        GlassStatusKind.Progress => Ui.AccentSoft,
        _ => Ui.AccentSoft
    };
}
