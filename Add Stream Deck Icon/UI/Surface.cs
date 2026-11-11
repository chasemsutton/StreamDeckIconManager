namespace Add_Stream_Deck_Icon.UI;

[Flags]
internal enum Edges
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 4,
    Right = 8
}

/// <summary>Flat panel that draws hairlines on chosen edges, or a rounded outline.</summary>
internal sealed class Surface : Panel
{
    public Surface(Color background, Edges edges = Edges.None)
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = background;
        Edges = edges;
    }

    public Edges Edges { get; set; }

    /// <summary>When above zero, draws a rounded outline instead of edge lines.</summary>
    public int Radius { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        if (Radius > 0)
        {
            var s = DeviceDpi / 96f;
            g.Clear(Parent?.BackColor ?? Theme.Window);
            Theme.HighQuality(g);
            using var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Radius * s);
            using var fill = new SolidBrush(BackColor);
            using var pen = new Pen(Theme.Border);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
            return;
        }

        g.Clear(BackColor);
        using var line = new Pen(Theme.Border);
        if (Edges.HasFlag(Edges.Top)) g.DrawLine(line, 0, 0, Width, 0);
        if (Edges.HasFlag(Edges.Bottom)) g.DrawLine(line, 0, Height - 1, Width, Height - 1);
        if (Edges.HasFlag(Edges.Left)) g.DrawLine(line, 0, 0, 0, Height);
        if (Edges.HasFlag(Edges.Right)) g.DrawLine(line, Width - 1, 0, Width - 1, Height);
    }
}

/// <summary>Square image drawn as a Stream Deck key. Optional hover overlay with a glyph.</summary>
internal sealed class KeyImage : Control
{
    private Image? _image;
    private bool _hover;

    public KeyImage()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public Image? Image
    {
        get => _image;
        set { _image = value; Invalidate(); }
    }

    public float CornerRadius { get; set; } = 12;

    /// <summary>Glyph shown over the key on hover. Null means the key is not interactive.</summary>
    public string? HoverGlyph { get; set; }

    protected override void OnMouseEnter(EventArgs e) { _hover = HoverGlyph is not null; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var s = DeviceDpi / 96f;
        g.Clear(Parent?.BackColor ?? Theme.Window);
        Theme.HighQuality(g);

        var rect = new RectangleF(1, 1, Width - 2, Height - 2);
        Theme.DrawKey(g, rect, _image, CornerRadius * s, _hover ? Theme.Accent : Theme.KeyEdge, (_hover ? 1.5f : 1f) * s);

        if (!_hover || HoverGlyph is null)
            return;

        using (var path = Theme.RoundedRect(RectangleF.Inflate(rect, -1.5f * s, -1.5f * s), CornerRadius * s))
        using (var shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            g.FillPath(shade, path);

        using var glyph = Theme.GlyphFont(14f);
        TextRenderer.DrawText(g, HoverGlyph, glyph, Rectangle.Round(rect), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>Dark colors for context menus.</summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? Theme.TextFaint
            : e.Item.Tag as string == "danger" ? Theme.Danger
            : Theme.Text;
        base.OnRenderItemText(e);
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Raised;
        public override Color ImageMarginGradientBegin => Theme.Raised;
        public override Color ImageMarginGradientMiddle => Theme.Raised;
        public override Color ImageMarginGradientEnd => Theme.Raised;
        public override Color MenuBorder => Theme.BorderStrong;
        public override Color MenuItemBorder => Theme.RaisedHover;
        public override Color MenuItemSelected => Theme.RaisedHover;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }
}
