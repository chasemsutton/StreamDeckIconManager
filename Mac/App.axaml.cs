using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using StreamDeckIconManager.Mac.UI;

namespace StreamDeckIconManager.Mac;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Cmd+Q: close the window instead, so unsaved changes get the same question.
            desktop.ShutdownRequested += (_, e) =>
            {
                if (!window.CanCloseWithoutAsking)
                {
                    e.Cancel = true;
                    window.Close();
                }
            };

            // macOS app menu: replaces Avalonia's default "About Avalonia" item.
            var about = new NativeMenuItem("About Stream Deck Icon Manager");
            about.Click += async (_, _) => await window.ShowAboutAsync();
            NativeMenu.SetMenu(this, new NativeMenu { about });
        }

        base.OnFrameworkInitializationCompleted();
    }
}
