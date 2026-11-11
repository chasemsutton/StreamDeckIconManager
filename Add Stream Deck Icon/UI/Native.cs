using System.Runtime.InteropServices;

namespace Add_Stream_Deck_Icon.UI;

internal static class Native
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    /// <summary>Asks Windows 10 20H1+ / Windows 11 for a dark title bar. Ignored on older builds.</summary>
    public static void UseDarkTitleBar(Form form)
    {
        try
        {
            var on = 1;
            if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
        }
        catch
        {
            // Cosmetic only.
        }
    }

    /// <summary>
    /// Tints the title bar so it blends into the window instead of reading as a separate strip.
    /// Windows 11 only; older versions keep the dark title bar.
    /// </summary>
    public static void SetCaptionColors(Form form, Color caption, Color text)
    {
        try
        {
            var captionRef = ColorTranslator.ToWin32(caption);
            var textRef = ColorTranslator.ToWin32(text);
            var borderRef = ColorTranslator.ToWin32(Theme.Border);
            DwmSetWindowAttribute(form.Handle, 35, ref captionRef, sizeof(int)); // DWMWA_CAPTION_COLOR
            DwmSetWindowAttribute(form.Handle, 36, ref textRef, sizeof(int));    // DWMWA_TEXT_COLOR
            DwmSetWindowAttribute(form.Handle, 34, ref borderRef, sizeof(int));  // DWMWA_BORDER_COLOR
        }
        catch
        {
            // Cosmetic only.
        }
    }

    /// <summary>Gives native scrollbars the dark Explorer style.</summary>
    public static void UseDarkScrollbars(Control control)
    {
        void Apply()
        {
            try
            {
                SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            }
            catch
            {
                // Cosmetic only.
            }
        }

        if (control.IsHandleCreated)
            Apply();
        else
            control.HandleCreated += (_, _) => Apply();
    }
}
