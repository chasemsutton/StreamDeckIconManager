using System.Diagnostics;

namespace Add_Stream_Deck_Icon;

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
