using System.Globalization;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace StreamDeckIconManager.Mac;

/// <summary>
/// Scales oversized images down to Stream Deck's icon size. Elgato's icon pack guide asks for
/// 144 × 144 px images; larger ones only cost memory and speed, in this app and in Stream Deck.
/// Images are fitted inside 144 × 144 keeping their shape (nothing is cropped). Smaller images
/// and SVGs (which scale freely) are left as they are.
/// </summary>
internal static class IconResizer
{
    public const int TargetSize = 144;

    /// <summary>Longer animations are left at full size rather than resized.</summary>
    private const int MaxAnimationFrames = 1200;

    /// <summary>True if the image is bigger than Stream Deck needs (and isn't an SVG).</summary>
    public static bool IsOversized(byte[] data, string extension)
    {
        if (IsSvg(extension))
            return false;

        try
        {
            using var skData = SKData.CreateCopy(data);
            using var codec = SKCodec.Create(skData);
            return codec is not null && Math.Max(codec.Info.Width, codec.Info.Height) > TargetSize;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the image scaled to fit 144 × 144 px, or null if it doesn't need it or can't be
    /// resized (in which case the caller keeps the original). Still images keep their format
    /// (a still GIF becomes PNG); animations become animated WEBP with every frame's timing kept.
    /// </summary>
    public static async Task<(byte[] Data, string Extension)?> FitAsync(byte[] data, string extension)
    {
        if (!IsOversized(data, extension))
            return null;

        try
        {
            if (AnimatedPreview.CanAnimate(extension))
            {
                var animated = await Task.Run(() => AnimatedPreview.DecodeFrames(data, TargetSize, MaxAnimationFrames + 1, ImageHelper.ToPng)).ConfigureAwait(false);
                if (animated is not null)
                    return await EncodeAnimationAsync(animated.Value.Frames, animated.Value.DurationsMs).ConfigureAwait(false);
            }

            return await Task.Run(() => ResizeStill(data, extension)).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// <see cref="FitAsync"/> plus a fresh preview of the resized file, or null if the image is
    /// left as it is.
    /// </summary>
    public static async Task<(byte[] Data, string Extension, Bitmap Preview)?> FitWithPreviewAsync(byte[] data, string extension)
    {
        var fit = await FitAsync(data, extension).ConfigureAwait(false);
        if (fit is null)
            return null;

        try
        {
            var (newData, ext) = fit.Value;
            var preview = await Task.Run(() => ImageHelper.Preview(newData, ext)).ConfigureAwait(false);
            return (newData, ext, preview);
        }
        catch
        {
            return null; // The resized file can't be read back; keep the original.
        }
    }

    private static bool IsSvg(string extension) =>
        extension.TrimStart('.').Equals("svg", StringComparison.OrdinalIgnoreCase);

    private static (byte[] Data, string Extension)? ResizeStill(byte[] data, string extension)
    {
        using var source = SKBitmap.Decode(data);
        if (source is null)
            return null;

        var scale = (float)TargetSize / Math.Max(source.Width, source.Height);
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)),
            SKColorType.Bgra8888, SKAlphaType.Premul);
        using var resized = source.Resize(info, SKFilterQuality.High);
        if (resized is null)
            return null;

        var ext = extension.TrimStart('.').ToLowerInvariant();
        var (format, quality, newExt) = ext switch
        {
            "jpg" or "jpeg" => (SKEncodedImageFormat.Jpeg, 92, ext),
            "webp" => (SKEncodedImageFormat.Webp, 92, "webp"),
            _ => (SKEncodedImageFormat.Png, 100, "png") // PNG, and still GIFs
        };

        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(format, quality);
        return encoded is null ? null : (encoded.ToArray(), newExt);
    }

    /// <summary>Builds a lossless animated WEBP from PNG frames with Google's img2webp.</summary>
    private static async Task<(byte[] Data, string Extension)?> EncodeAnimationAsync(List<byte[]> frames, List<int> durationsMs)
    {
        if (!WebpTools.IsAvailable("img2webp") || frames.Count > MaxAnimationFrames)
            return null;

        var work = Path.Combine(Path.GetTempPath(), "sdim-resize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            // Run inside the work folder and pass short relative names, so long animations fit
            // on the command line.
            var args = new List<string> { "-loop", "0", "-lossless" };
            for (var i = 0; i < frames.Count; i++)
            {
                var name = $"f{i:D5}.png";
                await File.WriteAllBytesAsync(Path.Combine(work, name), frames[i]).ConfigureAwait(false);
                args.Add("-d");
                args.Add(durationsMs[i].ToString(CultureInfo.InvariantCulture));
                args.Add(name);
            }

            args.Add("-o");
            args.Add("out.webp");

            var output = Path.Combine(work, "out.webp");
            if (!await WebpTools.RunAsync("img2webp", args, work).ConfigureAwait(false) || !File.Exists(output))
                return null;

            var webp = await File.ReadAllBytesAsync(output).ConfigureAwait(false);
            return webp.Length > 0 ? (webp, "webp") : null;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* temp cleanup is best-effort */ }
        }
    }
}
