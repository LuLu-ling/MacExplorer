using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MacExplorer.Infrastructure;
using MacExplorer.ViewModels;
using MacExplorer.Views;
using MacExplorer.Native;
using Microsoft.Extensions.DependencyInjection;

namespace MacExplorer.Services;

public sealed class WindowService(IServiceProvider services)
{
    private readonly List<MainWindow> _windows = [];
    public IReadOnlyList<MainWindow> Windows => _windows;
    public MainWindow? ActiveWindow { get; private set; }

    private static IClassicDesktopStyleApplicationLifetime Desktop =>
        (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;

    public MainWindow OpenWindow(string? path = null) => Spawn(model => model.NewTab(path));

    public MainWindow OpenWindow(ExplorerTabViewModel tab, PixelPoint position, Size size) =>
        Spawn(model => model.Adopt(tab), position, size);

    private MainWindow Spawn(Action<MainViewModel> seed, PixelPoint? position = null, Size? size = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        var scope = services.CreateScope();
        MainWindow window;
        MainViewModel model;
        try
        {
            model = scope.ServiceProvider.GetRequiredService<MainViewModel>();
            window = new MainWindow
            {
                DataContext = model,
                Width = size?.Width ?? Config.Window.Width,
                Height = size?.Height ?? Config.Window.Height
            };
            if (position is { } origin)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = origin;
            }

            MacApplicationMenu.Attach(window);
            if (position is null)
                seed(model);
        }
        catch
        {
            scope.Dispose();
            throw;
        }

        window.Activated += OnActivated;
        window.Closed += OnClosed;
        _windows.Add(window);
        Desktop.MainWindow ??= window;
        try
        {
            window.Show();
            if (position is not null)
                seed(model);
            window.Activate();
            ActiveWindow = window;
            return window;
        }
        catch
        {
            window.Close();
            if (_windows.Contains(window))
                OnClosed(window, EventArgs.Empty);
            throw;
        }

        void OnClosed(object? sender, EventArgs e)
        {
            window.Activated -= OnActivated;
            window.Closed -= OnClosed;
            _windows.Remove(window);
            if (ActiveWindow == window)
                ActiveWindow = _windows.LastOrDefault();
            if (Desktop.MainWindow == window)
                Desktop.MainWindow = ActiveWindow;
            scope.Dispose();
        }
    }

    private void OnActivated(object? sender, EventArgs e) => ActiveWindow = (MainWindow)sender!;

    public void Reopen()
    {
        if (ActiveWindow is not { } window)
        {
            OpenWindow();
            return;
        }
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
    }

    public void CloseActiveTabOrWindow()
    {
        if (ActiveWindow?.DataContext is MainViewModel model)
            _ = model.CloseTab();
    }

    public void Quit() => Desktop.Shutdown();
}
