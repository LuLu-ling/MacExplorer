using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using MacExplorer.Controls;
using MacExplorer.Infrastructure;
using MacExplorer.Lifecycle;
using MacExplorer.Logging;
using MacExplorer.Models;
using MacExplorer.Native;
using MacExplorer.Services;
using MacExplorer.ViewModels;


namespace MacExplorer.Views;

public partial class MainWindow : FAAppWindow
{
    public MainWindow()
    {
        InitializeComponent();
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.Height = 48;
        DataContextChanged += (_, _) => BindDialogs();
        Closed += (_, _) =>
        {
            LogWrapper.Info("Window", $"Main window closing {Width:0}x{Height:0}");
            PersistWindow();
        };
        Activated += (_, _) => RefreshFinderPlaces();
        TitleBarHost.SizeChanged += (_, _) => UpdateTabStripOverflow();
        DragDrop.SetAllowDrop(SidebarHost, true);
        SidebarHost.AddHandler(DragDrop.DragOverEvent, Sidebar_OnDragOver);
        SidebarHost.AddHandler(DragDrop.DragLeaveEvent, Sidebar_OnDragLeave);
        SidebarHost.AddHandler(DragDrop.DropEvent, Sidebar_OnDrop);
        SidebarHost.AddHandler(ContextRequestedEvent, Sidebar_OnContextRequested, RoutingStrategies.Tunnel);

    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (IsInteractiveCaption(e.Source as Visual))
            return;
        BeginMoveDrag(e);
    }

    private static bool IsInteractiveCaption(Visual? start)
    {
        for (var visual = start; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Button or ToggleButton or TextBox or ScrollViewer or ScrollBar)
                return true;
            if (visual is ItemsControl { Name: "TabStrip" })
                return true;
            if (visual is Control { Classes.Count: > 0 } c && c.Classes.Contains("TabItem"))
                return true;
            if (visual is Control { Name: "TitleBarHost" or "DragAreaRectangle" })
                return false;
        }

