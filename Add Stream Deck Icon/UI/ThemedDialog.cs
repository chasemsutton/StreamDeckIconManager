namespace Add_Stream_Deck_Icon.UI;

/// <summary>
/// Base for the app's dialogs: a heading, a body that subclasses fill, and a footer
/// with secondary actions on the left and the primary action on the right.
/// Subclasses add controls, then call <see cref="Complete"/>.
/// </summary>
internal abstract class ThemedDialog : Form
{
    private readonly Label _subtitle;

    protected ThemedDialog(string title, string subtitle, int bodyWidth)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = title;
        Font = Theme.Body;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var bodyHost = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(28, 24, 28, 12),
            Margin = Padding.Empty,
            Dock = DockStyle.Fill
        };
        bodyHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, bodyWidth));

        var heading = new Label
        {
            Text = title,
            Font = Theme.DialogTitle,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };

        _subtitle = new Label
        {
            Text = subtitle,
            Font = Theme.Body,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(bodyWidth, 0),
            Margin = new Padding(0, 0, 0, 20),
            Visible = subtitle.Length > 0
        };

        Body = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        bodyHost.Controls.Add(heading, 0, 0);
        bodyHost.Controls.Add(_subtitle, 0, 1);
        bodyHost.Controls.Add(Body, 0, 2);

        var footer = new FooterPanel
        {
            Dock = DockStyle.Fill,
            Height = 64,
            Margin = Padding.Empty,
            Padding = new Padding(20, 1, 20, 0)
        };

        FooterLeft = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new Padding(0, 14, 0, 0),
            BackColor = Theme.Sidebar
        };

        FooterRight = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 14, 0, 0),
            BackColor = Theme.Sidebar
        };

        footer.Controls.Add(FooterLeft);
        footer.Controls.Add(FooterRight);

        root.Controls.Add(bodyHost, 0, 0);
        root.Controls.Add(footer, 0, 1);
        Controls.Add(root);
    }

    protected TableLayoutPanel Body { get; }
    protected FlowLayoutPanel FooterLeft { get; }
    protected FlowLayoutPanel FooterRight { get; }

    protected string Subtitle
    {
        set
        {
            _subtitle.Text = value;
            _subtitle.Visible = value.Length > 0;
        }
    }

    /// <summary>Adds body content top to bottom.</summary>
    protected void AddBody(params Control[] controls)
    {
        foreach (var control in controls)
        {
            control.Dock = DockStyle.Fill;
            Body.Controls.Add(control, 0, Body.Controls.Count);
        }
    }

    protected void Complete()
    {
        foreach (var button in FooterRight.Controls.OfType<ThemedButton>())
            button.Margin = new Padding(8, 0, 0, 0);
        foreach (var button in FooterLeft.Controls.OfType<ThemedButton>())
            button.Margin = new Padding(0, 0, 8, 0);

        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.UseDarkTitleBar(this);
        Native.SetCaptionColors(this, Theme.Window, Theme.Text);
    }

    private sealed class FooterPanel : Panel
    {
        public FooterPanel()
        {
            BackColor = Theme.Sidebar;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public override Size GetPreferredSize(Size proposedSize) => new(0, Height);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        }
    }
}
