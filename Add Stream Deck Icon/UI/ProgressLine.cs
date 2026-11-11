namespace Add_Stream_Deck_Icon.UI;

/// <summary>Slim rounded progress bar in the app's accent colour.</summary>
internal sealed class ProgressLine : Control
{
    private int _value;

    public ProgressLine()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 100); Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Window);
        Theme.HighQuality(g);
        var radius = Height / 2f;
        using (var track = Theme.RoundedRect(new RectangleF(0, 0, Width, Height), radius))
        using (var trackBrush = new SolidBrush(Theme.Raised))
            g.FillPath(trackBrush, track);

        if (_value <= 0)
            return;

        var width = Math.Max(Height, Width * _value / 100f);
        using var fill = Theme.RoundedRect(new RectangleF(0, 0, width, Height), radius);
        using var fillBrush = new SolidBrush(Theme.Accent);
        g.FillPath(fillBrush, fill);
    }
}