        return false;
    }

    private MainViewModel? VM => DataContext as MainViewModel;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        BindDialogs();
        LogWrapper.Info("Window", $"Main window opened {Width:0}x{Height:0}");
        if (VM is null) return;
        VM.RequestFocusPath = FocusPathBox;
        VM.RequestFocusSearch = () =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        };
        VM.RequestProperties = ShowProperties;
        UpdateTabStripOverflow();
    }

    private void RefreshFinderPlaces() => VM?.RefreshPlaces();

    private void BindDialogs()
    {
        if (VM is null) return;
        VM.Dialogs.Confirm = ConfirmAsync;
        VM.Dialogs.Conflict = ConflictAsync;
        VM.Dialogs.Error = ErrorAsync;
    }

    private void PersistWindow()
    {
        Config.Window.Width = Width;
        Config.Window.Height = Height;
    }
    private int _tabDragFrom = -1;
    private int _tabDragHover = -1;
    private bool _tabDragging;
    private double _tabPressX;
    private Control? _tabDragSource;

    private void Tab_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: ExplorerTabViewModel tab } border || VM is null)
            return;
        if (e.GetCurrentPoint(border).Properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed)
        {
            VM.CloseTab(tab);
            e.Handled = true;
            return;
        }
        if (e.Source is Button)
            return;
        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            return;

        VM.SelectedTab = tab;
        border.BringIntoView();
        _tabDragFrom = VM.Tabs.IndexOf(tab);
        _tabDragHover = _tabDragFrom;
        _tabDragging = false;
        _tabDragSource = border;
        _tabPressX = e.GetPosition(TabPanel()).X;
        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void Tab_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Border { Tag: ExplorerTabViewModel tab } border || VM is null)
            return;
        e.Handled = true;
        VM.SelectedTab = tab;
        border.BringIntoView();
        MacContextMenu.Show(
        [
            new("New tab", () => VM.NewTab()),
            new("Duplicate tab", () => VM.DuplicateTab()),
            new("Close tab", () => VM.CloseTab(tab), VM.CanCloseTab),
        ]);
    }

    private void Tab_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_tabDragFrom < 0 || _tabDragSource is null || VM is null)
            return;
        if (!e.GetCurrentPoint(_tabDragSource).Properties.IsLeftButtonPressed)
            return;

        var panel = TabPanel();
        if (panel is null)
            return;

        var x = e.GetPosition(panel).X;
        if (!_tabDragging)
        {
            if (Math.Abs(x - _tabPressX) < 6)
                return;
            _tabDragging = true;
            panel.Children[_tabDragFrom].ZIndex = 100;
            _tabDragSource.ZIndex = 100;
            SetTabShift(panel.Children[_tabDragFrom], 0, animate: false);
        }

        var origin = 0.0;
        for (var i = 0; i < _tabDragFrom; i++)
            origin += panel.Children[i].Bounds.Width + 2;
        var tabWidth = panel.Children[_tabDragFrom].Bounds.Width;
        var minDelta = -origin;
        var maxDelta = Math.Max(minDelta, panel.Bounds.Width - origin - tabWidth);
        var dragDelta = Math.Clamp(x - _tabPressX, minDelta, maxDelta);
        int hover;
        if (dragDelta <= minDelta + 0.5)
            hover = 0;
        else if (dragDelta >= maxDelta - 0.5)
            hover = panel.Children.Count - 1;
        else
            hover = TabIndexAt(panel, origin + tabWidth / 2 + dragDelta);
        ShiftTabs(panel, _tabDragFrom, hover, dragDelta);
        _tabDragHover = hover;
    }

    private void Tab_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        FinishTabDrag();
        e.Pointer.Capture(null);
    }

    private void Tab_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => FinishTabDrag();

    private void FinishTabDrag()
    {
        if (_tabDragFrom < 0 || VM is null)
            return;

        var from = _tabDragFrom;
        var to = _tabDragHover;
        var panel = TabPanel();
        _tabDragFrom = -1;
        _tabDragHover = -1;
        _tabDragging = false;
        if (panel is not null)
        {
            foreach (var child in panel.Children)
                child.ZIndex = 0;
            ResetTabShifts(panel);
        }

        _tabDragSource = null;

        if (to >= 0 && to != from)
            VM.MoveTab(from, to);
    }

    private void CloseTab_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        FinishTabDrag();
        if (sender is Button { Tag: ExplorerTabViewModel tab })
            VM?.CloseTab(tab);
    }


    private void TabStrip_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (VM is null || VM.Tabs.Count == 0)
            return;
        var delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
        var index = VM.SelectedTabIndex;
        if (delta > 0)
        {
            if (index < VM.Tabs.Count - 1)
                VM.SelectedTabIndex = index + 1;
        }
        else if (delta < 0)
        {
            if (index > 0)
                VM.SelectedTabIndex = index - 1;
        }

        if (TabPanel() is { } panel && (uint)VM.SelectedTabIndex < (uint)panel.Children.Count)
            panel.Children[VM.SelectedTabIndex].BringIntoView();
        e.Handled = true;
    }

    private void TabScroller_OnScrollChanged(object? sender, ScrollChangedEventArgs e) =>
        UpdateTabStripOverflow();

    private void TabScrollDecrease_OnClick(object? sender, RoutedEventArgs e) => ScrollTabs(-140);

    private void TabScrollIncrease_OnClick(object? sender, RoutedEventArgs e) => ScrollTabs(140);

    private void ScrollTabs(double delta)
    {
        var max = Math.Max(0, TabScroller.Extent.Width - TabScroller.Viewport.Width);
        TabScroller.Offset = TabScroller.Offset.WithX(Math.Clamp(TabScroller.Offset.X + delta, 0, max));
    }

    private void UpdateTabStripOverflow()
    {
        var add = (TabBarAddNewTabButton.Bounds.Width > 0 ? TabBarAddNewTabButton.Bounds.Width : 30) + 4;
        var available = TitleBarHost.Bounds.Width - 78 - add;
        var content = TabPanel()?.Bounds.Width ?? TabStrip.Bounds.Width;
        var overflow = content > available + 0.5;
        var chevrons = overflow ? 68 : 0;
        TabScroller.MaxWidth = Math.Max(0, available - chevrons);
        TabScrollDecreaseButton.IsVisible = overflow;
        TabScrollIncreaseButton.IsVisible = overflow;
        if (!overflow)
            return;
        var max = Math.Max(0, TabScroller.Extent.Width - TabScroller.Viewport.Width);
        TabScrollDecreaseButton.IsEnabled = TabScroller.Offset.X > 1;
        TabScrollIncreaseButton.IsEnabled = TabScroller.Offset.X < max - 1;
    }
    private Panel? TabPanel() => TabStrip.ItemsPanelRoot as Panel;

    private static int TabIndexAt(Panel panel, double x)
    {
        var acc = 0.0;
        for (var i = 0; i < panel.Children.Count; i++)
        {
            var w = panel.Children[i].Bounds.Width;
            if (x <= acc + w / 2)
                return i;
            acc += w + 2;
        }

        return Math.Max(0, panel.Children.Count - 1);
    }

    private static void ShiftTabs(Panel panel, int from, int hover, double dragDelta)
    {
        var width = panel.Children[from].Bounds.Width + 2;
        for (var i = 0; i < panel.Children.Count; i++)
        {
            var child = panel.Children[i];
            if (i == from)
            {
                SetTabShift(child, dragDelta, animate: false);
                continue;
            }

            var shift = 0.0;
            if (from < hover && i > from && i <= hover)
                shift = -width;
            else if (from > hover && i >= hover && i < from)
                shift = width;
            SetTabShift(child, shift, animate: true);
        }
    }

    private static void ResetTabShifts(Panel panel)
    {
        foreach (var child in panel.Children)
            SetTabShift(child, 0, animate: false);
    }

    private static void SetTabShift(Control child, double x, bool animate)
    {
        child.Transitions = animate
            ? new Avalonia.Animation.Transitions
            {
                new Avalonia.Animation.TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(180)
                }
            }
            : null;
        child.RenderTransform = x == 0
            ? null
            : Avalonia.Media.Transformation.TransformOperations.Parse(
                FormattableString.Invariant($"translateX({x}px)"));
    }

    private async void Sidebar_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SidebarItem item })
            await (VM?.NavigateSidebarAsync(item) ?? Task.CompletedTask);
    }

    private void Sidebar_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (VM is null || e.Handled)
            return;
        SidebarItem? item = null;
        for (var visual = e.Source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Control { Tag: SidebarItem found })
            {
                item = found;
                break;
            }
        }
        if (item is null)
            return;
        var entries = SidebarMenu(item);
        if (entries.Length == 0)
            return;
        e.Handled = true;
        MacContextMenu.Show(entries);
    }

    private MacMenuEntry[] SidebarMenu(SidebarItem item)
    {
        if (item.Path is not { Length: > 0 } path)
            return [];
        return item.Kind switch
        {
            SidebarKind.Favorite =>
            [
                new("Unfavorite", () => MacFinder.RemoveFavorite(path)),
                new("", Separator: true),
                new("Properties", () => ShowProperties(path)),
            ],
            SidebarKind.Location when MacWorkspace.IsDiskImage(path) =>
            [
                new("Eject", () => _ = VM!.EjectVolumeAsync(path)),
                new("", Separator: true),
                new("Properties", () => ShowProperties(path)),
            ],
            _ => []
        };
    }

    private void Sidebar_OnDragOver(object? sender, DragEventArgs e)
    {
        var paths = FileDrag.Paths(e.DataTransfer);
        var item = SidebarItemAt(e);
        if (FavoriteDrop(e, item, paths, e.DragEffects, out var effect, out var dest))
        {
            e.DragEffects = effect;
            FileDragTip.Show(e, dest is null ? DragDropEffects.None : DragDropEffects.Link, dest);
            e.Handled = true;
            return;
        }

        dest = SidebarDropPath(item);
        e.DragEffects = dest is null || paths is null
            ? DragDropEffects.None
            : FileDrag.Effect(paths, dest, e.DragEffects, e.KeyModifiers);
        FileDragTip.Show(e, e.DragEffects, dest);
        e.Handled = true;
    }


    private void Sidebar_OnDragLeave(object? sender, DragEventArgs e)
    {
        FileDragTip.Hide();
        e.Handled = true;
    }


    private async void Sidebar_OnDrop(object? sender, DragEventArgs e)
    {
        var paths = FileDrag.Paths(e.DataTransfer);
        var item = SidebarItemAt(e);
        FileDragTip.Hide();

        if (FavoriteDrop(e, item, paths, e.DragEffects, out var effect, out _))
        {
            e.DragEffects = effect;
            e.Handled = true;
            if (effect != DragDropEffects.None && paths is not null)
                MacFinder.AddFavorites(paths);
            return;
        }

        var dest = SidebarDropPath(item);
        if (VM?.SelectedTab is null || dest is null || paths is null)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        effect = FileDrag.Effect(paths, dest, e.DragEffects, e.KeyModifiers);
        e.DragEffects = effect;
        e.Handled = true;
        if (effect == DragDropEffects.None)
            return;
        await VM.SelectedTab.DropFilesAsync(paths, dest, effect == DragDropEffects.Move);
    }

    private bool FavoriteDrop(
        DragEventArgs e,
        SidebarItem? item,
        IReadOnlyList<string>? paths,
        DragDropEffects allowed,
        out DragDropEffects effect,
        out string? destination)
    {
        effect = DragDropEffects.None;
        destination = null;
        if (paths is null || !OverFavoritesHeaderOrGap(e, item))
            return false;
        if (paths.Count == 0 || paths.Any(static p => !Directory.Exists(p) || PathUtil.IsBundle(p)))
            return false;
        if (paths.Any(MacFinder.IsFavorite))
            return true;

        effect = (allowed & DragDropEffects.Link) != 0
            ? DragDropEffects.Link
            : (allowed & DragDropEffects.Copy) != 0
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        if (effect == DragDropEffects.None)
            return true;
        destination = "Favorites";
        return true;
    }

    private bool OverFavoritesHeaderOrGap(DragEventArgs e, SidebarItem? item)
    {
        if (item is { Id: "favorites" })
            return true;
        return item is null && InFavoritesBand(e);
    }

    private bool InFavoritesBand(DragEventArgs e)
    {
        if (SidebarList.ItemsPanelRoot is not Panel panel || VM is null)
            return false;

        var items = VM.Sidebar.Items;
        var start = -1;
        var stop = items.Count;
        for (var i = 0; i < items.Count; i++)
        {
            if (start < 0)
            {
                if (items[i].Id == "favorites")
                    start = i;
                continue;
            }
            if (items[i].Kind != SidebarKind.Favorite)
            {
                stop = i;
                break;
            }
        }
        if ((uint)start >= (uint)panel.Children.Count)
            return false;

        var top = panel.Children[start].Bounds.Top;
        var bottom = stop < panel.Children.Count
            ? panel.Children[stop].Bounds.Top
            : panel.Children[start].Bounds.Bottom;
        var pos = e.GetPosition(panel);
        return pos.Y >= top && pos.Y < bottom;
    }

    private static SidebarItem? SidebarItemAt(DragEventArgs e)
    {
        for (var visual = e.Source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Control { Tag: SidebarItem item })
                return item;
        }

        return null;
    }

    private static string? SidebarDropPath(SidebarItem? item) =>
        item is { Path.Length: > 0 } && Directory.Exists(item.Path) ? item.Path : null;


    private async void Breadcrumb_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
            await (VM?.OpenPathAsync(path) ?? Task.CompletedTask);
    }

    private void PathField_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button)
            return;
        FocusPathBox();
    }

    private void FocusPathBox()
    {
        if (VM?.SelectedTab is null) return;
        VM.SelectedTab.IsOmnibarFocused = true;
        PathBox.Focus();
        PathBox.SelectAll();
    }

    private async void PathBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (VM?.SelectedTab is null) return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            VM.SelectedTab.IsOmnibarFocused = false;
            return;
        }

        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await VM.SelectedTab.SubmitOmnibarAsync();
    }

    private void PathBox_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (VM?.SelectedTab is not null)
            VM.SelectedTab.IsOmnibarFocused = false;
    }

    private void SearchBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            SearchBox.Clear();
            if (VM?.SelectedTab is not null)
                VM.SelectedTab.SearchText = string.Empty;
        }
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primary, string close)
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primary,
            CloseButtonText = close,
            DefaultButton = FAContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync(this);
        return result == FAContentDialogResult.Primary;
    }

    private async Task<ConflictDecision> ConflictAsync(string name)
    {
        var dialog = new FAContentDialog
        {
            Title = "File already exists",
            Content = $"“{name}” already exists in this folder.",
            PrimaryButtonText = "Keep both",
            SecondaryButtonText = "Replace",
            CloseButtonText = "Skip",
            DefaultButton = FAContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync(this);
        return result switch
        {
            FAContentDialogResult.Primary => ConflictDecision.KeepBoth,
            FAContentDialogResult.Secondary => ConflictDecision.Replace,
            _ => ConflictDecision.Skip
        };
    }

    private async Task ErrorAsync(string title, string message)
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "OK",
            DefaultButton = FAContentDialogButton.Primary
        };
        await dialog.ShowAsync(this);
    }

    public Task ShowProperties() => ShowPropertiesAsync();

    public Task ShowProperties(string path) => ShowPropertiesAsync([ListingService.Create(path)]);

    private Task ShowPropertiesAsync(IReadOnlyList<FileItem>? items = null)
    {
        var tab = VM?.SelectedTab;
        items ??= tab is { IsHome: false, IsSettings: false }
            ? tab.SelectedItems.Count > 0
                ? tab.SelectedItems.ToList()
                : [ListingService.Create(tab.CurrentPath)]
            : null;
        if (items is not { Count: > 0 })
            return Task.CompletedTask;

        var vm = new PropertiesViewModel(items, AppServices.Get<FileService>(), AppServices.Get<IconService>());
        var window = new PropertiesWindow { DataContext = vm };
        void OnMainClosed(object? _, EventArgs e) => window.Close();
        Closed += OnMainClosed;
        window.Closed += async (_, _) =>
        {
            Closed -= OnMainClosed;
            if (vm.SavedPath is not { Length: > 0 } saved || tab is null)
                return;
            await tab.ReloadAsync();
            if (tab.Items.FirstOrDefault(i => string.Equals(i.Path, saved, StringComparison.Ordinal)) is { } item)
                tab.SetSelection([item]);
        };

        // Independent window: Show(owner) parents it on macOS, so navigating
        // (title change / orderFront) raises properties over the main window.
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Show();
        var scale = DesktopScaling;
        var x = Position.X + (int)((ClientSize.Width - window.Width) * scale / 2);
        var y = Position.Y + (int)((ClientSize.Height - window.Height) * scale / 2);
        window.Position = new PixelPoint(x, y);
        return Task.CompletedTask;
    }
}
