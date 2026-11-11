namespace Add_Stream_Deck_Icon.UI;

/// <summary>
/// Large radio-style choice: a glyph, a title, a description, an optional "Recommended" badge
/// and an optional note line (for example a warning). Cards in the same parent act as a group.
/// </summary>
internal sealed class OptionCard : Control
{
    private bool _checked;
    private bool _hover;
    private string _description = "";
    private string _note = "";
    private bool _recommended;

    public OptionCard(string title, string glyph)
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable, true);
        Text = title;
        Glyph = glyph;
        TabStop = true;
        Cursor = Cursors.Hand;
        Height = 104;
        Margin = new Padding(0, 0, 0, 10);
    }

    public event EventHandler? CheckedChanged;

    public string Glyph { get; }

    public string Description
    {
        get => _description;
        set { _description = value; Invalidate(); }
    }

    /// <summary>Extra line under the description, e.g. "3 confirmations".</summary>
    public string Note
    {
        get => _note;
        set { _note = value; Invalidate(); }
    }

    public Color NoteColor { get; set; } = Theme.TextFaint;

    public bool Recommended
    {
        get => _recommended;
        set { _recommended = value; Invalidate(); }
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;
            _checked = value;
            if (value && Parent is not null)
            {
                foreach (var other in Parent.Controls.OfType<OptionCard>())
                    if (other != this)
                        other.Checked = false;
            }

            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left)
            return;
        Focus();
        Checked = true;
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space)
        {
            Checked = true;
            e.Handled = true;
            return;
        }

        if (e.KeyCode is not (Keys.Up or Keys.Down) || Parent is null)
            return;

        // Arrow keys move between the enabled cards in the group, like radio buttons.
        var cards = Parent.Controls.OfType<OptionCard>().Where(c => c.Enabled).OrderBy(c => c.Top).ToList();
        var index = cards.IndexOf(this) + (e.KeyCode == Keys.Down ? 1 : -1);
        if (index >= 0 && index < cards.Count)
        {
            cards[index].Focus();
            cards[index].Checked = true;
        }

        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var s = DeviceDpi / 96f;
        var bg = Parent?.BackColor ?? Theme.Window;
        g.Clear(bg);
        Theme.HighQuality(g);

        var enabled = Enabled;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        var fill = _checked ? Theme.Mix(Theme.Raised, Theme.AccentSoft, 0.55f) : _hover && enabled ? Theme.RaisedHover : Theme.Raised;
        var border = _checked ? Theme.Accent : _hover && enabled ? Theme.BorderStrong : Theme.Border;

        using (var path = Theme.RoundedRect(rect, 10 * s))
        {
            using var brush = new SolidBrush(enabled ? fill : Theme.Mix(Theme.Raised, bg, 0.5f));
            using var pen = new Pen(enabled ? border : Theme.Border, (_checked ? 1.5f : 1f) * s);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        if (Focused && ShowFocusCues)
        {
            using var ring = Theme.RoundedRect(RectangleF.Inflate(rect, -3 * s, -3 * s), 8 * s);
            using var pen = new Pen(Color.FromArgb(120, Theme.Accent), 1f * s) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            g.DrawPath(pen, ring);
        }

        var text = enabled ? Theme.Text : Theme.TextFaint;
        var muted = enabled ? Theme.TextMuted : Theme.TextFaint;

        // Radio dot
        var dot = new RectangleF(16 * s, 18 * s, 18 * s, 18 * s);
        using (var pen = new Pen(_checked && enabled ? Theme.Accent : Theme.BorderStrong, 1.5f * s))
            g.DrawEllipse(pen, dot);
        if (_checked && enabled)
        {
            using var brush = new SolidBrush(Theme.Accent);
            g.FillEllipse(brush, RectangleF.Inflate(dot, -4.5f * s, -4.5f * s));
        }

        // Glyph and title
        var x = (int)(46 * s);
        using (var glyphFont = Theme.GlyphFont(12f))
            TextRenderer.DrawText(g, Glyph, glyphFont, new Rectangle(x, (int)(16 * s), (int)(22 * s), (int)(22 * s)),
                _checked && enabled ? Theme.Accent : muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var titleX = x + (int)(26 * s);
        var right = Width - (int)(16 * s);
        if (_recommended && enabled)
        {
            const string badge = "RECOMMENDED";
            var size = TextRenderer.MeasureText(g, badge, Theme.Caption, Size.Empty, TextFormatFlags.NoPadding);
            var badgeRect = new RectangleF(right - size.Width - 14 * s, 17 * s, size.Width + 14 * s, 20 * s);
            using (var path = Theme.RoundedRect(badgeRect, 10 * s))
            using (var brush = new SolidBrush(Theme.AccentSoft))
                g.FillPath(brush, path);
            TextRenderer.DrawText(g, badge, Theme.Caption, Rectangle.Round(badgeRect), Theme.AccentHover,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            right = (int)(badgeRect.Left - 8 * s);
        }

        TextRenderer.DrawText(g, Text, Theme.BodyStrong, new Rectangle(titleX, (int)(16 * s), right - titleX, (int)(22 * s)), text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // Description and note
        var bodyWidth = Width - x - (int)(16 * s);
        var noteHeight = _note.Length > 0 ? (int)(20 * s) : 0;
        var descRect = new Rectangle(x, (int)(42 * s), bodyWidth, Height - (int)(42 * s) - noteHeight - (int)(10 * s));
        TextRenderer.DrawText(g, _description, Theme.Body, descRect, muted,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

        if (_note.Length > 0)
            TextRenderer.DrawText(g, _note, Theme.Small, new Rectangle(x, Height - noteHeight - (int)(10 * s), bodyWidth, noteHeight),
                enabled ? NoteColor : Theme.TextFaint, TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
