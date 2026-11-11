using System.Text.Json.Serialization;

namespace Add_Stream_Deck_Icon;

internal sealed class PackIcon
{
    public int IconNumber { get; set; }

    /// <summary>File name inside the pack's icons folder (or the source path for a queued image).</summary>
    public string Path { get; set; }

    public string Name { get; set; }
    public string[] Tags { get; set; }

    /// <summary>The image file exactly as it was read; written back unchanged.</summary>
    public byte[] Data { get; set; }

    /// <summary>Decoded preview for showing in the app. Never written to disk.</summary>
    public Bitmap Picture { get; set; }

    public PackIcon(int index, string path, string name, string[] tags, byte[] data, Bitmap picture)
    {
        IconNumber = index;
        Path = path;
        Name = name;
        Tags = tags;
        Data = data;
        Picture = picture;
    }

    public static PackIcon FromFile(string fileName)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ');
        var tags = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Append("custom").ToArray();
        var (data, preview) = ImageHelper.Load(fileName);
        return new PackIcon(0, fileName, name, tags, data, preview);
    }

    public IconDto ToDto() => new()
    {
        Path = Path,
        Name = Name,
        Tags = Tags
    };

    public static int ParseNumberFromPath(string path)
    {
        const string prefix = "Custom Icon-";
        var start = path.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return 0;

        var numberPart = path[(start + prefix.Length)..];
        var end = numberPart.IndexOf('.');
        if (end < 0)
            end = numberPart.Length;

        return int.TryParse(numberPart[..end], out var number) ? number : 0;
    }
}

internal sealed class IconDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("tags")]
    public string[] Tags { get; set; } = Array.Empty<string>();
}
