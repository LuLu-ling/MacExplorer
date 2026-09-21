using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using FluentAvalonia.Styling;
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
        ApplyTheme(Config.Appearance.Theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            MacSidebarPane.Warmup();
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


    public static void ApplyTheme(ThemeMode mode)
    {
        if (Current is not { } app)
            return;
        app.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        foreach (var style in app.Styles)
        {
            if (style is FluentAvaloniaTheme fluent)
                fluent.PreferSystemTheme = mode is ThemeMode.Default;
        }
        MacAppearance.Apply(mode);
    }
}
