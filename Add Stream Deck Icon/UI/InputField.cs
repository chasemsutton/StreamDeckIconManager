namespace Add_Stream_Deck_Icon.UI;

/// <summary>Label, rounded text box and a hint line that turns into an error message.</summary>
internal sealed class InputField : TableLayoutPanel
{
    private readonly Label _caption;
    private readonly Label _hint;
    private readonly InputShell _shell;
    private string _hintText;
    private bool _hasError;

    public InputField(string caption, string hint = "", string placeholder = "", int maxLength = 50)
    {
        _hintText = hint;
        ColumnCount = 1;
        RowCount = 3;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        Margin = new Padding(0, 0, 0, 14);
        Padding = Padding.Empty;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _caption = new Label
        {
            Text = caption,
            AutoSize = true,
            Font = Theme.BodyStrong,
            ForeColor = Theme.Text,
            Margin = new Padding(0, 0, 0, 6)
        };

        TextBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Raised,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            PlaceholderText = placeholder,
            MaxLength = maxLength,
            Dock = DockStyle.Fill
        };

        _shell = new InputShell(TextBox)
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = Padding.Empty
        };

        _hint = new Label
        {
            Text = hint,
            AutoSize = true,
            Font = Theme.Small,
            ForeColor = Theme.TextFaint,
            Margin = new Padding(0, 5, 0, 0),
            Visible = hint.Length > 0
        };

        Controls.Add(_caption, 0, 0);
        Controls.Add(_shell, 0, 1);
        Controls.Add(_hint, 0, 2);

        TextBox.TextChanged += (_, _) => { if (_hasError) ErrorText = null; };
    }

    public TextBox TextBox { get; }

    public string Value
    {
        get => TextBox.Text.Trim();
        set => TextBox.Text = value;
    }

    public string HintText
    {
        get => _hintText;
        set
        {
            _hintText = value;
            if (!_hasError)
            {
                _hint.Text = value;
                _hint.Visible = value.Length > 0;
            }
        }
    }

    /// <summary>Shows an error under the field. Null clears it.</summary>
    public string? ErrorText
    {
        set
        {
            _hasError = !string.IsNullOrEmpty(value);
            _hint.Text = _hasError ? value! : _hintText;
            _hint.ForeColor = _hasError ? Theme.Danger : Theme.TextFaint;
            _hint.Visible = _hint.Text.Length > 0;
            _shell.HasError = _hasError;
        }
    }

    public void FocusInput()
    {
        TextBox.Focus();
        TextBox.SelectAll();
    }

    private sealed class InputShell : Panel
    {
        private readonly TextBox _box;
        private bool _hasError;

        public InputShell(TextBox box)
        {
            _box = box;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Raised;
            Padding = new Padding(11, 9, 11, 8);
            Cursor = Cursors.IBeam;
            Controls.Add(box);
            box.GotFocus += (_, _) => Invalidate();
            box.LostFocus += (_, _) => Invalidate();
            Click += (_, _) => box.Focus();
        }

        public bool HasError
        {
            set { _hasError = value; Invalidate(); }
        }

        public override Size GetPreferredSize(Size proposedSize) =>
            new(Math.Max(proposedSize.Width, 100), _box.PreferredHeight + Padding.Vertical);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            var wanted = _box.PreferredHeight + Padding.Vertical;
            if (Height != wanted)
                Height = wanted;
            base.OnLayout(levent);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var scale = DeviceDpi / 96f;
            g.Clear(Parent?.BackColor ?? Theme.Window);
            Theme.HighQuality(g);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using var path = Theme.RoundedRect(rect, 6 * scale);
            using (var fill = new SolidBrush(Theme.Raised))
                g.FillPath(fill, path);

            var focused = _box.Focused;
            var color = _hasError ? Theme.Danger : focused ? Theme.Accent : Theme.Border;
            using var pen = new Pen(color, focused || _hasError ? 1.5f * scale : 1f);
            g.DrawPath(pen, path);
        }
    }
}
