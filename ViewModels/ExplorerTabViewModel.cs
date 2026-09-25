using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacExplorer.Infrastructure;
using MacExplorer.Localization;
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
    private DialogCallbacks _dialogs;
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _iconCts;
    private CancellationTokenSource? _sizeCts;
    private DispatcherTimer? _watchTimer;
    private bool _disposed;
    private int _listingVersion;
    private string? _unavailableTitleKey;
    private string? _unavailableMessageKey;
    private string? _unavailableRawMessage;

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

    internal void AttachDialogs(DialogCallbacks dialogs) => _dialogs = dialogs;

    public ObservableCollection<FileItem> Items { get; }
    public ObservableCollection<object> ViewItems { get; }
    public ObservableCollection<FileGroup> Groups { get; }
    public ObservableCollection<FileItem> SelectedItems { get; }

    [ObservableProperty] public partial string CurrentPath { get; set; } = SpecialFolders.HomeKey;
    [ObservableProperty] public partial string Title { get; set; } = Lang.Text("Places.Home");
    [ObservableProperty] public partial bool IsSelectedTab { get; set; }
    [ObservableProperty] public partial bool IsClosing { get; set; }
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
    public SortField SortField => Config.Layout.SortFieldValue;
    public SortDirection SortDirection => Config.Layout.SortDirectionValue;
    public FolderPriority FolderPriority => Config.Layout.FolderPriorityValue;

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
        ? Lang.Text("Explorer.Empty.Search")
        : IsTag
            ? Lang.Text("Explorer.Empty.Tag")
            : Lang.Text("Explorer.Empty.Folder");
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
        ApplyLocalizedChrome();
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
        _unavailableTitleKey = null;
        _unavailableMessageKey = null;
        _unavailableRawMessage = null;
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
            StatusText = Items.Count == 1
                ? Lang.Text("Explorer.Status.Item", Items.Count)
                : Lang.Text("Explorer.Status.Items", Items.Count);
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
            SetUnavailable("Explorer.AccessDenied.Title", "Explorer.AccessDenied.Message");
            LogWrapper.Warn(ex, "Explorer", $"Access denied: {CurrentPath}");
        }
        catch (DirectoryNotFoundException ex)
        {
            if (_disposed || version != _listingVersion) return;
            ClearItems();
            SetSelection([]);
            SetUnavailable("Explorer.Unavailable.Title", "Explorer.Unavailable.Message");
            LogWrapper.Warn(ex, "Explorer", $"Not found: {CurrentPath}");
        }
        catch (Exception ex)
        {
            if (_disposed || version != _listingVersion) return;
            ClearItems();
            SetSelection([]);
            SetUnavailable("Explorer.OpenFailed.Title", message: ex.Message);
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
            _ => Lang.Text("Explorer.Status.Selected", SelectedItems.Count)
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
            await _dialogs.Error(Lang.Text("Explorer.OpenItemFailed"), item.DisplayName);
        else
            RememberRecent(item.Path);
    }

    public async Task OpenWithAsync(string application, bool always = false)
    {
        var items = SelectedItems;
        if (items.Count == 0)
            return;
        var paths = items.Select(static i => i.Path).ToList();
        var name = items[0].DisplayName;
        if (!_files.OpenWith(paths, application, always))
            await _dialogs.Error(Lang.Text("Explorer.OpenItemFailed"), name);
        else
            foreach (var path in paths)
                RememberRecent(path);
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
        if (!item.IsRenaming)
            return;
        item.IsRenaming = false;
        var name = item.RenameText.Trim();
        if (string.IsNullOrEmpty(name) || name == item.Name)
            return;
        var result = _files.Rename(item.Path, name);
        if (!result.Ok)
            await _dialogs.Error(Lang.Text("Explorer.RenameFailed"), result.Error ?? Lang.Text("Common.Error.Unknown"));
        await ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public async Task DeleteAsync()
    {
        var names = string.Join(", ", SelectedItems.Take(3).Select(i => i.DisplayName));
        if (SelectedItems.Count > 3)
            names += "…";
        var ok = await _dialogs.Confirm(
            Lang.Text(IsTrash ? "Dialog.DeletePermanently.Title" : "Dialog.MoveToTrash.Title"),
            IsTrash
                ? Lang.Text("Dialog.DeletePermanently.Message", names)
                : Lang.Text("Dialog.MoveToTrash.Message", SelectedItems.Count, names),
            Lang.Text(IsTrash ? "Common.Action.Delete" : "Dialog.MoveToTrash.Action"),
            Lang.Text("Common.Action.Cancel"));
        if (!ok) return;

        foreach (var item in SelectedItems.ToArray())
        {
            var result = IsTrash ? _files.Delete(item.Path) : _files.Trash(item.Path);
            if (!result.Ok)
                await _dialogs.Error(Lang.Text("Explorer.DeleteFailed"), result.Error ?? item.DisplayName);
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
            await _dialogs.Error(Lang.Text("Explorer.CreateFolderFailed"), result.Error ?? "");
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
    public async Task NewFileAsync(string? extension)
    {
        if (IsHome || IsSettings || IsTag) return;
        var result = _files.NewFile(CurrentPath, extension);
        if (!result.Ok)
        {
            await _dialogs.Error(Lang.Text("Explorer.CreateFileFailed"), result.Error ?? "");
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
        if (!await _dialogs.Confirm(
                Lang.Text("Dialog.EmptyTrash.Title"),
                Lang.Text("Dialog.EmptyTrash.Message"),
                Lang.Text("Dialog.EmptyTrash.Action"),
                Lang.Text("Common.Action.Cancel")))
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
        if (Config.Layout.SortFieldValue != sort)
        {
            Config.Layout.SortField = (int)sort;
            Config.Layout.SortDirection = (int)SortDirection.Ascending;
        }
        RebuildView();
    }

    [RelayCommand]
    public void SetSortDirection(string spec)
    {
        if (!Enum.TryParse<SortDirection>(spec, out var direction))
            return;
        Config.Layout.SortDirection = (int)direction;
        RebuildView();
    }

    [RelayCommand]
    public void SetFolderPriority(string spec)
    {
        if (!Enum.TryParse<FolderPriority>(spec, out var priority))
            return;
        Config.Layout.FolderPriority = (int)priority;
        RebuildView();
    }

    public void ToggleSort(string field)
    {
        if (!Enum.TryParse<SortField>(field, out var sort))
            return;
        if (Config.Layout.SortFieldValue == sort)
        {
            SetSortDirection(Config.Layout.SortDirectionValue is SortDirection.Ascending
                ? "Descending"
                : "Ascending");
            return;
        }

        SetSort(field);
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
        if (IsHomeAlias(path))
            path = SpecialFolders.HomeKey;
        else if (IsSettingsAlias(path))
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
            await _dialogs.Error(Lang.Text("Explorer.InvalidAddress.Title"), Lang.Text("Explorer.InvalidAddress.Message"));
            return;
        }
        if (Directory.Exists(path))
            await NavigateAsync(path);
        else if (File.Exists(path))
            _files.Open(path);
        else
            await _dialogs.Error(Lang.Text("Explorer.Unavailable.Title"), Lang.Text("Explorer.NotFound.Message", path));
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
                Lang.Text("Dialog.RemoveTags.Title"),
                Lang.Text("Dialog.RemoveTags.Message"),
                Lang.Text("Common.Action.Remove"),
                Lang.Text("Common.Action.Cancel")))
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


    protected override void OnLanguageChanged()
    {
        if (_disposed) return;
        ApplyLocalizedChrome();
        Breadcrumbs = PathUtil.Breadcrumbs(CurrentPath);
        foreach (var item in Items)
            item.NotifyLocalized();
        RebuildView();
        if (Items.Count > 0)
            StatusText = Items.Count == 1
                ? Lang.Text("Explorer.Status.Item", Items.Count)
                : Lang.Text("Explorer.Status.Items", Items.Count);
        if (SelectedItems.Count > 1)
            SelectionText = Lang.Text("Explorer.Status.Selected", SelectedItems.Count);
        ApplyUnavailable();
        OnPropertyChanged(nameof(EmptyText));
    }

    private void ApplyLocalizedChrome()
    {
        Title = IsHome ? Lang.Text("Places.Home") : IsSettings ? Lang.Text("Places.Settings") : IsTag
            ? SpecialFolders.TagName(CurrentPath)
            : Path.GetFileName(CurrentPath.TrimEnd('/')) is { Length: > 0 } name ? name : CurrentPath;
        PathText = IsHome ? Lang.Text("Places.Home") : IsSettings ? Lang.Text("Places.Settings") : IsTag
            ? SpecialFolders.TagName(CurrentPath)
            : CurrentPath;
    }

    private void SetUnavailable(string titleKey, string? messageKey = null, string? message = null)
    {
        _unavailableTitleKey = titleKey;
        _unavailableMessageKey = messageKey;
        _unavailableRawMessage = message;
        ApplyUnavailable();
    }

    private void ApplyUnavailable()
    {
        if (_unavailableTitleKey is null)
            return;
        UnavailableTitle = Lang.Text(_unavailableTitleKey);
        UnavailableMessage = _unavailableRawMessage ??
                             (_unavailableMessageKey is null ? null : Lang.Text(_unavailableMessageKey));
    }

    private static bool IsHomeAlias(string path) =>
        path == SpecialFolders.HomeKey ||
        path.Equals("Home", StringComparison.OrdinalIgnoreCase) ||
        Lang.EqualsText(path, "Places.Home");

    private static bool IsSettingsAlias(string path) =>
        path == SpecialFolders.SettingsKey ||
        path.Equals("Settings", StringComparison.OrdinalIgnoreCase) ||
        Lang.EqualsText(path, "Places.Settings");

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
        var items = Items.ToArray();
        try
        {
            await Parallel.ForEachAsync(items, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8),
                CancellationToken = token
            }, async (item, ct) =>
            {
                var icon = _icons.Get(item.Path, size);
                if (icon is null || ct.IsCancellationRequested)
                    return;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                        item.Icon = icon;
                });
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoadFolderSizesAsync()
    {
        _sizeCts?.Cancel();
        _sizeCts = new CancellationTokenSource();
        var token = _sizeCts.Token;
        var folders = Items.Where(static i => i.IsDirectory).ToArray();
        if (folders.Length == 0)
            return;
        try
        {
            await Task.WhenAll(folders.Select(item => FillSizeAsync(item, token)));
            if (!token.IsCancellationRequested && GroupOption is GroupOption.Size)
                await Dispatcher.UIThread.InvokeAsync(RebuildView);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task FillSizeAsync(FileItem item, CancellationToken token)
    {
        var size = await FolderSize.ComputeAsync(item.Path, token);
        if (token.IsCancellationRequested)
            return;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            item.Size = size;
            item.SizeKnown = true;
        });
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
        FolderSize.Invalidate(CurrentPath);
        if (!string.IsNullOrEmpty(e.FullPath))
            FolderSize.Invalidate(e.FullPath);
        if (e is RenamedEventArgs renamed)
            FolderSize.Invalidate(renamed.OldFullPath);
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
