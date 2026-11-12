using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;

namespace StreamDeckIconManager.Mac.UI;

/// <summary>
/// "A new version is available": the one decision in the update flow. Download and install it
/// (the app then downloads, quits, swaps itself and reopens without asking again), or not now.
/// When this copy can't replace itself (for example it's running from the Downloads folder), the
/// main button opens the download page instead.
/// </summary>
internal sealed class UpdateAvailableDialog : ThemedDialog
{
    public UpdateAvailableDialog(Version newVersion, Version currentVersion, bool isBeta, string? releasePageUrl,
        bool hasUnsavedChanges, bool canInstall)
        : base(isBeta ? "Beta update available" : "Update available",
            $"{(isBeta ? "Beta version" : "Version")} {newVersion} is available. You have {currentVersion}. " +
            (canInstall
                ? "Installing closes Stream Deck Icon Manager, updates it and opens it again."
                : "This copy can't update itself, so download the new version from the release page. " +
                  "Tip: keep the app in your Applications folder so future updates install automatically."),
            400)
    {
        if (isBeta)
            AddBody(Paragraph("This is a beta release, so it may have bugs. You can switch to the Stable channel at any time.", Palette.TextMuted));

        if (hasUnsavedChanges && canInstall)
            AddBody(Paragraph("You have unsaved changes. They'll be lost if you install now. Choose Not now, then use Save to Stream Deck first to keep them.",
                Palette.Warning));

        if (AppUpdateService.IsReleasePage(releasePageUrl))
        {
            var notes = new ThemedButton("What's new", ButtonKind.Ghost);
            notes.Click += (_, _) => OpenInBrowser(releasePageUrl!);
            FooterLeft.Children.Add(notes);
        }

        var later = new ThemedButton("Not now", ButtonKind.Ghost) { IsCancel = true };
        later.Click += (_, _) => Finish(false);
        var go = new ThemedButton(canInstall ? "Download and install" : "Open download page", ButtonKind.Primary, Glyphs.Import)
        {
            IsDefault = !hasUnsavedChanges
        };
        go.Click += (_, _) => Finish(true);
        FooterRight.Children.Add(later);
        FooterRight.Children.Add(go);

        // With unsaved changes, don't make Return the easy way to lose them.
        Opened += (_, _) => (hasUnsavedChanges ? later : go).Focus();
    }

    public static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // No browser; nothing else to do.
        }
    }
}

/// <summary>Download progress for an update, with Cancel.</summary>
internal sealed class UpdateProgressDialog : ThemedDialog
{
    private readonly TextBlock _status;
    private readonly ProgressBar _bar;
    private readonly CancellationTokenSource _cancel = new();

    public UpdateProgressDialog(Version version)
        : base($"Updating to {version}", "The app closes and reopens by itself when the new version is ready.", 400)
    {
        _status = Palette.Label("Starting download...", Palette.Body, Palette.Text, strong: true);
        _status.Margin = new Thickness(0, 0, 0, 12);
        _bar = new ProgressBar { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 0, 0, 8) };
        AddBody(_status, _bar);

        var cancel = new ThemedButton("Cancel", ButtonKind.Ghost) { IsCancel = true };
        cancel.Click += (_, _) => _cancel.Cancel();
        FooterRight.Children.Add(cancel);

        // Closing the window cancels too; the caller closes it when the download ends.
        Closing += (_, _) => _cancel.Cancel();
    }

    public CancellationToken Token => _cancel.Token;

    public void Report((string Status, int Percent) progress)
    {
        _status.Text = progress.Status;
        _bar.Value = progress.Percent;
    }
}
