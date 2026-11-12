using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace StreamDeckIconManager.Mac.UI;

/// <summary>An icon being named: its image plus the name and tags the user settles on.</summary>
internal sealed class IconDraft
{
    public IconDraft(IImage picture, string name, IEnumerable<string> tags, byte[]? data = null, string? extension = null)
    {
        Picture = picture;
        Name = name;
        Tags = tags.ToArray();
        Data = data;
        Extension = extension;
    }

    public IImage Picture { get; }
    public byte[]? Data { get; }
    public string? Extension { get; }
    public string Name { get; set; }
    public string[] Tags { get; set; }
}

/// <summary>
/// Steps through one or more icons so each can get a name and search tags.
/// Fields start filled from the file name, so pressing Return through the list is enough.
/// Cancel leaves every icon unchanged.
/// </summary>
internal sealed class IconDetailsDialog : ThemedDialog
{
    public const string CustomTag = "custom";

    private readonly IReadOnlyList<IconDraft> _drafts;
    private readonly bool _editing;
    private readonly KeyView _preview;
    private readonly TextBlock _step;
    private readonly InputField _name;
    private readonly InputField _tags;
    private readonly ThemedButton _back;
    private readonly ThemedButton _next;
    private readonly ThemedButton? _useDefaults;
    private int _index;

    public IconDetailsDialog(IReadOnlyList<IconDraft> drafts, string packName, bool editing,
        int oversizedCount = 0, bool keepFullSize = false)
        : base(editing ? "Edit name and tags" : $"Add icons to {packName}",
            "Tags are what you type in the Stream Deck icon search to find this icon.",
            520)
    {
        _drafts = drafts;
        _editing = editing;

        _preview = new KeyView
        {
            Width = 148,
            Height = 148,
            CornerRadiusValue = 20,
            EdgeWidth = 1.5,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 24, 0)
        };

        _step = Palette.Label("", Palette.Caption, Palette.Accent, strong: true);
        _step.Margin = new Thickness(0, 0, 0, 10);

        _name = new InputField("Name", maxLength: 32);
        _tags = new InputField("Tags", $"Separate with commas. \"{CustomTag}\" is added for you.", "e.g. mute, mic, audio", 200)
        {
            Margin = new Thickness(0)
        };
        InputFilters.Attach(_name.Box, InputFilters.PrintableNoComma);
        InputFilters.Attach(_tags.Box, InputFilters.Printable);

        var fields = new StackPanel { Children = { _step, _name, _tags } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetColumn(fields, 1);
        row.Children.Add(_preview);
        row.Children.Add(fields);
        AddBody(row);

        KeepFullSize = keepFullSize;
        if (!editing && oversizedCount > 0)
            AddBody(BuildSizeChoice(oversizedCount, keepFullSize));

        if (!editing && drafts.Count > 1)
        {
            _useDefaults = new ThemedButton("Keep file names for the rest", ButtonKind.Ghost);
            _useDefaults.Click += (_, _) =>
            {
                Commit();
                Finish(true);
            };
            FooterLeft.Children.Add(_useDefaults);
        }

        _back = new ThemedButton("Back", ButtonKind.Ghost);
        _back.Click += (_, _) => Step(-1);
        var cancel = new ThemedButton("Cancel", ButtonKind.Ghost) { IsCancel = true };
        cancel.Click += (_, _) => Finish(false);
        _next = new ThemedButton("Next", ButtonKind.Primary) { IsDefault = true };
        _next.Click += (_, _) =>
        {
            if (_index == _drafts.Count - 1)
            {
                if (_name.Value.Length == 0)
                {
                    _name.ErrorText = "Give the icon a name.";
                    _name.FocusInput();
                    return;
                }

                Commit();
                Finish(true);
            }
            else
            {
                Step(1);
            }
        };

        FooterRight.Children.Add(cancel);
        FooterRight.Children.Add(_back);
        FooterRight.Children.Add(_next);

        ShowDraft(0);
        Opened += (_, _) => _name.FocusInput();
    }

    /// <summary>
    /// The user's choice for "Keep full-size images" (only offered when adding images larger than
    /// Stream Deck's 144 px icon size).
    /// </summary>
    public bool KeepFullSize { get; private set; }

    private Control BuildSizeChoice(int oversizedCount, bool keepFullSize)
    {
        var info = Paragraph((oversizedCount == 1 ? "1 image is" : $"{oversizedCount} images are") +
                             $" larger than Stream Deck's {IconResizer.TargetSize} × {IconResizer.TargetSize} px icon size and will be scaled down to fit. Nothing is cropped.",
            Palette.TextMuted, Palette.Small, 6);

        var keep = new CheckBox
        {
            Content = "Keep full-size images",
            IsChecked = keepFullSize,
            Foreground = Palette.Text,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var warning = Paragraph("Large images can make this app and Stream Deck slow and use a lot of memory. " +
                                "Stream Deck shows icons at 144 px, so the extra detail isn't visible on your keys.",
            Palette.Warning, Palette.Small, 0);
        warning.IsVisible = keepFullSize;

        keep.IsCheckedChanged += (_, _) =>
        {
            KeepFullSize = keep.IsChecked == true;
            warning.IsVisible = KeepFullSize;
        };

        return new StackPanel { Margin = new Thickness(0, 6, 0, 4), Children = { info, keep, warning } };
    }

    /// <summary>Turns a comma separated tag string into clean tags, always including "custom".</summary>
    public static string[] ParseTags(string text) =>
        text.Split(',')
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Prepend(CustomTag)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Tags as shown in the field, without the automatic "custom" tag.</summary>
    public static string FormatTags(IEnumerable<string> tags) =>
        string.Join(", ", tags.Where(t => !t.Equals(CustomTag, StringComparison.OrdinalIgnoreCase)));

    private void Commit()
    {
        var draft = _drafts[_index];
        if (_name.Value.Length > 0)
            draft.Name = _name.Value;
        draft.Tags = ParseTags(_tags.Value);
    }

    private void Step(int delta)
    {
        if (delta > 0 && _name.Value.Length == 0)
        {
            _name.ErrorText = "Give the icon a name.";
            _name.FocusInput();
            return;
        }

        Commit();
        ShowDraft(Math.Clamp(_index + delta, 0, _drafts.Count - 1));
        _name.FocusInput();
    }

    private void ShowDraft(int index)
    {
        _index = index;
        var draft = _drafts[index];
        _preview.Image = draft.Picture;
        _preview.PlayIfAnimated(draft.Data, draft.Extension);
        _name.Value = draft.Name;
        _tags.Value = FormatTags(draft.Tags);
        _name.ErrorText = null;

        var many = _drafts.Count > 1;
        _step.Text = $"ICON {index + 1} OF {_drafts.Count}";
        _step.IsVisible = many;
        _back.IsVisible = many && index > 0;
        if (_useDefaults is not null)
            _useDefaults.IsVisible = index < _drafts.Count - 1;

        var last = index == _drafts.Count - 1;
        _next.Text = !last ? "Next" : _editing ? "Save" : many ? $"Add {_drafts.Count} icons" : "Add icon";
        _next.Glyph = last && !_editing ? Glyphs.Add : null;
    }
}
