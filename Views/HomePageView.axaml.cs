using Avalonia.Controls;
using Avalonia.Interactivity;
using MacExplorer.Lifecycle;
using MacExplorer.Services;
using MacExplorer.ViewModels;

namespace MacExplorer.Views;

public partial class HomePageView : UserControl
{
    public HomePageView() => InitializeComponent();

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
}
