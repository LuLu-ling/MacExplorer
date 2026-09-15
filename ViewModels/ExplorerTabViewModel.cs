using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacExplorer.Infrastructure;
using MacExplorer.Logging;
using MacExplorer.Models;
using MacExplorer.Native;
using MacExplorer.Services;

namespace MacExplorer.ViewModels;

public sealed partial class ExplorerTabViewModel : ViewModelBase, IDisposable
{
    private readonly FileService _files;
    private readonly ListingService _listing;
    private readonly IconService _icons;
    private readonly DialogCallbacks _dialogs;
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _iconCts;
    private CancellationTokenSource? _sizeCts;
    private DispatcherTimer? _watchTimer;
    private bool _disposed;
    private int _listingVersion;

    public ExplorerTabViewModel(
        FileService files,
        ListingService listing,
        IconService icons,
        DialogCallbacks dialogs,
        string path)
    {
        _files = files;
        _listing = listing;
        _icons = icons;
        _dialogs = dialogs;
        Items = [];
        ViewItems = [];
        Groups = [];
        SelectedItems = [];
        Breadcrumbs = [];
        CurrentPath = path;
        Layout = Config.Layout.KindValue is LayoutKind.Columns ? LayoutKind.Details : Config.Layout.KindValue;
        LayoutSize = Config.Layout.Size;
        SyncGroupingFromConfig();
        Grouping.SettingsChanged += OnGroupingSettingsChanged;
        _ = NavigateAsync(path, record: false);
    }

    public ObservableCollection<FileItem> Items { get; }
    public ObservableCollection<object> ViewItems { get; }
    public ObservableCollection<FileGroup> Groups { get; }
    public ObservableCollection<FileItem> SelectedItems { get; }

    [ObservableProperty] public partial string CurrentPath { get; set; } = SpecialFolders.HomeKey;
    [ObservableProperty] public partial string Title { get; set; } = "Home";
    [ObservableProperty] public partial bool IsSelectedTab { get; set; }
    [ObservableProperty] public partial string PathText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsHome { get; set; } = true;
    [ObservableProperty] public partial bool IsSettings { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? UnavailableTitle { get; set; }
    [ObservableProperty] public partial string? UnavailableMessage { get; set; }
    [ObservableProperty] public partial LayoutKind Layout { get; set; }
    [ObservableProperty] public partial int LayoutSize { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial GroupOption GroupOption { get; set; }
    [ObservableProperty] public partial SortDirection GroupDirection { get; set; }
    [ObservableProperty] public partial GroupByDateUnit GroupByDateUnit { get; set; }
    [ObservableProperty] public partial bool IsGroupOverview { get; set; }

    [ObservableProperty] public partial IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; set; }
    [ObservableProperty] public partial FileItem? PreviewItem { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;
    [ObservableProperty] public partial string SelectionText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool CanGoBack { get; set; }
    [ObservableProperty] public partial bool CanGoForward { get; set; }
    [ObservableProperty] public partial bool CanGoUp { get; set; }

    public bool IsTrash => SpecialFolders.IsTrash(CurrentPath);
    public bool IsTag => SpecialFolders.IsTag(CurrentPath);
    public bool ShowFolder => !IsHome && !IsSettings;
    public string Glyph => Glyphs.ForPath(CurrentPath);
    public IBrush? Marker => IsTag ? MacTags.Resolve(SpecialFolders.TagName(CurrentPath)).Brush : null;
    public bool HasMarker => Marker is not null;

    public bool IsEmpty => Items.Count == 0 && !IsHome && UnavailableTitle is null;
    public string EmptyText => SearchText.Length > 0
        ? "No items match your search"
        : IsTag
            ? "No items with this tag"
            : "This folder is empty";
    public LayoutMetrics Metrics => LayoutMetrics.For(LayoutSize);
    public DetailsColumns Columns => DetailsColumns.Shared;
    public bool HasSelection => SelectedItems.Count > 0;
    public bool IsGrouped => GroupOption is not GroupOption.None;
    public bool CanGroupByOriginalFolder => IsTrash;
    public bool CanGroupByDateDeleted => IsTrash;
    public bool CanGroupByFolderPath => IsTag || SearchText.Length > 0;
    public bool CanGroupBySyncStatus => false;
    public bool HasSingleSelection => SelectedItems.Count == 1;

    public async Task NavigateAsync(string path, bool record = true)
    {
        if (_disposed) return;
        path = Normalize(path);
        if (record && !string.Equals(CurrentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            _back.Push(CurrentPath);
            _forward.Clear();
        }

        CurrentPath = path;
        IsHome = path == SpecialFolders.HomeKey;
        IsSettings = path == SpecialFolders.SettingsKey;
        Title = IsHome ? "Home" : IsSettings ? "Settings" : IsTag ? SpecialFolders.TagName(path)
            : Path.GetFileName(path.TrimEnd('/')) is { Length: > 0 } name ? name : path;
        PathText = IsHome ? "Home" : IsSettings ? "Settings" : IsTag ? SpecialFolders.TagName(path) : path;
        Breadcrumbs = PathUtil.Breadcrumbs(path);
        UpdateNav();
        LogWrapper.Info("Explorer", $"Navigate {PathText}");
        NotifyGroupingAvailability();
        NormalizeGrouping();
        await ReloadAsync();
        if (_disposed) return;
        OnPropertyChanged(nameof(ShowFolder));
        OnPropertyChanged(nameof(IsTrash));
        OnPropertyChanged(nameof(IsTag));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(Marker));
        OnPropertyChanged(nameof(HasMarker));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));

        RememberRecent(path);
    }

