namespace Add_Stream_Deck_Icon.UI;

/// <summary>
/// Asked once when GIF icons are found in existing packs: convert them to animated WebP (which
/// Stream Deck's icon library can preview) or leave them as they are. Converting happens here,
/// with progress, before the dialog closes.
/// </summary>
internal sealed class ConvertGifsDialog : ThemedDialog
{
    private readonly IReadOnlyList<(Manifest Pack, int Gifs)> _packs;
    private readonly TableLayoutPanel _info;
    private readonly TableLayoutPanel _progress;
    private readonly Label _status;
    private readonly ProgressLine _bar;
    private readonly ThemedButton _keep;
    private readonly ThemedButton _convert;
    private bool _busy;

    public ConvertGifsDialog(IReadOnlyList<(Manifest Pack, int Gifs)> packsWithGifs)
        : base("Convert GIF icons?", Describe(packsWithGifs), 440)
    {
        _packs = packsWithGifs;

        _info = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        _info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var shown = packsWithGifs.Take(5).ToList();
        foreach (var (pack, gifs) in shown)
            _info.Controls.Add(PackLine(pack.Name, gifs == 1 ? "1 GIF" : $"{gifs} GIFs"), 0, _info.Controls.Count);
        if (packsWithGifs.Count > shown.Count)
            _info.Controls.Add(PackLine($"and {packsWithGifs.Count - shown.Count} more packs", ""), 0, _info.Controls.Count);

        _info.Controls.Add(new Label
        {
            Text = "Converting keeps every frame and its timing exactly as it is. Keys you've already set up aren't affected, " +
                   "and new GIFs you add are converted either way.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Font = Theme.Small,
            ForeColor = Theme.TextFaint,
            Margin = new Padding(0, 14, 0, 4)
        }, 0, _info.Controls.Count);

        _status = new Label { AutoSize = true, Font = Theme.BodyStrong, ForeColor = Theme.Text, MaximumSize = new Size(440, 0), Margin = new Padding(0, 0, 0, 12) };
        _bar = new ProgressLine { Height = 6, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        _progress = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Visible = false };
        _progress.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _progress.Controls.Add(_status, 0, 0);
        _progress.Controls.Add(_bar, 0, 1);

        AddBody(_info, _progress);

        _keep = new ThemedButton("Leave them as GIFs", ButtonKind.Ghost);
        _keep.Click += (_, _) => { Choice = ExistingGifsChoice.Keep; DialogResult = DialogResult.OK; };
        _convert = new ThemedButton("Convert now", ButtonKind.Primary, Theme.Glyphs.Sync);
        _convert.Click += async (_, _) => await ConvertAsync();

        FooterRight.Controls.Add(_keep);
        FooterRight.Controls.Add(_convert);
        AcceptButton = _convert;

        Complete();
        Shown += (_, _) => _convert.Focus();
    }

    /// <summary>The user's answer, or null if they closed the window without choosing.</summary>
    public ExistingGifsChoice? Choice { get; private set; }

    /// <summary>Packs that had at least one GIF converted.</summary>
    public List<Manifest> ChangedPacks { get; } = new();

    public int Converted { get; private set; }
    public int Failed { get; private set; }

    private static string Describe(IReadOnlyList<(Manifest Pack, int Gifs)> packs)
    {
        var total = packs.Sum(p => p.Gifs);
        var where = packs.Count == 1 ? $"\"{packs[0].Pack.Name}\" has" : $"{packs.Count} of your packs have";
        return $"{where} {(total == 1 ? "a GIF icon" : $"{total} GIF icons")}. Stream Deck shows GIFs as broken images in its icon library. " +
               "We recommend converting them to animated WEBP, which Stream Deck displays properly.";
    }

    private static Control PackLine(string name, string count)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(new Label { Text = name, AutoSize = true, Font = Theme.Body, ForeColor = Theme.Text, Margin = Padding.Empty, UseMnemonic = false }, 0, 0);
        row.Controls.Add(new Label { Text = count, AutoSize = true, Font = Theme.Small, ForeColor = Theme.TextMuted, Margin = new Padding(0, 1, 0, 0) }, 1, 0);
        return row;
    }

    /// <summary>Escape closes the window without choosing, like the X: the question comes back next time.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && !_busy)
        {
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy && e.CloseReason == CloseReason.UserClosing)
            e.Cancel = true;
        base.OnFormClosing(e);
    }

    private async Task ConvertAsync()
    {
        if (_busy)
            return;

        _busy = true;
        _keep.Visible = false;
        _convert.Visible = false;
        _info.Visible = false;
        _progress.Visible = true;

        var icons = _packs.SelectMany(p => p.Pack.Icons.Where(i => GifConverter.IsGif(i.Path)).Select(i => (p.Pack, Icon: i))).ToList();
        for (var i = 0; i < icons.Count; i++)
        {
            var (pack, icon) = icons[i];
            _status.Text = $"Converting \"{icon.Name}\" in {pack.Name} ({i + 1} of {icons.Count})";
            _bar.Value = (int)(100.0 * i / icons.Count) + 2;

            var webp = await GifConverter.ToWebpAsync(icon.Data);
            if (webp is null)
            {
                Failed++;
                continue;
            }

            icon.Data = webp;
            icon.Path = Path.ChangeExtension(icon.Path, ".webp");
            Converted++;
            if (!ChangedPacks.Contains(pack))
                ChangedPacks.Add(pack);
        }

        _bar.Value = 100;
        _busy = false;
        Choice = ExistingGifsChoice.Convert;
        DialogResult = DialogResult.OK;
    }
}
