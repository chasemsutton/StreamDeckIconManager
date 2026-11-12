using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace StreamDeckIconManager.Mac.UI;

/// <summary>
/// Design tokens, matching the Windows app: the window is the deck's housing (deep slate), icons
/// sit on black LCD "keys", and a single cool-blue accent marks the action that matters.
/// </summary>
internal static class Palette
{
    public static readonly Color WindowColor = Color.FromRgb(0x15, 0x17, 0x1C);
    public static readonly Color SidebarColor = Color.FromRgb(0x1A, 0x1D, 0x23);
    public static readonly Color RaisedColor = Color.FromRgb(0x23, 0x27, 0x30);

    public static readonly IBrush Window = new SolidColorBrush(WindowColor);
    public static readonly IBrush Sidebar = new SolidColorBrush(SidebarColor);
    public static readonly IBrush Raised = new SolidColorBrush(RaisedColor);
    public static readonly IBrush RaisedHover = Brush(0x2B, 0x30, 0x3B);
    public static readonly IBrush SidebarHover = new SolidColorBrush(Mix(SidebarColor, RaisedColor, 0.5));
    public static readonly IBrush TrayHover = new SolidColorBrush(Mix(WindowColor, SidebarColor, 0.8));
    public static readonly IBrush Border = Brush(0x2E, 0x33, 0x3E);
    public static readonly IBrush BorderStrong = Brush(0x3D, 0x44, 0x52);
    public static readonly IBrush Key = Brush(0x0A, 0x0B, 0x0E);
    public static readonly IBrush KeyEdge = Brush(0x30, 0x35, 0x40);

    public static readonly IBrush Text = Brush(0xE9, 0xEB, 0xF1);
    public static readonly IBrush TextMuted = Brush(0x98, 0x9F, 0xAE);
    public static readonly IBrush TextFaint = Brush(0x67, 0x6E, 0x7D);

    public static readonly IBrush Accent = Brush(0x5B, 0x8C, 0xFF);
    public static readonly IBrush AccentSoft = Brush(0x24, 0x33, 0x55);
    public static readonly IBrush Danger = Brush(0xF0, 0x6B, 0x6B);
    public static readonly IBrush Success = Brush(0x4C, 0xC3, 0x8A);
    public static readonly IBrush Warning = Brush(0xE8, 0xB0, 0x4B);
    public static readonly IBrush White = Brushes.White;

    // Type sizes (Windows: Segoe UI 9.75 pt body = 13 px).
    public const double Body = 13;
    public const double Small = 12;
    public const double Caption = 11;
    public const double Title = 24;
    public const double DialogTitle = 19;

    public static readonly Typeface Regular = new(FontFamily.Default);
    public static readonly Typeface SemiBold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    private static IBrush Brush(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t),
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Draws an image inside a black Stream Deck style key, letterboxed to fit.</summary>
    public static void DrawKey(DrawingContext ctx, Rect bounds, IImage? image, double radius, IBrush edge, double edgeWidth = 1)
    {
        var key = new RoundedRect(bounds, radius);
        ctx.DrawRectangle(Key, null, key);

        if (image is not null && image.Size.Width > 0 && image.Size.Height > 0)
        {
            using (ctx.PushClip(key))
            {
                var inner = bounds.Deflate(bounds.Width * 0.06);
                ctx.DrawImage(image, new Rect(image.Size), Fit(image.Size, inner));
            }
        }

        var half = edgeWidth / 2;
        ctx.DrawRectangle(null, new Pen(edge, edgeWidth), new RoundedRect(bounds.Deflate(half), Math.Max(0, radius - half)));
    }

