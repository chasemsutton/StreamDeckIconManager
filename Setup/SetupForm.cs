using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Add_Stream_Deck_Icon.UI;
using StreamDeckIconManager;

namespace StreamDeckIconManager.Setup;

/// <summary>Slim rounded progress bar in the app's accent colour.</summary>
internal sealed class ProgressLine : Control
{
    private int _value;

    public ProgressLine()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 100); Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Window);
        Theme.HighQuality(g);
        var radius = Height / 2f;
        using (var track = Theme.RoundedRect(new RectangleF(0, 0, Width, Height), radius))
        using (var trackBrush = new SolidBrush(Theme.Raised))
            g.FillPath(trackBrush, track);

        if (_value <= 0)
            return;

        var width = Math.Max(Height, Width * _value / 100f);
        using var fill = Theme.RoundedRect(new RectangleF(0, 0, width, Height), radius);
        using var fillBrush = new SolidBrush(Theme.Accent);
        g.FillPath(fillBrush, fill);
    }
}

/// <summary>Button strip along the bottom, with a hairline on top.</summary>
internal sealed class FooterPanel : Panel
{
    public FooterPanel()
    {
        BackColor = Theme.Sidebar;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, 0, 0, Width, 0);
    }
}

/// <summary>
/// Setup window, styled like the app. While it works there is no Install button and Close is
/// greyed out; after a failure or cancel, "Try again" appears.
/// </summary>
internal sealed class SetupForm : Form
{
    private readonly Label _statusLabel;
    private readonly ProgressLine _progressBar;
    private readonly ThemedButton _installButton;
    private readonly ThemedButton _closeButton;
    private readonly int? _waitPid;
    private bool _busy;

    public SetupForm(int? waitPid = null)
    {
        _waitPid = waitPid;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Stream Deck Icon Manager Setup";
        Font = Theme.Body;
        Icon = LoadIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 250);
        BackColor = Theme.Window;
        ForeColor = Theme.Text;

        var logo = new PictureBox
        {
            Location = new Point(24, 24),
            Size = new Size(48, 48),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = Icon is null ? null : new Icon(Icon, 256, 256).ToBitmap()
        };

        var title = new Label
        {
            Text = "Stream Deck Icon Manager",
            Font = Theme.DialogTitle,
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(84, 24)
        };

        var version = new Label
        {
            Text = $"Version {InstallerVersion}",
            Font = Theme.Small,
            ForeColor = Theme.TextFaint,
            AutoSize = true,
            Location = new Point(86, 54)
        };

        _statusLabel = new Label
        {
            Text = "Getting ready...",
            Font = Theme.Body,
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Location = new Point(24, 92),
            Size = new Size(412, 56)
        };

        _progressBar = new ProgressLine
        {
            Location = new Point(24, 156),
            Size = new Size(412, 6)
        };

        _closeButton = new ThemedButton("Close", ButtonKind.Ghost) { Margin = new Padding(0, 0, 8, 0) };
        _closeButton.Click += (_, _) => Close();

