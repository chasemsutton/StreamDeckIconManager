using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Add_Stream_Deck_Icon.UI;

/// <summary>
/// Design tokens for the app. Concept: the window is the deck's housing (deep slate),
/// icons sit on black LCD "keys", and a single cool-blue accent marks the one action
/// that matters on each surface.
/// </summary>
internal static class Theme
{
    // Surfaces, darkest to lightest. Neutrals lean slightly blue to sit with the accent.
    public static readonly Color Window = Color.FromArgb(0x15, 0x17, 0x1C);
    public static readonly Color Sidebar = Color.FromArgb(0x1A, 0x1D, 0x23);
    public static readonly Color Raised = Color.FromArgb(0x23, 0x27, 0x30);
    public static readonly Color RaisedHover = Color.FromArgb(0x2B, 0x30, 0x3B);
    public static readonly Color Border = Color.FromArgb(0x2E, 0x33, 0x3E);
    public static readonly Color BorderStrong = Color.FromArgb(0x3D, 0x44, 0x52);
    public static readonly Color Key = Color.FromArgb(0x0A, 0x0B, 0x0E);
    public static readonly Color KeyEdge = Color.FromArgb(0x30, 0x35, 0x40);

    // Text
    public static readonly Color Text = Color.FromArgb(0xE9, 0xEB, 0xF1);
    public static readonly Color TextMuted = Color.FromArgb(0x98, 0x9F, 0xAE);
    public static readonly Color TextFaint = Color.FromArgb(0x67, 0x6E, 0x7D);

    // Accent and semantic colors
    public static readonly Color Accent = Color.FromArgb(0x5B, 0x8C, 0xFF);
    public static readonly Color AccentHover = Color.FromArgb(0x74, 0x9E, 0xFF);
    public static readonly Color AccentPressed = Color.FromArgb(0x4A, 0x76, 0xE0);
    public static readonly Color AccentSoft = Color.FromArgb(0x24, 0x33, 0x55);
    public static readonly Color Danger = Color.FromArgb(0xF0, 0x6B, 0x6B);
    public static readonly Color DangerSoft = Color.FromArgb(0x3A, 0x22, 0x26);
    public static readonly Color Success = Color.FromArgb(0x4C, 0xC3, 0x8A);
    public static readonly Color Warning = Color.FromArgb(0xE8, 0xB0, 0x4B);

    // Type
    public const string UiFamily = "Segoe UI";
    public static readonly Font Body = new(UiFamily, 9.75f);
    public static readonly Font BodyStrong = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Small = new(UiFamily, 8.75f);
    public static readonly Font Caption = new("Segoe UI Semibold", 8f);
    public static readonly Font Title = new("Segoe UI Semibold", 18f);
    public static readonly Font DialogTitle = new("Segoe UI Semibold", 14f);

    /// <summary>Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10.</summary>
    public static readonly string GlyphFamily = PickGlyphFamily();

    public static Font GlyphFont(float size) => new(GlyphFamily, size);

    private static string PickGlyphFamily()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }

    public static class Glyphs
    {
        public const string Add = "\uE710";
        public const string Delete = "\uE74D";
        public const string Edit = "\uE70F";
        public const string Save = "\uE74E";
        public const string Import = "\uE896";
        public const string Export = "\uE898";
        public const string Sync = "\uE895";
        public const string Folder = "\uE8B7";
        public const string Picture = "\uE8B9";
        public const string Tag = "\uE8EC";
        public const string Back = "\uE72B";
        public const string Clear = "\uE894";
        public const string Photo = "\uEB9F";
        public const string Settings = "\uE713";
        public const string Lightning = "\uE945";
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void HighQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t),
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));

    /// <summary>
    /// Draws an image inside a black Stream Deck style key, letterboxed to fit.
    /// </summary>
    public static void DrawKey(Graphics g, RectangleF bounds, Image? image, float radius, Color edge, float edgeWidth = 1f)
    {
        using (var keyPath = RoundedRect(bounds, radius))
        {
            using var fill = new SolidBrush(Key);
            g.FillPath(fill, keyPath);

            if (image is not null)
            {
                var state = g.Save();
                g.SetClip(keyPath);
                var inset = bounds.Width * 0.06f;
                var inner = RectangleF.Inflate(bounds, -inset, -inset);
                g.DrawImage(image, Fit(image.Size, inner));
                g.Restore(state);
            }
        }

        var half = edgeWidth / 2f;
        using var edgePath = RoundedRect(RectangleF.Inflate(bounds, -half, -half), Math.Max(0, radius - half));
        using var pen = new Pen(edge, edgeWidth);
        g.DrawPath(pen, edgePath);
    }

    public static RectangleF Fit(Size source, RectangleF box)
    {
        if (source.Width <= 0 || source.Height <= 0)
            return box;

        var scale = Math.Min(box.Width / source.Width, box.Height / source.Height);
        var w = source.Width * scale;
        var h = source.Height * scale;
        return new RectangleF(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
    }
}
