using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Humanizer;

namespace MacExplorer.Models;

public sealed partial class FileItem : ObservableObject
{
    public required string Path { get; init; }
    public required string Name { get; set; }
    public required string DisplayName { get; set; }
    public required bool IsDirectory { get; init; }
    public bool IsBundle { get; init; }
    public bool IsHidden { get; init; }
    [ObservableProperty] public partial long Size { get; set; }
    [ObservableProperty] public partial bool SizeKnown { get; set; }
    public DateTime Modified { get; init; }
    public DateTime Created { get; init; }
    public string ItemType { get; init; } = "Item";
    public string Extension { get; init; } = string.Empty;
    public string? OriginalFolder { get; init; }
    public string OriginalFolderName { get; init; } = "";
    public DateTime DateDeleted { get; init; }
    public string? GroupKey { get; set; }

    [ObservableProperty] public partial Bitmap? Icon { get; set; }
    [ObservableProperty] public partial bool IsCut { get; set; }
    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial bool IsRenaming { get; set; }
    [ObservableProperty] public partial bool IsDropTarget { get; set; }
    [ObservableProperty] public partial string RenameText { get; set; } = string.Empty;
    [ObservableProperty] public partial IReadOnlyList<FileTag> Tags { get; set; } = [];



    partial void OnIsCutChanged(bool value) => OnPropertyChanged(nameof(CutOpacity));
    partial void OnSizeChanged(long value) => NotifySize();
    partial void OnSizeKnownChanged(bool value) => NotifySize();
    partial void OnTagsChanged(IReadOnlyList<FileTag> value)
    {
        OnPropertyChanged(nameof(NameDots));
        OnPropertyChanged(nameof(HasNameDots));
        OnPropertyChanged(nameof(HasTags));
    }


    public bool IsNavigable => IsDirectory && !IsBundle;
    public string SizeText => IsDirectory && !SizeKnown ? "Calculating…" : FormatSize(Size);
    public double CutOpacity => IsCut ? 0.45 : 1;
    public string ModifiedText => Modified.ToString("yyyy/MM/dd HH:mm");
    public string CreatedText => Created.ToString("yyyy/MM/dd HH:mm");

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.##} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.##} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB"
    };
    public string ModifiedHuman => Modified.Humanize();
    public string TypeAndSize => IsDirectory && !SizeKnown ? ItemType : $"{ItemType}  ·  {SizeText}";
    public IReadOnlyList<FileTag> NameDots => Tags;
    public bool HasNameDots => Tags.Count > 0;
    public bool HasTags => Tags.Count > 0;


    private void NotifySize()
    {
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(TypeAndSize));
    }
}
