using System.Diagnostics;

namespace StreamDeckIconManager.Mac;

/// <summary>
/// Locates and runs Google's gif2webp / img2webp, shipped in tools/gif2webp next to the app.
/// </summary>
internal static class WebpTools
{
    private static string ToolFolder => Path.Combine(AppContext.BaseDirectory, "tools", "gif2webp");

    public static string PathOf(string tool) =>
        Path.Combine(ToolFolder, OperatingSystem.IsWindows() ? tool + ".exe" : tool);

    public static bool IsAvailable(string tool)
    {
        var path = PathOf(tool);
        if (!File.Exists(path))
            return false;

        if (!OperatingSystem.IsWindows())
            EnsureExecutable(path);
        return true;
    }

    /// <summary>
    /// Zip tools can drop the "executable" permission; put it back so the tool can run.
    /// </summary>
    private static void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            var mode = File.GetUnixFileMode(path);
            const UnixFileMode exec = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((mode & UnixFileMode.UserExecute) == 0)
                File.SetUnixFileMode(path, mode | exec);
        }
        catch
        {
            // Read-only app location; running it will fail and the caller keeps the original.
        }
    }

    /// <summary>
    /// Runs a tool and waits up to two minutes. Returns true if it exited with code 0.
    /// </summary>
    public static async Task<bool> RunAsync(string tool, IEnumerable<string> arguments, string workingDirectory)
    {
        var start = new ProcessStartInfo
        {
            FileName = PathOf(tool),
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start);
        if (process is null)
            return false;

        // Drain output so a chatty tool can't block on a full pipe.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { /* already gone */ }
            return false;
        }

        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        return process.ExitCode == 0;
    }
}
