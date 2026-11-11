namespace Add_Stream_Deck_Icon.UI;

/// <summary>
/// The app icon, read from the same .ico that gives the .exe its icon.
/// Source artwork: design/app-icon.svg.
/// </summary>
internal static class AppIcon
{
    private static readonly Lazy<Icon?> Full = new(() =>
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("AppIcon.ico");
        return stream is null ? null : new Icon(stream);
    });

    /// <summary>Icon for window title bars and the taskbar; Windows picks the right size.</summary>
    public static Icon? Window => Full.Value;

    /// <summary>The icon drawn at a specific size, for use inside the UI.</summary>
    public static Bitmap? Bitmap(int size) =>
        Full.Value is { } icon ? new Icon(icon, size, size).ToBitmap() : null;
}
