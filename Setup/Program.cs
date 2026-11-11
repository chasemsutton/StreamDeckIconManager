namespace StreamDeckIconManager.Setup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm(ReadWaitPid(args)));
    }

    /// <summary>
    /// The app passes "--wait-pid N" (1.2.0.1 and later) so Setup waits for it to exit.
    /// Older versions pass nothing; Setup then looks for running copies by install path.
    /// </summary>
    private static int? ReadWaitPid(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--wait-pid", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out var pid))
                return pid;
        }

        return null;
    }
}
