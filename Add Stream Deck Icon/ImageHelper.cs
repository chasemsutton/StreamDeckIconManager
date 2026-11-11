using System.Drawing.Imaging;
using SkiaSharp;

namespace Add_Stream_Deck_Icon;

/// <summary>
/// Icon images. Each icon keeps its original file bytes, which are written back unchanged, plus
/// a preview bitmap for showing it in the app. Windows' built-in decoder handles PNG, JPG and GIF;
/// SkiaSharp handles WEBP and SVG, which Windows can't read.
/// </summary>
internal static class ImageHelper
{
    /// <summary>The formats Stream Deck accepts in icon packs.</summary>
    public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg" };

    public const string FileFilter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg)|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg";

    /// <summary>Plain-language list for messages.</summary>
    public const string FormatList = "PNG, JPG, GIF, WEBP or SVG";

    /// <summary>Size SVGs are drawn at for previews: Stream Deck's recommended 144 px, doubled.</summary>
    private const int SvgPreviewSize = 288;

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
        var bitmap = new Bitmap(144, 144, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(0x2B, 0x30, 0x3B));
        using var font = new Font("Segoe UI Semibold", 48f);
        TextRenderer.DrawText(g, "?", font, new Rectangle(0, 0, 144, 144), Color.FromArgb(0x98, 0x9F, 0xAE),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return bitmap;
    }

    /// <summary>Decodes image bytes into a bitmap for display. Throws if they can't be read.</summary>
    public static Bitmap Preview(byte[] data, string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        if (ext == "svg")
            return RenderSvg(data);

        if (ext != "webp")
        {
            try
            {
                // The stream must stay open for the bitmap's lifetime, so it isn't disposed here.
                return new Bitmap(new MemoryStream(data));
            }
            catch (ArgumentException)
            {
                // Not something Windows can decode; try SkiaSharp below.
            }
        }

        return DecodeWithSkia(data);
    }

    /// <summary>Creates a detached 32-bit copy of an image.</summary>
    public static Bitmap CloneBitmap(Image source)
    {
        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(clone);
        graphics.DrawImage(source, new Rectangle(0, 0, clone.Width, clone.Height));
        return clone;
    }

    public static byte[] ToPng(Image image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>File extension to save under, without the dot, e.g. "png" or "jpeg".</summary>
    public static string ExtensionOf(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext.Length == 0 ? "png" : ext;
    }

    private static Bitmap RenderSvg(byte[] data)
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

        using var bitmap = new SKBitmap(SvgPreviewSize, SvgPreviewSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Translate((SvgPreviewSize - width * scale) / 2, (SvgPreviewSize - height * scale) / 2);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
        }

        return ToGdi(bitmap);
    }

    private static Bitmap DecodeWithSkia(byte[] data)
    {
        // For animated WEBP this is the first frame, which is what the preview needs.
        using var bitmap = SKBitmap.Decode(data) ?? throw new InvalidDataException("This image couldn't be read.");
        return ToGdi(bitmap);
    }

    private static Bitmap ToGdi(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(png.ToArray());
        using var decoded = new Bitmap(stream);
        return CloneBitmap(decoded);
    }
}
