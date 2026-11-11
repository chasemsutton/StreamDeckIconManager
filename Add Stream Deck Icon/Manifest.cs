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

    /// <summary>Every name the pack's emblem file can have (icon.png, icon.svg, ...).</summary>
    private static readonly string[] EmblemFileNames = ImageHelper.Extensions.Select(e => "icon" + e).ToArray();

    public string Name { get; set; } = "";
    public string Author { get; set; } = PackPaths.DefaultAuthor;
    public string Description { get; set; } = PackPaths.DefaultDescription;
    public string IconPackPath { get; set; } = "";
    public int HighestIconNumber { get; set; }
    public List<PackIcon> Icons { get; set; } = new();

    /// <summary>Preview of the pack's emblem, for display.</summary>
    public Image Emblem { get; private set; } = null!;

    /// <summary>The emblem file exactly as provided; written back unchanged.</summary>
    public byte[] EmblemData { get; private set; } = Array.Empty<byte>();

    /// <summary>The emblem's file extension without the dot, e.g. "png" or "svg".</summary>
    public string EmblemExtension { get; private set; } = "png";

    /// <summary>The emblem file named in manifest.json when the pack was loaded.</summary>
    private string _emblemFile = "icon.png";

    /// <summary>Load an existing pack from disk (backs up custom packs and loads icons).</summary>
    public Manifest(string iconPackPath)
    {
        IconPackPath = iconPackPath;
        UseDefaultEmblem();
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
        UseDefaultEmblem();
        NewPackPath(streamDeckPacksPath);
    }

    private Manifest()
    {
        UseDefaultEmblem();
    }

    public void NewPackPath(string streamDeckPacksPath)
    {
        IconPackPath = Path.Combine(streamDeckPacksPath, PackPaths.BuildPackFolderName(Name));
        Directory.CreateDirectory(IconPackPath);
        Directory.CreateDirectory(Path.Combine(IconPackPath, "icons"));
        Directory.CreateDirectory(Path.Combine(IconPackPath, "backup", "icons-old"));
    }

    public void ChangePackName(string newPackName) => Name = newPackName;

    /// <summary>Adds a copy of a queued image (its original bytes, any supported format) as a new icon.</summary>
    public void AddIcon(string name, string[] tags, PackIcon source)
    {
        HighestIconNumber++;
        var path = $"Custom Icon-{HighestIconNumber}.{ImageHelper.ExtensionOf(source.Path)}";
        Icons.Add(new PackIcon(HighestIconNumber, path, name, tags, source.Data, source.Picture));
    }

    public void RemoveIcon(int index)
    {
        Icons[index].Picture.Dispose();
        Icons.RemoveAt(index);
    }

    /// <summary>
    /// Replaces the emblem. Takes ownership of <paramref name="preview"/>. With
    /// <paramref name="disposeOld"/> false the caller frees the old preview (once nothing shows it).
    /// </summary>
    public void SetEmblem(byte[] data, string extension, Image preview, bool disposeOld = true)
    {
        var old = Emblem;
        EmblemData = data;
        EmblemExtension = extension.TrimStart('.').ToLowerInvariant();
        Emblem = preview;
        if (disposeOld)
            old?.Dispose();
    }

    private void UseDefaultEmblem()
    {
        var preview = ImageHelper.CloneBitmap(Resources.imgIcon1);
        SetEmblem(ImageHelper.ToPng(preview), "png", preview);
    }

    /// <summary>Loads the emblem from a file, keeping the default if it's missing or unreadable.</summary>
    private void TryLoadEmblem(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            var (data, preview) = ImageHelper.Load(path);
            SetEmblem(data, ImageHelper.ExtensionOf(path), preview);
        }
        catch
        {
            // Unreadable emblem: keep what we have.
        }
    }

    public void SaveManifest() => WriteTo(IconPackPath);

    /// <summary>
    /// Writes the pack's Stream Deck files (manifest.json, icons.json, the emblem, icons/) into a
    /// folder, replacing what's there. Leaves anything else, such as the app's backup folder, alone.
    /// Image files are written byte for byte as they were read.
    /// </summary>
    public void WriteTo(string folder)
    {
        var iconsDir = Path.Combine(folder, "icons");
        Directory.CreateDirectory(iconsDir);

        foreach (var file in new DirectoryInfo(iconsDir).EnumerateFiles())
            file.Delete();

        File.Delete(Path.Combine(folder, "icons.json"));
        File.Delete(Path.Combine(folder, "manifest.json"));
        foreach (var emblemFile in EmblemFileNames)
            File.Delete(Path.Combine(folder, emblemFile));

        foreach (var icon in Icons)
            File.WriteAllBytes(Path.Combine(iconsDir, icon.Path), icon.Data);

        File.WriteAllBytes(Path.Combine(folder, EmblemFileName), EmblemData);
        File.WriteAllText(Path.Combine(folder, "icons.json"), CompileIconsJson());
        File.WriteAllText(Path.Combine(folder, "manifest.json"), CompileManifestJson());
    }

    private string EmblemFileName => "icon." + EmblemExtension;

    /// <summary>
    /// Reads any icon pack folder for importing: one made by this app (including old exports) or
    /// by another tool. Icons come straight from icons/. Files that can't be read at all are
    /// skipped and counted in <paramref name="skippedIcons"/>.
    /// </summary>
    public static Manifest ReadForImport(string packDir, out int skippedIcons)
    {
        skippedIcons = 0;
        var pack = new Manifest { IconPackPath = packDir };

        ManifestDto? dto = null;
        var manifestPath = Path.Combine(packDir, "manifest.json");
        if (File.Exists(manifestPath))
            dto = JsonSerializer.Deserialize<ManifestDto>(File.ReadAllText(manifestPath), JsonOptions);

        var folderName = Path.GetFileName(packDir.TrimEnd(Path.DirectorySeparatorChar));
        pack.Name = string.IsNullOrWhiteSpace(dto?.Name) ? folderName.Split('.').Reverse().Skip(2).FirstOrDefault() ?? "Imported pack" : dto!.Name!;
        pack.Author = string.IsNullOrWhiteSpace(dto?.Author) ? PackPaths.DefaultAuthor : dto!.Author!;
        pack.Description = string.IsNullOrWhiteSpace(dto?.Description) ? PackPaths.DefaultDescription : dto!.Description!;

        var iconsJson = Path.Combine(packDir, "icons.json");
        var entries = File.Exists(iconsJson)
            ? JsonSerializer.Deserialize<List<IconDto>>(File.ReadAllText(iconsJson), JsonOptions) ?? new List<IconDto>()
            : new List<IconDto>();

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
                continue;

            var imagePath = Path.Combine(packDir, "icons", entry.Path);
            if (!File.Exists(imagePath) || !ImageHelper.IsSupported(imagePath))
            {
                // Missing, or a format Stream Deck itself doesn't accept in icon packs.
                skippedIcons++;
                continue;
            }

            // Kept even if it can't be previewed: Stream Deck may still be able to show it.
            var (data, preview, _) = ImageHelper.LoadKeepingBytes(imagePath);

            var number = PackIcon.ParseNumberFromPath(entry.Path);
            var name = string.IsNullOrWhiteSpace(entry.Name) ? Path.GetFileNameWithoutExtension(entry.Path) : entry.Name;
            pack.Icons.Add(new PackIcon(number, Path.GetFileName(entry.Path), name, entry.Tags ?? Array.Empty<string>(), data, preview));
            pack.HighestIconNumber = Math.Max(pack.HighestIconNumber, number);
        }

        pack.TryLoadEmblem(Path.Combine(packDir, string.IsNullOrWhiteSpace(dto?.Icon) ? "icon.png" : dto!.Icon!));
        return pack;
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
            if (!string.IsNullOrWhiteSpace(dto.Icon))
                _emblemFile = dto.Icon;
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

        foreach (var oldEmblem in Directory.GetFiles(backupRoot, "icon-old.*"))
            File.Delete(oldEmblem);
        CopyFileOverwrite(Path.Combine(IconPackPath, _emblemFile), Path.Combine(backupRoot, "icon-old" + Path.GetExtension(_emblemFile)));

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
        if (File.Exists(iconsJsonPath))
        {
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

                // Keep the file even if it can't be previewed, so saving never deletes it.
                var (data, preview, _) = ImageHelper.LoadKeepingBytes(imagePath);
                Icons.Add(new PackIcon(number, dto.Path, dto.Name, dto.Tags ?? Array.Empty<string>(), data, preview));
                if (number > HighestIconNumber)
                    HighestIconNumber = number;
            }
        }

        var emblemBackup = Path.Combine(IconPackPath, "backup", "icon-old" + Path.GetExtension(_emblemFile));
        TryLoadEmblem(File.Exists(emblemBackup) ? emblemBackup : Path.Combine(IconPackPath, _emblemFile));
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
            // A new version on every save, so installing an exported pack over an older copy of
            // the same pack is always treated as an update.
            Version = $"1.{DateTime.UtcNow:yyMMdd}.{(int)DateTime.UtcNow.TimeOfDay.TotalSeconds}",
            Icon = EmblemFileName,
            // Stream Deck shows a pack's Tags, not its Description, when you hover the pack in the
            // icon library, so the description goes here too. Commas would split it into separate tags.
            Tags = HoverText(Description)
        };
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    private static string HoverText(string description)
    {
        var text = string.IsNullOrWhiteSpace(description) ? PackPaths.DefaultDescription : description.Trim();
        return text.Replace(", ", " - ").Replace(",", " - ");
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
