namespace Add_Stream_Deck_Icon;

internal static class InputFilters
{
    public static bool LetterDigitSpace(char c) => char.IsLetterOrDigit(c) || c == ' ';
    public static bool LetterDigitSpaceComma(char c) => char.IsLetterOrDigit(c) || c == ' ' || c == ',';
    public static bool LetterSpace(char c) => char.IsLetter(c) || c == ' ';
    public static bool Printable(char c) => !char.IsControl(c);
    public static bool PrintableNoComma(char c) => !char.IsControl(c) && c != ',';
    /// <summary>Pack descriptions. No commas: Stream Deck would split the hover text at them.</summary>
    public static bool SentenceText(char c) => char.IsLetterOrDigit(c) || " .!?'&()-:".Contains(c);

    /// <summary>
    /// Blocks typed characters that <paramref name="allowed"/> rejects and strips them from pasted text,
    /// while leaving shortcuts such as Ctrl+A, Ctrl+C and Ctrl+V working.
    /// </summary>
    public static void Attach(TextBox box, Func<char, bool> allowed)
    {
        box.KeyPress += (_, e) => e.Handled = !char.IsControl(e.KeyChar) && !allowed(e.KeyChar);
        box.TextChanged += (_, _) =>
        {
            if (box.Text.All(allowed))
                return;

            var caret = box.SelectionStart;
            var before = box.Text.Length;
            box.Text = new string(box.Text.Where(allowed).ToArray());
            box.SelectionStart = Math.Max(0, caret - (before - box.Text.Length));
        };
    }

    /// <summary>Removes disallowed characters from a value that did not come from typing.</summary>
    public static string Clean(string value, Func<char, bool> allowed) =>
        new string(value.Where(allowed).ToArray()).Trim();
}