    public async Task ReloadAsync()
    {
        if (_disposed) return;
        var version = ++_listingVersion;
        AttachWatcher();
        UnavailableTitle = null;
        UnavailableMessage = null;
        if (IsHome || IsSettings)
        {
            IsBusy = false;
            ClearItems();
            SetSelection([]);
            StatusText = string.Empty;
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        IsBusy = true;
        try
        {
            var keep = SelectedItems.Select(i => i.Path).ToHashSet(StringComparer.Ordinal);
            var path = CurrentPath;
            var listed = await Task.Run(() => _listing.List(path));
            if (_disposed || version != _listingVersion) return;
            listed = _listing.Filter(listed, SearchText);
            Items.Clear();
            foreach (var item in listed)
                Items.Add(item);
            RebuildView();
            SetSelection(Items.Where(i => keep.Contains(i.Path)).ToList());
            StatusText = $"{Items.Count} item{(Items.Count == 1 ? string.Empty : "s")}";
            LogWrapper.Debug("Explorer", $"Listed {Items.Count} item(s) in {CurrentPath}");
            OnPropertyChanged(nameof(IsEmpty));
            _ = LoadIconsAsync();
            _ = LoadFolderSizesAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            if (_disposed || version != _listingVersion) return;
            ClearItems();
            SetSelection([]);
            UnavailableTitle = "Access denied";
            UnavailableMessage = "macOS blocked this folder. Grant Files and Folders permission in System Settings.";
            LogWrapper.Warn(ex, "Explorer", $"Access denied: {CurrentPath}");
        }
        catch (DirectoryNotFoundException ex)
        {
            if (_disposed || version != _listingVersion) return;
            ClearItems();
            SetSelection([]);
            UnavailableTitle = "Location is unavailable";
            UnavailableMessage = "This folder does not exist or cannot be found.";
            LogWrapper.Warn(ex, "Explorer", $"Not found: {CurrentPath}");
        }
        catch (Exception ex)
        {
            if (_disposed || version != _listingVersion) return;
            ClearItems();
            SetSelection([]);
            UnavailableTitle = "Couldn't open this folder";
            UnavailableMessage = ex.Message;
            LogWrapper.Warn(ex, "Explorer", $"List failed: {CurrentPath}");
        }
        finally
        {
            if (!_disposed && version == _listingVersion)
            {
                IsBusy = false;
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(IsTrash));
                OnPropertyChanged(nameof(CanGroupByOriginalFolder));
                OnPropertyChanged(nameof(CanGroupByDateDeleted));
                OnPropertyChanged(nameof(CanGroupByFolderPath));
            }
        }
    }

