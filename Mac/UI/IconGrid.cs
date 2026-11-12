using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace StreamDeckIconManager.Mac.UI;

/// <summary>
/// One icon in the grid. <paramref name="Data"/> and <paramref name="Extension"/> are the icon's
/// file, used to play animated GIF/WEBP icons while the pointer is over them.
/// </summary>
internal sealed record IconTile(IImage Image, string Caption, string Tooltip, byte[]? Data = null, string? Extension = null);

/// <summary>
/// Grid that shows each icon on a Stream Deck style key; lives inside a ScrollViewer.
/// Click, Cmd+click and Shift+click select; double-click or Return edits; Delete removes.
/// </summary>
internal sealed class IconGrid : Control
{
    private const double Pad = 20;
    private const double KeySize = 84;
    private const double CellWidth = 100;
    private const double CaptionGap = 8;
    private const double CaptionHeight = 18;
    private const double ColGap = 8;
    private const double RowGap = 18;
    private const double RowHeight = KeySize + CaptionGap + CaptionHeight + RowGap;

    private readonly List<IconTile> _items = new();
    private readonly HashSet<int> _selected = new();
    private int _anchor = -1;
    private int _focus = -1;
    private int _hover = -1;

    private readonly DispatcherTimer _animTimer = new();
    private AnimatedPreview? _anim;
    private int _animIndex = -1;
    private int _animFrame;

    public IconGrid()
    {
        Focusable = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        _animTimer.Tick += OnAnimationTick;
        ToolTip.SetShowDelay(this, 500);
    }

    public event EventHandler? SelectionChanged;
    public event EventHandler<int>? ItemActivated;
    public event EventHandler? DeleteRequested;

    public IReadOnlyCollection<int> SelectedIndices => _selected;
    public int Count => _items.Count;

