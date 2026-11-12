using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace StreamDeckIconManager.Mac.UI;

/// <summary>
/// Dashed tray at the bottom of the pack view. Empty, it invites a drop or a click to browse.
/// With images queued, it previews them and offers Add / Clear.
/// </summary>
internal sealed class DropZone : Grid
{
    private const double PadLeft = 16;
    private readonly StackPanel _actions;
    private IReadOnlyList<IImage> _queued = Array.Empty<IImage>();
    private bool _dragActive;
    private bool _hover;
    private string _packName = "";
    private readonly Surface _surface;

    public DropZone()
    {
        Height = 92;
        Cursor = new Cursor(StandardCursorType.Hand);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        ColumnDefinitions = new ColumnDefinitions("*,Auto");

        // Panel.Render is sealed, so the tray is drawn by a child that fills both columns.
        _surface = new Surface(this);
        SetColumnSpan(_surface, 2);
        Children.Add(_surface);

        ClearButton = new ThemedButton("Clear", ButtonKind.Ghost);
        AddButton = new ThemedButton("Add icons", ButtonKind.Primary, Glyphs.Add);

        _actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0),
            IsVisible = false,
            Cursor = Cursor.Default,
            Children = { ClearButton, AddButton }
        };
        SetColumn(_actions, 1);
        Children.Add(_actions);
    }

    public event EventHandler? BrowseRequested;

    public ThemedButton AddButton { get; }
    public ThemedButton ClearButton { get; }

    public void SetQueue(IReadOnlyList<IImage> images, string packName)
    {
        _queued = images;
        _packName = packName;
        _actions.IsVisible = images.Count > 0;
        AddButton.Text = images.Count == 1 ? "Add 1 icon" : $"Add {images.Count} icons";
        _surface.InvalidateVisual();
    }

    public bool DragActive
    {
        get => _dragActive;
        set
        {
            if (_dragActive == value)
                return;
            _dragActive = value;
            _surface.InvalidateVisual();
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        _surface.InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        _surface.InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // Presses on Add / Clear are handled by the buttons and don't get here.
        if (!e.Handled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            BrowseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class Surface : Control
    {
        private readonly DropZone _owner;

        public Surface(DropZone owner) => _owner = owner;

        public override void Render(DrawingContext context) => _owner.DrawTray(context);
    }

    private void DrawTray(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size).Deflate(1);
        var active = _dragActive || _hover;
        var fill = _dragActive ? Palette.AccentSoft : active ? Palette.TrayHover : Palette.Window;
        var dashed = !(_queued.Count > 0 && !_dragActive);
        var pen = new Pen(_dragActive ? Palette.Accent : active ? Palette.BorderStrong : Palette.Border, _dragActive ? 1.5 : 1,
            dashed ? new DashStyle(new double[] { 4, 3 }, 0) : null);
        context.DrawRectangle(fill, pen, new RoundedRect(rect, 10));

        if (_queued.Count == 0 || _dragActive)
            PaintInvite(context);
        else
            PaintQueue(context);
    }

    private void PaintInvite(DrawingContext context)
    {
        var h = Bounds.Height;
        var badge = new Rect(PadLeft + 4, (h - 44) / 2, 44, 44);
        context.DrawRectangle(_dragActive ? Palette.Accent : Palette.Raised, null, new RoundedRect(badge, 10));
        Draw.Glyph(context, _dragActive ? Glyphs.Import : Glyphs.Add, badge.Deflate(12), _dragActive ? Palette.White : Palette.Text);

        var x = badge.Right + 14;
        var w = Bounds.Width - x - 18;
        var title = _dragActive
            ? (_packName.Length > 0 ? $"Drop to queue images for {_packName}" : "Drop to queue images")
            : "Drop images here, or click to browse";
        var titleText = Palette.Line(title, Palette.Body, Palette.Text, strong: true, maxWidth: w);
        context.DrawText(titleText, new Point(x, h / 2 - titleText.Height - 1));
        context.DrawText(Palette.Line("PNG, JPG, GIF, WEBP or SVG. Square images work best, ideally 144 × 144 px.", Palette.Small, Palette.TextFaint, maxWidth: w),
            new Point(x, h / 2 + 2));
    }

    private void PaintQueue(DrawingContext context)
    {
        var h = Bounds.Height;
        const double key = 48;
        const double gap = 8;
        var x = PadLeft + 4;
        var y = (h - key) / 2;
        var limit = _actions.IsVisible ? _actions.Bounds.Left - 24 : Bounds.Width - 18;
        const double textWidth = 220;
        var room = Math.Max(1, (int)((limit - x - textWidth) / (key + gap)));
        var shown = Math.Min(_queued.Count, Math.Min(room, 8));
        var overflow = _queued.Count - shown;

        for (var i = 0; i < shown; i++)
        {
            Palette.DrawKey(context, new Rect(x, y, key, key), _queued[i], 8, Palette.KeyEdge);
            x += key + gap;
        }

        if (overflow > 0)
        {
            var more = new Rect(x, y, key, key);
            context.DrawRectangle(Palette.Raised, null, new RoundedRect(more, 8));
            var text = Palette.Line($"+{overflow}", Palette.Body, Palette.TextMuted, strong: true);
            context.DrawText(text, new Point(more.Center.X - text.Width / 2, more.Center.Y - text.Height / 2));
            x += key + gap;
        }

        var tx = x + 8;
        var tw = Math.Max(1, limit - tx);
        var count = _queued.Count == 1 ? "1 image ready" : $"{_queued.Count} images ready";
        var countText = Palette.Line(count, Palette.Body, Palette.Text, strong: true, maxWidth: tw);
        context.DrawText(countText, new Point(tx, h / 2 - countText.Height - 1));
        var sub = _packName.Length > 0 ? $"Name and tag them, then add to {_packName}" : "Create a pack to add them to";
        context.DrawText(Palette.Line(sub, Palette.Small, Palette.TextFaint, maxWidth: tw), new Point(tx, h / 2 + 2));
    }
}
