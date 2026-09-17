using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MacExplorer.Localization;
using MacExplorer.Models;
using MacExplorer.Native;
using MacExplorer.Services;
namespace MacExplorer.ViewModels;

public sealed partial class SidebarViewModel : ViewModelBase
{
    private readonly VolumeService _volumes;

    public SidebarViewModel(VolumeService volumes)
    {
        _volumes = volumes;
        Items = [];
        Rebuild();
        MacTags.LearnedChanged += () => Dispatcher.UIThread.Post(RefreshTagColors, DispatcherPriority.Background);
        _ = Task.Run(MacTags.LearnFromIndex);
    }

    public ObservableCollection<SidebarItem> Items { get; }

    [ObservableProperty] public partial SidebarItem? SelectedItem { get; set; }

    public void Rebuild()
    {
        var selectedId = SelectedItem?.Id;
        Items.Clear();
        Items.Add(new SidebarItem
        {
            Id = "home",
            Title = Lang.Text("Places.Home"),
            Glyph = Glyphs.ForPath(SpecialFolders.HomeKey),
            Kind = SidebarKind.Home,
            Path = SpecialFolders.HomeKey
        });

        AddSection("favorites", Lang.Text("Places.Favorites"), Glyphs.Pin, MacFinder.FavoriteFolders().Select(pin => new SidebarItem
        {
            Id = "pin:" + pin,
            Title = Path.GetFileName(pin.TrimEnd('/')) is { Length: > 0 } n ? n : pin,
            Glyph = Glyphs.ForPath(pin),
            Kind = SidebarKind.Favorite,
            Path = pin,
            Depth = 1
        }));

        if (SpecialFolders.ICloudExists())
        {
            Items.Add(new SidebarItem
            {
                Id = "icloud",
                Title = Lang.Text("Places.iCloudDrive"),
                Glyph = Glyphs.ForPath(SpecialFolders.ICloud),
                Kind = SidebarKind.Cloud,
                Path = SpecialFolders.ICloud
            });
        }

        AddSection("locations", Lang.Text("Places.Locations"), Glyphs.Drive, _volumes.List().Select(v => new SidebarItem
        {
            Id = "vol:" + v.Path,
            Title = v.Name,
            Glyph = Glyphs.ForPath(v.Path),
            Kind = SidebarKind.Location,
            Path = v.Path,
            Depth = 1
        }).Append(new SidebarItem
        {
            Id = "trash",
            Title = Lang.Text("Places.Trash"),
            Glyph = Glyphs.ForPath(SpecialFolders.Trash),
            Kind = SidebarKind.Location,
            Path = SpecialFolders.Trash,
            Depth = 1
        }));

        AddSection("tags", Lang.Text("Places.FileTags"), Glyphs.Tag,
            MacTags.All().Select(Tag));

        if (selectedId is not null)
        {
            var match = Items.FirstOrDefault(i => i.Id == selectedId);
            if (match is not null)
            {
                match.IsSelected = true;
                SelectedItem = match;
            }
        }
    }

    public void SelectPath(string path)
    {
        SidebarItem? best = null;
        var bestScore = -1;
        foreach (var item in Items)
        {
            var score = MatchScore(item, path);
            item.IsSelected = false;
            if (score > bestScore)
            {
                bestScore = score;
                best = item;
            }
        }

        if (best is not null)
            best.IsSelected = true;
        SelectedItem = best;
    }

    public (int Lo, int Hi)? ReorderRange(int index)
    {
        if ((uint)index >= (uint)Items.Count)
            return null;
        var item = Items[index];
        if (!item.CanReorder)
            return null;
        var lo = index;
        var hi = index;
        while (lo > 0 && Items[lo - 1].Kind == item.Kind && Items[lo - 1].CanReorder)
            lo--;
        while (hi + 1 < Items.Count && Items[hi + 1].Kind == item.Kind && Items[hi + 1].CanReorder)
            hi++;
        return hi > lo ? (lo, hi) : null;
    }

    public bool TryMove(int from, int to)
    {
        if ((uint)from >= (uint)Items.Count || (uint)to >= (uint)Items.Count || from == to)
            return false;
        if (ReorderRange(from) is not { } range || to < range.Lo || to > range.Hi)
            return false;
        var kind = Items[from].Kind;
        Items.Move(from, to);
        Persist(kind);
        return true;
    }

    public void ToggleSection(SidebarItem section)
    {
        if (!section.IsSection) return;
        section.IsExpanded = !section.IsExpanded;
        var show = section.IsExpanded;
        var started = false;
        foreach (var item in Items)
        {
            if (item == section)
            {
                started = true;
                continue;
            }

            if (!started) continue;
            if (item.IsSection && item.Depth == 0)
                break;
            if (item.Depth >= 1)
                item.IsVisible = show;
        }
    }

    private void AddSection(string id, string title, string glyph, IEnumerable<SidebarItem> children)
    {
        var section = new SidebarItem
        {
            Id = id,
            Title = title,
            Glyph = glyph,
            Kind = SidebarKind.Section,
            IsSection = true,
            IsExpanded = true
        };
        Items.Add(section);
        foreach (var child in children)
            Items.Add(child);
    }

    private void Persist(SidebarKind kind)
    {
        switch (kind)
        {
            case SidebarKind.Favorite:
                MacFinder.ReorderFavorites(
                    Items.Where(static i => i.Kind == SidebarKind.Favorite && i.Path is { Length: > 0 })
                        .Select(static i => i.Path!)
                        .ToArray());
                break;
            case SidebarKind.Tag:
                MacTags.ReorderFavoriteNames(
                    Items.Where(static i => i.Kind == SidebarKind.Tag)
                        .Select(static i => i.Title)
                        .ToArray());
                break;
        }
    }

    private static SidebarItem Tag(FileTag tag) => new()
    {
        Id = SpecialFolders.TagPath(tag.Name),
        Title = tag.Name,
        Glyph = Glyphs.ForPath(SpecialFolders.TagPath(tag.Name)),
        Kind = SidebarKind.Tag,
        Path = SpecialFolders.TagPath(tag.Name),
        Depth = 1,
        Marker = tag.Brush
    };

    private void RefreshTagColors()
    {
        foreach (var item in Items)
        {
            if (item.Kind != SidebarKind.Tag)
                continue;
            item.Marker = MacTags.Resolve(item.Title).Brush;
        }
    }

    protected override void OnLanguageChanged() => Rebuild();


    private static int MatchScore(SidebarItem item, string path)
    {
        if (item.IsSection || item.Path is null)
            return -1;

        if (SpecialFolders.IsTag(path))
            return item.Kind == SidebarKind.Tag &&
                   string.Equals(item.Path, path, StringComparison.Ordinal)
                ? int.MaxValue
                : -1;

        if (item.Kind is SidebarKind.Tag)
            return -1;
        if (item.Kind == SidebarKind.Home)
            return path == SpecialFolders.HomeKey ? int.MaxValue : -1;

        if (string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
            return 1_000_000 + item.Path.Length;

        var prefix = item.Path.TrimEnd('/');
        if (prefix.Length == 0)
            prefix = "/";

        if (prefix == "/")
            return path.StartsWith('/') && path != SpecialFolders.HomeKey && path != SpecialFolders.SettingsKey
                ? 1
                : -1;

        if (path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return prefix.Length;

        return -1;
    }
}
