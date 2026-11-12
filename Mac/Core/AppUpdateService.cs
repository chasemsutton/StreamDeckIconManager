using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreamDeckIconManager.Mac;

/// <summary>
/// Checks GitHub for a newer release with a Mac download for this processor, and installs it:
/// downloads the zip, unpacks it, signs the new app locally (ad hoc, like the first-run step), then
/// hands over to a small script that waits for this app to quit, swaps the app bundles and opens
/// the new version. Apps can't be replaced from inside on Windows, but macOS allows it.
/// </summary>
internal sealed class AppUpdateService
{
    // Static initializers run in order: the version must exist before the client uses it.
    public static Version CurrentVersion { get; } = ReadCurrentVersion();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly HttpClient Http = CreateClient();

    private const string BundleId = "com.suttontools.streamdeckiconmanager";
    private const string BundleName = "Stream Deck Icon Manager.app";

    /// <summary>True for builds released as betas (tag mac-&lt;version&gt;-beta). See docs/RELEASING.md.</summary>
    public static bool IsBetaBuild { get; } = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(a => a.Key == "SdimBeta" && a.Value == "true");

    /// <summary>"arm64" on Apple silicon, "x64" on Intel Macs.</summary>
    public static string MacArchitecture => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    /// <summary>Where downloads are unpacked, one subfolder per update.</summary>
    private static string WorkRoot => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "StreamDeckIconManager", "Updates")
        : Path.Combine(Path.GetTempPath(), "StreamDeckIconManager-MacUpdates");

    public async Task<MacUpdateCheckResult> CheckForUpdateAsync(UpdateChannel channel, CancellationToken cancellationToken = default)
    {
        var releases = await GetJsonAsync<List<GitHubRelease>>(ReleaseNaming.ReleasesApiUri, cancellationToken).ConfigureAwait(false);
        if (releases is null)
            return MacUpdateCheckResult.Failed("Could not read release information from GitHub.");

        // Newest release (stable only, or including betas) that has a Mac download for this processor.
        // Windows-only releases have no Mac zip, so they're never offered here.
        var newest = releases
            .Where(r => !r.Draft && (channel == UpdateChannel.Beta || !r.Prerelease))
            .Select(r => (Release: r, Asset: r.Assets.FirstOrDefault(a => IsMacAsset(a.Name)), Ok: TryParseTag(r.TagName, out var v), Version: v))
            .Where(x => x.Asset is not null && x.Ok && x.Version is not null)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();

        if (newest.Release is null || newest.Version is null || newest.Version <= CurrentVersion)
            return MacUpdateCheckResult.UpToDate();

        if (!IsTrustedDownloadUrl(newest.Asset!.BrowserDownloadUrl))
            return MacUpdateCheckResult.Failed("The release download isn't from the expected GitHub repository.");

        return MacUpdateCheckResult.Available(newest.Version, newest.Release.HtmlUrl, newest.Release.Prerelease, newest.Asset.BrowserDownloadUrl);
    }

    /// <summary>Reads tags such as "mac-1.2.1.0", "1.2.1.0", "v1.2.1.0" or "1.2.1.0-beta".</summary>
    internal static bool TryParseTag(string tag, out Version? version)
    {
        var cleaned = tag.Trim();
        if (cleaned.StartsWith(ReleaseNaming.MacTagPrefix, StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[ReleaseNaming.MacTagPrefix.Length..];
        if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[1..];
        var suffix = cleaned.IndexOfAny(new[] { '-', '+', ' ' });
        if (suffix >= 0)
            cleaned = cleaned[..suffix];
        return Version.TryParse(cleaned, out version);
    }

    private static bool IsMacAsset(string name) =>
        name.StartsWith("StreamDeckIconManager", StringComparison.OrdinalIgnoreCase)
        && name.EndsWith($"Mac-{MacArchitecture}.zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>Only open links to this app's GitHub releases.</summary>
    public static bool IsReleasePage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith($"/{ReleaseNaming.Owner}/{ReleaseNaming.Repo}/", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrustedDownloadUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith($"/{ReleaseNaming.Owner}/{ReleaseNaming.Repo}/releases/download/", StringComparison.OrdinalIgnoreCase);

    #region Installing

    /// <summary>
    /// The app bundle this copy is running from, or null if it can't be replaced in place
    /// (not on macOS, not inside a .app, run from a read-only or "translocated" location).
    /// </summary>
    public static string? InstalledBundlePath
    {
        get
        {
            if (!OperatingSystem.IsMacOS())
                return null;

            // .../Stream Deck Icon Manager.app/Contents/MacOS/
            var macOS = AppContext.BaseDirectory.TrimEnd('/');
            var bundle = Path.GetDirectoryName(Path.GetDirectoryName(macOS));
            if (bundle is null || !bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return null;

            // macOS runs quarantined apps from a random read-only copy; updating that is pointless.
            if (bundle.Contains("/AppTranslocation/", StringComparison.Ordinal))
                return null;

            var parent = Path.GetDirectoryName(bundle);
            return parent is not null && CanWrite(parent) && CanWrite(bundle) ? bundle : null;
        }
    }

    public static bool CanInstallInPlace => InstalledBundlePath is not null;

    private static bool CanWrite(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, ".sdim-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Downloads and prepares the update: unpacks it, checks it's this app, and signs it locally.
    /// Returns the folder holding the new app and the swap script. Doesn't touch the installed app.
    /// </summary>
    public async Task<PreparedUpdate> DownloadAsync(MacUpdateCheckResult update, IProgress<(string Status, int Percent)> progress,
        CancellationToken cancellationToken)
    {
        if (update.DownloadUrl is null || update.LatestVersion is null)
            throw new InvalidOperationException("No update is available to install.");
        var bundle = InstalledBundlePath ?? throw new InvalidOperationException("This copy of the app can't update itself.");

        var work = Path.Combine(WorkRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var zip = Path.Combine(work, "update.zip");
            await DownloadFileAsync(update.DownloadUrl, zip, progress, cancellationToken).ConfigureAwait(false);

            progress.Report(("Unpacking...", 92));
            var extracted = Path.Combine(work, "extracted");
            Directory.CreateDirectory(extracted);
            // ditto keeps the files' permissions, like Finder does when it unzips.
            await RunAsync("/usr/bin/ditto", new[] { "-x", "-k", zip, extracted }, cancellationToken, "Couldn't unpack the update.").ConfigureAwait(false);

            var newApp = Directory.GetDirectories(extracted, "*.app", SearchOption.AllDirectories)
                             .FirstOrDefault(d => Path.GetFileName(d).Equals(BundleName, StringComparison.OrdinalIgnoreCase))
                         ?? throw new InvalidDataException("The download doesn't contain the app.");
            CheckBundle(newApp, update.LatestVersion);

            progress.Report(("Preparing the new version...", 96));
            // Same as the first-run step: no quarantine flag, and a local (ad hoc) signature, which
            // Apple silicon Macs require before they'll run an app.
            await RunAsync("/usr/bin/xattr", new[] { "-cr", newApp }, cancellationToken, null).ConfigureAwait(false);
            await RunAsync("/usr/bin/codesign", new[] { "--force", "--deep", "--sign", "-", newApp }, cancellationToken,
                "Couldn't sign the new version.").ConfigureAwait(false);
            await RunAsync("/usr/bin/codesign", new[] { "--verify", "--deep", newApp }, cancellationToken,
                "The new version's signature didn't check out.").ConfigureAwait(false);

            File.Delete(zip);
            var script = Path.Combine(work, "install.sh");
            await File.WriteAllTextAsync(script, SwapScript, cancellationToken).ConfigureAwait(false);
            progress.Report(("Ready to install.", 100));
            return new PreparedUpdate(work, newApp, bundle, script);
        }
        catch
        {
            TryDeleteDirectory(work);
            throw;
        }
    }

    /// <summary>
    /// Starts the swap script, which waits for this app to quit, moves the new app into place
    /// (putting the old one back if that fails) and opens it. The caller must quit straight after.
    /// </summary>
    public static void LaunchInstaller(PreparedUpdate update)
    {
        var start = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(update.Script);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add(update.InstalledBundle);
        start.ArgumentList.Add(update.NewBundle);
        start.ArgumentList.Add(update.WorkFolder);
        Process.Start(start);
    }

    /// <summary>
    /// If the last update couldn't swap the app (the script leaves a note), returns why, once.
    /// </summary>
    public static string? TakeFailedUpdateNote()
    {
        try
        {
            var note = Path.Combine(Path.GetDirectoryName(WorkRoot)!, "update-failed.txt");
            if (!File.Exists(note))
                return null;
            var text = File.ReadAllText(note).Trim();
            File.Delete(note);
            return text.Length > 0 ? text : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Deletes leftovers from earlier updates. Called at startup.</summary>
    public static void CleanupOldDownloads()
    {
        try
        {
            if (!Directory.Exists(WorkRoot))
                return;
            foreach (var dir in Directory.GetDirectories(WorkRoot))
                TryDeleteDirectory(dir);
        }
        catch
        {
            // Best-effort.
        }
    }

    // $1 app PID, $2 installed .app, $3 new .app, $4 work folder (holds this script).
    private const string SwapScript = """
        #!/bin/sh
        PID="$1"; OLD="$2"; NEW="$3"; WORK="$4"
        LOG="$HOME/Library/Logs/StreamDeckIconManager.log"
        NOTE="$HOME/Library/Caches/StreamDeckIconManager/update-failed.txt"
        fail() { mkdir -p "$(dirname "$LOG")" "$(dirname "$NOTE")"; echo "update: $1" >> "$LOG"; echo "$1" > "$NOTE"; }
        n=0
        while kill -0 "$PID" 2>/dev/null; do
          sleep 0.2; n=$((n+1))
          if [ "$n" -gt 300 ]; then fail "The app didn't quit, so the update was skipped."; exit 1; fi
        done
        BACKUP="$WORK/previous.app"
        if mv "$OLD" "$BACKUP"; then
          if mv "$NEW" "$OLD"; then
            rm -rf "$BACKUP"
          else
            fail "macOS didn't allow replacing the app, so you're still on the old version."
            mv "$BACKUP" "$OLD"
          fi
        else
          fail "macOS didn't allow replacing the app, so you're still on the old version."
        fi
        open "$OLD"
        cd /
        rm -rf "$WORK"
        """;

    /// <summary>Checks the downloaded bundle is this app, for this Mac, at the expected version.</summary>
    private static void CheckBundle(string app, Version expected)
    {
        var plist = Path.Combine(app, "Contents", "Info.plist");
        var exe = Path.Combine(app, "Contents", "MacOS", "StreamDeckIconManager");
        if (!File.Exists(plist) || !File.Exists(exe))
            throw new InvalidDataException("The download isn't a complete copy of the app.");

        var text = File.ReadAllText(plist);
        if (!text.Contains($"<string>{BundleId}</string>", StringComparison.Ordinal))
            throw new InvalidDataException("The download isn't Stream Deck Icon Manager.");
        if (!text.Contains($"<string>{ReleaseNaming.FormatVersion(expected)}</string>", StringComparison.Ordinal))
            throw new InvalidDataException($"The download isn't version {expected}.");

        // Mach-O CPU type: 0x0100000C arm64, 0x01000007 x86_64.
        using var stream = File.OpenRead(exe);
        var header = new byte[8];
        if (stream.Read(header, 0, 8) != 8 || BitConverter.ToUInt32(header, 0) != 0xFEEDFACF)
            throw new InvalidDataException("The download's app isn't a Mac program.");
        var cpu = BitConverter.ToUInt32(header, 4);
        var expectedCpu = MacArchitecture == "arm64" ? 0x0100000Cu : 0x01000007u;
        if (cpu != expectedCpu)
            throw new InvalidDataException("The download is for a different kind of Mac.");
    }

    private static async Task DownloadFileAsync(string url, string destination, IProgress<(string Status, int Percent)> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(("Downloading update...", 0));
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;

        await using var remote = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var local = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        var lastPercent = -1;
        while ((read = await remote.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await local.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            done += read;
            var percent = total is > 0 ? (int)Math.Clamp(done * 90L / total.Value, 0, 90) : 45;
            if (percent != lastPercent)
            {
                lastPercent = percent;
                progress.Report(total is > 0
                    ? ($"Downloading update... {done / (1024 * 1024)} of {total.Value / (1024 * 1024)} MB", percent)
                    : ("Downloading update...", percent));
            }
        }
    }

    private static async Task RunAsync(string file, IEnumerable<string> args, CancellationToken cancellationToken, string? failure)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in args)
            start.ArgumentList.Add(a);

        using var process = Process.Start(start) ?? throw new InvalidOperationException(failure ?? $"Couldn't run {file}.");
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0 && failure is not null)
        {
            var detail = (await stderr.ConfigureAwait(false)).Trim();
            throw new InvalidOperationException(detail.Length > 0 ? $"{failure}\n{detail}" : failure);
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
            // Best-effort.
        }
    }

    #endregion

    private static async Task<T?> GetJsonAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StreamDeckIconManager-Mac", CurrentVersion.ToString()));
        return client;
    }

    private static Version ReadCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (info is not null && Version.TryParse(info.Split('+')[0], out var parsed))
            return parsed;
        return assembly.GetName().Version ?? new Version(0, 0, 0, 0);
    }
}

/// <summary>A downloaded, unpacked and signed update, ready for <see cref="AppUpdateService.LaunchInstaller"/>.</summary>
internal sealed record PreparedUpdate(string WorkFolder, string NewBundle, string InstalledBundle, string Script);

internal sealed record MacUpdateCheckResult(bool Succeeded, bool IsUpdateAvailable, Version? LatestVersion, string? ReleasePageUrl,
    bool IsPrerelease, string? DownloadUrl, string? ErrorMessage)
{
    public static MacUpdateCheckResult Available(Version version, string? page, bool prerelease, string downloadUrl) =>
        new(true, true, version, page, prerelease, downloadUrl, null);

    public static MacUpdateCheckResult UpToDate() => new(true, false, null, null, false, null, null);
    public static MacUpdateCheckResult Failed(string message) => new(false, false, null, null, false, null, message);
}

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
