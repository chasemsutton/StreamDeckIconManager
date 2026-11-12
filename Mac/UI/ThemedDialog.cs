using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace StreamDeckIconManager.Mac.UI;

/// <summary>
/// Base for the app's dialogs: a heading, a body that subclasses fill, and a footer with
/// secondary actions on the left and the primary action on the right.
/// Show with <c>await dialog.ShowDialog&lt;bool&gt;(owner)</c>; <see cref="Finish"/> closes it.
/// </summary>
internal abstract class ThemedDialog : Window
{
    private readonly TextBlock _subtitle;

    protected ThemedDialog(string title, string subtitle, double bodyWidth)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.Window;
        BodyWidth = bodyWidth;

        var heading = Palette.Label(title, Palette.DialogTitle, Palette.Text, strong: true);
        heading.Margin = new Thickness(0, 0, 0, 4);
        _subtitle = Palette.Label(subtitle, Palette.Body, Palette.TextMuted, wrap: true);
        _subtitle.Margin = new Thickness(0, 0, 0, 20);
        _subtitle.IsVisible = subtitle.Length > 0;

        Body = new StackPanel();

        var content = new StackPanel
        {
            Width = bodyWidth,
            Margin = new Thickness(28, 24, 28, 12),
            Children = { heading, _subtitle, Body }
        };

        FooterLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
        FooterRight = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var footerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(FooterLeft, 0);
        Grid.SetColumn(FooterRight, 2);
        footerGrid.Children.Add(FooterLeft);
        footerGrid.Children.Add(FooterRight);

        var footer = new Border
        {
            Background = Palette.Sidebar,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 14),
            Child = footerGrid
        };

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(content);
        Content = root;
    }

    protected double BodyWidth { get; }
    protected StackPanel Body { get; }
    protected StackPanel FooterLeft { get; }
    protected StackPanel FooterRight { get; }

    /// <summary>While true, the window can't be closed (Cancel/Skip are the ways out).</summary>
    protected bool Busy { get; set; }

    /// <summary>If set, Escape closes the dialog (without choosing) when it isn't busy.</summary>
    protected bool EscapeCloses { get; set; }

    protected string Subtitle
    {
        set
        {
            _subtitle.Text = value;
            _subtitle.IsVisible = value.Length > 0;
        }
    }

    protected void AddBody(params Control[] controls)
    {
        foreach (var control in controls)
            Body.Children.Add(control);
    }

    protected void Finish(bool ok)
    {
        Busy = false;
        Close(ok);
    }

    protected TextBlock Paragraph(string text, IBrush? brush = null, double size = Palette.Body, double bottom = 8)
    {
        var label = Palette.Label(text, size, brush ?? Palette.TextMuted, wrap: true);
        label.Margin = new Thickness(0, 0, 0, bottom);
        return label;
    }

    /// <summary>A name on the left and a count on the right, for "which packs" lists.</summary>
    protected static Control PackLine(string name, string count)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 4) };
        var left = Palette.Label(name, Palette.Body, Palette.Text);
        var right = Palette.Label(count, Palette.Small, Palette.TextMuted);
        right.Margin = new Thickness(12, 1, 0, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Busy)
            e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && EscapeCloses && e.Key == Key.Escape && !Busy)
        {
            e.Handled = true;
            Close(false);
        }
    }
}

internal enum MessageKind
{
    Info,
    Warning,
    Error,
    Question
}

/// <summary>A themed message box. Returns the label of the button pressed, or null if closed.</summary>
internal sealed class MessageDialog : ThemedDialog
{
    private MessageDialog(string title, string text, IReadOnlyList<(string Label, ButtonKind Kind)> buttons, int defaultIndex, int cancelIndex)
        : base(title, "", 400)
    {
        AddBody(Paragraph(text, Palette.Text, bottom: 12));
        EscapeCloses = cancelIndex < 0;

        for (var i = 0; i < buttons.Count; i++)
        {
            var (label, kind) = buttons[i];
            var button = new ThemedButton(label, kind) { IsDefault = i == defaultIndex, IsCancel = i == cancelIndex };
            button.Click += (_, _) =>
            {
                Result = label;
                Close(true);
            };
            FooterRight.Children.Add(button);
            if (i == defaultIndex)
                Opened += (_, _) => button.Focus();
        }
    }

    public string? Result { get; private set; }

    public static async Task<string?> ShowAsync(Window owner, string title, string text, MessageKind kind = MessageKind.Info,
        params string[] buttons)
    {
        if (buttons.Length == 0)
            buttons = new[] { "OK" };

        // The last button is the main one; "Cancel" / "No" style buttons are ghosts.
        var spec = buttons.Select((b, i) => (b, i == buttons.Length - 1
            ? (kind == MessageKind.Warning && b.StartsWith("Delete", StringComparison.Ordinal) ? ButtonKind.Danger : ButtonKind.Primary)
            : ButtonKind.Ghost)).ToList();
        var cancel = Array.FindIndex(buttons, b => b is "Cancel" or "No" or "Not now");
        var defaultIndex = kind == MessageKind.Warning && cancel >= 0 ? cancel : buttons.Length - 1;

        var dialog = new MessageDialog(title, text, spec, defaultIndex, cancel);
        await dialog.ShowDialog<bool>(owner);
        return dialog.Result;
    }
}
