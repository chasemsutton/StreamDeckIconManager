using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreamDeckIconManager.Mac;

internal enum UpdateChannel
{
    /// <summary>Normal GitHub releases only.</summary>
    Stable,

    /// <summary>The newest release, including ones marked as pre-release on GitHub.</summary>
    Beta
}

/// <summary>How "Save to Stream Deck" gets changes into Stream Deck.</summary>
internal enum ApplyMode
{
    /// <summary>Stream Deck installs each changed pack while running, confirming each.</summary>
    Live,

    /// <summary>Save everything and restart Stream Deck.</summary>
    Restart
}

/// <summary>What to do with GIF icons already in packs (new GIFs are always converted).</summary>
internal enum ExistingGifsChoice
{
    Convert,
    Keep
}

/// <summary>The one-time answer about images in existing packs that are larger than Stream Deck needs.</summary>
internal enum ExistingLargeImagesChoice
{
    Shrink,
    Keep
}

/// <summary>
/// Per-user settings. macOS: ~/Library/Application Support/StreamDeckIconManager/settings.json.
/// </summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Elsewhere (running this build on Windows for a check) it uses its own folder, so it never
    /// touches the Windows app's settings.
    /// </summary>
    private static string FilePath { get; } = OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support",
            "StreamDeckIconManager", "settings.json")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamDeckIconManager-MacBuild", "settings.json");

    /// <summary>
    /// Someone who installs a beta build starts on the Beta channel; a full release starts on
    /// Stable, like the Windows app. Saved on first run, so updating to a full release doesn't
    /// quietly move a beta tester off betas. Only releases with a Mac download are offered.
    /// </summary>
    public UpdateChannel UpdateChannel { get; set; } = AppUpdateService.IsBetaBuild ? UpdateChannel.Beta : UpdateChannel.Stable;

    public bool ConvertGifsToWebp { get; set; } = true;
    public bool KeepFullSizeImages { get; set; }
    public ExistingGifsChoice? ExistingGifs { get; set; }
    public ExistingLargeImagesChoice? ExistingLargeImages { get; set; }
    public ApplyMode? LastApplyMode { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // A damaged settings file falls back to defaults.
            return new AppSettings();
        }

        // First run: remember the starting channel.
        var fresh = new AppSettings();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Settings are a convenience; failing to save them shouldn't break the app.
        }
    }
}