    public static Rect Fit(Size source, Rect box)
    {
        if (source.Width <= 0 || source.Height <= 0)
            return box;

        var scale = Math.Min(box.Width / source.Width, box.Height / source.Height);
        var w = source.Width * scale;
        var h = source.Height * scale;
        return new Rect(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
    }

    /// <summary>Single-line text for custom drawing, cut off with an ellipsis at <paramref name="maxWidth"/>.</summary>
    public static FormattedText Line(string text, double size, IBrush brush, bool strong = false, double maxWidth = double.PositiveInfinity,
        TextAlignment alignment = TextAlignment.Left)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            strong ? SemiBold : Regular, size, brush)
        {
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment
        };
        if (!double.IsInfinity(maxWidth))
            formatted.MaxTextWidth = Math.Max(1, maxWidth);
        return formatted;
    }

    public static TextBlock Label(string text, double size = Body, IBrush? brush = null, bool strong = false, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush ?? Text,
        FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis
    };
}

/// <summary>
/// Line icons (24 × 24, drawn with a 2 px round stroke), standing in for the Segoe Fluent glyphs
/// the Windows app uses. Shapes follow the Lucide icon set (ISC license).
/// </summary>
internal static class Glyphs
{
    public const string Add = "M5 12h14 M12 5v14";
    public const string Delete = "M3 6h18 M19 6v14a2 2 0 0 1 -2 2H7a2 2 0 0 1 -2 -2V6 M8 6V4a2 2 0 0 1 2 -2h4a2 2 0 0 1 2 2v2 M10 11v6 M14 11v6";
    public const string Edit = "M17 3a2.85 2.85 0 1 1 4 4L7.5 20.5 2 22l1.5 -5.5Z";
    public const string Save = "M19 21H5a2 2 0 0 1 -2 -2V5a2 2 0 0 1 2 -2h11l5 5v11a2 2 0 0 1 -2 2z M17 21v-8H7v8 M7 3v5h8";
    public const string Import = "M21 15v4a2 2 0 0 1 -2 2H5a2 2 0 0 1 -2 -2v-4 M7 10l5 5 5 -5 M12 15V3";
    public const string Export = "M21 15v4a2 2 0 0 1 -2 2H5a2 2 0 0 1 -2 -2v-4 M17 8l-5 -5 -5 5 M12 3v12";
    public const string Sync = "M21 12a9 9 0 0 0 -9 -9 9.75 9.75 0 0 0 -6.74 2.74L3 8 M3 3v5h5 M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74 -2.74L21 16 M16 16h5v5";
    public const string Picture = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2H5a2 2 0 0 1 -2 -2V5a2 2 0 0 1 2 -2z M9 7a2 2 0 1 0 0 4a2 2 0 1 0 0 -4z M21 15l-3.1 -3.1a2 2 0 0 0 -2.8 0L6 21";
    public const string Tag = "M12.6 2.6A2 2 0 0 0 11.2 2H4a2 2 0 0 0 -2 2v7.2a2 2 0 0 0 .6 1.4l8.7 8.7a2.4 2.4 0 0 0 3.4 0l6.6 -6.6a2.4 2.4 0 0 0 0 -3.4z M7.5 6.5a1 1 0 1 0 0 2a1 1 0 1 0 0 -2z";
    public const string Settings = "M4 21v-7 M4 10V3 M12 21v-9 M12 8V3 M20 21v-5 M20 12V3 M1 14h6 M9 8h6 M17 16h6";
    public const string Lightning = "M13 2L3 14h9l-1 8 10 -12h-9l1 -8z";
    public const string Photo = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2H5a2 2 0 0 1 -2 -2V5a2 2 0 0 1 2 -2z M8.5 8a1.5 1.5 0 1 0 0 3a1.5 1.5 0 1 0 0 -3z M21 15l-5 -5L5 21";

    /// <summary>
    /// An icon control. Without a <paramref name="brush"/>, it takes the colour of the text around
    /// it (so it follows a button's hover colours).
    /// </summary>
    public static Control Icon(string data, double size = 15, IBrush? brush = null, double stroke = 2)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = stroke,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        };

        if (brush is not null)
            path.Stroke = brush;
        else
            path.Bind(Shape.StrokeProperty, new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(Avalonia.Controls.Presenters.ContentPresenter) }
            });

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Canvas { Width = 24, Height = 24, Children = { path } }
        };
    }
}
