using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using MacExplorer.Infrastructure;
using MacExplorer.Lifecycle;
using MacExplorer.Models;
using MacExplorer.Native;
using MacExplorer.ViewModels;
using MacExplorer.Views;

namespace MacExplorer;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        AppLifecycle.RegisterDispatcherHook();
        MacContextMenu.Install();

        RequestedThemeVariant = Config.Appearance.Theme switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = AppServices.Get<MainViewModel>(),
                Width = Config.Window.Width,
                Height = Config.Window.Height
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