        _installButton = new ThemedButton("Try again", ButtonKind.Primary) { Margin = Padding.Empty, Visible = false };
        _installButton.Click += async (_, _) => await RunInstallAsync();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new Padding(0, 14, 0, 0),
            BackColor = Theme.Sidebar
        };
        buttons.Controls.Add(_closeButton);
        buttons.Controls.Add(_installButton);

        var footer = new FooterPanel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(20, 1, 20, 0) };
        footer.Controls.Add(buttons);

        Controls.Add(logo);
        Controls.Add(title);
        Controls.Add(version);
        Controls.Add(_statusLabel);
        Controls.Add(_progressBar);
        Controls.Add(footer);
        AcceptButton = _installButton;

        ResumeLayout(false);
        PerformLayout();

        Shown += async (_, _) => await RunInstallAsync();
    }

    private static Version InstallerVersion { get; } = ReadVersion();

    private static Icon? LoadIcon()
    {
        using var stream = typeof(SetupForm).Assembly.GetManifestResourceStream("AppIcon.ico");
        return stream is null ? null : new Icon(stream);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.UseDarkTitleBar(this);
        Native.SetCaptionColors(this, Theme.Window, Theme.Text);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing mid-install could leave the swap half done; Close is disabled then anyway.
        if (_busy && e.CloseReason == CloseReason.UserClosing)
            e.Cancel = true;
        base.OnFormClosing(e);
    }

    /// <summary>Working: no Install button, Close visibly disabled.</summary>
    private void ShowWorking()
    {
        _busy = true;
        _installButton.Visible = false;
        _closeButton.Enabled = false;
        _statusLabel.ForeColor = Theme.TextMuted;
    }

    /// <summary>Stopped after a failure or cancel: offer Try again.</summary>
    private void ShowStopped(string message, bool isError)
    {
        _busy = false;
        _statusLabel.Text = message;
        _statusLabel.ForeColor = isError ? Theme.Danger : Theme.TextMuted;
        _installButton.Visible = true;
        _closeButton.Enabled = true;
        _installButton.Focus();
    }

    private async Task RunInstallAsync()
    {
        if (_busy)
            return;

        ShowWorking();
        _progressBar.Value = 0;

        try
        {
            var progress = new Progress<InstallProgress>(p =>
            {
                _statusLabel.Text = p.Status;
                _progressBar.Value = Math.Clamp(p.Percent, 0, 100);
            });

            // Download and unpack first. This changes nothing on disk outside a temp folder,
            // and gives an app that launched us time to exit.
            var package = await Installer.PrepareAsync(InstallerVersion, progress);
            try
            {
                if (!await EnsureAppClosedAsync())
                {
                    _progressBar.Value = 0;
                    ShowStopped("Update cancelled. Your current version hasn't been changed.", isError: false);
                    return;
                }

                _statusLabel.Text = "Installing...";
                _progressBar.Value = 90;
                await Task.Run(() => Installer.Apply(package));
            }
            finally
            {
                package.Dispose();
            }

            _statusLabel.Text = "Installed. Opening Stream Deck Icon Manager...";
            _progressBar.Value = 100;

            Process.Start(new ProcessStartInfo
            {
                FileName = ReleaseNaming.InstalledAppPath,
                WorkingDirectory = ReleaseNaming.InstallDirectory,
                UseShellExecute = true
            });

            _busy = false;
            Close();
        }
        catch (Exception ex)
        {
            ShowStopped(ex is HttpRequestException
                ? "Couldn't download the update. Check your internet connection, then click Try again."
                : ex.Message, isError: true);
        }
    }

    /// <summary>
    /// Waits for running copies of the app to exit. Version 1.1.0.1 starts Setup and only exits
    /// after the user dismisses a message, so this can take a moment. Returns false if the
    /// user cancels.
    /// </summary>
    private async Task<bool> EnsureAppClosedAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        var extendedForDismiss = false;
        while (true)
        {
            var running = AppProcesses.Find(_waitPid);
            if (running.Count == 0)
                return true;

            // Versions before 1.2.0.1 start Setup, then sit on an "Update Ready" message and only
            // exit once it's dismissed. Press its OK button for the user; the app then closes itself.
            var dismissed = running.Count(AppProcesses.DismissUpdateReadyMessage);
            if (dismissed > 0 && !extendedForDismiss)
            {
                // Give the old version time to shut down, but only once: if dismissing didn't
                // work, fall through to asking the user rather than waiting forever.
                extendedForDismiss = true;
                deadline = DateTime.UtcNow.AddSeconds(15);
            }

            if (DateTime.UtcNow < deadline)
            {
                _statusLabel.Text = dismissed > 0
                    ? "Closing the previous version..."
                    : "Waiting for Stream Deck Icon Manager to close...";
                AppProcesses.DisposeAll(running);
                await Task.Delay(500);
                continue;
            }

            var closeIt = new TaskDialogButton("Close it and continue");
            var retry = new TaskDialogButton("Try again");
            var choice = TaskDialog.ShowDialog(this, new TaskDialogPage
            {
                Caption = "Stream Deck Icon Manager Setup",
                Heading = "Stream Deck Icon Manager is still open",
                Text = "Close it to finish the update. If you let Setup close it, any changes you haven't saved in it will be lost.",
                Icon = TaskDialogIcon.Warning,
                Buttons = { closeIt, retry, TaskDialogButton.Cancel },
                DefaultButton = retry
            });

            if (choice == TaskDialogButton.Cancel)
            {
                AppProcesses.DisposeAll(running);
                return false;
            }

            if (choice == closeIt)
            {
                _statusLabel.Text = "Closing Stream Deck Icon Manager...";
                await Task.Run(() => AppProcesses.Close(running));
            }

            AppProcesses.DisposeAll(running);
            deadline = DateTime.UtcNow.AddSeconds(5);
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

/// <summary>Downloaded, unpacked app files waiting to be installed. Disposing deletes them.</summary>
internal sealed class PreparedPackage : IDisposable
{
    private readonly string _workRoot;

    public PreparedPackage(string workRoot) => _workRoot = workRoot;

    public string SourceRoot { get; set; } = "";

    public void Dispose()
    {
        try { Directory.Delete(_workRoot, true); } catch { /* temp cleanup is best-effort */ }
    }
}

/// <summary>Finds and closes running copies of the installed app.</summary>
internal static class AppProcesses
{
    public static List<Process> Find(int? waitPid)
    {
        var found = new List<Process>();
        var installDir = Path.GetFullPath(ReleaseNaming.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var appName = Path.GetFileNameWithoutExtension(ReleaseNaming.AppExeFileName);

        foreach (var process in Process.GetProcessesByName(appName))
        {
            if (process.Id == waitPid || IsUnder(process, installDir))
                found.Add(process);
            else
                process.Dispose();
        }

        if (waitPid is { } pid && found.All(p => p.Id != pid))
        {
            try
            {
                var waited = Process.GetProcessById(pid);
                if (!waited.HasExited)
                    found.Add(waited);
                else
                    waited.Dispose();
            }
            catch (ArgumentException)
            {
                // Already exited.
            }
        }

        return found;
    }

    // Closing a message box that only has an OK button is the same as pressing OK. (Posting the
    // OK command doesn't work: Windows gives that lone button the Cancel ID.)
    private const uint WmClose = 0x0010;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// If the process is showing the "Update Ready" message that older versions display after
    /// starting Setup, dismisses it as if OK were pressed. Returns true if one was found.
    /// </summary>
    public static bool DismissUpdateReadyMessage(Process process)
    {
        var found = false;
        try
        {
            var pid = (uint)process.Id;
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var owner);
                if (owner != pid || !IsWindowVisible(hwnd))
                    return true;

                var cls = new System.Text.StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                if (cls.ToString() != "#32770") // standard dialog / message box
                    return true;

                var title = new System.Text.StringBuilder(128);
                GetWindowText(hwnd, title, title.Capacity);
                if (!title.ToString().Equals("Update Ready", StringComparison.OrdinalIgnoreCase))
                    return true;

                PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
                found = true;
                return false;
            }, IntPtr.Zero);
        }
        catch
        {
            // Process exited or access denied; the normal wait handles it.
        }

        return found;
    }

    public static void Close(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            try
            {
                if (process.HasExited)
                    continue;

                // A polite close first; it won't work while the app shows a modal message.
                process.CloseMainWindow();
                if (!process.WaitForExit(3000))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch
            {
                // Exited in the meantime, or access denied; the next check will report it.
            }
        }
    }

    public static void DisposeAll(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
            process.Dispose();
    }

    private static bool IsUnder(Process process, string directory)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return path is not null && path.StartsWith(directory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

internal static class Installer
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Downloads and unpacks the app into a temp folder. Touches nothing in the install folder.
    /// </summary>
    public static async Task<PreparedPackage> PrepareAsync(Version installerVersion, IProgress<InstallProgress> progress, CancellationToken cancellationToken = default)
    {
        progress.Report(new InstallProgress("Looking up release package...", 5));

        var package = await ResolveAppPackageAsync(installerVersion, cancellationToken).ConfigureAwait(false);
        var workRoot = Path.Combine(Path.GetTempPath(), "StreamDeckIconManager-Setup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);
        var prepared = new PreparedPackage(workRoot);

        try
        {
            var zipPath = Path.Combine(workRoot, package.FileName);
            var extractPath = Path.Combine(workRoot, "app");

            progress.Report(new InstallProgress("Downloading application files...", 10));
            await DownloadAsync(package.DownloadUrl, zipPath, progress, cancellationToken).ConfigureAwait(false);

            progress.Report(new InstallProgress("Extracting files...", 80));
            ZipFile.ExtractToDirectory(zipPath, extractPath);

            prepared.SourceRoot = FindAppRoot(extractPath)
                ?? throw new DirectoryNotFoundException("Downloaded package did not contain the application files.");
            return prepared;
        }
        catch
        {
            prepared.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Replaces the installed app with the prepared files. The new files are staged next to the
    /// install folder and swapped in with folder renames, so a failure leaves the old version
    /// working instead of half deleted.
    /// </summary>
    public static void Apply(PreparedPackage package)
    {
        var installDir = Path.GetFullPath(ReleaseNaming.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var staging = installDir + ".new";
        var backup = installDir + ".old";

        DeleteDirectory(staging);
        CopyDirectory(package.SourceRoot, staging);
        if (!File.Exists(Path.Combine(staging, ReleaseNaming.AppExeFileName)))
            throw new FileNotFoundException("The downloaded files are missing the application executable.");

        if (Directory.Exists(backup) && !DeleteDirectory(backup))
            backup += "-" + DateTime.Now.ToString("yyyyMMddHHmmss");

        var hadOldVersion = Directory.Exists(installDir);
        if (hadOldVersion && !TryMoveDirectory(installDir, backup))
        {
            DeleteDirectory(staging);
            throw new IOException(
                "Some of Stream Deck Icon Manager's files are still in use, so the update couldn't replace them. " +
                "Your current version hasn't been changed. Close Stream Deck Icon Manager, then click Try again.");
        }

        if (!TryMoveDirectory(staging, installDir))
        {
            if (hadOldVersion)
                TryMoveDirectory(backup, installDir);
            DeleteDirectory(staging);
            throw new IOException("Couldn't move the new files into place. Your current version hasn't been changed. Click Try again.");
        }

        DeleteDirectory(backup);
        CreateShortcuts();
    }

    private static bool TryMoveDirectory(string from, string to)
    {
        // Antivirus scanners and the just-closed app can hold files for a moment, so retry briefly.
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            Thread.Sleep(500);
        }

        return false;
    }

    private static bool DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var destFile = Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, sourceFile));
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(sourceFile, destFile, overwrite: true);
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

    private static void CreateShortcuts()
    {
        var testRun = Environment.GetEnvironmentVariable("SDIM_INSTALL_DIR") is { Length: > 0 };
        // A test install (SDIM_INSTALL_DIR) must not repoint the real shortcuts at the test folder.
        var testShortcuts = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ReleaseNaming.InstallDirectory))!, "test-shortcuts");
        var startMenu = Path.Combine(
            testRun ? testShortcuts : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            "Stream Deck Icon Manager.lnk");
        var desktop = Path.Combine(
            testRun ? Path.Combine(testShortcuts, "Desktop") : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Stream Deck Icon Manager.lnk");

        CreateShortcut(startMenu);
        CreateShortcut(desktop);

        // Updates replace the .exe at the same path, so Explorer would keep showing the cached
        // icon from the old version. Tell the shell these files changed so it redraws them.
        RefreshShellIcons(ReleaseNaming.InstalledAppPath, startMenu, desktop);
    }

    private const int ShcneUpdateItem = 0x00002000;
    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfPath = 0x0005;
    private const uint ShcnfIdList = 0x0000;
    private const uint ShcnfFlush = 0x1000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string? item1, IntPtr item2);

    [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")]
    private static extern void SHChangeNotifyGlobal(int eventId, uint flags, IntPtr item1, IntPtr item2);

    private static void RefreshShellIcons(params string[] paths)
    {
        try
        {
            foreach (var path in paths)
                SHChangeNotify(ShcneUpdateItem, ShcnfPath | ShcnfFlush, path, IntPtr.Zero);

            // Makes Explorer re-read icons it has cached, including taskbar pins.
            SHChangeNotifyGlobal(ShcneAssocChanged, ShcnfIdList | ShcnfFlush, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Cosmetic only; the new icon still shows once Windows refreshes its cache.
        }
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
        shortcut.IconLocation = ReleaseNaming.InstalledAppPath + ",0";
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
