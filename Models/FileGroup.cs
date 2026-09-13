using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MacExplorer.Models;

public sealed partial class FileGroup : ObservableObject
{
    public required string Key { get; init; }
    public int SortIndexOverride { get; set; }

    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial string? Subtext { get; set; }
    [ObservableProperty] public partial string CountText { get; set; } = "";
    [ObservableProperty] public partial bool ShowCountTextBelow { get; set; }
    [ObservableProperty] public partial string? Icon { get; set; }
    [ObservableProperty] public partial IBrush? Marker { get; set; }
    [ObservableProperty] public partial bool ShowImage { get; set; }

    public List<FileItem> Items { get; } = [];
    public FileItem? Lead => Items.Count > 0 ? Items[0] : null;
    public bool ShowGlyph => !string.IsNullOrEmpty(Icon);

    public void UpdateCount() =>
        CountText = Items.Count == 1 ? $"{Items.Count} item" : $"{Items.Count} items";
}
