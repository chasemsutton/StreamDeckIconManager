namespace Add_Stream_Deck_Icon.UI;

/// <summary>
/// Shown once an update has downloaded: install it now (the app closes and the installer takes
/// over) or cancel and keep using the current version.
/// </summary>
internal sealed class UpdateReadyDialog : ThemedDialog
{
    public UpdateReadyDialog(Version newVersion, bool isBeta, bool hasUnsavedChanges)
        : base("Update ready to install",
            $"{(isBeta ? "Beta version" : "Version")} {newVersion} is downloaded. Installing closes Stream Deck Icon Manager; " +
            "the installer then updates it and opens it again.",
            400)
    {
        if (hasUnsavedChanges)
        {
            AddBody(new Label
            {
                Text = "You have unsaved changes. They'll be lost if you install now. Cancel, then use Save to Stream Deck first to keep them.",
                AutoSize = true,
                MaximumSize = new Size(400, 0),
                Font = Theme.Body,
                ForeColor = Theme.Warning,
                Margin = new Padding(0, 0, 0, 8)
            });
        }

        var cancel = new ThemedButton("Cancel", ButtonKind.Ghost) { DialogResult = DialogResult.Cancel };
        var install = new ThemedButton("Install now", ButtonKind.Primary, Theme.Glyphs.Import) { DialogResult = DialogResult.OK };
        FooterRight.Controls.Add(cancel);
        FooterRight.Controls.Add(install);
        AcceptButton = install;
        CancelButton = cancel;

        Complete();
        // With unsaved changes, don't make Enter the easy way to lose them.
        Shown += (_, _) => (hasUnsavedChanges ? cancel : install).Focus();
    }
}
