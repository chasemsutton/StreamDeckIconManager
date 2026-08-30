namespace Add_Stream_Deck_Icon;

internal sealed class UpdateProgressForm : Form
{
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;

    public UpdateProgressForm()
    {
        Text = "Updating Stream Deck Icon Manager";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 110);
        BackColor = Color.FromArgb(45, 45, 45);
        ForeColor = Color.White;
        ShowInTaskbar = false;

        _statusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 18),
            Size = new Size(388, 23),
            Text = "Starting update...",
            ForeColor = Color.White
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(16, 55),
            Size = new Size(388, 23),
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous
        };

        Controls.Add(_statusLabel);
        Controls.Add(_progressBar);
    }

    public void Report(UpdateProgress progress)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(() => Report(progress));
            return;
        }

        _statusLabel.Text = progress.Status;
        _progressBar.Value = Math.Clamp(progress.PercentComplete, 0, 100);
    }
}
