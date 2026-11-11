using System.Diagnostics;
using System.Drawing.Imaging;
using SkiaSharp;

namespace Add_Stream_Deck_Icon;

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

    private static string Img2WebpPath => Path.Combine(AppContext.BaseDirectory, "tools", "gif2webp", "img2webp.exe");

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
                var animated = await Task.Run(() => AnimatedPreview.DecodeFrames(data, TargetSize, int.MaxValue)).ConfigureAwait(false);
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

    /// <summary>Builds a lossless animated WEBP from frames with Google's img2webp.</summary>
    private static async Task<(byte[] Data, string Extension)?> EncodeAnimationAsync(List<Bitmap> frames, List<int> durationsMs)
    {
        if (!File.Exists(Img2WebpPath))
        {
            foreach (var frame in frames)
                frame.Dispose();
            return null;
        }

        // Each frame adds about 20 characters to the command line (Windows allows about 32,000).
        if (frames.Count > MaxAnimationFrames)
        {
            foreach (var frame in frames)
                frame.Dispose();
            return null;
        }

        var work = Path.Combine(Path.GetTempPath(), "sdim-resize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            // Run inside the work folder and pass short relative names, so long animations fit
            // on the command line.
            var start = new ProcessStartInfo
            {
                FileName = Img2WebpPath,
                WorkingDirectory = work,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-loop");
            start.ArgumentList.Add("0");
            start.ArgumentList.Add("-lossless");

            for (var i = 0; i < frames.Count; i++)
            {
                var name = $"f{i:D5}.png";
                frames[i].Save(Path.Combine(work, name), ImageFormat.Png);
                start.ArgumentList.Add("-d");
                start.ArgumentList.Add(durationsMs[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
                start.ArgumentList.Add(name);
            }

            var output = Path.Combine(work, "out.webp");
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add("out.webp");

            using var process = Process.Start(start);
            if (process is null)
                return null;

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch { /* already gone */ }
                return null;
            }

            if (process.ExitCode != 0 || !File.Exists(output))
                return null;

            var webp = await File.ReadAllBytesAsync(output).ConfigureAwait(false);
            return webp.Length > 0 ? (webp, "webp") : null;
        }
        finally
        {
            foreach (var frame in frames)
                frame.Dispose();
            try { Directory.Delete(work, true); } catch { /* temp cleanup is best-effort */ }
        }
    }
}
