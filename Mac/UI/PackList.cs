using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace StreamDeckIconManager.Mac.UI;

/// <summary>The sidebar list of packs: emblem, name and icon count per row. Lives in a ScrollViewer.</summary>
internal sealed class PackList : Control
{
    private const double RowHeight = 56;
    private const double Inset = 10;
    private int _selected = -1;
    private int _hover = -1;

    public PackList()
    {
        Focusable = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    /// <summary>Row contents, read on every repaint so renames and new emblems show at once.</summary>
    public Func<int, (IImage Emblem, string Name, int Count)>? RowData { get; set; }

    public int Count
    {
        get => _count;
        set
        {
            _count = value;
            if (_selected >= value)
                _selected = value - 1;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private int _count;

    public event EventHandler? SelectedIndexChanged;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            value = Math.Clamp(value, -1, _count - 1);
            if (_selected == value)
                return;
            _selected = value;
            InvalidateVisual();
            if (value >= 0)
                this.BringIntoView(RowBounds(value));
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Refresh() => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 260 : availableSize.Width, _count * RowHeight + 8);

    private Rect RowBounds(int index) => new(Inset, 4 + index * RowHeight + 2, Math.Max(0, Bounds.Width - 2 * Inset), RowHeight - 4);

    private int HitTest(Point p)
    {
        var index = (int)Math.Floor((p.Y - 4) / RowHeight);
        return index >= 0 && index < _count && p.Y >= 4 ? index : -1;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (RowData is null)
            return;

        for (var i = 0; i < _count; i++)
        {
            var row = RowBounds(i);
            var selected = i == _selected;
            var data = RowData(i);

            if (selected || i == _hover)
                context.DrawRectangle(selected ? Palette.Raised : Palette.SidebarHover, null, new RoundedRect(row, 8));

            if (selected)
                context.DrawRectangle(Palette.Accent, null, new RoundedRect(new Rect(row.X, row.Y + row.Height * 0.3, 3, row.Height * 0.4), 1.5));

            const double emblemSize = 36;
            var emblem = new Rect(row.X + 12, row.Y + (row.Height - emblemSize) / 2, emblemSize, emblemSize);
            Palette.DrawKey(context, emblem, data.Emblem, 7, Palette.KeyEdge);

            var textX = emblem.Right + 12;
            var textWidth = row.Right - textX - 10;
            context.DrawText(Palette.Line(data.Name, Palette.Body, Palette.Text, strong: selected, maxWidth: textWidth), new Point(textX, row.Y + 9));
            context.DrawText(Palette.Line(data.Count == 1 ? "1 icon" : $"{data.Count} icons", Palette.Small, Palette.TextFaint, maxWidth: textWidth),
                new Point(textX, row.Y + 28));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTest(e.GetPosition(this));
        if (hit == _hover)
            return;
        _hover = hit;
        Cursor = hit >= 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var hit = HitTest(e.GetPosition(this));
        if (hit >= 0)
        {
            Focus();
            SelectedIndex = hit;
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_count == 0)
            return;

        if (e.Key == Key.Up)
        {
            SelectedIndex = Math.Max(0, _selected - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            SelectedIndex = Math.Min(_count - 1, _selected + 1);
            e.Handled = true;
        }
    }
}
