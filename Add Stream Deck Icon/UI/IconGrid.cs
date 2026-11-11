namespace Add_Stream_Deck_Icon.UI;

/// <summary>
/// One icon in the grid. <paramref name="Data"/> and <paramref name="Extension"/> are the icon's
/// file, used to play animated GIF/WEBP icons while the mouse is over them.
/// </summary>
internal sealed record IconTile(Image Image, string Caption, string Tooltip, byte[]? Data = null, string? Extension = null);

/// <summary>
/// Scrollable grid that shows each icon on a Stream Deck style key.
/// Click, Ctrl+click and Shift+click select; double-click or Enter edits; Delete removes.
/// </summary>
internal sealed class IconGrid : ScrollableControl
{
    // Logical (96 dpi) metrics
    private const int Pad = 20;
    private const int KeySize = 84;
    private const int CellWidth = 100;
    private const int CaptionGap = 8;
    private const int CaptionHeight = 18;
    private const int ColGap = 8;
    private const int RowGap = 18;

    private readonly List<IconTile> _items = new();
    private readonly HashSet<int> _selected = new();
    private readonly ToolTip _tip = new() { InitialDelay = 500, ReshowDelay = 100 };
    private int _anchor = -1;
    private int _focus = -1;
    private int _hover = -1;

    // Animation of the hovered icon (GIF/WEBP), if it has more than one frame.
    private readonly System.Windows.Forms.Timer _animTimer = new();
    private AnimatedPreview? _anim;
    private int _animIndex = -1;
    private int _animFrame;

