using System.Reflection;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace StreamDeckIconManager.Mac;

/// <summary>
/// Icon images. Each icon keeps its original file bytes, which are written back unchanged, plus a
/// preview bitmap for showing it in the app. SkiaSharp decodes PNG, JPG, GIF and WEBP; Svg.Skia
/// draws SVGs.
/// </summary>
internal static class ImageHelper
{
    /// <summary>The formats Stream Deck accepts in icon packs.</summary>
    public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg" };

    /// <summary>Plain-language list for messages.</summary>
    public const string FormatList = "PNG, JPG, GIF, WEBP or SVG";

    /// <summary>Size SVGs are drawn at for previews: Stream Deck's recommended 144 px, doubled.</summary>
    private const int SvgPreviewSize = 288;

    /// <summary>Longest side of preview bitmaps: 144 px doubled for Retina screens.</summary>
    private const int MaxPreviewSize = 288;

    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Reads an image file: its untouched bytes and a preview.</summary>
    public static (byte[] Data, Bitmap Preview) Load(string path)
    {
        var data = File.ReadAllBytes(path);
        return (data, Preview(data, Path.GetExtension(path)));
    }

    /// <summary>
    /// Like <see cref="Load"/>, but if the image can't be decoded, returns a placeholder preview
    /// instead of failing. The original bytes are kept either way, so saving never loses a file.
    /// </summary>
    public static (byte[] Data, Bitmap Preview, bool Readable) LoadKeepingBytes(string path)
    {
        var data = File.ReadAllBytes(path);
        try
        {
            return (data, Preview(data, Path.GetExtension(path)), true);
        }
        catch
        {
            return (data, Placeholder(), false);
        }
    }

    /// <summary>Grey tile with a question mark, shown for images that can't be previewed.</summary>
    public static Bitmap Placeholder()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(144, 144, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(0x2B, 0x30, 0x3B));
            using var paint = new SKPaint { Color = new SKColor(0x98, 0x9F, 0xAE), IsAntialias = true, TextSize = 64, TextAlign = SKTextAlign.Center };
            canvas.DrawText("?", 72, 72 + 22, paint);
        }

        return ToAvalonia(bitmap);
    }

    /// <summary>
    /// Decodes image bytes into a bitmap for display, no larger than <see cref="MaxPreviewSize"/>.
    /// Throws if they can't be read. Only for showing in the app: files are saved from the original bytes.
    /// </summary>
    public static Bitmap Preview(byte[] data, string extension)
    {
        using var full = DecodeFull(data, extension);
        var longest = Math.Max(full.Width, full.Height);
        if (longest <= MaxPreviewSize)
            return ToAvalonia(full);

        var scale = (float)MaxPreviewSize / longest;
        var info = new SKImageInfo(Math.Max(1, (int)(full.Width * scale)), Math.Max(1, (int)(full.Height * scale)),
            SKColorType.Bgra8888, SKAlphaType.Premul);
        using var thumb = full.Resize(info, SKFilterQuality.High) ?? throw new InvalidDataException("This image couldn't be resized.");
        return ToAvalonia(thumb);
    }

    private static SKBitmap DecodeFull(byte[] data, string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        if (ext == "svg")
            return RenderSvg(data);

        // For animated GIF/WEBP this is the first frame, which is what the preview needs.
        return SKBitmap.Decode(data) ?? throw new InvalidDataException("This image couldn't be read.");
    }

    /// <summary>File extension to save under, without the dot, e.g. "png" or "jpeg".</summary>
    public static string ExtensionOf(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext.Length == 0 ? "png" : ext;
    }

    /// <summary>The emblem new packs start with (the same image the Windows app uses).</summary>
    public static byte[] DefaultEmblemPng() => ReadResource("DefaultEmblem.png");

    public static byte[] ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                           ?? throw new FileNotFoundException("Missing embedded resource " + name);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static SKBitmap RenderSvg(byte[] data)
    {
        using var svg = new Svg.Skia.SKSvg();
        SKPicture? picture;
        using (var input = new MemoryStream(data))
            picture = svg.Load(input);

        if (picture is null)
            throw new InvalidDataException("This SVG file couldn't be read.");

        var bounds = picture.CullRect;
        var width = bounds.Width > 0 ? bounds.Width : SvgPreviewSize;
        var height = bounds.Height > 0 ? bounds.Height : SvgPreviewSize;
        var scale = Math.Min(SvgPreviewSize / width, SvgPreviewSize / height);

        var bitmap = new SKBitmap(new SKImageInfo(SvgPreviewSize, SvgPreviewSize, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Translate((SvgPreviewSize - width * scale) / 2, (SvgPreviewSize - height * scale) / 2);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
        }

        return bitmap;
    }

    /// <summary>Copies a Skia bitmap into an Avalonia bitmap (BGRA, premultiplied).</summary>
    public static Bitmap ToAvalonia(SKBitmap source)
    {
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (source.ColorType == SKColorType.Bgra8888 && source.AlphaType == SKAlphaType.Premul)
            return Wrap(source.GetPixels(), info, source.RowBytes);

        using var converted = new SKBitmap(info);
        using (var pixels = source.PeekPixels())
        {
            if (pixels is null || !pixels.ReadPixels(info, converted.GetPixels(), converted.RowBytes, 0, 0))
            {
                using var canvas = new SKCanvas(converted);
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(source, 0, 0);
            }
        }

        return Wrap(converted.GetPixels(), info, converted.RowBytes);
    }

    private static Bitmap Wrap(IntPtr pixels, SKImageInfo info, int rowBytes) =>
        // This constructor copies the pixels, so the Skia bitmap can be freed afterwards.
        new(PixelFormat.Bgra8888, AlphaFormat.Premul, pixels, new PixelSize(info.Width, info.Height), new Vector(96, 96), rowBytes);

    /// <summary>Encodes a Skia bitmap as PNG bytes.</summary>
    public static byte[] ToPng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
