using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using MacExplorer.Infrastructure;
using MacExplorer.Models;
using MacExplorer.Native;
using MacExplorer.Services;

namespace MacExplorer.ViewModels;

public sealed partial class HomeCard : ObservableObject
{
    public required string Title { get; init; }
    public required string Path { get; init; }
    public required string Glyph { get; init; }
    public string? Subtitle { get; init; }
    [ObservableProperty] public partial Bitmap? Icon { get; set; }
}

public sealed class HomeViewModel : ViewModelBase
{
    private readonly VolumeService _volumes;
    private readonly IconService _icons;

    public HomeViewModel(VolumeService volumes, IconService icons)
    {
        _volumes = volumes;
        _icons = icons;
        QuickAccess = [];
        Drives = [];
        Recents = [];
        Refresh();
    }

    public ObservableCollection<HomeCard> QuickAccess { get; }
    public ObservableCollection<HomeCard> Drives { get; }
    public ObservableCollection<HomeCard> Recents { get; }

    public bool ShowQuickAccess => Config.Home.ShowQuickAccess;
    public bool ShowVolumes => Config.Home.ShowVolumes;
    public bool ShowRecents => Config.Home.ShowRecents;

    public void Refresh()
    {
        QuickAccess.Clear();
        foreach (var pin in MacFinder.FavoriteFolders().Where(Directory.Exists))
            QuickAccess.Add(Card(pin, Path.GetFileName(pin.TrimEnd('/'))));

        Drives.Clear();
        foreach (var volume in _volumes.List())
            Drives.Add(Card(volume.Path, volume.Name, Glyphs.Drive));

        Recents.Clear();
        foreach (var path in Config.Home.Recents.Where(PathUtil.Exists).Take(16))
            Recents.Add(Card(path, Path.GetFileName(path), File.Exists(path) ? Glyphs.Document : Glyphs.Folder));

        _ = LoadIconsAsync();
        OnPropertyChanged(nameof(ShowQuickAccess));
        OnPropertyChanged(nameof(ShowVolumes));
        OnPropertyChanged(nameof(ShowRecents));
    }

    private static HomeCard Card(string path, string? title, string? glyph = null) => new()
    {
        Title = string.IsNullOrEmpty(title) ? path : title,
        Path = path,
        Glyph = glyph ?? Glyphs.Folder
    };

    private async Task LoadIconsAsync()
    {
        var cards = QuickAccess.Concat(Drives).Concat(Recents).ToArray();
        await Task.WhenAll(cards.Select(async card =>
        {
            var icon = await _icons.GetAsync(card.Path, 32);
            if (icon is not null)
                card.Icon = icon;
        }));
    }
}
