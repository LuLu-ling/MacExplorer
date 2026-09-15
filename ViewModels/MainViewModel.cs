using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacExplorer.Infrastructure;
using MacExplorer.Logging;
using MacExplorer.Models;
using MacExplorer.Native;
using MacExplorer.Services;

namespace MacExplorer.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly FileService _files;
    private readonly ListingService _listing;
    private readonly IconService _icons;
    private readonly DialogCallbacks _dialogs;
    private readonly VolumeService _volumes;
    private ExplorerTabViewModel? _trackedTab;
    private bool _disposed;

    public MainViewModel(
        FileService files,
        ListingService listing,
        IconService icons,
        DialogCallbacks dialogs,
        VolumeService volumes)
    {
        _files = files;
        _listing = listing;
        _icons = icons;
        _dialogs = dialogs;
        _volumes = volumes;
        Sidebar = new SidebarViewModel(volumes);
        Home = new HomeViewModel(volumes, icons);
        SettingsPage = new SettingsViewModel();
        Tabs = [];
        IsSidebarOpen = Config.Sidebar.IsOpen;
        ShowInfoPane = Config.InfoPane.Show;
        SidebarWidth = Config.Sidebar.Width;
        InfoPaneWidth = Config.InfoPane.Width;
        ShowHidden = Config.Files.ShowHidden;
        ShowExtensions = Config.Files.ShowExtensions;
        MacFinder.FavoritesChanged += OnFavoritesChanged;
    }

    public ObservableCollection<ExplorerTabViewModel> Tabs { get; }
    public SidebarViewModel Sidebar { get; }
    public HomeViewModel Home { get; }
    public SettingsViewModel SettingsPage { get; }
    public DialogCallbacks Dialogs => _dialogs;

    [ObservableProperty] public partial ExplorerTabViewModel? SelectedTab { get; set; }
    [ObservableProperty] public partial bool IsSidebarOpen { get; set; }
    [ObservableProperty] public partial bool ShowInfoPane { get; set; }
    [ObservableProperty] public partial double SidebarWidth { get; set; }
    [ObservableProperty] public partial double InfoPaneWidth { get; set; }
    [ObservableProperty] public partial bool ShowHidden { get; set; }
    [ObservableProperty] public partial bool ShowExtensions { get; set; }

    public Action? RequestFocusPath { get; set; }
    public Action? RequestFocusSearch { get; set; }
    public Func<Task>? RequestProperties { get; set; }
    public Action? RequestCloseWindow { get; set; }

    [ObservableProperty] public partial bool ShowSettings { get; set; }

    public bool CanCloseTab => Tabs.Count > 0;
    public string WindowTitle => SelectedTab?.Title is { Length: > 0 } t ? $"{t} – MacExplorer" : "MacExplorer";
    public bool IsInfoPaneVisible => ShowInfoPane && SelectedTab is not { IsSettings: true };

    public const double SidebarMin = 160;
    public const double SidebarMax = 480;
    public const double InfoPaneMin = 200;
    public const double InfoPaneMax = 560;

    public GridLength SidebarColumn
    {
        get => IsSidebarOpen ? new(Math.Clamp(SidebarWidth, SidebarMin, SidebarMax)) : new(0);
        set
        {
            if (IsSidebarOpen && value.IsAbsolute)
                SidebarWidth = Math.Clamp(value.Value, SidebarMin, SidebarMax);
        }
    }

    public double SidebarColumnMin => IsSidebarOpen ? SidebarMin : 0;

    public GridLength InfoPaneColumn
    {
        get => IsInfoPaneVisible ? new(Math.Clamp(InfoPaneWidth, InfoPaneMin, InfoPaneMax)) : new(0);
        set
        {
            if (IsInfoPaneVisible && value.IsAbsolute)
                InfoPaneWidth = Math.Clamp(value.Value, InfoPaneMin, InfoPaneMax);
        }
    }

    public double InfoPaneColumnMin => IsInfoPaneVisible ? InfoPaneMin : 0;

    public IReadOnlyList<PaletteCommand> PaletteCommands =>
    [
        new("New tab", Glyphs.Add, "⌘T", NewTabCommand),
        new("Close tab", Glyphs.Delete, "⌘W", CloseTabCommand),
        new("New folder", Glyphs.NewFolder, "⇧⌘N", SelectedTab?.NewFolderCommand),
        new("Copy", Glyphs.Copy, "⌘C", SelectedTab?.CopyCommand),
        new("Cut", Glyphs.Cut, "⌘X", SelectedTab?.CutCommand),
        new("Paste", Glyphs.Paste, "⌘V", SelectedTab?.PasteCommand),
        new("Properties", Glyphs.Properties, "⌥↩", OpenPropertiesCommand),
        new("Delete", Glyphs.Delete, "⌘⌫", SelectedTab?.DeleteCommand),
        new("Select all", Glyphs.Select, "⌘A", SelectedTab?.SelectAllCommand),
        new("Details layout", Glyphs.Details, "⌘1", SetLayoutCommand, "Details"),
        new("List layout", Glyphs.List, "⌘2", SetLayoutCommand, "List"),
        new("Cards layout", Glyphs.Cards, "⌘3", SetLayoutCommand, "Cards"),
        new("Grid layout", Glyphs.Grid, "⌘4", SetLayoutCommand, "Grid"),
        new("Toggle info pane", Glyphs.PanelRight, "⌘P", ToggleInfoPaneCommand),
        new("Settings", Glyphs.Settings, "⌘,", OpenSettingsCommand)
    ];

    [RelayCommand]
    public void NewTab(string? path = null)
    {
        var resolved = path ?? SpecialFolders.HomeKey;
        var tab = new ExplorerTabViewModel(_files, _listing, _icons, _dialogs, resolved);
        Tabs.Add(tab);
        SelectedTab = tab;
        OnPropertyChanged(nameof(CanCloseTab));
        LogWrapper.Info("Window", $"New tab {resolved} (count={Tabs.Count})");
    }

    public void MoveTab(int from, int to)
    {
        if ((uint)from >= (uint)Tabs.Count || (uint)to >= (uint)Tabs.Count || from == to)
            return;
        Tabs.Move(from, to);
        OnPropertyChanged(nameof(SelectedTabIndex));
    }

    [RelayCommand]
    private Task NewFolderAsync() => SelectedTab?.NewFolderAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private Task NewFileAsync() => SelectedTab?.NewFileAsync() ?? Task.CompletedTask;

    [RelayCommand]
    public void CloseTab(ExplorerTabViewModel? tab = null)
    {
        tab ??= SelectedTab;
        if (tab is null || !Tabs.Contains(tab)) return;
        if (Tabs.Count == 1)
        {
            RequestCloseWindow?.Invoke();
            return;
        }
        var index = Tabs.IndexOf(tab);
        LogWrapper.Info("Window", $"Close tab {tab.CurrentPath}");
        Tabs.Remove(tab);
        tab.Dispose();
        SelectedTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        OnPropertyChanged(nameof(CanCloseTab));
    }

    [RelayCommand]
    public void DuplicateTab()
    {
        if (SelectedTab is null) return;
        NewTab(SelectedTab.CurrentPath);
    }

    [RelayCommand]
    public void CloseOtherTabs()
    {
        var keep = SelectedTab;
        foreach (var tab in Tabs.ToArray())
        {
            if (tab == keep) continue;
            Tabs.Remove(tab);
            tab.Dispose();
        }

        OnPropertyChanged(nameof(CanCloseTab));
    }

    [RelayCommand]
    public Task NavigateSidebarAsync(SidebarItem? item)
    {
        if (item is null) return Task.CompletedTask;
        if (item.IsSection)
        {
            Sidebar.ToggleSection(item);
            return Task.CompletedTask;
        }

        if (item.Kind == SidebarKind.Settings)
            return OpenSettingsAsync();

        if (item.Path is null) return Task.CompletedTask;

        Sidebar.SelectPath(item.Path);
        return SelectedTab?.NavigateAsync(item.Path) ?? Task.CompletedTask;
    }

    [RelayCommand]
    public Task OpenHomeAsync() => SelectedTab?.NavigateAsync(SpecialFolders.HomeKey) ?? Task.CompletedTask;

    [RelayCommand]
    public Task OpenPathAsync(string path) => SelectedTab?.NavigateAsync(path) ?? Task.CompletedTask;

    [RelayCommand]
    public void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    [RelayCommand]
    public void ToggleInfoPane() => ShowInfoPane = !ShowInfoPane;

    [RelayCommand]
    public Task OpenPropertiesAsync() => RequestProperties?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    public void SetLayout(string kind) => SelectedTab?.SetLayout(kind);

    [RelayCommand]
    public void SetSort(string field) => SelectedTab?.SetSort(field);

    [RelayCommand]
    public void SetGroup(string spec) => SelectedTab?.SetGroup(spec);

    [RelayCommand]
    public void SetGroupDirection(string spec) => SelectedTab?.SetGroupDirection(spec);

    [RelayCommand]
    public void ToggleHidden() => ShowHidden = !ShowHidden;

    [RelayCommand]
    public void ToggleExtensions() => ShowExtensions = !ShowExtensions;

    partial void OnShowHiddenChanged(bool value)
    {
        Config.Files.ShowHidden = value;
        _ = SelectedTab?.ReloadAsync();
    }

    partial void OnShowExtensionsChanged(bool value)
    {
        Config.Files.ShowExtensions = value;
        _ = SelectedTab?.ReloadAsync();
    }

    [RelayCommand]
    private void FocusPath() => RequestFocusPath?.Invoke();

    [RelayCommand]
    private void FocusSearch() => RequestFocusSearch?.Invoke();

    [RelayCommand]
    public Task OpenSettingsAsync()
    {
        ShowSettings = true;
        return SelectedTab?.NavigateAsync(SpecialFolders.SettingsKey) ?? Task.CompletedTask;
    }

    [RelayCommand]
    public void PinCurrent()
    {
        if (SelectedTab is null || SelectedTab.IsHome || SelectedTab.IsSettings) return;
        var path = SelectedTab.CurrentPath;
        if (SpecialFolders.IsVirtual(path) || !Directory.Exists(path)) return;
        MacFinder.ToggleFavorite(path);
    }

    private void OnFavoritesChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (!_disposed)
            RefreshPlaces();
    }, DispatcherPriority.Background);

    public void RefreshPlaces()
    {
        Sidebar.Rebuild();
        Home.Refresh();
        if (SelectedTab is { } tab)
            Sidebar.SelectPath(tab.CurrentPath);
    }

    public async Task EjectVolumeAsync(string path)
    {
        LogWrapper.Info("Volume", $"Eject {path}");
        if (!MacWorkspace.Eject(path))
        {
            await _dialogs.Error("Unable to eject", $"“{MacWorkspace.VolumeName(path)}” could not be ejected.");
            return;
        }

        foreach (var tab in Tabs.ToArray())
        {
            if (MacWorkspace.PathOnVolume(tab.CurrentPath, path))
                await tab.NavigateAsync(SpecialFolders.HomeKey);
        }

        RefreshPlaces();
    }

    partial void OnSelectedTabChanged(ExplorerTabViewModel? value)
    {
        if (_trackedTab is not null)
            _trackedTab.PropertyChanged -= OnTabPropertyChanged;
        _trackedTab = value;
        if (value is null) return;
        value.PropertyChanged += OnTabPropertyChanged;
        SyncFromTab(value);
    }
    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_trackedTab is null) return;
        if (e.PropertyName is nameof(ExplorerTabViewModel.CurrentPath)
            or nameof(ExplorerTabViewModel.IsSettings)
            or nameof(ExplorerTabViewModel.IsHome))
        {
            Sidebar.SelectPath(_trackedTab.CurrentPath);
            Home.Refresh();
            ShowSettings = _trackedTab.IsSettings;
            NotifyInfoPaneLayout();
        }

        if (e.PropertyName is nameof(ExplorerTabViewModel.Title)
            or nameof(ExplorerTabViewModel.CurrentPath))
            OnPropertyChanged(nameof(WindowTitle));
    }

    private void SyncFromTab(ExplorerTabViewModel? tab)
    {
        if (tab is null) return;
        foreach (var t in Tabs)
            t.IsSelectedTab = t == tab;
        Sidebar.SelectPath(tab.CurrentPath);
        Home.Refresh();
        ShowSettings = tab.IsSettings;
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(PaletteCommands));
        OnPropertyChanged(nameof(SelectedTabIndex));
        NotifyInfoPaneLayout();
    }

    public int SelectedTabIndex
    {
        get => SelectedTab is null ? -1 : Tabs.IndexOf(SelectedTab);
        set
        {
            if ((uint)value < (uint)Tabs.Count)
                SelectedTab = Tabs[value];
        }
    }

    partial void OnIsSidebarOpenChanged(bool value)
    {
        Config.Sidebar.IsOpen = value;
        OnPropertyChanged(nameof(SidebarColumn));
        OnPropertyChanged(nameof(SidebarColumnMin));
    }

    partial void OnShowInfoPaneChanged(bool value)
    {
        Config.InfoPane.Show = value;
        NotifyInfoPaneLayout();
    }

    partial void OnSidebarWidthChanged(double value)
    {
        var width = Math.Clamp(value, SidebarMin, SidebarMax);
        if (Math.Abs(width - value) > 0.5)
        {
            SidebarWidth = width;
            return;
        }
        Config.Sidebar.Width = width;
        OnPropertyChanged(nameof(SidebarColumn));
    }

    partial void OnInfoPaneWidthChanged(double value)
    {
        var width = Math.Clamp(value, InfoPaneMin, InfoPaneMax);
        if (Math.Abs(width - value) > 0.5)
        {
            InfoPaneWidth = width;
            return;
        }
        Config.InfoPane.Width = width;
        OnPropertyChanged(nameof(InfoPaneColumn));
    }

    private void NotifyInfoPaneLayout()
    {
        OnPropertyChanged(nameof(IsInfoPaneVisible));
        OnPropertyChanged(nameof(InfoPaneColumn));
        OnPropertyChanged(nameof(InfoPaneColumnMin));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        MacFinder.FavoritesChanged -= OnFavoritesChanged;
        SelectedTab = null;
        foreach (var tab in Tabs)
            tab.Dispose();
        Tabs.Clear();
        RequestFocusPath = null;
        RequestFocusSearch = null;
        RequestProperties = null;
        RequestCloseWindow = null;
    }
}

public sealed class PaletteCommand(string title, string glyph, string? gesture, IRelayCommand? command, object? parameter = null)
{
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public string? Gesture { get; } = gesture;
    public IRelayCommand? Command { get; } = command;
    public object? Parameter { get; } = parameter;
}
