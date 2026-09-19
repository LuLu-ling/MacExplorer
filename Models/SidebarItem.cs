using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MacExplorer.Models;

public enum SidebarKind
{
    Home,
    Favorite,
    Section,
    Location,
    Cloud,
    Tag,
    Settings
}

public sealed partial class SidebarItem : ObservableObject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Glyph { get; init; }
    public required SidebarKind Kind { get; init; }
    public string? Path { get; init; }
    public bool IsSection { get; init; }
    public int Depth { get; init; }
    [ObservableProperty] public partial IBrush? Marker { get; set; }


    [ObservableProperty] public partial bool IsExpanded { get; set; } = true;
    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
    [ObservableProperty] public partial bool IsRenaming { get; set; }
    [ObservableProperty] public partial string RenameText { get; set; } = string.Empty;
    public bool ShowChevron => IsSection;
    public bool HasMarker => Marker is not null;
    public bool CanReorder => Kind is SidebarKind.Favorite or SidebarKind.Tag or SidebarKind.Location or SidebarKind.Cloud;
    partial void OnMarkerChanged(IBrush? value) => OnPropertyChanged(nameof(HasMarker));

}
