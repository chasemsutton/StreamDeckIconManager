namespace Add_Stream_Deck_Icon.UI;

/// <summary>A pack with images larger than Stream Deck's icon size.</summary>
internal sealed record LargeImages(Manifest Pack, IReadOnlyList<PackIcon> Icons, bool Emblem)
{
    public long Bytes => Icons.Sum(i => (long)i.Data.Length) + (Emblem ? Pack.EmblemData.Length : 0);
}

/// <summary>A resized image, applied to its pack once the dialog closes.</summary>
internal sealed record ShrunkImage(Manifest Pack, PackIcon? Icon, byte[] Data, string Extension, Bitmap Preview);

/// <summary>
/// Asked once when existing packs hold images larger than Stream Deck's 144 px icon size: scale
/// them down or leave them. Resizing happens here, with progress; the results are applied by the
/// caller after the dialog closes, so nothing on screen changes while it's open.
/// </summary>
internal sealed class ShrinkImagesDialog : ThemedDialog
{
    private readonly IReadOnlyList<LargeImages> _packs;
    private readonly TableLayoutPanel _info;
    private readonly TableLayoutPanel _progress;
    private readonly Label _status;
    private readonly ProgressLine _bar;
    private readonly ThemedButton _keep;
    private readonly ThemedButton _shrink;
    private bool _busy;

    public ShrinkImagesDialog(IReadOnlyList<LargeImages> packs)
        : base("Shrink large images?", Describe(packs), 460)
    {
        _packs = packs;

        _info = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        _info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var shown = packs.Take(5).ToList();
        foreach (var pack in shown)
            _info.Controls.Add(PackLine(pack.Pack.Name, $"{Count(pack)}  ·  {FormatSize(pack.Bytes)}"), 0, _info.Controls.Count);
        if (packs.Count > shown.Count)
            _info.Controls.Add(PackLine($"and {packs.Count - shown.Count} more packs", ""), 0, _info.Controls.Count);

        _info.Controls.Add(new Label
        {
            Text = $"Images are scaled to fit {IconResizer.TargetSize} × {IconResizer.TargetSize} px, the size Stream Deck shows them at. " +
                   "Nothing is cropped, and animations keep every frame and its timing. Keys you've already set up aren't affected.",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Font = Theme.Small,
            ForeColor = Theme.TextFaint,
            Margin = new Padding(0, 14, 0, 4)
        }, 0, _info.Controls.Count);

        _status = new Label { AutoSize = true, Font = Theme.BodyStrong, ForeColor = Theme.Text, MaximumSize = new Size(460, 0), Margin = new Padding(0, 0, 0, 12), UseMnemonic = false };
        _bar = new ProgressLine { Height = 6, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        _progress = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Visible = false };
        _progress.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _progress.Controls.Add(_status, 0, 0);
        _progress.Controls.Add(_bar, 0, 1);

        AddBody(_info, _progress);

        _keep = new ThemedButton("Leave them as they are", ButtonKind.Ghost);
        _keep.Click += (_, _) => { Choice = ExistingLargeImagesChoice.Keep; DialogResult = DialogResult.OK; };
        _shrink = new ThemedButton("Shrink now", ButtonKind.Primary, Theme.Glyphs.Picture);
        _shrink.Click += async (_, _) => await ShrinkAsync();

        FooterRight.Controls.Add(_keep);
        FooterRight.Controls.Add(_shrink);
        AcceptButton = _shrink;

        Complete();
        Shown += (_, _) => _shrink.Focus();
    }

    /// <summary>The user's answer, or null if they closed the window without choosing.</summary>
    public ExistingLargeImagesChoice? Choice { get; private set; }

    /// <summary>Resized images (icons, and emblems with a null Icon), for the caller to apply.</summary>
    public List<ShrunkImage> Results { get; } = new();

    public int Failed { get; private set; }

    private static string Count(LargeImages pack)
    {
        var parts = new List<string>();
        if (pack.Icons.Count > 0)
            parts.Add(pack.Icons.Count == 1 ? "1 icon" : $"{pack.Icons.Count} icons");
        if (pack.Emblem)
            parts.Add("emblem");
        return string.Join(" + ", parts);
    }

    private static string Describe(IReadOnlyList<LargeImages> packs)
    {
        var images = packs.Sum(p => p.Icons.Count + (p.Emblem ? 1 : 0));
        var where = packs.Count == 1 ? $"\"{packs[0].Pack.Name}\" has" : $"{packs.Count} of your packs have";
        return $"{where} {(images == 1 ? "an image" : $"{images} images")} larger than Stream Deck needs " +
               $"({FormatSize(packs.Sum(p => p.Bytes))}). Large images make this app and Stream Deck slow and use a lot of memory. " +
               "We recommend shrinking them.";
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
        >= 1024L * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _ => $"{Math.Max(1, bytes / 1024)} KB"
    };

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

    private async Task ShrinkAsync()
    {
        if (_busy)
            return;

        _busy = true;
        _keep.Visible = false;
        _shrink.Visible = false;
        _info.Visible = false;
        _progress.Visible = true;

        var work = _packs
            .SelectMany(p => p.Icons.Select(i => (p.Pack, Icon: (PackIcon?)i))
                .Concat(p.Emblem ? new[] { (p.Pack, Icon: (PackIcon?)null) } : Array.Empty<(Manifest, PackIcon?)>()))
            .ToList();

        for (var i = 0; i < work.Count; i++)
        {
            var (pack, icon) = work[i];
            _status.Text = icon is null
                ? $"Shrinking the emblem of {pack.Name} ({i + 1} of {work.Count})"
                : $"Shrinking \"{icon.Name}\" in {pack.Name} ({i + 1} of {work.Count})";
            _bar.Value = (int)(100.0 * i / work.Count) + 2;

            var fit = icon is null
                ? await IconResizer.FitWithPreviewAsync(pack.EmblemData, pack.EmblemExtension)
                : await IconResizer.FitWithPreviewAsync(icon.Data, Path.GetExtension(icon.Path));
            if (fit is not { } done)
            {
                Failed++;
                continue;
            }

            Results.Add(new ShrunkImage(pack, icon, done.Data, done.Extension, done.Preview));
        }

        _bar.Value = 100;
        _busy = false;
        Choice = ExistingLargeImagesChoice.Shrink;
        DialogResult = DialogResult.OK;
    }
}
