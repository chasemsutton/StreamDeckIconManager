using System.Text.Json.Serialization;

namespace Add_Stream_Deck_Icon;

internal sealed class PackIcon
{
    public int IconNumber { get; set; }
    public string Path { get; set; }
    public string Name { get; set; }
    public string[] Tags { get; set; }
    public Bitmap Picture { get; set; }

    public PackIcon(int index, string path, string name, string[] tags, Bitmap picture)
    {
        IconNumber = index;
        Path = path;
        Name = name;
        Tags = tags;
        Picture = picture;
    }

    public static PackIcon FromFile(string fileName)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ');
        var tags = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Append("custom").ToArray();
        return new PackIcon(0, fileName, name, tags, ImageHelper.LoadBitmap(fileName));
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
