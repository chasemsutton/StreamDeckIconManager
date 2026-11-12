using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace StreamDeckIconManager.Mac.UI;

/// <summary>
/// Asked once when GIF icons are found in existing packs: convert them to animated WebP (which
/// Stream Deck's icon library can preview) or leave them as they are. Converting happens here,
/// with progress, before the dialog closes.
/// </summary>
internal sealed class ConvertGifsDialog : ThemedDialog
{
    private readonly IReadOnlyList<(Manifest Pack, int Gifs)> _packs;
    private readonly StackPanel _info;
    private readonly StackPanel _progress;
    private readonly TextBlock _status;
    private readonly ProgressBar _bar;
    private readonly ThemedButton _keep;
    private readonly ThemedButton _convert;

    public ConvertGifsDialog(IReadOnlyList<(Manifest Pack, int Gifs)> packsWithGifs)
        : base("Convert GIF icons?", Describe(packsWithGifs), 440)
    {
        _packs = packsWithGifs;
        EscapeCloses = true;

        _info = new StackPanel();
        var shown = packsWithGifs.Take(5).ToList();
        foreach (var (pack, gifs) in shown)
            _info.Children.Add(PackLine(pack.Name, gifs == 1 ? "1 GIF" : $"{gifs} GIFs"));
        if (packsWithGifs.Count > shown.Count)
            _info.Children.Add(PackLine($"and {packsWithGifs.Count - shown.Count} more packs", ""));

        var note = Paragraph("Converting keeps every frame and its timing exactly as it is. Keys you've already set up aren't affected, " +
                             "and new GIFs you add are converted either way.", Palette.TextFaint, Palette.Small, 4);
        note.Margin = new Thickness(0, 14, 0, 4);
        _info.Children.Add(note);

        _status = Palette.Label("", Palette.Body, Palette.Text, strong: true, wrap: true);
        _status.Margin = new Thickness(0, 0, 0, 12);
        _bar = new ProgressBar { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 0, 0, 8) };
        _progress = new StackPanel { IsVisible = false, Children = { _status, _bar } };

        AddBody(_info, _progress);

        _keep = new ThemedButton("Leave them as GIFs", ButtonKind.Ghost);
        _keep.Click += (_, _) =>
        {
            Choice = ExistingGifsChoice.Keep;
            Finish(true);
        };
        _convert = new ThemedButton("Convert now", ButtonKind.Primary, Glyphs.Sync) { IsDefault = true };
        _convert.Click += async (_, _) => await ConvertAsync();

        FooterRight.Children.Add(_keep);
        FooterRight.Children.Add(_convert);
        Opened += (_, _) => _convert.Focus();
    }

    /// <summary>The user's answer, or null if they closed the window without choosing.</summary>
    public ExistingGifsChoice? Choice { get; private set; }

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

    private async Task ConvertAsync()
    {
        if (Busy)
            return;

        Busy = true;
        _keep.IsVisible = false;
        _convert.IsVisible = false;
        _info.IsVisible = false;
        _progress.IsVisible = true;

        var icons = _packs.SelectMany(p => p.Pack.Icons.Where(i => GifConverter.IsGif(i.Path)).Select(i => (p.Pack, Icon: i))).ToList();
        for (var i = 0; i < icons.Count; i++)
        {
            var (pack, icon) = icons[i];
            _status.Text = $"Converting \"{icon.Name}\" in {pack.Name} ({i + 1} of {icons.Count})";
            _bar.Value = 100.0 * i / icons.Count + 2;

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
        Choice = ExistingGifsChoice.Convert;
        Finish(true);
    }
}

/// <summary>A pack with images larger than Stream Deck's icon size.</summary>
internal sealed record LargeImages(Manifest Pack, IReadOnlyList<PackIcon> Icons, bool Emblem)
{
    public long Bytes => Icons.Sum(i => (long)i.Data.Length) + (Emblem ? Pack.EmblemData.Length : 0);
}

/// <summary>A resized image, applied to its pack once the dialog closes.</summary>
internal sealed record ShrunkImage(Manifest Pack, PackIcon? Icon, byte[] Data, string Extension, Bitmap Preview);

/// <summary>
/// Asked once when existing packs hold images larger than Stream Deck's 144 px icon size: scale
/// them down or leave them. Resizing happens here, with progress; the caller applies the results
/// after the dialog closes.
/// </summary>
internal sealed class ShrinkImagesDialog : ThemedDialog
{
    private readonly IReadOnlyList<LargeImages> _packs;
    private readonly StackPanel _info;
    private readonly StackPanel _progress;
    private readonly TextBlock _status;
    private readonly ProgressBar _bar;
    private readonly ThemedButton _keep;
    private readonly ThemedButton _shrink;

    public ShrinkImagesDialog(IReadOnlyList<LargeImages> packs)
        : base("Shrink large images?", Describe(packs), 460)
    {
        _packs = packs;
        EscapeCloses = true;

        _info = new StackPanel();
        var shown = packs.Take(5).ToList();
        foreach (var pack in shown)
            _info.Children.Add(PackLine(pack.Pack.Name, $"{Count(pack)}  ·  {FormatSize(pack.Bytes)}"));
        if (packs.Count > shown.Count)
            _info.Children.Add(PackLine($"and {packs.Count - shown.Count} more packs", ""));

        var note = Paragraph($"Images are scaled to fit {IconResizer.TargetSize} × {IconResizer.TargetSize} px, the size Stream Deck shows them at. " +
                             "Nothing is cropped, and animations keep every frame and its timing. Keys you've already set up aren't affected.",
            Palette.TextFaint, Palette.Small, 4);
        note.Margin = new Thickness(0, 14, 0, 4);
        _info.Children.Add(note);

        _status = Palette.Label("", Palette.Body, Palette.Text, strong: true, wrap: true);
        _status.Margin = new Thickness(0, 0, 0, 12);
        _bar = new ProgressBar { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 0, 0, 8) };
        _progress = new StackPanel { IsVisible = false, Children = { _status, _bar } };

        AddBody(_info, _progress);

        _keep = new ThemedButton("Leave them as they are", ButtonKind.Ghost);
        _keep.Click += (_, _) =>
        {
            Choice = ExistingLargeImagesChoice.Keep;
            Finish(true);
        };
        _shrink = new ThemedButton("Shrink now", ButtonKind.Primary, Glyphs.Picture) { IsDefault = true };
        _shrink.Click += async (_, _) => await ShrinkAsync();

        FooterRight.Children.Add(_keep);
        FooterRight.Children.Add(_shrink);
        Opened += (_, _) => _shrink.Focus();
    }

    public ExistingLargeImagesChoice? Choice { get; private set; }
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

    private async Task ShrinkAsync()
    {
        if (Busy)
            return;

        Busy = true;
        _keep.IsVisible = false;
        _shrink.IsVisible = false;
        _info.IsVisible = false;
        _progress.IsVisible = true;

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
            _bar.Value = 100.0 * i / work.Count + 2;

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
        Choice = ExistingLargeImagesChoice.Shrink;
        Finish(true);
    }
}
