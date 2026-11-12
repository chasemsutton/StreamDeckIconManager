using Avalonia;

namespace StreamDeckIconManager.Mac.UI;

/// <summary>Create a pack or edit an existing pack's name, description and author.</summary>
internal sealed class PackDetailsDialog : ThemedDialog
{
    private readonly InputField _name;
    private readonly InputField _description;
    private readonly InputField _author;
    private readonly Func<string, string?> _validateName;

    public PackDetailsDialog(Func<string, string?> validateName, Manifest? existing = null)
        : base(
            existing is null ? "New icon pack" : "Edit pack details",
            existing is null
                ? "A pack shows up as its own folder in the Stream Deck icon library."
                : "Changes are applied when you save to Stream Deck.",
            380)
    {
        _validateName = validateName;

        _name = new InputField("Pack name", placeholder: "e.g. Streaming Scenes", maxLength: 50);
        _description = new InputField("Description",
            "Optional. Shown when you hover over the pack in Stream Deck's icon library.",
            PackPaths.DefaultDescription, 200);
        _author = new InputField("Author", "Optional. Letters and spaces only.", PackPaths.DefaultAuthor, 50);

        InputFilters.Attach(_name.Box, InputFilters.LetterDigitSpace);
        InputFilters.Attach(_description.Box, InputFilters.SentenceText);
        InputFilters.Attach(_author.Box, InputFilters.LetterSpace);

        if (existing is not null)
        {
            _name.Value = existing.Name;
            _description.Value = existing.Description == PackPaths.DefaultDescription ? "" : existing.Description;
            _author.Value = existing.Author == PackPaths.DefaultAuthor ? "" : existing.Author;
        }

        AddBody(_name, _description, _author);

        var cancel = new ThemedButton("Cancel", ButtonKind.Ghost) { IsCancel = true };
        cancel.Click += (_, _) => Finish(false);
        var ok = new ThemedButton(existing is null ? "Create pack" : "Save details", ButtonKind.Primary,
            existing is null ? Glyphs.Add : null) { IsDefault = true };
        ok.Click += (_, _) => Submit();
        FooterRight.Children.Add(cancel);
        FooterRight.Children.Add(ok);

        Opened += (_, _) => _name.FocusInput();
    }

    public string PackName => _name.Value;
    public string Description => _description.Value.Length == 0 ? PackPaths.DefaultDescription : _description.Value;
    public string Author => _author.Value.Length == 0 ? PackPaths.DefaultAuthor : _author.Value;

    private void Submit()
    {
        if (_name.Value.Length == 0)
        {
            _name.ErrorText = "Give the pack a name.";
            _name.FocusInput();
            return;
        }

        var problem = _validateName(_name.Value);
        if (problem is not null)
        {
            _name.ErrorText = problem;
            _name.FocusInput();
            return;
        }

        Finish(true);
    }
}

/// <summary>Shown when an imported pack's name clashes with one that is already installed.</summary>
internal sealed class RenameImportDialog : ThemedDialog
{
    private readonly InputField _name;
    private readonly Func<string, string?> _validateName;

    public RenameImportDialog(string currentName, Func<string, string?> validateName)
        : base("Rename imported pack",
            $"You already have a pack called \"{currentName}\". Choose another name for the one you're importing.",
            380)
    {
        _validateName = validateName;
        _name = new InputField("New pack name", maxLength: 50) { Margin = new Thickness(0, 0, 0, 6) };
        InputFilters.Attach(_name.Box, InputFilters.LetterDigitSpace);
        _name.Value = currentName + " 2";
        AddBody(_name);

        var cancel = new ThemedButton("Cancel import", ButtonKind.Ghost) { IsCancel = true };
        cancel.Click += (_, _) => Finish(false);
        var ok = new ThemedButton("Import", ButtonKind.Primary, Glyphs.Import) { IsDefault = true };
        ok.Click += (_, _) => Submit();
        FooterRight.Children.Add(cancel);
        FooterRight.Children.Add(ok);

        Opened += (_, _) => _name.FocusInput();
    }

    public string PackName => _name.Value;

    private void Submit()
    {
        var problem = _name.Value.Length == 0 ? "Enter a name for the pack." : _validateName(_name.Value);
        if (problem is not null)
        {
            _name.ErrorText = problem;
            _name.FocusInput();
            return;
        }

        Finish(true);
    }
}
