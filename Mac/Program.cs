using Avalonia;

namespace StreamDeckIconManager.Mac;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // A beta: leave a trace somewhere findable if the app can't even start.
            CrashLog.Write(ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = true })
            .LogToTrace();
}

/// <summary>Writes unexpected errors to ~/Library/Logs/StreamDeckIconManager.log (or the temp folder elsewhere).</summary>
internal static class CrashLog
{
    public static string FilePath { get; } = OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "StreamDeckIconManager.log")
        : Path.Combine(Path.GetTempPath(), "StreamDeckIconManager.log");

    public static void Write(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {AppUpdateService.CurrentVersion}\n{ex}\n\n");
        }
        catch
        {
            // Nowhere to write; nothing else to do.
        }
    }
}
