namespace StreamDeckIconManager.Mac;

/// <summary>
/// Turns GIF icons into lossless animated WebP with Google's gif2webp (tools/gif2webp).
/// Stream Deck's icon library previews WebP but shows GIFs as broken images in a pack's grid.
/// Lossless means every frame stays pixel-identical and frame timing is kept.
/// </summary>
internal static class GifConverter
{
    public static bool IsAvailable => WebpTools.IsAvailable("gif2webp");

    public static bool IsGif(string path) =>
        Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Converts GIF bytes to animated WebP. Returns null if the tool is missing or the conversion
    /// fails, in which case the caller keeps the GIF.
    /// </summary>
    public static async Task<byte[]?> ToWebpAsync(byte[] gif)
    {
        if (!IsAvailable)
            return null;

        var work = Path.Combine(Path.GetTempPath(), "sdim-gif2webp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var input = Path.Combine(work, "in.gif");
            var output = Path.Combine(work, "out.webp");
            await File.WriteAllBytesAsync(input, gif).ConfigureAwait(false);

            if (!await WebpTools.RunAsync("gif2webp", new[] { "-quiet", input, "-o", output }, work).ConfigureAwait(false)
                || !File.Exists(output))
                return null;

            var webp = await File.ReadAllBytesAsync(output).ConfigureAwait(false);
            return webp.Length > 0 ? webp : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* temp cleanup is best-effort */ }
        }
    }

    /// <summary>
    /// Converts every GIF in <paramref name="icons"/> in place: new bytes, ".webp" file name.
    /// The preview stays as it is. Returns how many were converted.
    /// </summary>
    public static async Task<int> ConvertAllAsync(IEnumerable<PackIcon> icons)
    {
        var converted = 0;
        foreach (var icon in icons.Where(i => IsGif(i.Path)).ToList())
        {
            var webp = await ToWebpAsync(icon.Data);
            if (webp is null)
                continue;

            icon.Data = webp;
            icon.Path = Path.ChangeExtension(icon.Path, ".webp");
            converted++;
        }

        return converted;
    }
}
