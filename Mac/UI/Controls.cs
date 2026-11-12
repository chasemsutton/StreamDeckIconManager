using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace StreamDeckIconManager.Mac.UI;

internal enum ButtonKind
{
    Primary,
    Secondary,
    Ghost,
    Danger
}

/// <summary>A button in one of the app's four styles, with an optional icon before the text.</summary>
internal sealed class ThemedButton : Button
{
    private readonly TextBlock _text;
    private readonly Border _glyphHost;
    private ButtonKind _kind;

    public ThemedButton(string text, ButtonKind kind = ButtonKind.Secondary, string? glyph = null)
    {
        Classes.Add("sd");
        _text = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _glyphHost = new Border { VerticalAlignment = VerticalAlignment.Center };
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children = { _glyphHost, _text }
        };
        _kind = kind;
        Classes.Add(ClassOf(kind));
        Glyph = glyph;
    }

    protected override Type StyleKeyOverride => typeof(Button);

    public string Text
    {
        get => _text.Text ?? "";
        set => _text.Text = value;
    }

    public string? Glyph
    {
        set
        {
            _glyphHost.Child = value is null ? null : Glyphs.Icon(value, 14);
            _glyphHost.IsVisible = value is not null;
        }
    }

    public ButtonKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value)
                return;
            Classes.Remove(ClassOf(_kind));
            _kind = value;
            Classes.Add(ClassOf(value));
        }
    }

    private static string ClassOf(ButtonKind kind) => kind switch
    {
        ButtonKind.Primary => "primary",
        ButtonKind.Ghost => "ghost",
        ButtonKind.Danger => "danger",
        _ => "secondary"
    };
}

/// <summary>
/// An image on a Stream Deck style key. Optionally clickable (accent edge and a picture glyph on
/// hover), and able to play animated GIF/WEBP icons.
/// </summary>
internal sealed class KeyView : Control
{
    private readonly DispatcherTimer _timer = new();
    private IImage? _image;
    private AnimatedPreview? _anim;
    private int _frame;
    private int _playToken;
    private bool _hover;

    public KeyView()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        _timer.Tick += (_, _) =>
        {
            if (_anim is null)
            {
                _timer.Stop();
                return;
            }

            _frame = (_frame + 1) % _anim.Frames.Count;
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, _anim.DurationsMs[_frame]));
            InvalidateVisual();
        };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public double CornerRadiusValue { get; set; } = 12;
    public double EdgeWidth { get; set; } = 1;

    /// <summary>Shows an empty key with a dashed edge (the "no packs yet" illustration).</summary>
    public bool Ghost { get; set; }

    /// <summary>A line icon drawn in the middle of a <see cref="Ghost"/> key.</summary>
    public string? GhostGlyph { get; set; }

    /// <summary>Clickable: accent edge and a picture glyph while the pointer is over it.</summary>
    public bool Clickable
    {
        get => _clickable;
        set
        {
            _clickable = value;
            Cursor = value ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        }
    }

    private bool _clickable;

    public event EventHandler? Click;

    public IImage? Image
    {
        get => _image;
        set
        {
            _image = value;
            InvalidateVisual();
        }
    }

    /// <summary>Starts playing the icon if it's an animated GIF/WEBP; stops any previous one.</summary>
    public async void PlayIfAnimated(byte[]? data, string? extension)
    {
        var token = ++_playToken;
        _timer.Stop();
        _anim = null;
        _frame = 0;
        InvalidateVisual();

        if (data is null || !AnimatedPreview.CanAnimate(extension))
            return;

        var anim = await AnimatedPreview.LoadAsync(data, extension);
        if (anim is null || token != _playToken)
            return;

        _anim = anim;
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, anim.DurationsMs[0]));
        _timer.Start();
        InvalidateVisual();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_clickable && e.InitialPressMouseButton == MouseButton.Left && new Rect(Bounds.Size).Contains(e.GetPosition(this)))
            Click?.Invoke(this, EventArgs.Empty);
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size).Deflate(1);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        if (Ghost)
        {
            var path = new RoundedRect(rect, CornerRadiusValue);
            var filled = _image is not null || GhostGlyph is not null;
            context.DrawRectangle(filled ? Palette.Raised : Palette.Sidebar, new Pen(Palette.Border, 1, new DashStyle(new double[] { 4, 3 }, 0)), path);
            if (_image is not null)
                context.DrawImage(_image, new Rect(_image.Size), Palette.Fit(_image.Size, rect.Deflate(rect.Width * 0.18)));
            if (GhostGlyph is not null)
            {
                var size = rect.Width * 0.38;
                Draw.Glyph(context, GhostGlyph, new Rect(rect.Center.X - size / 2, rect.Center.Y - size / 2, size, size), Palette.TextMuted, 1.6);
            }

            return;
        }

        var hover = _clickable && _hover;
        var image = _anim is not null ? _anim.Frames[_frame] : _image;
        Palette.DrawKey(context, rect, image, CornerRadiusValue, hover ? Palette.Accent : Palette.KeyEdge, hover ? 1.5 : EdgeWidth);

        if (hover)
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), null, new RoundedRect(rect.Deflate(1.5), CornerRadiusValue));
            var size = Math.Min(28, rect.Width * 0.3);
            Draw.Glyph(context, Glyphs.Picture, new Rect(rect.Center.X - size / 2, rect.Center.Y - size / 2, size, size), Palette.White);
        }
    }
}

