using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using MacExplorer.Infrastructure;
using MacExplorer.Lifecycle;
using MacExplorer.Localization;
using MacExplorer.Models;
using MacExplorer.Native;
using MacExplorer.Services;

namespace MacExplorer;

public partial class App : Application
{
    public override void Initialize()
    {
        Name = "MacExplorer";
        AvaloniaXamlLoader.Load(this);
        LocalizationService.Initialize();
    }
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
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            var windows = AppServices.Get<WindowService>();
            windows.OpenWindow();
            MacApplicationMenu.Install(windows);
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activation)
                activation.Activated += (_, e) =>
                {
                    if (e.Kind == ActivationKind.Reopen)
                        windows.Reopen();
                };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
