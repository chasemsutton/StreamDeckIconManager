namespace Add_Stream_Deck_Icon.UI;

/// <summary>An icon being named: its image plus the name and tags the user settles on.</summary>
internal sealed class IconDraft
{
    public IconDraft(Image picture, string name, IEnumerable<string> tags, byte[]? data = null, string? extension = null)
    {
        Picture = picture;
        Name = name;
        Tags = tags.ToArray();
        Data = data;
        Extension = extension;
    }

    public Image Picture { get; }

    /// <summary>The image file, so animated GIF/WEBP icons can play in the preview.</summary>
    public byte[]? Data { get; }
    public string? Extension { get; }
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

    /// <summary>
    /// The user's choice for "Keep full-size images" (only offered when adding images larger than
    /// Stream Deck's 144 px icon size).
    /// </summary>
    public bool KeepFullSize { get; private set; }

    public IconDetailsDialog(IReadOnlyList<IconDraft> drafts, string packName, bool editing,
        int oversizedCount = 0, bool keepFullSize = false)
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

        KeepFullSize = keepFullSize;
        if (!editing && oversizedCount > 0)
            AddBody(BuildSizeChoice(oversizedCount, keepFullSize));

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

    /// <summary>
    /// Explains that large images get scaled to Stream Deck's icon size, with a "keep full size"
    /// checkbox that reveals a warning when ticked.
    /// </summary>
    private Control BuildSizeChoice(int oversizedCount, bool keepFullSize)
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 6, 0, 4)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var info = new Label
        {
            Text = (oversizedCount == 1 ? "1 image is" : $"{oversizedCount} images are") +
                   $" larger than Stream Deck's {IconResizer.TargetSize} × {IconResizer.TargetSize} px icon size and will be scaled down to fit. Nothing is cropped.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Font = Theme.Small,
            ForeColor = Theme.TextMuted,
            Margin = new Padding(0, 0, 0, 6)
        };

        var keep = new CheckBox
        {
            Text = "Keep full-size images",
            Checked = keepFullSize,
            AutoSize = true,
            Font = Theme.Body,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 4)
        };
        keep.FlatAppearance.BorderColor = Theme.BorderStrong;
        keep.FlatAppearance.CheckedBackColor = Theme.Accent;
        keep.FlatAppearance.MouseOverBackColor = Theme.RaisedHover;

        var warning = new Label
        {
            Text = "Large images can make this app and Stream Deck slow and use a lot of memory. " +
                   "Stream Deck shows icons at 144 px, so the extra detail isn't visible on your keys.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Font = Theme.Small,
            ForeColor = Theme.Warning,
            Visible = keepFullSize,
            Margin = new Padding(0, 0, 0, 0)
        };

        keep.CheckedChanged += (_, _) =>
        {
            KeepFullSize = keep.Checked;
            warning.Visible = keep.Checked;
        };

        panel.Controls.Add(info, 0, 0);
        panel.Controls.Add(keep, 0, 1);
        panel.Controls.Add(warning, 0, 2);
        return panel;
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
        _step.Visible = many;
        _back.Visible = many && index > 0;
        if (_useDefaults is not null)
            _useDefaults.Visible = index < _drafts.Count - 1;

        var last = index == _drafts.Count - 1;
        _next.Text = !last ? "Next" : _editing ? "Save" : many ? $"Add {_drafts.Count} icons" : "Add icon";
        _next.Glyph = last && !_editing ? Theme.Glyphs.Add : null;
    }

    /// <summary>Large key preview. Animated GIF/WEBP icons play continuously here.</summary>
    private sealed class KeyPreviewBox : Control
    {
        private readonly System.Windows.Forms.Timer _timer = new();
        private Image? _image;
        private AnimatedPreview? _anim;
        private int _frame;
        private int _playToken;

        public KeyPreviewBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _timer.Tick += (_, _) =>
            {
                if (_anim is null)
                {
                    _timer.Stop();
                    return;
                }

                _frame = (_frame + 1) % _anim.Frames.Count;
                _timer.Interval = Math.Max(20, _anim.DurationsMs[_frame]);
                Invalidate();
            };
        }

        public Image? Image
        {
            get => _image;
            set { _image = value; Invalidate(); }
        }

        /// <summary>Starts playing the icon if it's an animated GIF/WEBP; stops any previous one.</summary>
        public async void PlayIfAnimated(byte[]? data, string? extension)
        {
            var token = ++_playToken;
            _timer.Stop();
            _anim = null;
            _frame = 0;
            Invalidate();

            if (data is null || !AnimatedPreview.CanAnimate(extension))
                return;

            var anim = await AnimatedPreview.LoadAsync(data, extension);
            if (anim is null || IsDisposed || token != _playToken)
                return;

            _anim = anim;
            _timer.Interval = Math.Max(20, anim.DurationsMs[0]);
            _timer.Start();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var s = DeviceDpi / 96f;
            g.Clear(Parent?.BackColor ?? Theme.Window);
            Theme.HighQuality(g);
            var image = _anim is not null ? _anim.Frames[_frame] : _image;
            Theme.DrawKey(g, new RectangleF(1, 1, Width - 2, Height - 2), image, 20 * s, Theme.KeyEdge, 1.5f * s);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
