using System.Drawing.Drawing2D;

namespace Add_Stream_Deck_Icon.UI;

/// <summary>
/// Dashed tray at the bottom of the pack view. Empty, it invites a drop or a click to browse.
/// With images queued, it previews them and offers Add / Clear.
/// </summary>
internal sealed class DropZone : Panel
{
    private readonly FlowLayoutPanel _actions;
    private IReadOnlyList<Image> _queued = Array.Empty<Image>();
    private bool _dragActive;
    private bool _hover;
    private string _packName = "";

    public DropZone()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Window;
        Height = 92;
        Padding = new Padding(16, 2, 18, 2);
        Cursor = Cursors.Hand;

        ClearButton = new ThemedButton("Clear", ButtonKind.Ghost) { Margin = new Padding(0, 0, 8, 0) };
        AddButton = new ThemedButton("Add icons", ButtonKind.Primary, Theme.Glyphs.Add) { Margin = Padding.Empty };

        _actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Window,
            Visible = false,
            Cursor = Cursors.Default
        };
        _actions.Controls.Add(ClearButton);
        _actions.Controls.Add(AddButton);
        Controls.Add(_actions);
    }

    public event EventHandler? BrowseRequested;

    public ThemedButton AddButton { get; }
    public ThemedButton ClearButton { get; }

    public void SetQueue(IReadOnlyList<Image> images, string packName)
    {
        _queued = images;
        _packName = packName;
        _actions.Visible = images.Count > 0;
        AddButton.Text = images.Count == 1 ? "Add 1 icon" : $"Add {images.Count} icons";
        PerformLayout();
        Invalidate();
    }

    public bool DragActive
    {
        get => _dragActive;
        set
        {
            if (_dragActive == value)
                return;
            _dragActive = value;
            Invalidate();
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        // Vertically center the buttons inside the tray.
        if (_actions is { Visible: true })
        {
            var top = (Height - Padding.Vertical - _actions.PreferredSize.Height) / 2;
            _actions.Padding = new Padding(0, Math.Max(0, top), 0, 0);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left)
            BrowseRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var s = DeviceDpi / 96f;
        g.Clear(BackColor);
        Theme.HighQuality(g);

        var rect = new RectangleF(1, 1, Width - 2, Height - 2);
        var active = _dragActive || _hover;
        var fillColor = _dragActive ? Theme.AccentSoft : active ? Theme.Mix(Theme.Window, Theme.Sidebar, 0.8f) : Theme.Window;
        if (_actions.BackColor != fillColor)
            _actions.BackColor = fillColor;

        using (var path = Theme.RoundedRect(rect, 10 * s))
        {
            using var fill = new SolidBrush(fillColor);
            g.FillPath(fill, path);
            using var pen = new Pen(_dragActive ? Theme.Accent : active ? Theme.BorderStrong : Theme.Border, (_dragActive ? 1.5f : 1f) * s)
            {
                DashStyle = _queued.Count > 0 && !_dragActive ? DashStyle.Solid : DashStyle.Dash
            };
            g.DrawPath(pen, path);
        }

        if (_queued.Count == 0 || _dragActive)
            PaintInvite(g, s);
        else
            PaintQueue(g, s);
    }

    private void PaintInvite(Graphics g, float s)
    {
        var badge = new RectangleF(Padding.Left + 4 * s, (Height - 44 * s) / 2, 44 * s, 44 * s);
        using (var path = Theme.RoundedRect(badge, 10 * s))
        using (var brush = new SolidBrush(_dragActive ? Theme.Accent : Theme.Raised))
            g.FillPath(brush, path);

        using (var glyph = Theme.GlyphFont(15f))
            TextRenderer.DrawText(g, _dragActive ? Theme.Glyphs.Import : Theme.Glyphs.Add, glyph, Rectangle.Round(badge),
                _dragActive ? Color.White : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var x = (int)(badge.Right + 14 * s);
        var w = Width - x - Padding.Right;
        var title = _dragActive
            ? (_packName.Length > 0 ? $"Drop to queue images for {_packName}" : "Drop to queue images")
            : "Drop images here, or click to browse";
        TextRenderer.DrawText(g, title, Theme.BodyStrong,
            new Rectangle(x, (int)(Height / 2f - 20 * s), w, (int)(20 * s)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "PNG, JPG or GIF. Square images work best, ideally 144 × 144 px.", Theme.Small,
            new Rectangle(x, (int)(Height / 2f + 2 * s), w, (int)(18 * s)), Theme.TextFaint,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void PaintQueue(Graphics g, float s)
    {
        var key = 48 * s;
        var gap = 8 * s;
        var x = Padding.Left + 4 * s;
        var y = (Height - key) / 2;
        var limit = _actions is { Visible: true } ? _actions.Left - 24 * s : Width - Padding.Right;
        var textWidth = 220 * s;
        var room = Math.Max(1, (int)((limit - x - textWidth) / (key + gap)));
        var shown = Math.Min(_queued.Count, Math.Min(room, 8));
        var overflow = _queued.Count - shown;

        for (var i = 0; i < shown; i++)
        {
            Theme.DrawKey(g, new RectangleF(x, y, key, key), _queued[i], 8 * s, Theme.KeyEdge);
            x += key + gap;
        }

        if (overflow > 0)
        {
            var more = new RectangleF(x, y, key, key);
            using var path = Theme.RoundedRect(more, 8 * s);
            using var brush = new SolidBrush(Theme.Raised);
            g.FillPath(brush, path);
            TextRenderer.DrawText(g, $"+{overflow}", Theme.BodyStrong, Rectangle.Round(more), Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += key + gap;
        }

        var tx = (int)(x + 8 * s);
        var tw = (int)Math.Max(0, limit - tx);
        var count = _queued.Count == 1 ? "1 image ready" : $"{_queued.Count} images ready";
        TextRenderer.DrawText(g, count, Theme.BodyStrong,
            new Rectangle(tx, (int)(Height / 2f - 20 * s), tw, (int)(20 * s)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var sub = _packName.Length > 0 ? $"Name and tag them, then add to {_packName}" : "Create a pack to add them to";
        TextRenderer.DrawText(g, sub, Theme.Small,
            new Rectangle(tx, (int)(Height / 2f + 2 * s), tw, (int)(18 * s)), Theme.TextFaint,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
