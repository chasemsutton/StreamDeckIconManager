namespace Add_Stream_Deck_Icon;

internal static class PackPaths
{
    public const string CustomPackPrefix = "zzcustomuser";
    public const string DefaultDescription = "Custom icons for Elgato Stream Deck";
    public const string DefaultAuthor = "Custom User";
    public const string DefaultManifestTags = "Custom User Icon";

    public static string IconPacksRoot { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Elgato", "StreamDeck", "IconPacks");

    public static string BuildPackFolderName(string packName) =>
        $"{CustomPackPrefix}.{NormalizeKey(packName)}.iconpack.sdIconPack";

    public static string NormalizeKey(string name) =>
        name.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();

    public static bool NamesConflict(string a, string b) =>
        string.Equals(NormalizeKey(a), NormalizeKey(b), StringComparison.Ordinal);

    public static bool IsCustomPackDirectory(string path)
    {
        var folder = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return folder.StartsWith(CustomPackPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedImageExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase);
    }
}
