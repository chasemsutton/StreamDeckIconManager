namespace Add_Stream_Deck_Icon.UI;

/// <summary>
/// Shown after saving. Stream Deck only reads icon packs at startup, so this offers to restart it.
/// </summary>
internal sealed class RestartDialog : ThemedDialog
{
    private readonly ThemedButton _restart;
    private readonly ThemedButton _later;
    private readonly Label _error;
    private readonly bool _running;

    public RestartDialog(int packCount)
        : base("Saved to Stream Deck", BuildSubtitle(packCount, StreamDeckApp.IsRunning), 400)
    {
        _running = StreamDeckApp.IsRunning;

        _error = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            ForeColor = Theme.Danger,
            Font = Theme.Small,
            Margin = new Padding(0, 0, 0, 8),
            Visible = false
        };
        AddBody(_error);

        _later = new ThemedButton(_running ? "Restart later" : "Not now", ButtonKind.Ghost) { DialogResult = DialogResult.Cancel };
        _restart = new ThemedButton(_running ? "Restart Stream Deck" : "Start Stream Deck", ButtonKind.Primary, Theme.Glyphs.Sync);
        _restart.Click += async (_, _) => await RestartAsync();

        FooterRight.Controls.Add(_later);
        FooterRight.Controls.Add(_restart);
        AcceptButton = _restart;
        CancelButton = _later;

        Complete();
        Shown += (_, _) => _restart.Focus();
    }

    /// <summary>True once Stream Deck has been restarted or started from this dialog.</summary>
    public bool Restarted { get; private set; }

    private static string BuildSubtitle(int packCount, bool running)
    {
        var what = packCount == 1 ? "Your icon pack is saved." : $"Your {packCount} icon packs are saved.";
        return running
            ? $"{what} Stream Deck loads icon packs when it starts, so restart it to see your changes in the icon library."
            : $"{what} Stream Deck isn't running. Your changes will show up the next time it starts.";
    }

    private async Task RestartAsync()
    {
        _restart.Enabled = false;
        _later.Enabled = false;
        _restart.Text = _running ? "Restarting…" : "Starting…";
        _error.Visible = false;

        try
        {
            await StreamDeckApp.RestartAsync();
            Restarted = true;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _error.Text = ex is InvalidOperationException ? ex.Message : "Couldn't restart Stream Deck: " + ex.Message;
            _error.Visible = true;
            _restart.Text = "Try again";
            _restart.Enabled = true;
            _later.Enabled = true;
            _later.Text = "Close";
        }
    }
}