/// <summary>Helpers for drawing in Render overrides.</summary>
internal static class Draw
{
    private static readonly Dictionary<string, Geometry> Geometries = new();

    /// <summary>Draws one of the <see cref="Glyphs"/> line icons inside <paramref name="box"/>.</summary>
    public static void Glyph(DrawingContext context, string data, Rect box, IBrush brush, double stroke = 2)
    {
        if (!Geometries.TryGetValue(data, out var geometry))
            Geometries[data] = geometry = Geometry.Parse(data);

        var scale = Math.Min(box.Width, box.Height) / 24;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(box.X, box.Y)))
            context.DrawGeometry(null, new Pen(brush, stroke, null, PenLineCap.Round, PenLineJoin.Round), geometry);
    }
}

/// <summary>Label, text box and hint (or error) line.</summary>
internal sealed class InputField : StackPanel
{
    private readonly TextBlock _hint;
    private readonly string _hintText;

    public InputField(string label, string hint = "", string placeholder = "", int maxLength = 0)
    {
        Spacing = 6;
        Margin = new Thickness(0, 0, 0, 14);
        _hintText = hint;

        Box = new TextBox
        {
            Watermark = placeholder,
            MaxLength = maxLength,
            AcceptsReturn = false
        };

        _hint = Palette.Label(hint, Palette.Small, Palette.TextFaint, wrap: true);
        _hint.IsVisible = hint.Length > 0;

        Children.Add(Palette.Label(label, Palette.Body, Palette.Text, strong: true));
        Children.Add(Box);
        Children.Add(_hint);

        Box.TextChanged += (_, _) =>
        {
            if (ErrorText is not null)
                ErrorText = null;
        };
    }

    public TextBox Box { get; }

    /// <summary>The text, trimmed.</summary>
    public string Value
    {
        get => (Box.Text ?? "").Trim();
        set
        {
            Box.Text = value;
            Box.CaretIndex = value.Length;
        }
    }

    public string? ErrorText
    {
        get => _error;
        set
        {
            _error = value;
            _hint.Text = value ?? _hintText;
            _hint.Foreground = value is null ? Palette.TextFaint : Palette.Danger;
            _hint.IsVisible = (_hint.Text ?? "").Length > 0;
            Box.Classes.Set("error", value is not null);
        }
    }

    private string? _error;

    public void FocusInput()
    {
        Box.Focus();
        Box.SelectAll();
    }
}

/// <summary>
/// Large radio-style choice: an icon, a title, a description, an optional "Recommended" badge
/// and an optional note line. Cards in the same parent act as a group.
/// </summary>
internal sealed class OptionCard : Border
{
    private readonly TextBlock _title;
    private readonly TextBlock _description;
    private readonly TextBlock _note;
    private readonly Border _badge;
    private readonly Border _recommended;
    private readonly Border _radio;
    private readonly Border _radioDot;
    private bool _checked;
    private bool _hover;

    public OptionCard(string title, string glyph)
    {
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1.5);
        Padding = new Thickness(14, 12);
        Margin = new Thickness(0, 0, 0, 10);
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;

