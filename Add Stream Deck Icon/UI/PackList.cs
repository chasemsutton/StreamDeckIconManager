namespace Add_Stream_Deck_Icon.UI;

/// <summary>Sidebar list of packs: emblem, name and icon count per row.</summary>
internal sealed class PackList : ListBox
{
    private int _hover = -1;

    public PackList()
    {
        DrawMode = DrawMode.OwnerDrawFixed; // keeps ItemHeight honored
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Sidebar;
        ForeColor = Theme.Text;
        IntegralHeight = false;
        ItemHeight = (int)(56 * DeviceDpi / 96f);
        // Paint every row ourselves rather than through native WM_DRAWITEM, which skipped text on some rows.
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Native.UseDarkScrollbars(this);
    }

    /// <summary>Supplies (emblem, name, icon count) for a row.</summary>
    public Func<int, (Image? Emblem, string Name, int Count)>? RowData { get; set; }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = IndexFromPoint(e.Location);
        if (hit == _hover)
            return;
        var old = _hover;
        _hover = hit;
        InvalidateRow(old);
        InvalidateRow(hit);
        Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        var old = _hover;
        _hover = -1;
        InvalidateRow(old);
    }

    private void InvalidateRow(int index)
    {
        if (index >= 0 && index < Items.Count)
            Invalidate(GetItemRectangle(index));
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        Invalidate();
        base.OnSelectedIndexChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.HighQuality(g);

        for (var i = Math.Max(0, TopIndex); i < Items.Count; i++)
        {
            var bounds = GetItemRectangle(i);
            if (bounds.Top > ClientSize.Height)
                break;
            DrawRow(g, i, bounds, i == SelectedIndex);
        }
    }

    private void DrawRow(Graphics g, int index, Rectangle bounds, bool selected)
    {
        var s = DeviceDpi / 96f;
        var row = RectangleF.Inflate(bounds, -8 * s, -2 * s);

        if (selected || index == _hover)
        {
            using var path = Theme.RoundedRect(row, 8 * s);
            using var fill = new SolidBrush(selected ? Theme.Raised : Theme.Mix(Theme.Sidebar, Theme.Raised, 0.5f));
            g.FillPath(fill, path);
        }

        if (selected)
        {
            // Small accent tick on the left edge marks the open pack.
            var tick = new RectangleF(row.X, row.Y + row.Height * 0.3f, 3 * s, row.Height * 0.4f);
            using var path = Theme.RoundedRect(tick, 1.5f * s);
            using var brush = new SolidBrush(Theme.Accent);
            g.FillPath(brush, path);
        }

        var data = RowData?.Invoke(index) ?? (null, Items[index]?.ToString() ?? "", 0);
        var emblemSize = 36 * s;
        var emblem = new RectangleF(row.X + 12 * s, row.Y + (row.Height - emblemSize) / 2, emblemSize, emblemSize);
        Theme.DrawKey(g, emblem, data.Emblem, 7 * s, Theme.KeyEdge);

        var textX = (int)(emblem.Right + 12 * s);
        var textWidth = (int)(row.Right - textX - 8 * s);
        var nameRect = new Rectangle(textX, (int)(row.Y + 9 * s), textWidth, (int)(20 * s));
        var countRect = new Rectangle(textX, (int)(row.Y + 28 * s), textWidth, (int)(18 * s));

        TextRenderer.DrawText(g, data.Name, selected ? Theme.BodyStrong : Theme.Body, nameRect,
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(g, data.Count == 1 ? "1 icon" : $"{data.Count} icons", Theme.Small, countRect,
            Theme.TextFaint, TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}
