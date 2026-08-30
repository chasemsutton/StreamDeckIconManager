using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamDeckIconManager;

namespace StreamDeckIconManager.Setup;

internal sealed class SetupForm : Form
{
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _installButton;
    private readonly Button _closeButton;
    private bool _busy;

    public SetupForm()
    {
        Text = $"Stream Deck Icon Manager Setup v{InstallerVersion}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 200);
        BackColor = Color.FromArgb(45, 45, 45);
        ForeColor = Color.White;

        _titleLabel = new Label
        {
            AutoSize = false,
            Location = new Point(20, 18),
            Size = new Size(440, 40),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            Text = "Stream Deck Icon Manager",
            ForeColor = Color.White
        };

        _statusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(20, 70),
            Size = new Size(440, 40),
            Text = "Click Install to download and install the latest application files.",
            ForeColor = Color.Gainsboro
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(20, 120),
            Size = new Size(440, 22),
            Minimum = 0,
            Maximum = 100
        };

        _installButton = new Button
        {
            Text = "Install",
            Location = new Point(270, 155),
            Size = new Size(90, 28),
            UseVisualStyleBackColor = true
        };
        _installButton.Click += async (_, _) => await RunInstallAsync();

        _closeButton = new Button
        {
            Text = "Close",
            Location = new Point(370, 155),
            Size = new Size(90, 28),
            UseVisualStyleBackColor = true
        };
        _closeButton.Click += (_, _) => Close();

        Controls.Add(_titleLabel);
        Controls.Add(_statusLabel);
        Controls.Add(_progressBar);
        Controls.Add(_installButton);
        Controls.Add(_closeButton);

        Shown += async (_, _) => await RunInstallAsync();
    }

    private static Version InstallerVersion { get; } = ReadVersion();

    private async Task RunInstallAsync()
    {
        if (_busy)
            return;

        _busy = true;
        _installButton.Enabled = false;
        _closeButton.Enabled = false;

        try
        {
            var progress = new Progress<InstallProgress>(p =>
            {
                _statusLabel.Text = p.Status;
                _progressBar.Value = Math.Clamp(p.Percent, 0, 100);
            });

            await Installer.InstallAsync(InstallerVersion, progress);
            _statusLabel.Text = "Installation complete. Launching app...";
            _progressBar.Value = 100;

            Process.Start(new ProcessStartInfo
            {
                FileName = ReleaseNaming.InstalledAppPath,
                WorkingDirectory = ReleaseNaming.InstallDirectory,
                UseShellExecute = true
            });

            Close();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Installation failed.";
            MessageBox.Show(this, ex.Message, "Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _installButton.Enabled = true;
            _closeButton.Enabled = true;
            _busy = false;
        }
    }

    private static Version ReadVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)
            && Version.TryParse(informational.Split('+', StringSplitOptions.TrimEntries)[0], out var parsed))
        {
            return parsed;
        }

        return assembly.GetName().Version ?? new Version(1, 0, 0, 0);
    }
}

internal readonly record struct InstallProgress(string Status, int Percent);

internal static class Installer
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task InstallAsync(Version installerVersion, IProgress<InstallProgress> progress, CancellationToken cancellationToken = default)
    {
        progress.Report(new InstallProgress("Looking up release package...", 5));

        var package = await ResolveAppPackageAsync(installerVersion, cancellationToken).ConfigureAwait(false);
        var workRoot = Path.Combine(Path.GetTempPath(), "StreamDeckIconManager-Setup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);
        var zipPath = Path.Combine(workRoot, package.FileName);
        var extractPath = Path.Combine(workRoot, "app");

        try
        {
            progress.Report(new InstallProgress("Downloading application files...", 10));
            await DownloadAsync(package.DownloadUrl, zipPath, progress, cancellationToken).ConfigureAwait(false);

            progress.Report(new InstallProgress("Extracting files...", 80));
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);
            ZipFile.ExtractToDirectory(zipPath, extractPath);

            var sourceRoot = FindAppRoot(extractPath)
                ?? throw new DirectoryNotFoundException("Downloaded package did not contain the application files.");

            progress.Report(new InstallProgress("Installing...", 90));
            CopyAppFiles(sourceRoot, ReleaseNaming.InstallDirectory);
            CreateShortcuts();

            progress.Report(new InstallProgress("Done.", 100));
        }
        finally
        {
            try { Directory.Delete(workRoot, true); } catch { /* ignore */ }
        }
    }

    private static async Task<AppPackageInfo> ResolveAppPackageAsync(Version installerVersion, CancellationToken cancellationToken)
    {
        // Prefer the release that matches this setup's version; fall back to latest.
        GitHubRelease? release = await TryGetReleaseAsync(ReleaseNaming.ReleaseByTagApiUri(installerVersion.ToString()), cancellationToken)
            .ConfigureAwait(false);

        release ??= await TryGetReleaseAsync(ReleaseNaming.LatestReleaseApiUri, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not find a GitHub release for Stream Deck Icon Manager.");

        var expectedName = ReleaseNaming.AppPackageFileName(installerVersion);
        var asset = release.Assets.FirstOrDefault(a =>
                a.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            ?? release.Assets.FirstOrDefault(a => ReleaseNaming.IsAppPackageAssetName(a.Name))
            ?? throw new InvalidOperationException(
                $"Release {release.TagName} does not include an application package ({expectedName}).");

        if (!IsTrustedDownloadUrl(asset.BrowserDownloadUrl))
            throw new InvalidOperationException("Application package URL is not from the expected GitHub repository.");

        return new AppPackageInfo(asset.Name, asset.BrowserDownloadUrl);
    }

    private static async Task<GitHubRelease?> TryGetReleaseAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<InstallProgress> progress,
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
                var percent = 10 + (int)Math.Clamp(readTotal * 65L / total.Value, 0, 65);
                progress.Report(new InstallProgress($"Downloading application files... {percent}%", percent));
            }
        }
    }

    private static string? FindAppRoot(string extractPath)
    {
        var direct = Path.Combine(extractPath, ReleaseNaming.AppExeFileName);
        if (File.Exists(direct))
            return extractPath;

        return Directory.EnumerateFiles(extractPath, ReleaseNaming.AppExeFileName, SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault(d => !string.IsNullOrEmpty(d));
    }

    private static void CopyAppFiles(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);

        foreach (var file in Directory.EnumerateFiles(destinationRoot, "*", SearchOption.AllDirectories))
        {
            try { File.Delete(file); } catch { /* file may be locked; overwrite next */ }
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, sourceFile);
            var destFile = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(sourceFile, destFile, overwrite: true);
        }

        if (!File.Exists(Path.Combine(destinationRoot, ReleaseNaming.AppExeFileName)))
            throw new FileNotFoundException("Installed files are missing the application executable.");
    }

    private static void CreateShortcuts()
    {
        var startMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            "Stream Deck Icon Manager.lnk");
        var desktop = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Stream Deck Icon Manager.lnk");

        CreateShortcut(startMenu);
        CreateShortcut(desktop);
    }

    private static void CreateShortcut(string shortcutPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Could not create shortcuts (WScript.Shell unavailable).");
        dynamic shell = Activator.CreateInstance(shellType)!;
        var shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = ReleaseNaming.InstalledAppPath;
        shortcut.WorkingDirectory = ReleaseNaming.InstallDirectory;
        shortcut.Description = "Stream Deck Icon Manager";
        shortcut.Save();
    }

    private static bool IsTrustedDownloadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;

        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StreamDeckIconManagerSetup", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private sealed record AppPackageInfo(string FileName, string DownloadUrl);

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
