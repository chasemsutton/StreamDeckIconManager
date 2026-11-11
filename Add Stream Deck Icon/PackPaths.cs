namespace Add_Stream_Deck_Icon;

internal static class PackPaths
{
    public const string CustomPackPrefix = "zzcustomuser";
    public const string DefaultDescription = "Custom icons for Elgato Stream Deck";
    public const string DefaultAuthor = "Custom User";

    /// <summary>Where Stream Deck itself keeps installed icon packs (live installs land here).</summary>
    public static string StreamDeckIconPacksFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Elgato", "StreamDeck", "IconPacks");

    /// <summary>
    /// The folder the app reads and writes packs in: Stream Deck's, unless SDIM_ICONPACKS_ROOT
    /// points the app at a test folder.
    /// </summary>
    public static string IconPacksRoot { get; } =
        Environment.GetEnvironmentVariable("SDIM_ICONPACKS_ROOT") is { Length: > 0 } overrideRoot
            ? overrideRoot
            : StreamDeckIconPacksFolder;

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
}