    public IconGrid()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable, true);
        AutoScroll = true;
        BackColor = Theme.Window;
        TabStop = true;
        Native.UseDarkScrollbars(this);
        _animTimer.Tick += OnAnimationTick;
    }

    public event EventHandler? SelectionChanged;
    public event EventHandler<int>? ItemActivated;
    public event EventHandler? DeleteRequested;

    public string EmptyTitle { get; set; } = "No icons yet";
    public string EmptyBody { get; set; } = "";

    public IReadOnlyCollection<int> SelectedIndices => _selected;

    public void SetItems(IEnumerable<IconTile> items, bool keepSelection = false)
    {
        StopAnimation();
        _items.Clear();
        _items.AddRange(items);
        if (!keepSelection)
        {
            _selected.Clear();
            _anchor = _focus = -1;
        }
        else
        {
            _selected.RemoveWhere(i => i >= _items.Count);
        }

        _hover = -1;
        UpdateScrollSize();
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectAll()
    {
        _selected.Clear();
        for (var i = 0; i < _items.Count; i++)
            _selected.Add(i);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        if (_selected.Count == 0)
            return;
        _selected.Clear();
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private float Dpi => DeviceDpi / 96f;

    private int Columns
    {
        get
        {
            var s = Dpi;
            var usable = ClientSize.Width - 2 * Pad * s + ColGap * s;
            return Math.Max(1, (int)(usable / ((CellWidth + ColGap) * s)));
        }
    }

    private float RowHeight => (KeySize + CaptionGap + CaptionHeight + RowGap) * Dpi;

    private RectangleF CellBounds(int index)
    {
        var s = Dpi;
        var cols = Columns;
        var col = index % cols;
        var row = index / cols;
        var x = Pad * s + col * (CellWidth + ColGap) * s;
        var y = Pad * s + row * RowHeight + AutoScrollPosition.Y;
        return new RectangleF(x, y, CellWidth * s, (KeySize + CaptionGap + CaptionHeight) * s);
    }

    private RectangleF KeyBounds(RectangleF cell)
    {
        var size = KeySize * Dpi;
        return new RectangleF(cell.X + (cell.Width - size) / 2, cell.Y, size, size);
    }

    private int HitTest(Point p)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (Rectangle.Round(CellBounds(i)).Contains(p))
                return i;
        }

        return -1;
    }

    private void UpdateScrollSize()
    {
        var rows = (_items.Count + Columns - 1) / Columns;
        var height = rows == 0 ? 0 : (int)(Pad * 2 * Dpi + rows * RowHeight);
        AutoScrollMinSize = new Size(0, height);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollSize();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.HighQuality(g);
        var s = Dpi;

        if (_items.Count == 0)
        {
            PaintEmpty(g);
            return;
        }

        for (var i = 0; i < _items.Count; i++)
        {
            var cell = CellBounds(i);
            if (cell.Bottom < 0 || cell.Top > ClientSize.Height)
                continue;

            var selected = _selected.Contains(i);
            var key = KeyBounds(cell);

            if (selected)
            {
                using var glow = Theme.RoundedRect(RectangleF.Inflate(key, 4 * s, 4 * s), 14 * s);
                using var glowBrush = new SolidBrush(Theme.AccentSoft);
                g.FillPath(glowBrush, glow);
            }

            var edge = selected ? Theme.Accent : i == _hover ? Theme.BorderStrong : Theme.KeyEdge;
            var image = i == _animIndex && _anim is not null ? _anim.Frames[_animFrame] : _items[i].Image;
            Theme.DrawKey(g, key, image, 11 * s, edge, selected ? 2f * s : 1f * s);

            if (i == _focus && Focused && ShowFocusCues && !selected)
            {
                using var ring = Theme.RoundedRect(RectangleF.Inflate(key, 3 * s, 3 * s), 13 * s);
                using var pen = new Pen(Theme.Accent, 1f * s) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                g.DrawPath(pen, ring);
            }

            var captionRect = Rectangle.Round(new RectangleF(cell.X - 2 * s, key.Bottom + CaptionGap * s, cell.Width + 4 * s, CaptionHeight * s));
            TextRenderer.DrawText(g, _items[i].Caption, Theme.Small, captionRect,
                selected ? Theme.Text : Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    private void PaintEmpty(Graphics g)
    {
        var s = Dpi;
        var center = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f - 24 * s);

        // Three ghost keys hint at what will fill this space.
        var size = 56 * s;
        var gap = 12 * s;
        var total = size * 3 + gap * 2;
        for (var i = 0; i < 3; i++)
        {
            var r = new RectangleF(center.X - total / 2 + i * (size + gap), center.Y - size, size, size);
            using var path = Theme.RoundedRect(r, 9 * s);
            using var brush = new SolidBrush(i == 1 ? Theme.Raised : Theme.Sidebar);
            using var pen = new Pen(Theme.Border, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        using (var glyph = Theme.GlyphFont(18f))
        {
            var r = new Rectangle((int)(center.X - size / 2), (int)(center.Y - size), (int)size, (int)size);
            TextRenderer.DrawText(g, Theme.Glyphs.Photo, glyph, r, Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        var titleRect = new Rectangle(0, (int)(center.Y + 18 * s), ClientSize.Width, (int)(24 * s));
        TextRenderer.DrawText(g, EmptyTitle, Theme.BodyStrong, titleRect, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix);

        var bodyRect = new Rectangle((int)(24 * s), titleRect.Bottom + (int)(2 * s), ClientSize.Width - (int)(48 * s), (int)(40 * s));
        TextRenderer.DrawText(g, EmptyBody, Theme.Body, bodyRect, Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (hit == _hover)
            return;

        _hover = hit;
        Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
        _tip.SetToolTip(this, hit >= 0 ? _items[hit].Tooltip : null);
        StopAnimation();
        if (hit >= 0)
            _ = StartAnimationAsync(hit);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        HideTooltip();
        StopAnimation();
        if (_hover < 0)
            return;
        _hover = -1;
        Invalidate();
    }

    /// <summary>Plays the icon's animation while it stays hovered. Decoding happens off the UI thread, once.</summary>
    private async Task StartAnimationAsync(int index)
    {
        var tile = _items[index];
        if (tile.Data is null || !AnimatedPreview.CanAnimate(tile.Extension))
            return;

        var anim = await AnimatedPreview.LoadAsync(tile.Data, tile.Extension);

        // Still hovering the same icon, and the grid hasn't been refilled meanwhile?
        if (anim is null || IsDisposed || _hover != index || index >= _items.Count || !ReferenceEquals(_items[index], tile))
            return;

        _anim = anim;
        _animIndex = index;
        _animFrame = 0;
        _animTimer.Interval = Math.Max(20, anim.DurationsMs[0]);
        _animTimer.Start();
        InvalidateCell(index);
    }

    private void StopAnimation()
    {
        _animTimer.Stop();
        if (_animIndex >= 0 && _animIndex < _items.Count)
            InvalidateCell(_animIndex);
        _anim = null;
        _animIndex = -1;
        _animFrame = 0;
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_anim is null || _animIndex < 0)
        {
            _animTimer.Stop();
            return;
        }

        _animFrame = (_animFrame + 1) % _anim.Frames.Count;
        _animTimer.Interval = Math.Max(20, _anim.DurationsMs[_animFrame]);
        InvalidateCell(_animIndex);
    }

    private void InvalidateCell(int index)
    {
        var cell = Rectangle.Round(CellBounds(index));
        cell.Inflate((int)(6 * Dpi), (int)(6 * Dpi));
        Invalidate(cell);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        HideTooltip();
        base.OnVisibleChanged(e);
    }

    /// <summary>Removes the tooltip; it comes back on the next hover over an icon.</summary>
    private void HideTooltip()
    {
        _tip.Hide(this);
        _tip.SetToolTip(this, null);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        // A click often opens something (a dialog or menu); don't leave the tooltip hanging over it.
        HideTooltip();
        Focus();
        var hit = HitTest(e.Location);

        if (e.Button == MouseButtons.Right && hit >= 0 && !_selected.Contains(hit))
        {
            SelectSingle(hit);
            return;
        }

        if (e.Button != MouseButtons.Left)
            return;

        if (hit < 0)
        {
            if ((ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
                ClearSelection();
            return;
        }

        if ((ModifierKeys & Keys.Shift) == Keys.Shift && _anchor >= 0)
            SelectRange(_anchor, hit, additive: (ModifierKeys & Keys.Control) == Keys.Control);
        else if ((ModifierKeys & Keys.Control) == Keys.Control)
            Toggle(hit);
        else
            SelectSingle(hit);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var hit = HitTest(e.Location);
        if (hit >= 0 && e.Button == MouseButtons.Left)
            ItemActivated?.Invoke(this, hit);
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_items.Count == 0)
            return;

        switch (e.KeyCode)
        {
            case Keys.A when e.Control:
                SelectAll();
                e.Handled = true;
                return;
            case Keys.Delete:
                if (_selected.Count > 0)
                    DeleteRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            case Keys.Enter:
            case Keys.F2:
                if (_selected.Count > 0)
                    ItemActivated?.Invoke(this, _focus >= 0 ? _focus : _selected.Min());
                e.Handled = true;
                return;
            case Keys.Escape:
                ClearSelection();
                return;
        }

        var delta = e.KeyCode switch
        {
            Keys.Left => -1,
            Keys.Right => 1,
            Keys.Up => -Columns,
            Keys.Down => Columns,
            _ => 0
        };

        if (delta == 0)
            return;

        var next = Math.Clamp((_focus < 0 ? 0 : _focus + delta), 0, _items.Count - 1);
        if (e.Shift && _anchor >= 0)
            SelectRange(_anchor, next, additive: false);
        else
            SelectSingle(next);
        EnsureVisible(next);
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); HideTooltip(); Invalidate(); }

    private void EnsureVisible(int index)
    {
        var cell = CellBounds(index);
        var y = -AutoScrollPosition.Y;
        if (cell.Top < 0)
            AutoScrollPosition = new Point(0, (int)(y + cell.Top - Pad * Dpi));
        else if (cell.Bottom > ClientSize.Height)
            AutoScrollPosition = new Point(0, (int)(y + cell.Bottom - ClientSize.Height + Pad * Dpi));
        Invalidate();
    }

    private void SelectSingle(int index)
    {
        _selected.Clear();
        _selected.Add(index);
        _anchor = _focus = index;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Toggle(int index)
    {
        if (!_selected.Remove(index))
            _selected.Add(index);
        _anchor = _focus = index;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectRange(int from, int to, bool additive)
    {
        if (!additive)
            _selected.Clear();
        for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
            _selected.Add(i);
        _focus = to;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
            _animTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