    public void SetSelection(IEnumerable<FileItem> items)
    {
        var selected = items as IList<FileItem> ?? items.ToList();
        var set = selected.ToHashSet();
        if (SelectedItems.Count == set.Count && SelectedItems.All(set.Contains))
            return;

        foreach (var item in Items)
            item.IsSelected = set.Contains(item);
        SelectedItems.Clear();
        foreach (var item in selected)
            SelectedItems.Add(item);
        PreviewItem = SelectedItems.Count == 1 ? SelectedItems[0] : null;
        SelectionText = SelectedItems.Count switch
        {
            0 => string.Empty,
            1 => SelectedItems[0].DisplayName,
            _ => $"{SelectedItems.Count} items selected"
        };
        CopyCommand.NotifyCanExecuteChanged();
        CutCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged();
        ShareCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasSingleSelection));
    }

    [RelayCommand]
    public Task BackAsync()
    {
        if (_back.Count == 0) return Task.CompletedTask;
        _forward.Push(CurrentPath);
        return NavigateAsync(_back.Pop(), record: false);
    }

    [RelayCommand]
    public Task ForwardAsync()
    {
        if (_forward.Count == 0) return Task.CompletedTask;
        _back.Push(CurrentPath);
        return NavigateAsync(_forward.Pop(), record: false);
    }

    [RelayCommand]
    public Task UpAsync()
    {
        if (IsHome || IsSettings || IsTag) return Task.CompletedTask;
        var parent = Path.GetDirectoryName(CurrentPath.TrimEnd('/'));
        return NavigateAsync(string.IsNullOrEmpty(parent) ? "/" : parent);
    }

    [RelayCommand]
    public Task RefreshAsync() => ReloadAsync();

    [RelayCommand]
    public async Task OpenAsync(FileItem? item = null)
    {
        item ??= SelectedItems.Count == 1 ? SelectedItems[0] : null;
        if (item is null) return;
        if (item.IsNavigable)
        {
            await NavigateAsync(item.Path);
            return;
        }

        if (!_files.Open(item.Path))
            await _dialogs.Error("Couldn't open item", item.DisplayName);
        else
            RememberRecent(item.Path);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void Copy()
    {
        _files.Copy(SelectedItems.Select(i => i.Path).ToList());
        foreach (var item in Items)
            item.IsCut = false;
        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void Cut()
    {
        _files.Cut(SelectedItems.Select(i => i.Path).ToList());
        var selected = SelectedItems.Select(i => i.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var item in Items)
            item.IsCut = selected.Contains(item.Path);
        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public async Task PasteAsync()
    {
        if (IsHome || IsSettings || IsTag) return;
        var pasted = await _files.PasteAsync(CurrentPath, name => _dialogs.Conflict(name));
        if (pasted.Count > 0)
            await ReloadAsync();
    }

    public async Task DropFilesAsync(IReadOnlyList<string> paths, string destination, bool move)
    {
        if (paths.Count == 0 || SpecialFolders.IsVirtual(destination))
            return;
        var completed = await _files.TransferAsync(paths, destination, move, name => _dialogs.Conflict(name));
        if (completed.Count > 0)
            await ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    public void Rename()
    {
        if (SelectedItems.Count != 1) return;
        var item = SelectedItems[0];
        foreach (var other in Items)
            other.IsRenaming = false;
        item.RenameText = item.Name;
        item.IsRenaming = true;
    }

    public async Task CommitRenameAsync(FileItem item)
    {
        item.IsRenaming = false;
        var name = item.RenameText.Trim();
        if (string.IsNullOrEmpty(name) || name == item.Name)
            return;
        var result = _files.Rename(item.Path, name);
        if (!result.Ok)
            await _dialogs.Error("Couldn't rename", result.Error ?? "Unknown error");
        await ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public async Task DeleteAsync()
    {
        var names = string.Join(", ", SelectedItems.Take(3).Select(i => i.DisplayName));
        if (SelectedItems.Count > 3)
            names += "…";
        var ok = await _dialogs.Confirm(
            IsTrash ? "Delete permanently?" : "Move to Trash?",
            IsTrash
                ? $"These items will be deleted immediately:\n{names}"
                : $"Move {SelectedItems.Count} item(s) to Trash?\n{names}",
            IsTrash ? "Delete" : "Move to Trash",
            "Cancel");
        if (!ok) return;

        foreach (var item in SelectedItems.ToArray())
        {
            var result = IsTrash ? _files.Delete(item.Path) : _files.Trash(item.Path);
            if (!result.Ok)
                await _dialogs.Error("Couldn't delete item", result.Error ?? item.DisplayName);
        }

        await ReloadAsync();
    }

    [RelayCommand]
    public async Task NewFolderAsync()
    {
        if (IsHome || IsSettings || IsTag) return;
        var result = _files.NewFolder(CurrentPath);
        if (!result.Ok)
        {
            await _dialogs.Error("Couldn't create folder", result.Error ?? "");
            return;
        }

        await ReloadAsync();
        var created = Items.FirstOrDefault(i => i.Path == result.ResultPath);
        if (created is not null)
        {
            SetSelection([created]);
            Rename();
        }
    }

    [RelayCommand]
    public async Task NewFileAsync()
    {
        if (IsHome || IsSettings || IsTag) return;
        var result = _files.NewFile(CurrentPath);
        if (!result.Ok)
        {
            await _dialogs.Error("Couldn't create file", result.Error ?? "");
            return;
        }

        await ReloadAsync();
        var created = Items.FirstOrDefault(i => i.Path == result.ResultPath);
        if (created is not null)
        {
            SetSelection([created]);
            Rename();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void Share(string? service)
    {
        var name = string.IsNullOrEmpty(service) ? "com.apple.share.AirDrop.send" : service;
        _files.Share(SelectedItems.Select(i => i.Path).ToList(), name);
    }

    [RelayCommand]
    public void Reveal()
    {
        var path = SelectedItems.Count == 1 ? SelectedItems[0].Path : CurrentPath;
        _files.Reveal(path);
    }

    [RelayCommand]
    public async Task EmptyTrashAsync()
    {
        if (!IsTrash) return;
        if (!await _dialogs.Confirm("Empty Trash?", "Items will be deleted immediately.", "Empty Trash", "Cancel"))
            return;
        foreach (var item in Items.ToArray())
            _files.Delete(item.Path);
        await ReloadAsync();
    }

    [RelayCommand]
    public void SelectAll() => SetSelection(Items);

    [RelayCommand]
    public void InvertSelection()
    {
        var selected = SelectedItems.ToHashSet();
        SetSelection(Items.Where(i => !selected.Contains(i)));
    }

    [RelayCommand]
    public void ClearSelection() => SetSelection([]);

    [RelayCommand]
    public void SetLayout(string kind)
    {
        if (!Enum.TryParse<LayoutKind>(kind, out var layout) || layout is LayoutKind.Columns)
            return;
        Layout = layout;
        Config.Layout.Kind = (int)layout;
        _ = LoadIconsAsync();
    }

    [RelayCommand]
    public void SetSort(string field)
    {
        if (!Enum.TryParse<SortField>(field, out var sort))
            return;
        if (Config.Layout.SortFieldValue == sort)
        {
            Config.Layout.SortDirection = Config.Layout.SortDirectionValue is SortDirection.Ascending
                ? (int)SortDirection.Descending
                : (int)SortDirection.Ascending;
        }
        else
        {
            Config.Layout.SortField = (int)sort;
            Config.Layout.SortDirection = (int)SortDirection.Ascending;
        }
        RebuildView();
    }

    [RelayCommand]
    public void SetGroup(string spec)
    {
        var parts = spec.Split(':');
        if (!Enum.TryParse<GroupOption>(parts[0], out var option))
            return;
        Config.Layout.GroupOption = (int)option;
        if (parts.Length > 1 && Enum.TryParse<GroupByDateUnit>(parts[1], out var unit))
            Config.Layout.GroupByDateUnit = (int)unit;
        Grouping.NotifySettingsChanged();
    }

    [RelayCommand]
    public void SetGroupDirection(string spec)
    {
        if (!Enum.TryParse<SortDirection>(spec, out var direction))
            return;
        Config.Layout.GroupDirection = (int)direction;
        Grouping.NotifySettingsChanged();
    }

    public async Task OpenAddressAsync(string address)
    {
        var path = address.Trim();
        if (path.Length == 0 || _disposed) return;
        if (path.Equals("Home", StringComparison.OrdinalIgnoreCase))
            path = SpecialFolders.HomeKey;
        else if (path.Equals("Settings", StringComparison.OrdinalIgnoreCase))
            path = SpecialFolders.SettingsKey;
        if (SpecialFolders.IsVirtual(path))
        {
            await NavigateAsync(path);
            return;
        }

        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal))
            path = SpecialFolders.UserHome + path[1..];
        else if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
            path = uri.LocalPath;
        try
        {
            path = Path.GetFullPath(path, SpecialFolders.IsVirtual(CurrentPath) ? SpecialFolders.UserHome : CurrentPath);
        }
        catch (ArgumentException)
        {
            await _dialogs.Error("Invalid address", "Enter a valid folder path.");
            return;
        }
        if (Directory.Exists(path))
            await NavigateAsync(path);
        else if (File.Exists(path))
            _files.Open(path);
        else
            await _dialogs.Error("Location is unavailable", $"“{path}” could not be found.");
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!IsHome && !IsSettings)
            _ = ReloadAsync();
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(CanGroupByFolderPath));
    }

    partial void OnLayoutSizeChanged(int value)
    {
        Config.Layout.Size = value;
        OnPropertyChanged(nameof(Metrics));
        _ = LoadIconsAsync();
    }

    public void ToggleTag(string name)
    {
        var items = SelectedItems.ToArray();
        if (items.Length == 0)
            return;

        var remove = items.All(i => i.Tags.Any(t => t.Name == name));
        var tag = items.SelectMany(i => i.Tags).FirstOrDefault(t => t.Name == name);
        if (string.IsNullOrEmpty(tag.Name))
            tag = MacTags.Resolve(name);
        var plan = new (FileItem Item, IReadOnlyList<FileTag> Tags)[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            IReadOnlyList<FileTag> next = remove
                ? item.Tags.Where(t => t.Name != name).ToArray()
                : item.Tags.Any(t => t.Name == name)
                    ? item.Tags
                    : [.. item.Tags, tag];
            plan[i] = (item, next);
        }

        _ = ApplyTagChangeAsync(plan);
    }

    public async Task RemoveTagsAsync()
    {
        var items = SelectedItems.Where(static i => i.HasTags).ToArray();
        if (items.Length == 0)
            return;
        if (!await _dialogs.Confirm(
                "Remove tags?",
                "Remove all tags from the selected items?",
                "Remove",
                "Cancel"))
            return;

        await ApplyTagChangeAsync(items.Select(static i => (i, (IReadOnlyList<FileTag>)[])).ToArray());
    }

    private async Task ApplyTagChangeAsync((FileItem Item, IReadOnlyList<FileTag> Tags)[] plan)
    {
        if (plan.Length == 0)
            return;

        await Task.Run(() =>
        {
            foreach (var (item, tags) in plan)
                MacTags.Write(item.Path, tags);
        });
        MacTags.NotifyLearned();
        if (IsTag)
        {
            await ReloadAsync();
            return;
        }

        var size = Metrics.IconPixels(Layout);
        foreach (var (item, _) in plan)
        {
            item.Tags = MacTags.Read(item.Path);
            if (!item.IsDirectory)
                continue;
            _icons.Invalidate(item.Path);
            MacWorkspace.NoteChanged(item.Path);
            try
            {
                var icon = await _icons.GetAsync(item.Path, size);
                if (icon is not null)
                    item.Icon = icon;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        if (GroupOption is GroupOption.FileTag)
            RebuildView();
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_listingVersion;
        _watcher?.Dispose();
        _iconCts?.Cancel();
        _iconCts?.Dispose();
        _sizeCts?.Cancel();
        _sizeCts?.Dispose();
        _watchTimer?.Stop();
        Grouping.SettingsChanged -= OnGroupingSettingsChanged;
    }

    private void OnGroupingSettingsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            SyncGroupingFromConfig();
            RebuildView();
        });
    }

    private void SyncGroupingFromConfig()
    {
        GroupOption = Config.Layout.GroupOptionValue;
        GroupDirection = Config.Layout.GroupDirectionValue;
        GroupByDateUnit = Config.Layout.GroupByDateUnitValue;
        OnPropertyChanged(nameof(IsGrouped));
        NotifyGroupingAvailability();
    }

    private void NotifyGroupingAvailability()
    {
        OnPropertyChanged(nameof(CanGroupByOriginalFolder));
        OnPropertyChanged(nameof(CanGroupByDateDeleted));
        OnPropertyChanged(nameof(CanGroupByFolderPath));
        OnPropertyChanged(nameof(IsGrouped));
    }

    private void NormalizeGrouping()
    {
        var option = Config.Layout.GroupOptionValue;
        if (Grouping.Allowed(option, IsTrash, IsTag || SearchText.Length > 0))
            return;
        Config.Layout.GroupOption = (int)GroupOption.None;
        Grouping.NotifySettingsChanged();
    }

    private void ClearItems()
    {
        Items.Clear();
        ViewItems.Clear();
        Groups.Clear();
        IsGroupOverview = false;
    }

    public void RebuildView()
    {
        var files = Items.ToList();
        var option = GroupOption;
        if (!Grouping.Allowed(option, IsTrash, IsTag || SearchText.Length > 0))
            option = GroupOption.None;

        ViewItems.Clear();
        Groups.Clear();
        if (option is GroupOption.None)
        {
            IsGroupOverview = false;
            var sorted = ListingService.Sort(files);
            ReplaceItems(sorted);
            foreach (var item in Items)
                ViewItems.Add(item);
            return;
        }

        var groups = Grouping.Arrange(ListingService.Sort(files), option, GroupByDateUnit, GroupDirection);
        Items.Clear();
        foreach (var group in groups)
        {
            Groups.Add(group);
            ViewItems.Add(group);
            foreach (var item in group.Items)
            {
                Items.Add(item);
                ViewItems.Add(item);
            }
        }
    }

    private void ReplaceItems(IReadOnlyList<FileItem> files)
    {
        if (Items.Count == files.Count)
        {
            var same = true;
            for (var i = 0; i < files.Count; i++)
            {
                if (ReferenceEquals(Items[i], files[i]))
                    continue;
                same = false;
                break;
            }
            if (same)
                return;
        }

        Items.Clear();
        foreach (var item in files)
            Items.Add(item);
    }

    private void UpdateNav()
    {
        CanGoBack = _back.Count > 0;
        CanGoForward = _forward.Count > 0;
        CanGoUp = !IsHome && !IsSettings && !IsTag && CurrentPath is not "/";
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
        UpCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadIconsAsync()
    {
        _iconCts?.Cancel();
        _iconCts = new CancellationTokenSource();
        var token = _iconCts.Token;
        var size = Metrics.IconPixels(Layout);
        foreach (var item in Items.ToArray())
        {
            if (token.IsCancellationRequested)
                return;
            try
            {
                var icon = await _icons.GetAsync(item.Path, size, token);
                if (icon is not null && !token.IsCancellationRequested)
                    item.Icon = icon;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task LoadFolderSizesAsync()
    {
        _sizeCts?.Cancel();
        _sizeCts = new CancellationTokenSource();
        var token = _sizeCts.Token;
        var folders = Items.Where(static i => i.IsDirectory).ToArray();
        try
        {
            await Parallel.ForEachAsync(folders, new ParallelOptions
            {
                MaxDegreeOfParallelism = 2,
                CancellationToken = token
            }, async (item, ct) =>
            {
                var size = await Task.Run(() => ListingService.DirectorySize(item.Path), ct);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    item.Size = size;
                    item.SizeKnown = true;
                });
            });
            if (GroupOption is GroupOption.Size)
                RebuildView();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void AttachWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (IsHome || IsSettings || !Directory.Exists(CurrentPath))
            return;
        try
        {
            _watcher = new FileSystemWatcher(CurrentPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };
            _watcher.Created += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.Renamed += OnFsEvent;
            _watcher.Changed += OnFsEvent;
        }
        catch
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            _watchTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _watchTimer.Tick -= WatchTick;
            _watchTimer.Tick += WatchTick;
            _watchTimer.Stop();
            _watchTimer.Start();
        });
    }

    private async void WatchTick(object? sender, EventArgs e)
    {
        _watchTimer?.Stop();
        await ReloadAsync();
    }

    private void RememberRecent(string path)
    {
        if (SpecialFolders.IsVirtual(path) || !PathUtil.Exists(path))
            return;
        var recents = Config.Home.Recents.ToList();
        recents.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        recents.Insert(0, path);
        if (recents.Count > 24)
            recents.RemoveRange(24, recents.Count - 24);
        Config.Home.Recents = recents;
    }

    private static string Normalize(string path)
    {
        if (SpecialFolders.IsVirtual(path))
            return path;
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}
