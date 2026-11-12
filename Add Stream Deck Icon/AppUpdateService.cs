using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamDeckIconManager;

namespace Add_Stream_Deck_Icon;

internal sealed class AppUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static Version CurrentVersion { get; } = ReadCurrentVersion();
    private static readonly HttpClient Http = CreateClient();

    public async Task<UpdateCheckResult> CheckForUpdateAsync(
        UpdateChannel channel = UpdateChannel.Stable,
        CancellationToken cancellationToken = default)
    {
        var releases = await GetJsonAsync<List<GitHubRelease>>(ReleaseNaming.ReleasesApiUri, cancellationToken).ConfigureAwait(false);
        if (releases is null)
            return UpdateCheckResult.Failed("Could not read release information from GitHub.");

        // Releases for other platforms (Mac-only betas) have no Windows setup and are skipped.
        var release = NewestWindowsRelease(releases, includePrereleases: channel == UpdateChannel.Beta);
        if (release is null)
            return UpdateCheckResult.UpToDate(CurrentVersion);

        if (!TryParseVersion(release.TagName, out var latestVersion))
            return UpdateCheckResult.Failed($"Unrecognized release version: {release.TagName}");

        if (latestVersion <= CurrentVersion)
            return UpdateCheckResult.UpToDate(latestVersion);

        var asset = SelectInstallerAsset(release.Assets, latestVersion);
        if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            return UpdateCheckResult.Failed("Latest release has no downloadable setup executable.");

        if (!IsTrustedDownloadUrl(asset.BrowserDownloadUrl))
            return UpdateCheckResult.Failed("Release download URL is not from the expected GitHub repository.");

        return UpdateCheckResult.Available(latestVersion, release.TagName, asset.Name, asset.BrowserDownloadUrl, release.HtmlUrl, release.Prerelease);
    }

    /// <summary>
    /// Highest-versioned published release that has a Windows setup, optionally including
    /// pre-releases. Drafts, and releases for other platforms (such as "mac-" tagged ones), are skipped.
    /// </summary>
    private static GitHubRelease? NewestWindowsRelease(IEnumerable<GitHubRelease> releases, bool includePrereleases) =>
        releases
            .Where(r => !r.Draft && (includePrereleases || !r.Prerelease))
            .Where(r => !r.TagName.StartsWith(ReleaseNaming.MacTagPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Assets.Any(a => ReleaseNaming.IsSetupAssetName(a.Name)))
            .Select(r => (Release: r, Ok: TryParseVersion(r.TagName, out var v), Version: v))
            .Where(x => x.Ok)
            .OrderByDescending(x => x.Version)
            .Select(x => x.Release)
            .FirstOrDefault();

    private static async Task<T?> GetJsonAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the downloaded installer and tells it which process to wait for.
    /// The caller must exit straight after: Setup replaces this app's files.
    /// </summary>
    public static void LaunchInstaller(string setupPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = $"--wait-pid {Environment.ProcessId}",
            WorkingDirectory = Path.GetDirectoryName(setupPath)!,
            UseShellExecute = true
        });
    }

    /// <summary>Downloads the update's installer and returns its path. Does not start it.</summary>
    public async Task<string> DownloadInstallerAsync(
        UpdateCheckResult update,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!update.IsUpdateAvailable || string.IsNullOrWhiteSpace(update.DownloadUrl))
            throw new InvalidOperationException("No update is available to install.");

        var workRoot = Path.Combine(DownloadsFolder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        try
        {
            var downloadName = update.AssetName ?? ReleaseNaming.SetupFileName(update.LatestVersion ?? CurrentVersion);
            var downloadPath = Path.Combine(workRoot, downloadName);

            progress?.Report(new UpdateProgress("Downloading update...", 0));
            await DownloadFileAsync(update.DownloadUrl, downloadPath, progress, cancellationToken).ConfigureAwait(false);

            string setupPath;
            if (downloadName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                setupPath = downloadPath;
            }
            else if (downloadName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                // Legacy zip releases that contain a setup executable.
                progress?.Report(new UpdateProgress("Extracting installer...", 90));
                var extractPath = Path.Combine(workRoot, "extracted");
                ZipFile.ExtractToDirectory(downloadPath, extractPath);
                setupPath = FindSetupExecutable(extractPath)
                    ?? throw new FileNotFoundException("Could not find a setup executable in the downloaded update package.");
            }
            else
            {
                throw new InvalidOperationException($"Unsupported update package type: {downloadName}");
            }

            progress?.Report(new UpdateProgress("Download complete.", 100));
            return setupPath;
        }
        catch
        {
            // Cancelled or failed: don't leave a partial installer behind.
            TryDeleteDirectory(workRoot);
            throw;
        }
    }

    /// <summary>Where downloaded installers go, one subfolder per download.</summary>
    private static string DownloadsFolder => Path.Combine(Path.GetTempPath(), "StreamDeckIconManager-Update");

    /// <summary>
    /// Deletes installers from earlier updates (about 70 MB each). Called at startup; an installer
    /// that's still running is locked and simply left for next time.
    /// </summary>
    public static void CleanupOldDownloads()
    {
        try
        {
            if (!Directory.Exists(DownloadsFolder))
                return;

            foreach (var dir in Directory.GetDirectories(DownloadsFolder))
                TryDeleteDirectory(dir);
        }
        catch
        {
            // Best-effort.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // Locked or already gone.
        }
    }

    private static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var remote = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var local = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await remote.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await local.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readTotal += read;

            if (total is > 0)
            {
                var percent = (int)Math.Clamp(readTotal * 90L / total.Value, 0, 90);
                progress?.Report(new UpdateProgress($"Downloading update... {percent}%", percent));
            }
            else
            {
                progress?.Report(new UpdateProgress("Downloading update...", 45));
            }
        }
    }

    private static string? FindSetupExecutable(string root)
    {
        var named = Directory.EnumerateFiles(root, "*Setup.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path => ReleaseNaming.IsSetupAssetName(Path.GetFileName(path)));
        if (named is not null)
            return named;

        return Directory.EnumerateFiles(root, "setup.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)
                .FirstOrDefault(path => Path.GetFileName(path).Contains("setup", StringComparison.OrdinalIgnoreCase));
    }

    private static GitHubAsset? SelectInstallerAsset(IReadOnlyList<GitHubAsset>? assets, Version latestVersion)
    {
        if (assets is null || assets.Count == 0)
            return null;

        var expected = ReleaseNaming.SetupFileName(latestVersion);
        return assets.FirstOrDefault(a => a.Name.Equals(expected, StringComparison.OrdinalIgnoreCase))
            ?? assets.FirstOrDefault(a => ReleaseNaming.IsSetupAssetName(a.Name))
            ?? assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                          && a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase))
            ?? assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTrustedDownloadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;

        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads tags such as "1.2.0.1", "v1.2.0.1" or "1.2.0.1-beta".</summary>
    internal static bool TryParseVersion(string tag, out Version version)
    {
        var cleaned = tag.Trim();
        if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[1..];

        var suffix = cleaned.IndexOfAny(new[] { '-', '+', ' ' });
        if (suffix >= 0)
            cleaned = cleaned[..suffix];

        return Version.TryParse(cleaned, out version!);
    }

    private static Version ReadCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var value = informational.Split('+', StringSplitOptions.TrimEntries)[0];
            if (Version.TryParse(value, out var parsed))
                return parsed;
        }

        return assembly.GetName().Version ?? new Version(1, 0, 0, 0);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("StreamDeckIconManager", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}

