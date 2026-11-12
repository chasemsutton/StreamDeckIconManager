using System.Diagnostics;
using System.Text.RegularExpressions;

namespace StreamDeckIconManager.Mac;

internal enum LiveInstallResult
{
    Installed,
    Skipped,
    TimedOut,
    Failed
}

/// <summary>
/// Finds, quits and opens the Elgato Stream Deck app on macOS so new icon packs load. Uses the
/// app's bundle ID (com.elgato.StreamDeck), so it works wherever the app is installed.
/// </summary>
internal static class StreamDeckApp
{
    public const string BundleId = "com.elgato.StreamDeck";

    /// <summary>On Windows (for testing this build only), the Stream Deck process name.</summary>
    private const string WindowsProcessName = "StreamDeck";

    public static bool IsRunning => OperatingSystem.IsMacOS()
        ? RunningPid() is not null
        : Process.GetProcessesByName(WindowsProcessName).Length > 0;

    /// <summary>Stream Deck's process ID if it's running, from macOS's app list.</summary>
    private static int? RunningPid()
    {
        var output = Run("lsappinfo", new[] { "info", "-only", "pid", "-app", BundleId }, TimeSpan.FromSeconds(5));
        var match = output is null ? null : Regex.Match(output, "\"pid\"\\s*=\\s*(\\d+)");
        if (match is { Success: true } && int.TryParse(match.Groups[1].Value, out var pid) && pid > 0)
            return pid;

        // Fallback if lsappinfo isn't available or answers differently.
        var running = Run("osascript", new[] { "-e", $"application id \"{BundleId}\" is running" }, TimeSpan.FromSeconds(5));
        if (running?.Trim() != "true")
            return null;

        // Running, but the ID is unknown (-1): it can still be quit, just not stopped by force.
        var pgrep = Run("pgrep", new[] { "-f", "Stream Deck.app/Contents/MacOS/" }, TimeSpan.FromSeconds(5));
        var found = pgrep?.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var p) ? p : 0)
            .FirstOrDefault(p => p > 0) ?? 0;
        return found > 0 ? found : -1;
    }

    /// <summary>Quits Stream Deck if it's running, then opens it again.</summary>
    /// <exception cref="InvalidOperationException">Stream Deck could not be found or did not quit.</exception>
    public static async Task RestartAsync()
    {
        if (!OperatingSystem.IsMacOS())
            throw new InvalidOperationException("Restarting Stream Deck is only supported on macOS in this build.");

        await Task.Run(() =>
        {
            if (RunningPid() is null)
                return;

            // Ask nicely first, as if you'd chosen Quit. macOS may ask once whether this app may
            // control Stream Deck; if that's declined, fall back to stopping the process.
            Run("osascript", new[] { "-e", $"tell application id \"{BundleId}\" to quit" }, TimeSpan.FromSeconds(10));
            if (!WaitUntilStopped(TimeSpan.FromSeconds(10)))
            {
                if (RunningPid() is { } pid and > 0)
                    Stop(pid);

                if (!WaitUntilStopped(TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("Stream Deck didn't quit in time. Quit it from the menu bar, then open it again.");
            }

            // Give Stream Deck a moment to release the USB device before it starts again.
            Thread.Sleep(1500);
        }).ConfigureAwait(false);

        Open();
    }

    /// <summary>Opens Stream Deck (or brings it forward if it's already running).</summary>
    public static void Open()
    {
        var result = RunWithExitCode("open", new[] { "-g", "-b", BundleId }, TimeSpan.FromSeconds(15));
        if (result != 0)
            throw new InvalidOperationException("Couldn't find the Elgato Stream Deck app. Open Stream Deck yourself to load your changes.");
    }

    private static bool WaitUntilStopped(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (RunningPid() is null)
                return true;
            Thread.Sleep(400);
        }

        return RunningPid() is null;
    }

    /// <summary>Stops Stream Deck and its plugins, like the Windows app does.</summary>
    private static void Stop(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone, or not ours to stop; the caller checks whether it stopped.
        }
    }

    /// <summary>
    /// Opens a .streamDeckIconPack in Stream Deck so it installs live, then waits for the outcome.
    /// Stream Deck asks the user to confirm first. The pack counts as installed once its
    /// manifest.json on disk carries <paramref name="expectedVersion"/>. macOS doesn't let this
    /// app see Stream Deck's confirmation window, so a "Cancel" there is only noticed when the
    /// user presses Skip (or after the timeout).
    /// </summary>
    public static async Task<LiveInstallResult> InstallPackageAsync(
        string packagePath,
        string installedManifestPath,
        string expectedVersion,
        CancellationToken skip)
    {
        if (OperatingSystem.IsMacOS())
        {
            if (RunWithExitCode("open", new[] { "-b", BundleId, packagePath }, TimeSpan.FromSeconds(15)) != 0)
                return LiveInstallResult.Failed;
        }
        else
        {
            Process.Start(new ProcessStartInfo(packagePath) { UseShellExecute = true });
        }

        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (true)
        {
            if (InstalledVersion(installedManifestPath) == expectedVersion)
                return LiveInstallResult.Installed;

            if (skip.IsCancellationRequested)
                return LiveInstallResult.Skipped;

            if (DateTime.UtcNow > deadline)
                return LiveInstallResult.TimedOut;

            try
            {
                await Task.Delay(300, skip).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // Loop once more so an install that just finished still counts.
            }
        }
    }

    private static string? InstalledVersion(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
                return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
            return doc.RootElement.TryGetProperty("Version", out var v) ? v.GetString() : null;
        }
        catch
        {
            // Mid-write; try again on the next poll.
            return null;
        }
    }

    /// <summary>Runs a command-line tool and returns what it printed, or null if it couldn't run.</summary>
    private static string? Run(string file, IEnumerable<string> args, TimeSpan timeout)
    {
        try
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

            using var process = Process.Start(start);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch { /* gone */ }
                return null;
            }

            return output.Result;
        }
        catch
        {
            return null;
        }
    }

    private static int RunWithExitCode(string file, IEnumerable<string> args, TimeSpan timeout)
    {
        try
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

            using var process = Process.Start(start);
            if (process is null)
                return -1;

            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch { /* gone */ }
                return -1;
            }

            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}
