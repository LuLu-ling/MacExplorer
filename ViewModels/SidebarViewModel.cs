using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MacExplorer.Infrastructure;
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

        var favorites = Section("favorites", Lang.Text("Places.Favorites"), Glyphs.Pin,
            MacFinder.FavoriteFolders().Select(pin => new SidebarItem
            {
                Id = "pin:" + pin,
                Title = Path.GetFileName(pin.TrimEnd('/')) is { Length: > 0 } n ? n : pin,
                Glyph = Glyphs.ForPath(pin),
                Kind = SidebarKind.Favorite,
                Path = pin,
                Depth = 1
            }));

        var locations = new List<SidebarItem>();
        if (SpecialFolders.ICloudExists())
        {
            locations.Add(new SidebarItem
            {
                Id = "icloud",
                Title = Lang.Text("Places.iCloudDrive"),
                Glyph = Glyphs.ForPath(SpecialFolders.ICloud),
                Kind = SidebarKind.Cloud,
                Path = SpecialFolders.ICloud,
                Depth = 1
            });
        }

        locations.AddRange(_volumes.List().Select(v => new SidebarItem
        {
            Id = "vol:" + v.Path,
            Title = v.Name,
            Glyph = Glyphs.ForPath(v.Path),
            Kind = SidebarKind.Location,
            Path = v.Path,
            Depth = 1
        }));
        locations.Add(new SidebarItem
        {
            Id = "trash",
            Title = Lang.Text("Places.Trash"),
            Glyph = Glyphs.ForPath(SpecialFolders.Trash),
            Kind = SidebarKind.Location,
            Path = SpecialFolders.Trash,
            Depth = 1
        });
        ApplyOrder(locations, Config.Sidebar.LocationOrder);

        var blocks = new List<(string Id, List<SidebarItem> Rows)>();
        if (Config.Home.AnyVisible)
        {
            blocks.Add(("home",
            [
                new SidebarItem
                {
                    Id = "home",
                    Title = Lang.Text("Places.Home"),
                    Glyph = Glyphs.ForPath(SpecialFolders.HomeKey),
                    Kind = SidebarKind.Home,
                    Path = SpecialFolders.HomeKey
                }
            ]));
        }

        blocks.Add(("favorites", favorites));
        blocks.Add(("locations", Section("locations", Lang.Text("Places.Locations"), Glyphs.Drive, locations)));
        blocks.Add(("tags", Section("tags", Lang.Text("Places.FileTags"), Glyphs.Tag, MacTags.All().Select(Tag))));
        ApplyOrder(blocks, Config.Sidebar.SectionOrder, static b => b.Id);

        foreach (var block in blocks)
        {
            foreach (var row in block.Rows)
                Items.Add(row);
        }

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

    public (int Start, int Count)[]? ReorderUnits(int index)
    {
        if ((uint)index >= (uint)Items.Count)
            return null;
        var item = Items[index];
        if (item.IsSection || item.Kind == SidebarKind.Home)
            return BlockUnits();
        if (ReorderRange(index) is not { } range)
            return null;
        var n = range.Hi - range.Lo + 1;
        var units = new (int Start, int Count)[n];
        for (var i = 0; i < n; i++)
            units[i] = (range.Lo + i, 1);
        return units;
    }

    public bool TryMoveUnits(IReadOnlyList<(int Start, int Count)> units, int from, int to)
    {
        if ((uint)from >= (uint)units.Count || (uint)to >= (uint)units.Count || from == to)
            return false;
        var src = units[from];
        var dst = units[to];
        if (src.Count <= 0 || dst.Count <= 0 ||
            (uint)src.Start >= (uint)Items.Count || (uint)dst.Start >= (uint)Items.Count)
            return false;

        var kind = Items[src.Start].Kind;
        if (from < to)
        {
            var insert = dst.Start + dst.Count - 1;
            for (var i = 0; i < src.Count; i++)
                Items.Move(src.Start, insert);
        }
        else
        {
            for (var i = 0; i < src.Count; i++)
                Items.Move(src.Start + i, dst.Start + i);
        }

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

    private (int Start, int Count)[] BlockUnits()
    {
        var units = new List<(int Start, int Count)>();
        for (var i = 0; i < Items.Count;)
        {
            var start = i++;
            while (i < Items.Count && !IsBlockStart(Items[i]))
                i++;
            units.Add((start, i - start));
        }

        return units.ToArray();
    }

    private static bool IsBlockStart(SidebarItem item) =>
        item.IsSection || item.Kind == SidebarKind.Home;

    private static List<SidebarItem> Section(string id, string title, string glyph, IEnumerable<SidebarItem> children)
    {
        var rows = new List<SidebarItem>
        {
            new()
            {
                Id = id,
                Title = title,
                Glyph = glyph,
                Kind = SidebarKind.Section,
                IsSection = true,
                IsExpanded = true
            }
        };
        rows.AddRange(children);
        return rows;
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
            case SidebarKind.Location:
            case SidebarKind.Cloud:
                Config.Sidebar.LocationOrder =
                [
                    .. Items.Where(static i => i.Kind is SidebarKind.Location or SidebarKind.Cloud)
                        .Select(static i => i.Id)
                ];
                break;
            case SidebarKind.Home:
            case SidebarKind.Section:
                Config.Sidebar.SectionOrder = CurrentSectionOrder();
                break;
        }
    }

    private List<string> CurrentSectionOrder()
    {
        var ids = BlockUnits().Select(u => Items[u.Start].Id).ToList();
        if (ids.Contains("home"))
            return ids;

        var at = Config.Sidebar.SectionOrder.IndexOf("home");
        ids.Insert(at < 0 ? 0 : Math.Min(at, ids.Count), "home");
        return ids;
    }

    private (int Lo, int Hi)? ReorderRange(int index)
    {
        if ((uint)index >= (uint)Items.Count)
            return null;
        var item = Items[index];
        if (!item.CanReorder)
            return null;
        var lo = index;
        var hi = index;
        while (lo > 0 && Items[lo - 1].CanReorder)
            lo--;
        while (hi + 1 < Items.Count && Items[hi + 1].CanReorder)
            hi++;
        return hi > lo ? (lo, hi) : null;
    }

    private static void ApplyOrder(List<SidebarItem> items, List<string> ids) =>
        ApplyOrder(items, ids, static i => i.Id);

    private static void ApplyOrder<T>(List<T> items, List<string> ids, Func<T, string> key)
    {
        if (ids.Count == 0 || items.Count < 2)
            return;
        var map = items.ToDictionary(key, StringComparer.Ordinal);
        var ordered = new List<T>(items.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (map.TryGetValue(id, out var item) && seen.Add(id))
                ordered.Add(item);
        }

        foreach (var item in items)
        {
            if (seen.Add(key(item)))
                ordered.Add(item);
        }

        items.Clear();
        items.AddRange(ordered);
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