        _badge = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(9),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 14, 0),
            Child = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        _glyph = glyph;

        _title = Palette.Label(title, Palette.Body, Palette.Text, strong: true);
        _recommended = new Border
        {
            Background = Palette.AccentSoft,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Child = Palette.Label("RECOMMENDED", 10, Palette.Accent, strong: true)
        };
        _description = Palette.Label("", Palette.Small, Palette.TextMuted, wrap: true);
        _description.Margin = new Thickness(0, 3, 0, 0);
        _note = Palette.Label("", Palette.Small, Palette.TextFaint, wrap: true);
        _note.Margin = new Thickness(0, 6, 0, 0);
        _note.IsVisible = false;

        _radioDot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Palette.White, IsVisible = false };
        _radio = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1.5),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12, 2, 0, 0),
            Child = _radioDot
        };

        var text = new StackPanel
        {
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Children = { _title, _recommended } },
                _description,
                _note
            }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(_badge, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(_radio, 2);
        grid.Children.Add(_badge);
        grid.Children.Add(text);
        grid.Children.Add(_radio);
        Child = grid;

        UpdateLook();
    }

    private readonly string _glyph;

    public event EventHandler? CheckedChanged;

    public string Description
    {
        set => _description.Text = value;
    }

    public string Note
    {
        set
        {
            _note.Text = value;
            _note.IsVisible = value.Length > 0;
        }
    }

    public IBrush NoteColor
    {
        set => _note.Foreground = value;
    }

    public bool Recommended
    {
        set => _recommended.IsVisible = value;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;
            _checked = value;
            if (value && Parent is Panel panel)
            {
                foreach (var sibling in panel.Children.OfType<OptionCard>().Where(c => c != this))
                    sibling.Checked = false;
            }

            UpdateLook();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEnabledProperty)
        {
            Opacity = IsEnabled ? 1 : 0.5;
            Cursor = IsEnabled ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        UpdateLook();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        UpdateLook();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled)
            return;
        Checked = true;
        Focus();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Up or Key.Down && Parent is Panel panel)
        {
            var cards = panel.Children.OfType<OptionCard>().Where(c => c.IsEnabled).ToList();
            var index = cards.IndexOf(this) + (e.Key == Key.Down ? 1 : -1);
            if (index >= 0 && index < cards.Count)
            {
                cards[index].Checked = true;
                cards[index].Focus();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            Checked = true;
            e.Handled = true;
        }
    }

    private void UpdateLook()
    {
        Background = _checked ? Palette.AccentSoft : _hover ? Palette.RaisedHover : Palette.Raised;
        BorderBrush = _checked ? Palette.Accent : _hover ? Palette.BorderStrong : Palette.Border;
        _badge.Background = _checked ? Palette.Accent : Palette.SidebarHover;
        ((Border)_badge.Child!).Child = Glyphs.Icon(_glyph, 17, _checked ? Palette.White : Palette.Text);
        _radio.BorderBrush = _checked ? Palette.Accent : Palette.BorderStrong;
        _radio.Background = _checked ? Palette.Accent : null;
        _radioDot.IsVisible = _checked;
    }
}

/// <summary>Restricts what can be typed or pasted into a text box.</summary>
internal static class InputFilters
{
    public static bool LetterDigitSpace(char c) => char.IsLetterOrDigit(c) || c == ' ';
    public static bool LetterSpace(char c) => char.IsLetter(c) || c == ' ';
    public static bool Printable(char c) => !char.IsControl(c);
    public static bool PrintableNoComma(char c) => !char.IsControl(c) && c != ',';

    /// <summary>Pack descriptions. No commas: Stream Deck would split the hover text at them.</summary>
    public static bool SentenceText(char c) => char.IsLetterOrDigit(c) || " .!?'&()-:".Contains(c);

    public static void Attach(TextBox box, Func<char, bool> allowed)
    {
        box.AddHandler(InputElement.TextInputEvent, (_, e) =>
        {
            if (e.Text is { Length: > 0 } text && !text.All(allowed))
            {
                var cleaned = new string(text.Where(allowed).ToArray());
                if (cleaned.Length == 0)
                    e.Handled = true;
                else
                    e.Text = cleaned;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        box.TextChanged += (_, _) =>
        {
            var current = box.Text ?? "";
            if (current.All(allowed))
                return;

            var caret = box.CaretIndex;
            var cleaned = new string(current.Where(allowed).ToArray());
            box.Text = cleaned;
            box.CaretIndex = Math.Clamp(caret - (current.Length - cleaned.Length), 0, cleaned.Length);
        };
    }
}
