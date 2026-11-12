namespace StreamDeckIconManager;

/// <summary>
/// Shared GitHub release asset naming for the app and setup bootstrapper.
/// </summary>
internal static class ReleaseNaming
{
    public const string Owner = "chasemsutton";
    public const string Repo = "StreamDeckIconManager";
    public const string AppExeFileName = "Add Stream Deck Icon.exe";

    /// <summary>
    /// Mac-only releases are tagged "mac-1.2.1.0" (or "mac-1.2.1.0-beta"). Windows versions up to
    /// 1.2.1.0 can't parse such tags, so they never offer a Mac release as an update; Windows
    /// releases keep plain tags. See docs/RELEASING.md.
    /// </summary>
    public const string MacTagPrefix = "mac-";

    public static string MacReleaseTag(Version version) => MacTagPrefix + FormatVersion(version);

    public static string SetupFileName(Version version) =>
        $"StreamDeckIconManager{FormatVersion(version)}Setup.exe";

    public static string AppPackageFileName(Version version) =>
        $"StreamDeckIconManager{FormatVersion(version)}App.zip";

    public static string FormatVersion(Version version) => version.ToString();

    /// <summary>Install location. Set SDIM_INSTALL_DIR to test Setup against another folder.</summary>
    public static string InstallDirectory =>
        Environment.GetEnvironmentVariable("SDIM_INSTALL_DIR") is { Length: > 0 } overrideDir
            ? overrideDir
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "StreamDeckIconManager");

    public static string InstalledAppPath => Path.Combine(InstallDirectory, AppExeFileName);

    public static Uri LatestReleaseApiUri { get; } =
        new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

    /// <summary>Recent releases, newest first, including pre-releases (used by the beta channel).</summary>
    public static Uri ReleasesApiUri { get; } =
        new($"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=30");

    public static Uri ReleaseByTagApiUri(string tag) =>
        new($"https://api.github.com/repos/{Owner}/{Repo}/releases/tags/{Uri.EscapeDataString(tag)}");

    public static bool IsSetupAssetName(string name) =>
        name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase)
        && name.StartsWith("StreamDeckIconManager", StringComparison.OrdinalIgnoreCase);

    public static bool IsAppPackageAssetName(string name) =>
        name.EndsWith("App.zip", StringComparison.OrdinalIgnoreCase)
        && name.StartsWith("StreamDeckIconManager", StringComparison.OrdinalIgnoreCase);
}
