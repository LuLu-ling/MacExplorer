using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using MacExplorer.Native;
using Avalonia.Interactivity;
using MacExplorer.Lifecycle;
using MacExplorer.Services;
using MacExplorer.ViewModels;

namespace MacExplorer.Views;

public partial class HomePageView : UserControl
{
    public HomePageView()
    {
        InitializeComponent();
        AddHandler(ContextRequestedEvent, Card_OnContextRequested);
    }

    private async void Card_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path })
            return;
        if (TopLevel.GetTopLevel(this)?.DataContext is not MainViewModel main)
            return;
        if (Directory.Exists(path))
            await main.OpenPathAsync(path);
        else if (File.Exists(path))
            AppServices.Get<FileService>().Open(path);
    }

    private void Card_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var button = (e.Source as Avalonia.Visual)?.FindAncestorOfType<Button>(includeSelf: true);
        if (button?.Tag is not string path || !Directory.Exists(path))
            return;
        e.Handled = true;
        MacContextMenu.Show([new("Open in New Window", () => AppServices.Get<WindowService>().OpenWindow(path))]);
    }
}
