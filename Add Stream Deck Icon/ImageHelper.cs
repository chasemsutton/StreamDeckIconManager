using System.Drawing.Imaging;

namespace Add_Stream_Deck_Icon;

internal static class ImageHelper
{
    /// <summary>
    /// Loads an image while preserving its encoded format (png/jpg/gif).
    /// The underlying stream is kept alive for the lifetime of the Bitmap.
    /// </summary>
    public static Bitmap LoadBitmap(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }

    /// <summary>
    /// Creates a detached pixel copy (useful for emblems saved as PNG).
    /// </summary>
    public static Bitmap CloneBitmap(Image source)
    {
        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(clone);
        graphics.DrawImage(source, new Rectangle(0, 0, clone.Width, clone.Height));
        return clone;
    }

    public static string ExtensionFor(Image image, string? fallbackPath = null)
    {
        var format = new ImageFormatConverter().ConvertToString(image.RawFormat)?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(format) && format is not ("memorybmp" or "rawformat"))
            return format;

        if (!string.IsNullOrEmpty(fallbackPath))
        {
            var ext = Path.GetExtension(fallbackPath).TrimStart('.').ToLowerInvariant();
            if (ext is "jpg" or "jpeg" or "png" or "gif")
                return ext == "jpeg" ? "jpg" : ext;
        }

        return "png";
    }
}
