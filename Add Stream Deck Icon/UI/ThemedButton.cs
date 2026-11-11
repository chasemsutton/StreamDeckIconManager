namespace Add_Stream_Deck_Icon.UI;

internal enum ButtonKind
{
    Primary,
    Secondary,
    Ghost,
    Danger
}

/// <summary>Flat rounded button with an optional leading glyph.</summary>
internal sealed class ThemedButton : Button
{
    private bool _hover;
    private bool _pressed;
    private ButtonKind _kind;
    private string? _glyph;

    public ThemedButton(string text, ButtonKind kind = ButtonKind.Secondary, string? glyph = null)
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw, true);

        Text = text;
        _kind = kind;
        _glyph = glyph;
        Font = Theme.BodyStrong;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Margin = new Padding(0, 0, 8, 0);
        AutoSize = true;
    }

    public ButtonKind Kind
    {
        get => _kind;
        set { _kind = value; Invalidate(); }
    }

    public string? Glyph
    {
        get => _glyph;
        set { _glyph = value; Invalidate(); }
    }

    /// <summary>Overrides the kind's text color, e.g. to flag a non-default state.</summary>
    public Color? TextColor
    {
        get => _textColor;
        set { _textColor = value; Invalidate(); }
    }

    private Color? _textColor;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var scale = DeviceDpi / 96f;
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        var glyph = _glyph is null ? 0 : (int)(24 * scale);
        return new Size(text + glyph + (int)(30 * scale), (int)(34 * scale));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Parent?.PerformLayout(this, nameof(Text));
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var scale = DeviceDpi / 96f;
        g.Clear(Parent?.BackColor ?? Theme.Window);
        Theme.HighQuality(g);

        var (fill, border, fore) = Colors();
        fore = _textColor ?? fore;
        if (!Enabled)
        {
            var bg = Parent?.BackColor ?? Theme.Window;
            fill = fill.A == 0 ? fill : Theme.Mix(fill, bg, 0.55f);
            border = border.A == 0 ? border : Theme.Mix(border, bg, 0.5f);
            fore = Theme.Mix(fore, bg, 0.55f);
        }

        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (var path = Theme.RoundedRect(rect, 6 * scale))
        {
            if (fill.A > 0)
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }

            if (border.A > 0)
            {
                using var pen = new Pen(border);
                g.DrawPath(pen, path);
            }
        }

        if (Focused && ShowFocusCues)
        {
            using var ring = Theme.RoundedRect(RectangleF.Inflate(rect, -2.5f * scale, -2.5f * scale), 4 * scale);
            using var pen = new Pen(_kind == ButtonKind.Primary ? Color.FromArgb(180, Color.White) : Theme.Accent, 1.5f * scale);
            g.DrawPath(pen, ring);
        }

        var textSize = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        var glyphWidth = 0;
        Font? glyphFont = null;
        if (_glyph is not null)
        {
            glyphFont = Theme.GlyphFont(10f);
            glyphWidth = TextRenderer.MeasureText(g, _glyph, glyphFont, Size.Empty, TextFormatFlags.NoPadding).Width + (int)(8 * scale);
        }

        var contentWidth = glyphWidth + textSize.Width;
        var x = Math.Max((int)(10 * scale), (Width - contentWidth) / 2);

        if (glyphFont is not null)
        {
            var glyphRect = new Rectangle(x, 0, glyphWidth, Height);
            TextRenderer.DrawText(g, _glyph, glyphFont, glyphRect, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            glyphFont.Dispose();
        }

        var textRect = new Rectangle(x + glyphWidth, 0, Width - x - glyphWidth, Height - 1);
        TextRenderer.DrawText(g, Text, Font, textRect, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private (Color fill, Color border, Color fore) Colors()
    {
        var transparent = Color.Transparent;
        return _kind switch
        {
            ButtonKind.Primary => (
                _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent,
                transparent,
                Color.White),
            ButtonKind.Secondary => (
                _pressed ? Theme.Border : _hover ? Theme.RaisedHover : Theme.Raised,
                _hover ? Theme.BorderStrong : Theme.Border,
                Theme.Text),
            ButtonKind.Ghost => (
                _pressed ? Theme.Border : _hover ? Theme.Raised : transparent,
                transparent,
                _hover ? Theme.Text : Theme.TextMuted),
            ButtonKind.Danger => (
                _pressed ? Theme.Mix(Theme.DangerSoft, Theme.Danger, 0.15f) : _hover ? Theme.DangerSoft : transparent,
                transparent,
                Theme.Danger),
            _ => (Theme.Raised, Theme.Border, Theme.Text)
        };
    }
}
