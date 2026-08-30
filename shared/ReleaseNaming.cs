namespace StreamDeckIconManager;

/// <summary>
/// Shared GitHub release asset naming for the app and setup bootstrapper.
/// </summary>
internal static class ReleaseNaming
{
    public const string Owner = "chasemsutton";
    public const string Repo = "StreamDeckIconManager";
    public const string AppExeFileName = "Add Stream Deck Icon.exe";

    public static string SetupFileName(Version version) =>
        $"StreamDeckIconManager{FormatVersion(version)}Setup.exe";

    public static string AppPackageFileName(Version version) =>
        $"StreamDeckIconManager{FormatVersion(version)}App.zip";

    public static string FormatVersion(Version version) => version.ToString();

    public static string InstallDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "StreamDeckIconManager");

    public static string InstalledAppPath => Path.Combine(InstallDirectory, AppExeFileName);

    public static Uri LatestReleaseApiUri { get; } =
        new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

    public static Uri ReleaseByTagApiUri(string tag) =>
        new($"https://api.github.com/repos/{Owner}/{Repo}/releases/tags/{Uri.EscapeDataString(tag)}");

    public static bool IsSetupAssetName(string name) =>
        name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase)
        && name.StartsWith("StreamDeckIconManager", StringComparison.OrdinalIgnoreCase);

    public static bool IsAppPackageAssetName(string name) =>
        name.EndsWith("App.zip", StringComparison.OrdinalIgnoreCase)
        && name.StartsWith("StreamDeckIconManager", StringComparison.OrdinalIgnoreCase);
}
