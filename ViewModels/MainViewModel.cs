using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacExplorer.Configuration;
using MacExplorer.Controls;
using MacExplorer.Infrastructure;
using MacExplorer.Localization;
using MacExplorer.Logging;
using MacExplorer.Models;
using MacExplorer.Native;
using MacExplorer.Services;
using MacExplorer.Input;

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
    private readonly ConfigObserver _homeCards;

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
        Shortcuts.Changed += OnShortcutsChanged;
        _homeCards = new ConfigObserver(ConfigEvent.Changed, OnHomeCardsChanged);
        Config.Home.ObserveVisibility(_homeCards);
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
    public Action? RequestCloseWindow { get; set; }
    public Action<ExplorerTabViewModel>? PrepareTabClose { get; set; }

    [ObservableProperty] public partial bool ShowSettings { get; set; }

    public bool CanCloseTab => Tabs.Count > 0;
    public string WindowTitle => SelectedTab?.Title is { Length: > 0 } t
        ? Lang.Text("Window.Title.Format", t)
        : "MacExplorer";
    public bool IsInfoPaneVisible => ShowInfoPane && SelectedTab is not { IsSettings: true };
    public string NewTabTip => Shortcuts.Tip("Tab.New", ShortcutId.NewTab);

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
        new(Lang.Text("Palette.NewTab"), Glyphs.Add, Display(ShortcutId.NewTab), NewTabCommand),
        new(Lang.Text("Palette.CloseTab"), Glyphs.Delete, Display(ShortcutId.CloseTab), CloseTabCommand),
        new(Lang.Text("Palette.NewFolder"), Glyphs.NewFolder, Display(ShortcutId.NewFolder), SelectedTab?.NewFolderCommand),
        new(Lang.Text("Palette.Copy"), Glyphs.Copy, Display(ShortcutId.Copy), SelectedTab?.CopyCommand),
        new(Lang.Text("Palette.Cut"), Glyphs.Cut, Display(ShortcutId.Cut), SelectedTab?.CutCommand),
        new(Lang.Text("Palette.Paste"), Glyphs.Paste, Display(ShortcutId.Paste), SelectedTab?.PasteCommand),
        new(Lang.Text("Menu.File.GetInfo"), Glyphs.Properties, Display(ShortcutId.GetInfo), OpenPropertiesCommand),
        new(Lang.Text("Palette.Delete"), Glyphs.Delete, Display(ShortcutId.MoveToTrash), SelectedTab?.DeleteCommand),
        new(Lang.Text("Palette.SelectAll"), Glyphs.Select, Display(ShortcutId.SelectAll), SelectedTab?.SelectAllCommand),
        new(Lang.Text("Palette.DetailsLayout"), Glyphs.Details, Display(ShortcutId.AsDetails), SetLayoutCommand, "Details"),
        new(Lang.Text("Palette.ListLayout"), Glyphs.List, Display(ShortcutId.AsList), SetLayoutCommand, "List"),
        new(Lang.Text("Palette.CardsLayout"), Glyphs.Cards, Display(ShortcutId.AsCards), SetLayoutCommand, "Cards"),
        new(Lang.Text("Palette.GridLayout"), Glyphs.Grid, Display(ShortcutId.AsGrid), SetLayoutCommand, "Grid"),
        new(Lang.Text("Palette.ToggleInfoPane"), Glyphs.PanelRight, Display(ShortcutId.ShowInfoPane), ToggleInfoPaneCommand),
        new(Lang.Text("Palette.Settings"), Glyphs.Settings, Display(ShortcutId.Settings), OpenSettingsCommand)
    ];

    private static string? Display(ShortcutId id)
    {
        var text = Shortcuts.Display(id);
        return text.Length == 0 ? null : text;
    }

    private void OnShortcutsChanged()
    {
        OnPropertyChanged(nameof(NewTabTip));
        OnPropertyChanged(nameof(PaletteCommands));
    }

    [RelayCommand]
    public void NewTab(string? path = null)
    {
        var resolved = path ?? Config.Home.DefaultPath;
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

    public bool Detach(ExplorerTabViewModel tab)
    {
        if (!Tabs.Contains(tab))
            return false;
        var index = Tabs.IndexOf(tab);
        if (SelectedTab == tab)
            SelectedTab = Tabs.Count == 1 ? null : Tabs[index >= Tabs.Count - 1 ? index - 1 : index + 1];
        Tabs.Remove(tab);
        tab.IsSelectedTab = false;
        tab.IsClosing = false;
        OnPropertyChanged(nameof(CanCloseTab));
        LogWrapper.Info("Window", $"Detach tab {tab.CurrentPath} (count={Tabs.Count})");
        return true;
    }

    public void Adopt(ExplorerTabViewModel tab, int index = -1)
    {
        if (Tabs.Contains(tab))
        {
            var from = Tabs.IndexOf(tab);
            var to = index < 0 || index >= Tabs.Count ? Tabs.Count - 1 : index;
            MoveTab(from, to);
            SelectedTab = tab;
            tab.IsClosing = false;
            return;
        }

        tab.AttachDialogs(_dialogs);
        tab.IsClosing = false;
        if (index < 0 || index >= Tabs.Count)
            Tabs.Add(tab);
        else
            Tabs.Insert(index, tab);
        SelectedTab = tab;
        OnPropertyChanged(nameof(CanCloseTab));
        LogWrapper.Info("Window", $"Adopt tab {tab.CurrentPath} (count={Tabs.Count})");
    }

    [RelayCommand]
    private Task NewFolderAsync() => SelectedTab?.NewFolderAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private Task NewFileAsync(string? extension) => SelectedTab?.NewFileAsync(extension) ?? Task.CompletedTask;

    [RelayCommand]
    public async Task CloseTab(ExplorerTabViewModel? tab = null)
    {
        tab ??= SelectedTab;
        if (tab is null || !Tabs.Contains(tab) || tab.IsClosing)
            return;
        if (IsLastLiveTab(tab))
        {
            RequestCloseWindow?.Invoke();
            return;
        }

        var index = Tabs.IndexOf(tab);
        LogWrapper.Info("Window", $"Close tab {tab.CurrentPath}");
        PrepareTabClose?.Invoke(tab);
        tab.IsClosing = true;
        if (SelectedTab == tab)
            SelectedTab = Tabs.First(t => !t.IsClosing);

        await Task.Delay(ReorderShift.Duration);
        if (_disposed || !Tabs.Contains(tab))
            return;
        Tabs.Remove(tab);
        tab.Dispose();
        OnPropertyChanged(nameof(CanCloseTab));
        if (Tabs.Count == 0 || Tabs.All(static t => t.IsClosing))
            RequestCloseWindow?.Invoke();
    }

    private bool IsLastLiveTab(ExplorerTabViewModel tab)
    {
        foreach (var other in Tabs)
        {
            if (!ReferenceEquals(other, tab) && !other.IsClosing)
                return false;
        }

        return true;
    }

    [RelayCommand]
    public void DuplicateTab()
    {
        if (SelectedTab is null) return;
        NewTab(SelectedTab.CurrentPath);
    }

    [RelayCommand]
    public async Task CloseOtherTabs()
    {
        var keep = SelectedTab;
        var closing = Tabs.Where(tab => tab != keep && !tab.IsClosing).ToArray();
        foreach (var tab in closing)
        {
            PrepareTabClose?.Invoke(tab);
            tab.IsClosing = true;
        }

        await Task.Delay(ReorderShift.Duration);
        if (_disposed)
            return;
        foreach (var tab in closing)
        {
            if (!Tabs.Contains(tab))
                continue;
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
    public void OpenProperties() => ShowInfo();

    public void ShowInfo(IReadOnlyList<string>? paths = null)
    {
        var targets = paths ?? InfoTargets();
        if (targets is { Count: > 0 })
            MacFinder.ShowInfo(targets);
    }

    private IReadOnlyList<string>? InfoTargets()
    {
        if (SelectedTab is not { } tab || tab.IsHome || tab.IsSettings)
            return null;
        if (tab.SelectedItems.Count > 0)
            return tab.SelectedItems.Select(static i => i.Path).ToList();
        return SpecialFolders.IsVirtual(tab.CurrentPath) ? null : [tab.CurrentPath];
    }

    [RelayCommand]
    public void SetLayout(string kind) => SelectedTab?.SetLayout(kind);

    [RelayCommand]
    public void SetSort(string field) => SelectedTab?.SetSort(field);

    [RelayCommand]
    public void SetSortDirection(string spec) => SelectedTab?.SetSortDirection(spec);

    [RelayCommand]
    public void SetFolderPriority(string spec) => SelectedTab?.SetFolderPriority(spec);

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

    private void OnHomeCardsChanged(ConfigEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (!_disposed)
            _ = ApplyHomeVisibilityAsync();
    }, DispatcherPriority.Background);

    private async Task ApplyHomeVisibilityAsync()
    {
        try
        {
            if (!Config.Home.AnyVisible)
            {
                var disk = SpecialFolders.Computer;
                foreach (var tab in Tabs.ToArray())
                {
                    if (tab.IsHome)
                        await tab.NavigateAsync(disk);
                }
            }
        }
        finally
        {
            if (!_disposed)
                RefreshPlaces();
        }
    }

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
            await _dialogs.Error(
                Lang.Text("Dialog.EjectFailed.Title"),
                Lang.Text("Dialog.EjectFailed.Message", MacWorkspace.VolumeName(path)));
            return;
        }

        foreach (var tab in Tabs.ToArray())
        {
            if (MacWorkspace.PathOnVolume(tab.CurrentPath, path))
                await tab.NavigateAsync(Config.Home.DefaultPath);
        }

        RefreshPlaces();
    }

    public async Task RenameTagAsync(string from, string to)
    {
        to = to.Trim();
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to)
            return;
        if (to.Contains('\n', StringComparison.Ordinal) ||
            MacTags.All().Any(tag =>
                !string.Equals(tag.Name, from, StringComparison.Ordinal) &&
                string.Equals(tag.Name, to, StringComparison.OrdinalIgnoreCase)))
        {
            await _dialogs.Error(Lang.Text("Explorer.RenameFailed"), Lang.Text("Dialog.TagExists.Message", to));
            return;
        }

        LogWrapper.Info("Tags", $"Rename {from} -> {to}");
        var extra = TaggedPaths(from);
        if (!await Task.Run(() => MacTags.Rename(from, to, extra)))
        {
            await _dialogs.Error(Lang.Text("Explorer.RenameFailed"), Lang.Text("Common.Error.Unknown"));
            return;
        }

        await RetargetTagAsync(from, to);
        RefreshPlaces();
    }

    public async Task DeleteTagAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (!await _dialogs.Confirm(
                Lang.Text("Dialog.DeleteTag.Title"),
                Lang.Text("Dialog.DeleteTag.Message", name),
                Lang.Text("Common.Action.Delete"),
                Lang.Text("Common.Action.Cancel")))
            return;

        LogWrapper.Info("Tags", $"Delete {name}");
        var extra = TaggedPaths(name);
        if (!await Task.Run(() => MacTags.Delete(name, extra)))
        {
            await _dialogs.Error(Lang.Text("Explorer.DeleteFailed"), Lang.Text("Common.Error.Unknown"));
            return;
        }

        await RetargetTagAsync(name, null);
        RefreshPlaces();
    }

    private async Task RetargetTagAsync(string from, string? to)
    {
        var oldPath = SpecialFolders.TagPath(from);
        var next = to is null ? Config.Home.DefaultPath : SpecialFolders.TagPath(to);
        foreach (var tab in Tabs.ToArray())
        {
            if (tab.CurrentPath == oldPath)
                await tab.NavigateAsync(next);
        }
    }

    private IReadOnlyList<string> TaggedPaths(string name) =>
        Tabs.SelectMany(static tab => tab.Items)
            .Where(item => item.Tags.Any(tag => tag.Name == name))
            .Select(static item => item.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(NewTabTip));
        OnPropertyChanged(nameof(PaletteCommands));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Config.Home.UnobserveVisibility(_homeCards);
        MacFinder.FavoritesChanged -= OnFavoritesChanged;
        Shortcuts.Changed -= OnShortcutsChanged;
        SelectedTab = null;
        foreach (var tab in Tabs)
            tab.Dispose();
        Tabs.Clear();
        RequestFocusPath = null;
        RequestFocusSearch = null;
        RequestCloseWindow = null;
        PrepareTabClose = null;
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
