using System.Diagnostics;

namespace Add_Stream_Deck_Icon.UI;

/// <summary>
/// "A new version is available": the one decision in the update flow. Download and install it
/// (the app then downloads, closes and hands over to the installer without asking again), or not
/// now. Links to the release notes when there are some.
/// </summary>
internal sealed class UpdateAvailableDialog : ThemedDialog
{
    public UpdateAvailableDialog(Version newVersion, Version currentVersion, bool isBeta, string? releasePageUrl, bool hasUnsavedChanges)
        : base(isBeta ? "Beta update available" : "Update available",
            $"{(isBeta ? "Beta version" : "Version")} {newVersion} is available. You have {currentVersion}. " +
            "Installing closes Stream Deck Icon Manager; the installer then updates it and opens it again.",
            400)
    {
        if (isBeta)
            AddBody(Note("This is a beta release, so it may have bugs. You can switch back to the Stable channel at any time.", Theme.TextMuted));

        if (hasUnsavedChanges)
            AddBody(Note("You have unsaved changes. They'll be lost if you install now. Choose Not now, then use Save to Stream Deck first to keep them.", Theme.Warning));

        if (IsReleasePage(releasePageUrl))
        {
            var notes = new ThemedButton("What's new", ButtonKind.Ghost);
            notes.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(releasePageUrl!) { UseShellExecute = true });
                }
                catch
                {
                    // No browser available; nothing else to do.
                }
            };
            FooterLeft.Controls.Add(notes);
        }

        var later = new ThemedButton("Not now", ButtonKind.Ghost) { DialogResult = DialogResult.Cancel };
        var install = new ThemedButton("Download and install", ButtonKind.Primary, Theme.Glyphs.Import) { DialogResult = DialogResult.OK };
        FooterRight.Controls.Add(later);
        FooterRight.Controls.Add(install);
        AcceptButton = install;
        CancelButton = later;

        Complete();
        // With unsaved changes, don't make Enter the easy way to lose them.
        Shown += (_, _) => (hasUnsavedChanges ? later : install).Focus();
    }

    private static Label Note(string text, Color color) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(400, 0),
        Font = Theme.Body,
        ForeColor = color,
        Margin = new Padding(0, 0, 0, 8)
    };

    /// <summary>Only open links to this app's GitHub releases.</summary>
    private static bool IsReleasePage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith($"/{StreamDeckIconManager.ReleaseNaming.Owner}/{StreamDeckIconManager.ReleaseNaming.Repo}/", StringComparison.OrdinalIgnoreCase);
}
