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

    public MainWindow OpenWindow(string? path = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        var scope = services.CreateScope();
        MainWindow window;
        try
        {
            var model = scope.ServiceProvider.GetRequiredService<MainViewModel>();
            window = new MainWindow
            {
                DataContext = model,
                Width = Config.Window.Width,
                Height = Config.Window.Height
            };
            MacApplicationMenu.Attach(window);
            model.NewTab(path);
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