    /// <summary>Cmd on macOS, Ctrl elsewhere.</summary>
    private static KeyModifiers CommandKey => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

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
        ToolTip.SetTip(this, null);
        InvalidateMeasure();
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectAll()
    {
        _selected.Clear();
        for (var i = 0; i < _items.Count; i++)
            _selected.Add(i);
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        if (_selected.Count == 0)
            return;
        _selected.Clear();
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private int Columns => ColumnsFor(Bounds.Width);

    private static int ColumnsFor(double width) =>
        Math.Max(1, (int)((width - 2 * Pad + ColGap) / (CellWidth + ColGap)));

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
        var cols = ColumnsFor(width);
        var rows = (_items.Count + cols - 1) / cols;
        var height = rows == 0 ? 0 : 2 * Pad + rows * RowHeight - RowGap;
        return new Size(width, height);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5)
            InvalidateMeasure();
    }

    private Rect CellBounds(int index)
    {
        var cols = Columns;
        var col = index % cols;
        var row = index / cols;
        return new Rect(Pad + col * (CellWidth + ColGap), Pad + row * RowHeight, CellWidth, KeySize + CaptionGap + CaptionHeight);
    }

    private static Rect KeyBounds(Rect cell) =>
        new(cell.X + (cell.Width - KeySize) / 2, cell.Y, KeySize, KeySize);

    private int HitTest(Point p)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (CellBounds(i).Contains(p))
                return i;
        }

        return -1;
    }

    public override void Render(DrawingContext context)
    {
        // Transparent fill so clicks between icons still reach the grid.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        for (var i = 0; i < _items.Count; i++)
        {
            var cell = CellBounds(i);
            var selected = _selected.Contains(i);
            var key = KeyBounds(cell);

            if (selected)
                context.DrawRectangle(Palette.AccentSoft, null, new RoundedRect(key.Inflate(4), 14));

            var edge = selected ? Palette.Accent : i == _hover ? Palette.BorderStrong : Palette.KeyEdge;
            var image = i == _animIndex && _anim is not null ? _anim.Frames[_animFrame] : _items[i].Image;
            Palette.DrawKey(context, key, image, 11, edge, selected ? 2 : 1);

            if (i == _focus && IsFocused && !selected)
                context.DrawRectangle(null, new Pen(Palette.Accent, 1, new DashStyle(new double[] { 1, 2 }, 0)), new RoundedRect(key.Inflate(3), 13));

            var caption = Palette.Line(_items[i].Caption, Palette.Small, selected ? Palette.Text : Palette.TextMuted,
                maxWidth: cell.Width + 4, alignment: TextAlignment.Center);
            context.DrawText(caption, new Point(cell.X - 2, key.Bottom + CaptionGap));
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
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, hit >= 0 ? _items[hit].Tooltip : null);
        StopAnimation();
        if (hit >= 0)
            _ = StartAnimationAsync(hit);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        StopAnimation();
        ToolTip.SetTip(this, null);
        if (_hover < 0)
            return;
        _hover = -1;
        InvalidateVisual();
    }

    /// <summary>Plays the icon's animation while it stays hovered. Decoding happens off the UI thread, once.</summary>
    private async Task StartAnimationAsync(int index)
    {
        var tile = _items[index];
        if (tile.Data is null || !AnimatedPreview.CanAnimate(tile.Extension))
            return;

        var anim = await AnimatedPreview.LoadAsync(tile.Data, tile.Extension);
        if (anim is null || _hover != index || index >= _items.Count || !ReferenceEquals(_items[index], tile))
            return;

        _anim = anim;
        _animIndex = index;
        _animFrame = 0;
        _animTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, anim.DurationsMs[0]));
        _animTimer.Start();
        InvalidateVisual();
    }

    private void StopAnimation()
    {
        _animTimer.Stop();
        var wasPlaying = _animIndex >= 0;
        _anim = null;
        _animIndex = -1;
        _animFrame = 0;
        if (wasPlaying)
            InvalidateVisual();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_anim is null || _animIndex < 0)
        {
            _animTimer.Stop();
            return;
        }

        _animFrame = (_animFrame + 1) % _anim.Frames.Count;
        _animTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, _anim.DurationsMs[_animFrame]));
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        ToolTip.SetIsOpen(this, false);
        Focus();
        var point = e.GetCurrentPoint(this);
        var hit = HitTest(point.Position);

        if (point.Properties.IsRightButtonPressed)
        {
            if (hit >= 0 && !_selected.Contains(hit))
                SelectSingle(hit);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2 && hit >= 0)
        {
            ItemActivated?.Invoke(this, hit);
            e.Handled = true;
            return;
        }

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var command = e.KeyModifiers.HasFlag(CommandKey);

        if (hit < 0)
        {
            if (!shift && !command)
                ClearSelection();
            return;
        }

        if (shift && _anchor >= 0)
            SelectRange(_anchor, hit, additive: command);
        else if (command)
            Toggle(hit);
        else
            SelectSingle(hit);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_items.Count == 0 || e.Handled)
            return;

        switch (e.Key)
        {
            case Key.A when e.KeyModifiers.HasFlag(CommandKey):
                SelectAll();
                e.Handled = true;
                return;
            case Key.Delete:
            case Key.Back:
                if (_selected.Count > 0)
                    DeleteRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            case Key.Enter:
            case Key.F2:
                if (_selected.Count > 0)
                    ItemActivated?.Invoke(this, _focus >= 0 ? _focus : _selected.Min());
                e.Handled = true;
                return;
            case Key.Escape:
                ClearSelection();
                return;
        }

        var delta = e.Key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            Key.Up => -Columns,
            Key.Down => Columns,
            _ => 0
        };

        if (delta == 0)
            return;

        var next = Math.Clamp(_focus < 0 ? 0 : _focus + delta, 0, _items.Count - 1);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _anchor >= 0)
            SelectRange(_anchor, next, additive: false);
        else
            SelectSingle(next);
        this.BringIntoView(CellBounds(next).Inflate(Pad));
        e.Handled = true;
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    private void SelectSingle(int index)
    {
        _selected.Clear();
        _selected.Add(index);
        _anchor = _focus = index;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Toggle(int index)
    {
        if (!_selected.Remove(index))
            _selected.Add(index);
        _anchor = _focus = index;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectRange(int from, int to, bool additive)
    {
        if (!additive)
            _selected.Clear();
        for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
            _selected.Add(i);
        _focus = to;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
