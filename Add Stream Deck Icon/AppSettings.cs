using System.Text.Json;
using System.Text.Json.Serialization;

namespace Add_Stream_Deck_Icon;

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
    /// <summary>Convert them to animated WebP, now and whenever more turn up.</summary>
    Convert,

    /// <summary>Leave them as GIFs and don't ask again.</summary>
    Keep
}

/// <summary>The one-time answer about images in existing packs that are larger than Stream Deck needs.</summary>
internal enum ExistingLargeImagesChoice
{
    /// <summary>They were scaled down to 144 px.</summary>
    Shrink,

    /// <summary>Left at full size.</summary>
    Keep
}

/// <summary>
/// Per-user settings, stored outside the install folder so installs and updates don't reset them.
/// </summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StreamDeckIconManager",
        "settings.json");

    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;

    /// <summary>
    /// Convert GIFs to lossless animated WebP when they're added or imported, so Stream Deck's
    /// icon library can preview them. On by default.
    /// </summary>
    public bool ConvertGifsToWebp { get; set; } = true;

    /// <summary>
    /// Keep images at their original size when adding or importing them, instead of scaling them
    /// down to Stream Deck's 144 px icon size. Off by default: large images slow down this app and
    /// Stream Deck and use a lot of memory.
    /// </summary>
    public bool KeepFullSizeImages { get; set; }

    /// <summary>
    /// The answer to "convert the GIFs already in your packs?". Null until the user chooses,
    /// so the question is asked once and then never again.
    /// </summary>
    public ExistingGifsChoice? ExistingGifs { get; set; }

    /// <summary>
    /// The answer to "shrink the large images already in your packs?". Asked once; null until the
    /// user chooses. Unlike GIFs, it isn't repeated later, so full-size images the user adds on
    /// purpose stay that way.
    /// </summary>
    public ExistingLargeImagesChoice? ExistingLargeImages { get; set; }

    /// <summary>The Save option used last time, offered first next time. Null until first save.</summary>
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
        }

        return new AppSettings();
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
