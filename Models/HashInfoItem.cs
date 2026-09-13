using CommunityToolkit.Mvvm.ComponentModel;

namespace MacExplorer.Models;

public sealed partial class HashInfoItem : ObservableObject
{
    [ObservableProperty] public partial string Algorithm { get; set; } = string.Empty;
    [ObservableProperty] public partial string? HashValue { get; set; }
    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial bool IsCalculating { get; set; }
    [ObservableProperty] public partial bool IsCalculated { get; set; }
    [ObservableProperty] public partial bool IsEnabled { get; set; }
}
