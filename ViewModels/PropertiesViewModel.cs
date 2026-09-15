using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacExplorer.Models;
using MacExplorer.Native;
using MacExplorer.Services;

namespace MacExplorer.ViewModels;

public sealed partial class PropertiesViewModel : ViewModelBase, IDisposable
{
    private readonly FileService _files;
    private readonly IconService _icons;
    private readonly string _originalName;

    public PropertiesViewModel(IReadOnlyList<FileItem> items, FileService files, IconService icons)
    {
        _files = files;
        _icons = icons;
        Items = items.Count > 0 ? items : [];
        IsSingle = Items.Count == 1;
        var primary = IsSingle ? Items[0] : null;
        CanRename = primary is not null;
        IsDirectory = primary?.IsDirectory ?? false;
        CanShowHashes = primary is { IsDirectory: false };
        Hashes = CanShowHashes ? new HashesViewModel(primary!.Path) : null;
        SelectedPage = "General";

        if (primary is not null)
        {
            DisplayName = primary.DisplayName;
            _originalName = primary.Name;
            ItemName = _originalName;
            ItemType = primary.ItemType;
            Location = Path.GetDirectoryName(primary.Path) ?? primary.Path;
            PathText = primary.Path;
            CreatedText = primary.CreatedText;
            ModifiedText = primary.ModifiedText;
            SizeText = primary.SizeText;
            Title = $"{DisplayName} – Properties";
        }
        else
        {
            var fileCount = Items.Count(i => !i.IsDirectory);
            var folderCount = Items.Count - fileCount;
            DisplayName = $"{fileCount} file{(fileCount == 1 ? string.Empty : "s")}, {folderCount} folder{(folderCount == 1 ? string.Empty : "s")}";
            _originalName = string.Empty;
            var types = Items.Select(i => i.ItemType).Distinct().ToArray();
            ItemType = types.Length == 1 ? types[0] : "Multiple types";
            var dirs = Items.Select(i => Path.GetDirectoryName(i.Path)).Where(static p => p is not null).Distinct().ToArray();
            Location = dirs.Length == 1 ? dirs[0]! : string.Empty;
            Title = "Properties";
        }

        _ = LoadAsync(primary);
    }

    public IReadOnlyList<FileItem> Items { get; }
    public bool IsSingle { get; }
    public bool CanRename { get; }
    public bool IsDirectory { get; }
    public string DisplayName { get; }
    public string Title { get; }

    [ObservableProperty] public partial string SelectedPage { get; set; }
    [ObservableProperty] public partial string ItemName { get; set; } = string.Empty;
    [ObservableProperty] public partial string ItemType { get; set; } = string.Empty;
    [ObservableProperty] public partial string Location { get; set; } = string.Empty;
    [ObservableProperty] public partial string PathText { get; set; } = string.Empty;
    [ObservableProperty] public partial string SizeText { get; set; } = string.Empty;
    [ObservableProperty] public partial string SizeOnDiskText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CreatedText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ModifiedText { get; set; } = string.Empty;
    [ObservableProperty] public partial string AccessedText { get; set; } = string.Empty;
    [ObservableProperty] public partial string AttributesText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ContentsText { get; set; } = string.Empty;
    [ObservableProperty] public partial Bitmap? Icon { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    public bool IsGeneral => SelectedPage == "General";
    public bool IsHashes => SelectedPage == "Hashes";
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool CanShowHashes { get; }
    public HashesViewModel? Hashes { get; }

    public bool HasItemType => !string.IsNullOrWhiteSpace(ItemType);
    public bool HasLocation => !string.IsNullOrWhiteSpace(Location);
    public bool HasSize => !string.IsNullOrWhiteSpace(SizeText);
    public bool HasSizeOnDisk => !string.IsNullOrWhiteSpace(SizeOnDiskText);
    public bool HasContents => !string.IsNullOrWhiteSpace(ContentsText);
    public bool HasPath => !string.IsNullOrWhiteSpace(PathText);
    public bool HasCreated => !string.IsNullOrWhiteSpace(CreatedText);
    public bool HasModified => !string.IsNullOrWhiteSpace(ModifiedText);
    public bool HasAccessed => !string.IsNullOrWhiteSpace(AccessedText);
    public bool HasDates => HasCreated || HasModified || HasAccessed;
    public bool HasAttributes => !string.IsNullOrWhiteSpace(AttributesText);

    partial void OnSelectedPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsGeneral));
        OnPropertyChanged(nameof(IsHashes));
    }

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private void SelectPage(string page) => SelectedPage = page;

    public string? SavedPath { get; private set; }

    public MacFileResult? Save()
    {
        if (!CanRename || Items.Count != 1)
            return null;
        var name = ItemName.Trim();
        if (string.IsNullOrEmpty(name) || name == _originalName)
            return null;
        var result = _files.Rename(Items[0].Path, name);
        if (result.Ok)
            SavedPath = result.ResultPath;
        return result;
    }

    public void Dispose() => Hashes?.Dispose();

    private async Task LoadAsync(FileItem? primary)
    {
        if (primary is not null)
            Icon = await _icons.GetAsync(primary.Path, 48);

        var snapshot = await Task.Run(() => Inspect(primary));
        SizeText = snapshot.Size;
        SizeOnDiskText = snapshot.SizeOnDisk;
        ContentsText = snapshot.Contents;
        AccessedText = snapshot.Accessed;
        AttributesText = snapshot.Attributes;
        Error = snapshot.Error;
        OnPropertyChanged(nameof(HasSize));
        OnPropertyChanged(nameof(HasSizeOnDisk));
        OnPropertyChanged(nameof(HasAccessed));
        OnPropertyChanged(nameof(HasDates));
        OnPropertyChanged(nameof(HasAttributes));
    }

    private sealed record Inspected(string Size, string SizeOnDisk, string Contents, string Accessed, string Attributes, string? Error);

    private Inspected Inspect(FileItem? primary)
    {
        try
        {
            if (Items.Count != 1)
            {
                long total = 0;
                foreach (var item in Items)
                {
                    try { total += SizeOf(item); }
                    catch { /* skip */ }
                }

                var formatted = FormatSize(total);
                return new Inspected(formatted, formatted, string.Empty, string.Empty, string.Empty, null);
            }

            var path = primary!.Path;
            var info = primary.IsDirectory ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
            var accessed = info.LastAccessTime.ToString("yyyy/MM/dd HH:mm");
            var attributes = File.GetAttributes(path).ToString();
            if (primary.IsDirectory)
            {
                var entries = Directory.GetFileSystemEntries(path);
                var dirs = entries.Count(Directory.Exists);
                var files = entries.Length - dirs;
                var size = FormatSize(SizeOf(primary));
                return new Inspected(size, size, $"{files} files, {dirs} folders", accessed, attributes, null);
            }

            var length = FormatSize(primary.SizeKnown ? primary.Size : new FileInfo(path).Length);
            return new Inspected(length, length, string.Empty, accessed, attributes, null);
        }
        catch (Exception ex)
        {
            return new Inspected(SizeText, SizeOnDiskText, ContentsText, AccessedText, AttributesText, ex.Message);
        }
    }


    private static long SizeOf(FileItem item) =>
        item.IsDirectory
            ? item.SizeKnown ? item.Size : FolderSize.Of(item.Path)
            : item.SizeKnown ? item.Size : new FileInfo(item.Path).Length;

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.##} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.##} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB"
    };
}
