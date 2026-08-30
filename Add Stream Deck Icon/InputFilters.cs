namespace Add_Stream_Deck_Icon;

internal static class InputFilters
{
    public static void LettersDigitsSpaces(object? sender, KeyPressEventArgs e) =>
        Filter(e, c => char.IsLetterOrDigit(c) || c == ' ');

    public static void LettersDigitsSpacesCommas(object? sender, KeyPressEventArgs e) =>
        Filter(e, c => char.IsLetterOrDigit(c) || c == ' ' || c == ',');

    public static void LettersSpaces(object? sender, KeyPressEventArgs e) =>
        Filter(e, c => char.IsLetter(c) || c == ' ');

    private static void Filter(KeyPressEventArgs e, Func<char, bool> allowed)
    {
        if (e.KeyChar == (char)Keys.Back)
        {
            e.Handled = false;
            return;
        }

        e.Handled = !allowed(e.KeyChar);
    }
}
