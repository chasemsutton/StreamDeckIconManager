using Add_Stream_Deck_Icon.UI;

namespace Add_Stream_Deck_Icon;

/// <summary>Download progress for an update, with Cancel.</summary>
internal sealed class UpdateProgressForm : Form
{
    private readonly Label _statusLabel;
    private readonly ProgressLine _progressBar;
    private readonly ThemedButton _cancelButton;
    private readonly CancellationTokenSource _cancel = new();

    public UpdateProgressForm(Version version)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Updating Stream Deck Icon Manager";
        Font = Theme.Body;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 180);
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        ShowInTaskbar = false;

        var title = new Label
        {
            AutoSize = true,
            Location = new Point(24, 22),
            Text = $"Downloading version {version}",
            Font = Theme.DialogTitle,
            ForeColor = Theme.Text
        };

        _statusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(24, 58),
            Size = new Size(392, 22),
            Text = "Starting...",
            Font = Theme.Body,
            ForeColor = Theme.TextMuted
        };

        _progressBar = new ProgressLine
        {
            Location = new Point(24, 88),
            Size = new Size(392, 6)
        };

        _cancelButton = new ThemedButton("Cancel", ButtonKind.Ghost) { Margin = Padding.Empty };
        _cancelButton.Click += (_, _) => Cancel();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new Padding(0, 14, 0, 0),
            BackColor = Theme.Sidebar
        };
        buttons.Controls.Add(_cancelButton);

        var footer = new Surface(Theme.Sidebar, Edges.Top) { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(20, 1, 20, 0) };
        footer.Controls.Add(buttons);

        Controls.Add(title);
        Controls.Add(_statusLabel);
        Controls.Add(_progressBar);
        Controls.Add(footer);
        CancelButton = _cancelButton;
        ResumeLayout(false);
    }

    /// <summary>Cancelled when the user clicks Cancel or closes the window.</summary>
    public CancellationToken Token => _cancel.Token;

    private void Cancel()
    {
        if (_cancel.IsCancellationRequested)
            return;
        _cancel.Cancel();
        _cancelButton.Enabled = false;
        _statusLabel.Text = "Cancelling...";
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.UseDarkTitleBar(this);
        Native.SetCaptionColors(this, Theme.Window, Theme.Text);
    }

    private bool _finished;

    /// <summary>Closes the window once the download has finished or stopped (not a cancel).</summary>
    public void Finish()
    {
        _finished = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The user closing the window cancels the download; the caller then calls Finish().
        if (!_finished && e.CloseReason == CloseReason.UserClosing && !_cancel.IsCancellationRequested)
        {
            e.Cancel = true;
            Cancel();
        }

        base.OnFormClosing(e);
    }

    public void Report(UpdateProgress progress)
    {
        if (IsDisposed || _cancel.IsCancellationRequested)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(() => Report(progress));
            return;
        }

        _statusLabel.Text = progress.Status;
        _progressBar.Value = Math.Clamp(progress.PercentComplete, 0, 100);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _cancel.Dispose();
        base.Dispose(disposing);
    }
}
