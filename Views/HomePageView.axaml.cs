using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MacExplorer.Lifecycle;
using MacExplorer.Native;
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
        var button = (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true);
        if (button?.DataContext is not HomeCard card)
            return;
        if (TopLevel.GetTopLevel(this)?.DataContext is not MainViewModel main)
            return;

        var entries = card.Kind switch
        {
            HomeCardKind.Favorite => PlaceMenu.For(card.Path, unfavorite: true),
            HomeCardKind.Drive => PlaceMenu.For(card.Path, eject: main.EjectVolumeAsync),
            _ => Directory.Exists(card.Path) ? [PlaceMenu.OpenWindow(card.Path)] : []
        };
        if (entries.Length == 0)
            return;
        e.Handled = true;
        MacContextMenu.Show(entries);
    }
}
