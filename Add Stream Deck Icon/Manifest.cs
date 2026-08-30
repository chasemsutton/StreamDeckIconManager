using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;
using Add_Stream_Deck_Icon.Properties;

namespace Add_Stream_Deck_Icon;

internal sealed class Manifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string Name { get; set; } = "";
    public string Author { get; set; } = PackPaths.DefaultAuthor;
    public string Description { get; set; } = PackPaths.DefaultDescription;
    public string IconPackPath { get; set; } = "";
    public int HighestIconNumber { get; set; }
    public Image Emblem { get; set; } = null!;
    public List<PackIcon> Icons { get; set; } = new();

    /// <summary>Load an existing pack from disk (backs up custom packs and loads icons).</summary>
    public Manifest(string iconPackPath)
    {
        IconPackPath = iconPackPath;
        Emblem = ImageHelper.CloneBitmap(Resources.imgIcon1);
        LoadPackMetadata();

        if (!PackPaths.IsCustomPackDirectory(IconPackPath))
            return;

        try
        {
            CreateBackup();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Something went wrong backing up data in: {IconPackPath}\n\nrecommend closing program now\n\n{ex}",
                "Error");
        }

        try
        {
            LoadIconsFromBackup();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Something went wrong fetching icons from: {IconPackPath}\n\n{ex}",
                "Error");
        }
    }

    /// <summary>Create a new empty pack under the Stream Deck IconPacks folder.</summary>
    public Manifest(string name, string description, string author, string streamDeckPacksPath)
    {
        Name = name;
        Description = description;
        Author = author;
        Emblem = ImageHelper.CloneBitmap(Resources.imgIcon1);
        NewPackPath(streamDeckPacksPath);
    }

    public void NewPackPath(string streamDeckPacksPath)
    {
        IconPackPath = Path.Combine(streamDeckPacksPath, PackPaths.BuildPackFolderName(Name));
        Directory.CreateDirectory(IconPackPath);
        Directory.CreateDirectory(Path.Combine(IconPackPath, "icons"));
        Directory.CreateDirectory(Path.Combine(IconPackPath, "backup", "icons-old"));
    }

    public void ChangePackName(string newPackName) => Name = newPackName;

    public void AddIcon(string name, string[] tags, Bitmap picture)
    {
        HighestIconNumber++;
        var path = $"Custom Icon-{HighestIconNumber}.{ImageHelper.ExtensionFor(picture)}";
        Icons.Add(new PackIcon(HighestIconNumber, path, name, tags, picture));

    }

    public void RemoveIcon(int index)
    {
        Icons[index].Picture.Dispose();
        Icons.RemoveAt(index);
    }

    public void SaveManifest()
    {
        var iconsDir = Path.Combine(IconPackPath, "icons");
        Directory.CreateDirectory(iconsDir);

        foreach (var file in new DirectoryInfo(iconsDir).EnumerateFiles())
            file.Delete();

        File.Delete(Path.Combine(IconPackPath, "icons.json"));
        File.Delete(Path.Combine(IconPackPath, "manifest.json"));
        File.Delete(Path.Combine(IconPackPath, "icon.png"));

        if (Icons.Count > 0)
            SaveImagesToIconsFolder();

        Emblem.Save(Path.Combine(IconPackPath, "icon.png"), ImageFormat.Png);
        File.WriteAllText(Path.Combine(IconPackPath, "icons.json"), CompileIconsJson());
        File.WriteAllText(Path.Combine(IconPackPath, "manifest.json"), CompileManifestJson());
    }

    public void DisposeAllImages()
    {
        foreach (var icon in Icons)
            icon.Picture.Dispose();
        Emblem.Dispose();
    }

    private void LoadPackMetadata()
    {
        try
        {
            var json = File.ReadAllText(Path.Combine(IconPackPath, "manifest.json"));
            var dto = JsonSerializer.Deserialize<ManifestDto>(json, JsonOptions);
            if (dto is null)
                return;

            Author = dto.Author ?? PackPaths.DefaultAuthor;
            Name = dto.Name ?? "";
            Description = dto.Description ?? PackPaths.DefaultDescription;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Something went wrong fetching icon pack data from manifest.json in: {IconPackPath}\n{ex}",
                "Error");
        }
    }

    private void CreateBackup()
    {
        var backupRoot = Path.Combine(IconPackPath, "backup");
        var backupIcons = Path.Combine(backupRoot, "icons-old");
        Directory.CreateDirectory(backupIcons);

        CopyFileOverwrite(Path.Combine(IconPackPath, "icons.json"), Path.Combine(backupRoot, "icons-old.json"));
        CopyFileOverwrite(Path.Combine(IconPackPath, "manifest.json"), Path.Combine(backupRoot, "manifest-old.json"));
        CopyFileOverwrite(Path.Combine(IconPackPath, "icon.png"), Path.Combine(backupRoot, "icon-old.png"));

        if (Directory.Exists(backupIcons))
        {
            foreach (var file in Directory.GetFiles(backupIcons))
                File.Delete(file);
            Directory.Delete(backupIcons);
        }

        CopyDirectoryFiles(Path.Combine(IconPackPath, "icons"), backupIcons);
    }

    private void LoadIconsFromBackup()
    {
        Icons.Clear();
        HighestIconNumber = 0;

        var iconsJsonPath = Path.Combine(IconPackPath, "icons.json");
        if (!File.Exists(iconsJsonPath))
            return;

        var json = File.ReadAllText(iconsJsonPath);
        var iconDtos = JsonSerializer.Deserialize<List<IconDto>>(json, JsonOptions) ?? new List<IconDto>();
        var backupIconsDir = Path.Combine(IconPackPath, "backup", "icons-old");

        foreach (var dto in iconDtos)
        {
            if (string.IsNullOrWhiteSpace(dto.Path) || string.IsNullOrWhiteSpace(dto.Name))
                continue;

            var number = PackIcon.ParseNumberFromPath(dto.Path);
            var imagePath = Path.Combine(backupIconsDir, dto.Path);
            if (!File.Exists(imagePath))
                imagePath = Path.Combine(IconPackPath, "icons", dto.Path);

            if (!File.Exists(imagePath))
                continue;

            Icons.Add(new PackIcon(number, dto.Path, dto.Name, dto.Tags ?? Array.Empty<string>(), ImageHelper.LoadBitmap(imagePath)));
            if (number > HighestIconNumber)
                HighestIconNumber = number;
        }

        var emblemBackup = Path.Combine(IconPackPath, "backup", "icon-old.png");
        var emblemPath = File.Exists(emblemBackup)
            ? emblemBackup
            : Path.Combine(IconPackPath, "icon.png");

        if (File.Exists(emblemPath))
        {
            using var loaded = Image.FromFile(emblemPath);
            Emblem = ImageHelper.CloneBitmap(loaded);
        }
        else
        {
            Emblem = ImageHelper.CloneBitmap(Resources.imgIcon1);
        }
    }

    private void SaveImagesToIconsFolder()
    {
        var iconsDir = Path.Combine(IconPackPath, "icons");
        foreach (var icon in Icons)
            icon.Picture.Save(Path.Combine(iconsDir, icon.Path), icon.Picture.RawFormat);
    }

    private string CompileIconsJson()
    {
        var dtos = Icons.Select(i => i.ToDto()).ToList();
        return JsonSerializer.Serialize(dtos, JsonOptions);
    }

    private string CompileManifestJson()
    {
        var dto = new ManifestDto
        {
            Author = Author,
            Description = Description,
            Name = Name,
            URL = "",
            Version = "1.0",
            Icon = "icon.png",
            Tags = PackPaths.DefaultManifestTags
        };
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    private static void CopyFileOverwrite(string source, string destination)
    {
        if (!File.Exists(source))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    private static void CopyDirectoryFiles(string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
            return;
        }

        Directory.CreateDirectory(targetDirectory);
        foreach (var file in new DirectoryInfo(sourceDirectory).GetFiles())
            file.CopyTo(Path.Combine(targetDirectory, file.Name), overwrite: true);
    }
}

internal sealed class ManifestDto
{
    [JsonPropertyName("Author")]
    public string? Author { get; set; }

    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("URL")]
    public string? URL { get; set; }

    [JsonPropertyName("Version")]
    public string? Version { get; set; }

    [JsonPropertyName("Icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("Tags")]
    public string? Tags { get; set; }
}
