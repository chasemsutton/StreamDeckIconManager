namespace Add_Stream_Deck_Icon.UI;

/// <summary>An icon being named: its image plus the name and tags the user settles on.</summary>
internal sealed class IconDraft
{
    public IconDraft(Image picture, string name, IEnumerable<string> tags)
    {
        Picture = picture;
        Name = name;
        Tags = tags.ToArray();
    }

    public Image Picture { get; }
    public string Name { get; set; }
    public string[] Tags { get; set; }
}

/// <summary>
/// Steps through one or more icons so each can get a name and search tags.
/// Fields start filled from the file name, so pressing Enter through the list is enough.
/// Cancel leaves every icon unchanged.
/// </summary>
internal sealed class IconDetailsDialog : ThemedDialog
{
    public const string CustomTag = "custom";

    private readonly IReadOnlyList<IconDraft> _drafts;
    private readonly bool _editing;
    private readonly string _packName;
    private readonly KeyPreviewBox _preview;
    private readonly Label _step;
    private readonly InputField _name;
    private readonly InputField _tags;
    private readonly ThemedButton _back;
    private readonly ThemedButton _next;
    private readonly ThemedButton? _useDefaults;
    private int _index;

    public IconDetailsDialog(IReadOnlyList<IconDraft> drafts, string packName, bool editing)
        : base(editing ? "Edit name and tags" : $"Add icons to {packName}",
            "Tags are what you type in the Stream Deck icon search to find this icon.",
            520)
    {
        _drafts = drafts;
        _editing = editing;
        _packName = packName;

        _preview = new KeyPreviewBox
        {
            Size = new Size(148, 148),
            Margin = new Padding(0, 4, 24, 0)
        };

        _step = new Label
        {
            AutoSize = true,
            Font = Theme.Caption,
            ForeColor = Theme.Accent,
            Margin = new Padding(0, 0, 0, 10)
        };

        _name = new InputField("Name", maxLength: 32);
        _tags = new InputField("Tags", $"Separate with commas. \"{CustomTag}\" is added for you.", "e.g. mute, mic, audio", 200)
        {
            Margin = Padding.Empty
        };
        InputFilters.Attach(_name.TextBox, InputFilters.PrintableNoComma);
        InputFilters.Attach(_tags.TextBox, InputFilters.Printable);

        var fields = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.Controls.Add(_step, 0, 0);
        fields.Controls.Add(_name, 0, 1);
        fields.Controls.Add(_tags, 0, 2);
        _name.Dock = DockStyle.Fill;
        _tags.Dock = DockStyle.Fill;

        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 8)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(_preview, 0, 0);
        row.Controls.Add(fields, 1, 0);
        AddBody(row);

        if (!editing && drafts.Count > 1)
        {
            _useDefaults = new ThemedButton("Keep file names for the rest", ButtonKind.Ghost);
            _useDefaults.Click += (_, _) => { Commit(); DialogResult = DialogResult.OK; };
            FooterLeft.Controls.Add(_useDefaults);
        }

        _back = new ThemedButton("Back", ButtonKind.Ghost);
        _back.Click += (_, _) => Step(-1);
        var cancel = new ThemedButton("Cancel", ButtonKind.Ghost) { DialogResult = DialogResult.Cancel };
        _next = new ThemedButton("Next", ButtonKind.Primary);
        _next.Click += (_, _) =>
        {
            if (_index == _drafts.Count - 1)
            {
                Commit();
                DialogResult = DialogResult.OK;
            }
            else
            {
                Step(1);
            }
        };

        FooterRight.Controls.Add(cancel);
        FooterRight.Controls.Add(_back);
        FooterRight.Controls.Add(_next);
        AcceptButton = _next;
        CancelButton = cancel;

        Complete();
        ShowDraft(0);
        Shown += (_, _) => _name.FocusInput();
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
        _name.Value = draft.Name;
        _tags.Value = FormatTags(draft.Tags);
        _name.ErrorText = null;

        var many = _drafts.Count > 1;
        _step.Text = $"ICON {index + 1} OF {_drafts.Count}";
        _step.Visible = many;
        _back.Visible = many && index > 0;
        if (_useDefaults is not null)
            _useDefaults.Visible = index < _drafts.Count - 1;

        var last = index == _drafts.Count - 1;
        _next.Text = !last ? "Next" : _editing ? "Save" : many ? $"Add {_drafts.Count} icons" : "Add icon";
        _next.Glyph = last && !_editing ? Theme.Glyphs.Add : null;
    }

    private sealed class KeyPreviewBox : Control
    {
        private Image? _image;

        public KeyPreviewBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public Image? Image
        {
            get => _image;
            set { _image = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var s = DeviceDpi / 96f;
            g.Clear(Parent?.BackColor ?? Theme.Window);
            Theme.HighQuality(g);
            Theme.DrawKey(g, new RectangleF(1, 1, Width - 2, Height - 2), _image, 20 * s, Theme.KeyEdge, 1.5f * s);
        }
    }
}
