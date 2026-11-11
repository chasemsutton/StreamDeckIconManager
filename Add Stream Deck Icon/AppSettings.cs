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