internal sealed class UpdateCheckResult
{
    private UpdateCheckResult(
        bool succeeded,
        bool isUpdateAvailable,
        bool isUpToDate,
        Version? latestVersion,
        string? tagName,
        string? assetName,
        string? downloadUrl,
        string? releasePageUrl,
        string? errorMessage,
        bool isPrerelease = false)
    {
        IsPrerelease = isPrerelease;
        Succeeded = succeeded;
        IsUpdateAvailable = isUpdateAvailable;
        IsUpToDate = isUpToDate;
        LatestVersion = latestVersion;
        TagName = tagName;
        AssetName = assetName;
        DownloadUrl = downloadUrl;
        ReleasePageUrl = releasePageUrl;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }
    public bool IsUpdateAvailable { get; }
    public bool IsUpToDate { get; }
    public Version? LatestVersion { get; }
    public string? TagName { get; }
    public string? AssetName { get; }
    public string? DownloadUrl { get; }
    public string? ReleasePageUrl { get; }
    public string? ErrorMessage { get; }

    /// <summary>True when the update is a GitHub pre-release (beta channel).</summary>
    public bool IsPrerelease { get; }

    public static UpdateCheckResult Available(
        Version latestVersion,
        string tagName,
        string assetName,
        string downloadUrl,
        string? releasePageUrl,
        bool isPrerelease) =>
        new(true, true, false, latestVersion, tagName, assetName, downloadUrl, releasePageUrl, null, isPrerelease);

    public static UpdateCheckResult UpToDate(Version latestVersion) =>
        new(true, false, true, latestVersion, null, null, null, null, null);

    public static UpdateCheckResult Failed(string message) =>
        new(false, false, false, null, null, null, null, null, message);
}

internal readonly record struct UpdateProgress(string Status, int PercentComplete);

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = new();
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}
