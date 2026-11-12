using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace StreamDeckIconManager.Mac;

/// <summary>Decoded frames of an animated icon, scaled to preview size, with each frame's duration.</summary>
internal sealed class AnimatedPreview
{
    /// <summary>Frames are scaled so the longer side is this many pixels (144 px doubled for Retina).</summary>
    private const int PreviewSize = 288;

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
        var decoded = DecodeFrames(data, PreviewSize, MaxFrames, ImageHelper.ToAvalonia);
        return decoded is null ? null : new AnimatedPreview(decoded.Value.Frames, decoded.Value.DurationsMs);
    }

    /// <summary>
    /// Decodes every frame of an animated GIF/WEBP, scaled so the longer side is at most
    /// <paramref name="maxSide"/> pixels, with each frame's duration in ms. Each scaled frame is
    /// handed to <paramref name="convert"/> (the bitmap is freed afterwards). Null for still
    /// images or anything that can't be decoded.
    /// </summary>
    internal static (List<T> Frames, List<int> DurationsMs)? DecodeFrames<T>(byte[] data, int maxSide, int maxFrames, Func<SKBitmap, T> convert)
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
            var frames = new List<T>(count);
            var durations = new List<int>(count);
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var required = frameInfos[i].RequiredFrame;
                    var full = new SKBitmap(info);
                    if (required >= 0 && fullFrames.TryGetValue(required, out var prior))
                    {
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

                    if (previewInfo.Width == info.Width && previewInfo.Height == info.Height)
                    {
                        frames.Add(convert(full));
                    }
                    else
                    {
                        using var small = full.Resize(previewInfo, SKFilterQuality.Medium);
                        frames.Add(convert(small ?? full));
                    }

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
                (frame as IDisposable)?.Dispose();
            return null;
        }
        catch
        {
            return null;
        }
    }
}
