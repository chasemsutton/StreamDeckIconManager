using System.Diagnostics;

namespace Add_Stream_Deck_Icon;

internal enum LiveInstallResult
{
    Installed,
    Declined,
    Skipped,
    TimedOut
}

/// <summary>Finds, stops and starts the Elgato Stream Deck desktop app so new icon packs load.</summary>
internal static class StreamDeckApp
{
    private const string ProcessName = "StreamDeck";

    private static readonly string[] KnownPaths =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Elgato", "StreamDeck", "StreamDeck.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Elgato", "StreamDeck", "StreamDeck.exe")
    };

    public static bool IsRunning
    {
        get
        {
            var processes = Process.GetProcessesByName(ProcessName);
            foreach (var p in processes)
                p.Dispose();
            return processes.Length > 0;
        }
    }

    /// <summary>Path to StreamDeck.exe, from the running process or the default install folders.</summary>
    public static string? FindExecutable()
    {
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        return path;
                }
                catch
                {
                    // Access to another process's modules can be denied; fall back to known paths.
                }
            }
        }

        return KnownPaths.FirstOrDefault(File.Exists);
    }

    /// <summary>Stops Stream Deck if it is running, then starts it again.</summary>
    /// <exception cref="InvalidOperationException">Stream Deck could not be found or did not stop.</exception>
    public static async Task RestartAsync()
    {
        var exe = FindExecutable()
                  ?? throw new InvalidOperationException("Couldn't find StreamDeck.exe. Start Stream Deck yourself to load your changes.");

        await Task.Run(() =>
        {
            foreach (var process in Process.GetProcessesByName(ProcessName))
            {
                using (process)
                {
                    Stop(process);
                    if (!process.WaitForExit(10_000))
                        throw new InvalidOperationException("Stream Deck didn't close in time. Quit it from the system tray, then start it again.");
                }
            }

            // Give Stream Deck a moment to release the USB device before it starts again.
            Thread.Sleep(1500);
        }).ConfigureAwait(false);

        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
    }

    /// <summary>
    /// Opens a .streamDeckIconPack so Stream Deck installs it live, then waits for the outcome.
    /// Stream Deck asks the user to confirm first. The pack counts as installed once its
    /// manifest.json on disk carries <paramref name="expectedVersion"/>.
    /// </summary>
    public static async Task<LiveInstallResult> InstallPackageAsync(
        string packagePath,
        string installedManifestPath,
        string expectedVersion,
        CancellationToken skip)
    {
        var before = StreamDeckWindows();
        Process.Start(new ProcessStartInfo(packagePath) { UseShellExecute = true });

        var promptSeen = false;
        DateTime? promptGoneAt = null;
        var deadline = DateTime.UtcNow.AddMinutes(5);

        while (true)
        {
            if (InstalledVersion(installedManifestPath) == expectedVersion)
                return LiveInstallResult.Installed;

            if (skip.IsCancellationRequested)
                return LiveInstallResult.Skipped;

            if (DateTime.UtcNow > deadline)
                return LiveInstallResult.TimedOut;

            // Stream Deck's confirmation shows up as a new window. If it opens and then closes
            // without the pack landing on disk, the user chose Cancel.
            var prompts = StreamDeckWindows().Except(before).ToList();
            if (prompts.Count > 0)
            {
                promptSeen = true;
                promptGoneAt = null;
            }
            else if (promptSeen)
            {
                promptGoneAt ??= DateTime.UtcNow;
                if (DateTime.UtcNow - promptGoneAt > TimeSpan.FromSeconds(3))
                    return LiveInstallResult.Declined;
            }

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

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    /// <summary>Visible top-level windows belonging to Stream Deck.</summary>
    private static HashSet<IntPtr> StreamDeckWindows()
    {
        var ids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            ids.Add((uint)p.Id);
            p.Dispose();
        }

        var windows = new HashSet<IntPtr>();
        if (ids.Count == 0)
            return windows;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (ids.Contains(pid) && IsWindowVisible(hwnd))
                windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    /// <summary>
    /// Stops Stream Deck and its plugins. Some plugins run as administrator (HWiNFO, for example)
    /// and can't be stopped from here; that's fine, they exit on their own once Stream Deck is gone.
    /// Only Stream Deck itself refusing to stop is an error.
    /// </summary>
    private static void Stop(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (AggregateException)
        {
            // Some child processes couldn't be stopped; handled below.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied on part of the tree; handled below.
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }

        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Exited in the meantime.
        }
    }
}
