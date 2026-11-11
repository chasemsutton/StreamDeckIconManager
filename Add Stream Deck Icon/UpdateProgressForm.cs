using Add_Stream_Deck_Icon.UI;

namespace Add_Stream_Deck_Icon;

internal sealed class UpdateProgressForm : Form
{
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;

    public UpdateProgressForm()
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
        ClientSize = new Size(440, 120);
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        ShowInTaskbar = false;

        _statusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(24, 24),
            Size = new Size(392, 24),
            Text = "Starting update…",
            Font = Theme.BodyStrong,
            ForeColor = Theme.Text
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(24, 62),
            Size = new Size(392, 10),
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous
        };

        Controls.Add(_statusLabel);
        Controls.Add(_progressBar);
        ResumeLayout(false);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.UseDarkTitleBar(this);
        Native.SetCaptionColors(this, Theme.Window, Theme.Text);
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
