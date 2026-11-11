using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Add_Stream_Deck_Icon;

/// <summary>Decoded frames of an animated icon, scaled to preview size, with each frame's duration.</summary>
internal sealed class AnimatedPreview
{
    /// <summary>Frames are scaled so the longer side is this many pixels.</summary>
    private const int PreviewSize = 144;

    /// <summary>Longer animations are cut short in the preview (the file itself is untouched).</summary>
    private const int MaxFrames = 600;

    private static readonly ConditionalWeakTable<byte[], Task<AnimatedPreview?>> Cache = new();

    private AnimatedPreview(List<Bitmap> frames, List<int> durationsMs)
    {
        Frames = frames;
        DurationsMs = durationsMs;
    }

    public IReadOnlyList<Bitmap> Frames { get; }
    public IReadOnlyList<int> DurationsMs { get; }

    public static bool CanAnimate(string? extension) =>
        extension is not null && (extension.TrimStart('.').Equals("gif", StringComparison.OrdinalIgnoreCase)
                                  || extension.TrimStart('.').Equals("webp", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Decodes the animation in the background, once per image (results are cached). Completes
    /// with null for still images or anything that can't be decoded.
    /// </summary>
    public static Task<AnimatedPreview?> LoadAsync(byte[] data, string? extension)
    {
        if (!CanAnimate(extension))
            return Task.FromResult<AnimatedPreview?>(null);

        lock (Cache)
        {
            if (!Cache.TryGetValue(data, out var task))
            {
                task = Task.Run(() => Decode(data));
                Cache.Add(data, task);
            }

            return task;
        }
    }

    private static AnimatedPreview? Decode(byte[] data)
    {
        var decoded = DecodeFrames(data, PreviewSize, MaxFrames);
        return decoded is null ? null : new AnimatedPreview(decoded.Value.Frames, decoded.Value.DurationsMs);
    }

    /// <summary>
    /// Decodes every frame of an animated GIF/WEBP, scaled so the longer side is at most
    /// <paramref name="maxSide"/> pixels, with each frame's duration in ms. Null for still images
    /// or anything that can't be decoded.
    /// </summary>
    internal static (List<Bitmap> Frames, List<int> DurationsMs)? DecodeFrames(byte[] data, int maxSide, int maxFrames)
    {
        try
        {
            using var skData = SKData.CreateCopy(data);
            using var codec = SKCodec.Create(skData);
            if (codec is null || codec.FrameCount < 2)
                return null;

            var frameInfos = codec.FrameInfo;
            var count = Math.Min(frameInfos.Length, maxFrames);
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var scale = Math.Min(1f, (float)maxSide / Math.Max(info.Width, info.Height));
            var previewInfo = new SKImageInfo(Math.Max(1, (int)(info.Width * scale)), Math.Max(1, (int)(info.Height * scale)),
                SKColorType.Bgra8888, SKAlphaType.Premul);

            // Frames can be drawn on top of an earlier one. Keep a full-size copy only while a later
            // frame still needs it, so memory stays small even for large animations.
            var stillNeeded = new int[count];
            for (var i = 0; i < count; i++)
                if (frameInfos[i].RequiredFrame >= 0 && frameInfos[i].RequiredFrame < count)
                    stillNeeded[frameInfos[i].RequiredFrame]++;

            var fullFrames = new Dictionary<int, SKBitmap>();
            var frames = new List<Bitmap>(count);
            var durations = new List<int>(count);
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var required = frameInfos[i].RequiredFrame;
                    var full = new SKBitmap(info);
                    if (required >= 0 && fullFrames.TryGetValue(required, out var prior))
                    {
                        // Copy the earlier frame's pixels into a bitmap we own. SkiaSharp's Copy() and
                        // CopyTo() leave each old buffer for the garbage collector instead of freeing it
                        // on Dispose; on a large 120-frame animation that piled up to about 1 GB.
                        using var pixels = prior.PeekPixels();
                        if (!pixels.ReadPixels(info, full.GetPixels(), full.RowBytes, 0, 0))
                        {
                            full.Erase(SKColors.Transparent);
                            required = -1;
                        }
                    }
                    else
                    {
                        full.Erase(SKColors.Transparent);
                        required = -1;
                    }

                    var result = codec.GetPixels(info, full.GetPixels(), new SKCodecOptions(i, required));
                    if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                    {
                        full.Dispose();
                        break;
                    }

                    using (var small = full.Resize(previewInfo, SKFilterQuality.Medium))
                        frames.Add(ToBitmap(small ?? full));

                    // A duration of 0 means "as fast as possible"; browsers treat it as about 100 ms.
                    durations.Add(frameInfos[i].Duration <= 10 ? 100 : frameInfos[i].Duration);

                    if (required >= 0 && --stillNeeded[required] == 0 && fullFrames.Remove(required, out var done))
                        done.Dispose();

                    if (stillNeeded[i] > 0)
                        fullFrames[i] = full;
                    else
                        full.Dispose();
                }
            }
            finally
            {
                foreach (var leftover in fullFrames.Values)
                    leftover.Dispose();
            }

            if (frames.Count > 1)
                return (frames, durations);

            foreach (var frame in frames)
                frame.Dispose();
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Copies BGRA premultiplied pixels straight into a GDI+ bitmap of the same layout.</summary>
    private static Bitmap ToBitmap(SKBitmap source)
    {
        var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var rowBytes = source.Width * 4;
            var buffer = new byte[rowBytes];
            var src = source.GetPixels();
            for (var y = 0; y < source.Height; y++)
            {
                Marshal.Copy(src + y * source.RowBytes, buffer, 0, rowBytes);
                Marshal.Copy(buffer, 0, data.Scan0 + y * data.Stride, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}
