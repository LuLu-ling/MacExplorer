using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Windowing;
using MacExplorer.Controls;
using MacExplorer.Infrastructure;
using MacExplorer.Lifecycle;
using MacExplorer.Logging;
using MacExplorer.Models;
using MacExplorer.Native;
using MacExplorer.Services;
using MacExplorer.ViewModels;
using System.Windows.Input;
using MacExplorer.Input;


using MacExplorer.Localization;
namespace MacExplorer.Views;

public partial class MainWindow : FAAppWindow
{
    private readonly DragHoverOpen _dragHoverOpen = new();
    private bool _tabsReady;
    private bool _tabOverflowReady;
    private double _tabScrollTo;
    private CancellationTokenSource? _tabScrollAnim;
    private readonly HashSet<ExplorerTabViewModel> _enteredTabs = [];

    public MainWindow()
    {
        InitializeComponent();
        AddressBar.MenuService = new BreadcrumbMenuService(AppServices.Get<VolumeService>());
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.Height = WindowChrome.TitleBarHeight;
        DataContextChanged += (_, _) => BindDialogs();
        Closed += (_, _) =>
        {
            LogWrapper.Info("Window", $"Main window closing {Width:0}x{Height:0}");
            _dragHoverOpen.Cancel();
            PersistWindow();
        };
        Activated += (_, _) => RefreshFinderPlaces();
        TitleBarHost.SizeChanged += (_, _) => UpdateTabStripOverflow();
        DragDrop.SetAllowDrop(AddressBar, true);
        AddressBar.AddHandler(DragDrop.DragOverEvent, AddressBar_OnDragOver);
        AddressBar.AddHandler(DragDrop.DragLeaveEvent, AddressBar_OnDragLeave);
        AddressBar.AddHandler(DragDrop.DropEvent, AddressBar_OnDrop);
        AddHandler(ToolTip.ToolTipOpeningEvent, OnToolTipOpening, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragEnterEvent, OnWindowDragEnter, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, OnWindowDragEnd, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragLeaveEvent, OnWindowDragLeave, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnTornPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnTornPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, OnTornCaptureLost, RoutingStrategies.Tunnel);
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (IsInteractiveCaption(e.Source as Visual))
            return;
        BeginMoveDrag(e);
    }

    private static void OnToolTipOpening(object? sender, RoutedEventArgs e)
    {
        if (FileDrag.SuppressToolTips)
            e.Handled = true;
    }

    private void OnWindowDragEnter(object? sender, DragEventArgs e)
    {
        if (TabDrag.Active)
            return;
        FileDrag.Begin(e.Source as Visual);
    }

    private void OnWindowDragEnd(object? sender, DragEventArgs e) => FileDrag.End();

    private void OnWindowDragLeave(object? sender, DragEventArgs e)
    {
        if (!StillInside(this, e))
            FileDrag.End();
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
        VM.RequestFocusSearch = () => SearchBox.FocusEditor();
        VM.RequestCloseWindow = Close;
        VM.PrepareTabClose = PrepareTabClose;
        _tabsReady = true;
        RememberOpenTabs();
        UpdateTabStripOverflow();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || VM is null)
            return;
        if (Dispatch(ShortcutId.FocusPath, VM.FocusPathCommand)
            || Dispatch(ShortcutId.Rename, VM.SelectedTab?.RenameCommand)
            || Dispatch(ShortcutId.GetInfo, VM.OpenPropertiesCommand)
            || Dispatch(ShortcutId.Refresh, VM.SelectedTab?.RefreshCommand))
            e.Handled = true;

        bool Dispatch(ShortcutId id, ICommand? command)
        {
            if (!Shortcuts.Matches(id, e) || command?.CanExecute(null) != true)
                return false;
            command.Execute(null);
            return true;
        }
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

    private void New_OnClick(object? sender, RoutedEventArgs e) =>
        ShowToolbarMenu(sender, e, static (vm, _) =>
        [
            new(Lang.Text("Toolbar.New.Folder"), () => vm.NewFolderCommand.Execute(null), Symbol: MacMenuSymbol.NewFolder),
            new(Lang.Text("Toolbar.New.File"), () => vm.NewFileCommand.Execute(null), Symbol: MacMenuSymbol.NewFile),
        ]);

    private void Selection_OnClick(object? sender, RoutedEventArgs e) =>
        ShowToolbarMenu(sender, e, static (_, tab) =>
        [
            new(Lang.Text("Toolbar.SelectAll"), tab.SelectAll, Symbol: MacMenuSymbol.SelectAll),
            new(Lang.Text("Toolbar.InvertSelection"), tab.InvertSelection, Symbol: MacMenuSymbol.Invert),
            new(Lang.Text("Toolbar.ClearSelection"), tab.ClearSelection, Symbol: MacMenuSymbol.Clear),
        ]);

    private void Sort_OnClick(object? sender, RoutedEventArgs e) =>
        ShowToolbarMenu(sender, e, static (_, tab) =>
        [
            new(Lang.Text("Sort.By"), Children: FolderView.SortByMenu(tab), Symbol: MacMenuSymbol.Sort),
            new(Lang.Text("Group.By"), Children: FolderView.GroupByMenu(tab), Symbol: MacMenuSymbol.Group),
        ]);

    private void Layout_OnClick(object? sender, RoutedEventArgs e) =>
        ShowToolbarMenu(sender, e, static (vm, tab) =>
        {
            var layout = tab.Layout;
            var size = tab.LayoutSize;
            return
            [
                new(Lang.Text("Layout.Details"), () => vm.SetLayout("Details"), Checked: layout is LayoutKind.Details, Symbol: MacMenuSymbol.Details),
                new(Lang.Text("Layout.List"), () => vm.SetLayout("List"), Checked: layout is LayoutKind.List, Symbol: MacMenuSymbol.List),
                new(Lang.Text("Layout.Cards"), () => vm.SetLayout("Cards"), Checked: layout is LayoutKind.Cards, Symbol: MacMenuSymbol.Cards),
                new(Lang.Text("Layout.Grid"), () => vm.SetLayout("Grid"), Checked: layout is LayoutKind.Grid, Symbol: MacMenuSymbol.Grid),
                new("", Separator: true),
                new(Lang.Text("Layout.Size"), Children:
                [
                    new("50%", () => tab.LayoutSize = 1, Checked: size == 1),
                    new("75%", () => tab.LayoutSize = 2, Checked: size == 2),
                    new("100%", () => tab.LayoutSize = 3, Checked: size == 3),
                    new("125%", () => tab.LayoutSize = 4, Checked: size == 4),
                    new("150%", () => tab.LayoutSize = 5, Checked: size == 5),
                ], Symbol: MacMenuSymbol.Size),
                new("", Separator: true),
                new(Lang.Text("Settings.Folders.ShowHidden"), vm.ToggleHidden, Checked: vm.ShowHidden, Symbol: MacMenuSymbol.Hidden),
                new(Lang.Text("Settings.Folders.ShowExtensions"), vm.ToggleExtensions, Checked: vm.ShowExtensions, Symbol: MacMenuSymbol.Extensions),
            ];
        });

    private void ShowToolbarMenu(
        object? sender,
        RoutedEventArgs e,
        Func<MainViewModel, ExplorerTabViewModel, IReadOnlyList<MacMenuEntry>> build)
    {
        e.Handled = true;
        if (sender is not Control anchor || VM is not { SelectedTab: { } tab } vm)
            return;
        MacContextMenu.ShowAt(anchor, build(vm, tab));
    }

    private int _tabDragFrom = -1;
    private int _tabDragHover = -1;
    private bool _tabDragging;
    private bool _tabTorn;
    private ExplorerTabViewModel? _tornTab;
    private Size _tornSize;
    private double _tearWidth = 160;
    private double _tabPressX;
    private double _tabGrabX;
    private PixelPoint _tabGrab;
    private PointerPressedEventArgs? _tabPress;
    private Control? _tabDragSource;

    private void Tab_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: ExplorerTabViewModel tab } border || VM is null)
            return;
        if (e.GetCurrentPoint(border).Properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed)
        {
            _ = VM.CloseTab(tab);
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
        _tabTorn = false;
        _tabDragSource = border;
        _tabPress = e;
        _tabPressX = e.GetPosition(TabPanel()).X;
        _tabGrabX = e.GetPosition(border).X;
        var origin = border.PointToScreen(default);
        var cursor = border.PointToScreen(e.GetPosition(border));
        _tabGrab = new PixelPoint(cursor.X - origin.X, cursor.Y - origin.Y);
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
            new(Lang.Text("Tab.New"), () => VM.NewTab(), Symbol: MacMenuSymbol.NewTab),
            new(Lang.Text("Tab.NewWindow"), () => AppServices.Get<WindowService>().OpenWindow(), Symbol: MacMenuSymbol.NewWindow),
            new(Lang.Text("Tab.OpenInNewWindow"), () => AppServices.Get<WindowService>().OpenWindow(tab.CurrentPath), Symbol: MacMenuSymbol.NewWindow),
            new(Lang.Text("Tab.Duplicate"), () => VM.DuplicateTab(), Symbol: MacMenuSymbol.Duplicate),
            new(Lang.Text("Tab.Close"), () => _ = VM.CloseTab(tab), VM.CanCloseTab, Symbol: MacMenuSymbol.Close),
        ]);
    }

    private void Tab_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_tabTorn || _tabDragFrom < 0 || _tabDragSource is null || VM is null)
            return;
        if (!e.GetCurrentPoint(_tabDragSource).Properties.IsLeftButtonPressed)
            return;

        var panel = TabPanel();
        if (panel is null)
            return;

        var x = e.GetPosition(panel).X;
        if (!_tabDragging)
        {
            var y = e.GetPosition(TitleBarHost).Y;
            if (Math.Abs(x - _tabPressX) < FileDrag.Threshold && y >= 0 && y <= TitleBarHost.Bounds.Height)
                return;
            _tabDragging = true;
            if ((uint)_tabDragFrom < (uint)panel.Children.Count)
            {
                panel.Children[_tabDragFrom].ZIndex = 100;
                _tabDragSource.ZIndex = 100;
                ReorderShift.Item(panel.Children[_tabDragFrom], 0, horizontal: true, animate: false);
            }
        }

        var hostY = e.GetPosition(TitleBarHost).Y;
        if (hostY < -24 || hostY > TitleBarHost.Bounds.Height + 24)
        {
            StartTear(e);
            return;
        }

        if ((uint)_tabDragFrom >= (uint)panel.Children.Count)
            return;
        var origin = 0.0;
        for (var i = 0; i < _tabDragFrom; i++)
            origin += panel.Children[i].Bounds.Width + 2;
        var tabWidth = panel.Children[_tabDragFrom].Bounds.Width;
        var minDelta = -origin;
        var maxDelta = Math.Max(minDelta, panel.Bounds.Width - origin - tabWidth);
        var dragDelta = Math.Clamp(x - _tabPressX, minDelta, maxDelta);
        var hover = ReorderShift.HoverAt(
            panel, _tabDragFrom, origin + tabWidth / 2 + dragDelta,
            horizontal: true, lo: 0, hi: panel.Children.Count - 1, spacing: 2);
        if (dragDelta <= minDelta + 0.5)
            hover = 0;
        else if (dragDelta >= maxDelta - 0.5)
            hover = panel.Children.Count - 1;
        ReorderShift.Item(panel.Children[_tabDragFrom], dragDelta, horizontal: true, animate: false);
        if (hover != _tabDragHover)
        {
            ReorderShift.Siblings(panel, _tabDragFrom, hover, tabWidth + 2, horizontal: true);
            _tabDragHover = hover;
        }
    }

    private void Tab_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_tabTorn)
            return;
        FinishTabDrag();
        e.Pointer.Capture(null);
    }

    private void Tab_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_tabTorn)
            FinishTabDrag();
    }

    private void FinishTabDrag()
    {
        if (_tabTorn || _tabDragFrom < 0 || VM is null)
            return;

        var from = _tabDragFrom;
        var to = _tabDragHover;
        var dragged = _tabDragging;
        var panel = TabPanel();
        _tabDragFrom = -1;
        _tabDragHover = -1;
        _tabDragging = false;
        _tabDragSource = null;
        _tabPress = null;
        if (panel is null)
            return;

        if (dragged && to >= 0 && to != from && (uint)from < (uint)panel.Children.Count)
        {
            ReorderShift.Settle(panel, from, to, horizontal: true, spacing: 2);
            _ = CommitTabDrop(panel, from, to);
            return;
        }

        foreach (var child in panel.Children)
            child.ZIndex = 0;
        ReorderShift.Reset(panel, animate: dragged);
    }

    private async Task CommitTabDrop(Panel panel, int from, int to)
    {
        await Task.Delay(ReorderShift.Duration);
        VM?.MoveTab(from, to);
        foreach (var child in panel.Children)
            child.ZIndex = 0;
        ReorderShift.Reset(panel, animate: false);
    }

    private void StartTear(PointerEventArgs e)
    {
        if (_tabTorn || VM is null)
            return;
        if ((uint)_tabDragFrom >= (uint)VM.Tabs.Count)
            return;

        var tab = VM.Tabs[_tabDragFrom];
        _tearWidth = _tabDragSource?.Bounds.Width is > 8 and var w ? w : 160;
        _tabTorn = true;
        _tornTab = tab;
        _tornSize = new Size(Width, Height);
        e.Pointer.Capture(this);
        _tabDragFrom = -1;
        _tabDragHover = -1;
        _tabDragging = false;
        _tabDragSource = null;
        _tabPress = null;
        if (TabPanel() is { } panel)
        {
            foreach (var child in panel.Children)
                child.ZIndex = 0;
            ReorderShift.Reset(panel);
        }

        var screen = this.PointToScreen(e.GetPosition(this));
        TabDrag.Begin(tab, VM, screen);
        TabDragPreview.Show(tab, screen, _tabGrab, _tearWidth);
        TrackTear(e);
    }

    private void OnTornPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_tabTorn)
            return;
        TrackTear(e);
        e.Handled = true;
    }

    private void OnTornPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_tabTorn)
            return;
        e.Handled = true;
        e.Pointer.Capture(null);
        FinishTear();
    }

    private void TrackTear(PointerEventArgs e)
    {
        var screen = this.PointToScreen(e.GetPosition(this));
        TabDrag.Screen = screen;
        TabDragPreview.Move(screen);
        MainWindow? hit = null;
        var index = 0;
        foreach (var window in AppServices.Get<WindowService>().Windows)
        {
            if (!HitTabBar(window, screen, out index))
                continue;
            hit = window;
            break;
        }

        foreach (var window in AppServices.Get<WindowService>().Windows)
            window.TitleBarHost.Classes.Set("tab-drop", window == hit);

        if (hit?.DataContext is MainViewModel dest)
            TabDrag.Offer(dest, index);
        else
            TabDrag.ClearOffer();

        UpdateTornHost(hit);
    }

    private void UpdateTornHost(MainWindow? hit)
    {
        if (_tornTab is not { } tab || VM is null)
            return;

        var overSource = hit is not null && ReferenceEquals(hit.DataContext, VM);
        if (overSource)
        {
            TabDragPreview.Hide();
            if (tab.IsClosing)
            {
                tab.IsClosing = false;
                RestoreTornSlot(tab);
            }

            DragTornInStrip(tab);
            return;
        }

        if (_tabDragFrom >= 0 && TabPanel() is { } panel)
        {
            foreach (var child in panel.Children)
                child.ZIndex = 0;
            ReorderShift.Reset(panel);
            _tabDragFrom = -1;
            _tabDragHover = -1;
            _tabDragging = false;
        }

        if (tab.IsClosing)
            return;
        TabDragPreview.Show(tab, TabDrag.Screen, _tabGrab, _tearWidth);
        tab.IsClosing = true;
        _enteredTabs.Remove(tab);
        if (TabBorder(tab) is { } closing)
            PlayTabExit(closing);
    }

    private void RestoreTornSlot(ExplorerTabViewModel tab)
    {
        if (TabBorder(tab) is not { } border)
            return;
        var width = _tearWidth > 8 ? _tearWidth : TabSlotWidth(border);
        var slot = TabSlot(border);
        SnapTabSize(border, width, 240, 0);
        if (slot is not null && !ReferenceEquals(slot, border))
            SnapTabSize(slot, width, double.PositiveInfinity, 0);
        ExpandTabSize(border, 240, width);
        if (slot is not null && !ReferenceEquals(slot, border))
            ExpandTabSize(slot, double.PositiveInfinity, width);
    }

    private void DragTornInStrip(ExplorerTabViewModel tab)
    {
        if (TabPanel() is not { } panel || VM is null)
            return;
        var from = VM.Tabs.IndexOf(tab);
        if ((uint)from >= (uint)panel.Children.Count)
            return;

        var x = panel.PointToClient(TabDrag.Screen).X;
        var origin = 0.0;
        for (var i = 0; i < from; i++)
        {
            var w = panel.Children[i].Bounds.Width;
            origin += (w < 8 ? 0 : w) + 2;
        }

        var tabWidth = panel.Children[from].Bounds.Width;
        if (tabWidth < 8)
            tabWidth = _tearWidth;
        var minDelta = -origin;
        var maxDelta = Math.Max(minDelta, panel.Bounds.Width - origin - tabWidth);
        var dragDelta = Math.Clamp(x - origin - _tabGrabX, minDelta, maxDelta);
        if (!_tabDragging || _tabDragFrom != from)
        {
            _tabDragging = true;
            _tabDragFrom = from;
            _tabDragHover = from;
            panel.Children[from].ZIndex = 100;
            ReorderShift.Item(panel.Children[from], 0, horizontal: true, animate: false);
        }

        var hover = ReorderShift.HoverAt(
            panel, from, origin + tabWidth / 2 + dragDelta,
            horizontal: true, lo: 0, hi: panel.Children.Count - 1, spacing: 2);
        if (dragDelta <= minDelta + 0.5)
            hover = 0;
        else if (dragDelta >= maxDelta - 0.5)
            hover = panel.Children.Count - 1;
        ReorderShift.Item(panel.Children[from], dragDelta, horizontal: true, animate: false);
        if (hover != _tabDragHover)
        {
            ReorderShift.Siblings(panel, from, hover, tabWidth + 2, horizontal: true);
            _tabDragHover = hover;
        }
    }

    private void FinishTear()
    {
        if (!_tabTorn || _tornTab is not { } tab || VM is null)
            return;

        var source = VM;
        var size = _tornSize;
        var dest = TabDrag.Dest;
        var index = TabDrag.Index;
        var screen = TabDrag.Screen;
        _tabTorn = false;
        _tornTab = null;
        TabDragPreview.Hide();
        TabDrag.End();
        foreach (var window in AppServices.Get<WindowService>().Windows)
            window.TitleBarHost.Classes.Set("tab-drop", false);

        if (dest is null)
        {
            source.Detach(tab);
            AppServices.Get<WindowService>().OpenWindow(
                tab, new PixelPoint(screen.X - 120, screen.Y - 20), size);
        }
        else if (ReferenceEquals(dest, source))
        {
            tab.IsClosing = false;
            var from = _tabDragFrom;
            var to = _tabDragHover;
            var dragged = _tabDragging;
            var panel = TabPanel();
            _tabDragFrom = -1;
            _tabDragHover = -1;
            _tabDragging = false;
            if (TabBorder(tab) is { } border)
            {
                _ = ReleaseTabSize(border);
                if (TabSlot(border) is { } slot && !ReferenceEquals(slot, border))
                    _ = ReleaseTabSize(slot);
            }
            if (panel is not null && dragged && from >= 0 && to >= 0 && to != from &&
                (uint)from < (uint)panel.Children.Count)
            {
                ReorderShift.Settle(panel, from, to, horizontal: true, spacing: 2);
                _ = CommitTabDrop(panel, from, to);
            }
            else if (panel is not null)
            {
                foreach (var child in panel.Children)
                    child.ZIndex = 0;
                ReorderShift.Reset(panel, animate: dragged);
            }
        }
        else
        {
            var destWindow = WindowOf(dest);
            destWindow?.PrepareAdoptEnter(tab);
            source.Detach(tab);
            dest.Adopt(tab, index);
            destWindow?.CommitAdoptEnter(tab);
        }

        if (source.Tabs.Count == 0)
            source.RequestCloseWindow?.Invoke();
    }

    private static MainWindow? WindowOf(MainViewModel model)
    {
        foreach (var window in AppServices.Get<WindowService>().Windows)
        {
            if (ReferenceEquals(window.DataContext, model))
                return window;
        }

        return null;
    }

    private static bool HitTabBar(MainWindow window, PixelPoint screen, out int index)
    {
        index = 0;
        var local = window.PointToClient(screen);
        if (local.Y < 0 || local.Y > 48 || local.X < 0 || local.X > window.Bounds.Width)
            return false;
        index = window.TabInsertIndex(screen);
        return true;
    }

    private int TabInsertIndex(PixelPoint screen)
    {
        if (TabPanel() is not { } panel || panel.Children.Count == 0)
            return VM?.Tabs.Count ?? 0;
        var x = panel.PointToClient(screen).X;
        var acc = 0.0;
        var index = 0;
        for (var i = 0; i < panel.Children.Count; i++)
        {
            var width = panel.Children[i].Bounds.Width;
            if (width < 8)
                continue;
            if (x < acc + width / 2)
                return index;
            acc += width + 2;
            index++;
        }

        return index;
    }

    private void OnTornCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_tabTorn)
            FinishTear();
    }
    private void CloseTab_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        FinishTabDrag();
        if (sender is Button { Tag: ExplorerTabViewModel tab })
            _ = VM?.CloseTab(tab);
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
        else
            return;
        e.Handled = true;
        TabBorder(VM.SelectedTab!)?.BringIntoView();
    }

    private void TabScroller_OnScrollChanged(object? sender, ScrollChangedEventArgs e) =>
        UpdateTabStripOverflow();

    private void TabScrollDecrease_OnClick(object? sender, RoutedEventArgs e) => ScrollTabs(-140);

    private void TabScrollIncrease_OnClick(object? sender, RoutedEventArgs e) => ScrollTabs(140);

    private void ScrollTabs(double delta)
    {
        var max = Math.Max(0, TabScroller.Extent.Width - TabScroller.Viewport.Width);
        var from = _tabScrollAnim is null ? TabScroller.Offset.X : _tabScrollTo;
        _tabScrollTo = Math.Clamp(from + delta, 0, max);
        _ = AnimateTabScrollAsync();
    }

    private async Task AnimateTabScrollAsync()
    {
        _tabScrollAnim?.Cancel();
        var cts = _tabScrollAnim = new CancellationTokenSource();
        var from = TabScroller.Offset.X;
        var to = _tabScrollTo;
        if (Math.Abs(to - from) < 0.5)
        {
            if (ReferenceEquals(_tabScrollAnim, cts))
                _tabScrollAnim = null;
            return;
        }

        var duration = ReorderShift.Duration;
        var clock = Stopwatch.StartNew();
        try
        {
            while (clock.Elapsed < duration)
            {
                cts.Token.ThrowIfCancellationRequested();
                var t = ReorderShift.Ease.Ease(Math.Clamp(
                    clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1));
                TabScroller.Offset = TabScroller.Offset.WithX(from + (to - from) * t);
                await Task.Delay(16, cts.Token);
            }

            TabScroller.Offset = TabScroller.Offset.WithX(to);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_tabScrollAnim, cts))
                _tabScrollAnim = null;
        }
    }

    private void StopTabScrollAnim()
    {
        _tabScrollAnim?.Cancel();
        _tabScrollAnim = null;
    }

    private void UpdateTabStripOverflow()
    {
        var add = (TabBarAddNewTabButton.Bounds.Width > 0 ? TabBarAddNewTabButton.Bounds.Width : 30) + 4;
        var available = TitleBarHost.Bounds.Width - WindowChrome.TrafficLightInset - add;
        var content = TabPanel()?.Bounds.Width ?? TabStrip.Bounds.Width;
        var overflow = content > available + 0.5;
        const double chevron = 24;
        const double gap = 2;
        var maxWidth = Math.Max(0, available - (overflow ? chevron * 2 + gap * 2 : 0));
        if (_tabOverflowReady)
            TabScroller.MaxWidth = maxWidth;
        else
        {
            var transitions = TabScroller.Transitions;
            TabScroller.Transitions = null;
            TabScroller.MaxWidth = maxWidth;
            TabScroller.Transitions = transitions;
            _tabOverflowReady = true;
        }

        ShowTabScrollButton(TabScrollDecreaseButton, overflow, new Thickness(0, 0, gap, 0));
        ShowTabScrollButton(TabScrollIncreaseButton, overflow, new Thickness(gap, 0, 0, 0));
        if (!overflow)
        {
            StopTabScrollAnim();
            if (TabScroller.Offset.X != 0)
                TabScroller.Offset = TabScroller.Offset.WithX(0);
            return;
        }

        var max = Math.Max(0, TabScroller.Extent.Width - TabScroller.Viewport.Width);
        TabScrollDecreaseButton.IsEnabled = TabScroller.Offset.X > 1;
        TabScrollIncreaseButton.IsEnabled = TabScroller.Offset.X < max - 1;
    }

    private static void ShowTabScrollButton(RepeatButton button, bool show, Thickness margin)
    {
        button.Width = show ? 24 : 0;
        button.Opacity = show ? 1 : 0;
        button.IsHitTestVisible = show;
        button.Margin = show ? margin : default;
    }

    private Panel? TabPanel() => TabStrip.ItemsPanelRoot as Panel;

    private void RememberOpenTabs()
    {
        if (TabPanel() is not { } panel)
            return;
        foreach (var child in panel.Children)
        {
            if (TabBorderOf(child)?.DataContext is ExplorerTabViewModel tab)
                _enteredTabs.Add(tab);
        }
    }

    private void Tab_OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!_tabsReady || sender is not Border { DataContext: ExplorerTabViewModel tab } border)
            return;
        if (tab.IsClosing || !_enteredTabs.Add(tab))
            return;
        PlayTabEnter(border);
    }

    private void PrepareAdoptEnter(ExplorerTabViewModel tab) => _enteredTabs.Add(tab);

    private void CommitAdoptEnter(ExplorerTabViewModel tab)
    {
        void Play()
        {
            if (TabBorder(tab) is { } border)
            {
                _enteredTabs.Add(tab);
                PlayTabEnter(border);
                return;
            }

            _enteredTabs.Remove(tab);
        }

        if (TabBorder(tab) is not null)
            Play();
        else
            Dispatcher.UIThread.Post(Play, DispatcherPriority.Loaded);
    }

    private void PlayTabEnter(Border border, bool fromZero = true)
    {
        var slot = TabSlot(border);
        var current = Math.Max(
            border.Bounds.Width,
            slot is not null ? slot.Bounds.Width : 0);
        if (!fromZero && current >= 8 && border.Opacity > 0.5)
            return;

        var target = TabSlotWidth(border);
        SnapTabSize(border, 0, 0, 0);
        if (slot is not null && !ReferenceEquals(slot, border))
            SnapTabSize(slot, 0, 0, 0);
        Dispatcher.UIThread.Post(() =>
        {
            if (border.DataContext is ExplorerTabViewModel { IsClosing: true })
                return;
            ExpandTabSize(border, 240, target);
            if (slot is not null && !ReferenceEquals(slot, border))
                ExpandTabSize(slot, double.PositiveInfinity, target);
            _ = ReleaseTabSize(border);
            if (slot is not null && !ReferenceEquals(slot, border))
                _ = ReleaseTabSize(slot);
        }, DispatcherPriority.Render);
    }

    private static async Task ReleaseTabSize(Control control)
    {
        await Task.Delay(ReorderShift.Duration);
        if (control is Border { DataContext: ExplorerTabViewModel { IsClosing: true } })
            return;
        var transitions = control.Transitions;
        control.Transitions = null;
        control.ClearValue(WidthProperty);
        control.ClearValue(MinWidthProperty);
        control.ClearValue(MaxWidthProperty);
        control.Transitions = transitions;
    }

    private double TabSlotWidth(Visual self)
    {
        if (TabPanel() is not { } panel)
            return 140;
        foreach (var child in panel.Children)
        {
            if (child.Bounds.Width < 8)
                continue;
            var tab = TabBorderOf(child);
            if (tab is null || ReferenceEquals(tab, self))
                continue;
            return tab.Bounds.Width;
        }

        return 140;
    }

    private void PrepareTabClose(ExplorerTabViewModel tab)
    {
        _enteredTabs.Remove(tab);
        if (TabBorder(tab) is not { } border)
            return;
        PlayTabExit(border);
    }

    private void PlayTabExit(Border border)
    {
        var width = border.Bounds.Width;
        if (width < 8)
            width = TabSlotWidth(border);
        var slot = TabSlot(border);
        SnapTabSize(border, width, 240, 1);
        if (slot is not null && !ReferenceEquals(slot, border))
            SnapTabSize(slot, Math.Max(slot.Bounds.Width, width), Math.Max(slot.Bounds.Width, width), 1);
        Dispatcher.UIThread.Post(() =>
        {
            if (border.DataContext is not ExplorerTabViewModel { IsClosing: true })
                return;
            CollapseTabSize(border);
            if (slot is not null && !ReferenceEquals(slot, border))
                CollapseTabSize(slot);
        }, DispatcherPriority.Render);
    }

    private static Control? TabSlot(Border border)
    {
        if (TabPanelOf(border) is not { } panel)
            return border;
        for (var visual = (Visual?)border; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual.GetVisualParent(), panel) && visual is Control slot)
                return slot;
        }

        return border;
    }

    private static Panel? TabPanelOf(Visual border)
    {
        for (var visual = border.GetVisualParent(); visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Panel panel)
                return panel;
        }

        return null;
    }

    private static void SnapTabSize(Control control, double width, double max, double opacity)
    {
        EnsureTabSizeTransitions(control);
        var transitions = control.Transitions;
        control.Transitions = null;
        control.MinWidth = 0;
        control.MaxWidth = max;
        control.Width = width;
        control.Opacity = opacity;
        control.IsHitTestVisible = opacity > 0;
        control.Transitions = transitions;
    }

    private static void ExpandTabSize(Control control, double max, double width)
    {
        control.MinWidth = 0;
        control.MaxWidth = max;
        control.Width = width;
        control.Opacity = 1;
        control.IsHitTestVisible = true;
    }

    private static void CollapseTabSize(Control control)
    {
        control.MinWidth = 0;
        control.Width = 0;
        control.Opacity = 0;
        control.IsHitTestVisible = false;
    }

    private static void EnsureTabSizeTransitions(Control control)
    {
        var list = control.Transitions ??= new Transitions();
        Add(WidthProperty);
        Add(MinWidthProperty);
        Add(MaxWidthProperty);
        Add(OpacityProperty);
        return;

        void Add(AvaloniaProperty property)
        {
            if (list.OfType<DoubleTransition>().Any(t => t.Property == property))
                return;
            list.Add(new DoubleTransition
            {
                Property = property,
                Duration = ReorderShift.Duration,
                Easing = ReorderShift.Ease
            });
        }
    }

    private Border? TabBorder(ExplorerTabViewModel tab)
    {
        if (TabPanel() is not { } panel)
            return null;
        foreach (var child in panel.Children)
        {
            var border = TabBorderOf(child);
            if (border?.Tag as ExplorerTabViewModel == tab ||
                border?.DataContext as ExplorerTabViewModel == tab)
                return border;
        }

        return null;
    }

    private static Border? TabBorderOf(Control child) =>
        child as Border ?? child.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(static b => b.Classes.Contains("TabItem"));


    private void AddressBar_OnDragOver(object? sender, DragEventArgs e)
    {
        if (TabDrag.Is(e.DataTransfer))
            return;
        if (AddressBar.IsEditing)
        {
            _dragHoverOpen.Cancel();
            e.DragEffects = DragDropEffects.None;
            FileDragTip.Hide();
            e.Handled = true;
            return;
        }

        var paths = FileDrag.Paths(e.DataTransfer);
        var dest = AddressBarPathAt(e);
        TrackHoverOpen(dest);
        if (dest is null || paths is null)
        {
            e.DragEffects = DragDropEffects.None;
            FileDragTip.Hide();
            e.Handled = true;
            return;
        }

        e.DragEffects = FileDrag.Effect(paths, dest, e.DragEffects, e.KeyModifiers);
        FileDragTip.Show(e, e.DragEffects, dest);
        e.Handled = true;
    }

    private void AddressBar_OnDragLeave(object? sender, DragEventArgs e)
    {
        if (StillInside(AddressBar, e))
            return;
        _dragHoverOpen.Cancel();
        FileDragTip.Hide();
        e.Handled = true;
    }

    private async void AddressBar_OnDrop(object? sender, DragEventArgs e)
    {
        if (TabDrag.Is(e.DataTransfer))
            return;
        _dragHoverOpen.Cancel();
        FileDragTip.Hide();
        await DropAtAsync(FileDrag.Paths(e.DataTransfer), AddressBar.IsEditing ? null : AddressBarPathAt(e), e);
    }


    private static string? AddressBarPathAt(DragEventArgs e)
    {
        for (var visual = e.Source as Visual; visual is not null && visual is not Controls.AddressBar; visual = visual.GetVisualParent())
        {
            if (visual is Control { Tag: string path } && path.Length > 0)
                return path;
        }

        return null;
    }

    private void TrackHoverOpen(string? path) =>
        _dragHoverOpen.Update(path, VM?.SelectedTab?.CurrentPath, target => _ = VM?.OpenPathAsync(target));

    private static bool StillInside(Visual host, DragEventArgs e)
    {
        var p = e.GetPosition(host);
        return p.X >= 0 && p.Y >= 0 && p.X <= host.Bounds.Width && p.Y <= host.Bounds.Height;
    }

    private async Task DropAtAsync(IReadOnlyList<string>? paths, string? dest, DragEventArgs e)
    {
        if (VM?.SelectedTab is null || dest is null || paths is null)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var effect = FileDrag.Effect(paths, dest, e.DragEffects, e.KeyModifiers);
        e.DragEffects = effect;
        e.Handled = true;
        if (effect == DragDropEffects.None)
            return;
        await VM.SelectedTab.DropFilesAsync(paths, dest, effect == DragDropEffects.Move);
    }


    private void FocusPathBox() => AddressBar.BeginEdit();

    private async void AddressBar_OnPathSubmitted(object? sender, string path)
    {
        if (VM?.SelectedTab is { } tab)
            await tab.OpenAddressAsync(path);
    }

    private void AddressBar_OnOpenInNewWindowRequested(object? sender, string path) =>
        AppServices.Get<WindowService>().OpenWindow(path);


    private async Task<bool> ConfirmAsync(string title, string message, string primary, string close) =>
        await ShowAlertAsync(title, message, MacAlertStyle.Warning, primary, close) == 0;

    private async Task<ConflictDecision> ConflictAsync(string name)
    {
        var result = await ShowAlertAsync(
            Lang.Text("Dialog.Conflict.Title"),
            Lang.Text("Dialog.Conflict.Message", name),
            MacAlertStyle.Warning,
            Lang.Text("Dialog.Conflict.KeepBoth"),
            Lang.Text("Dialog.Conflict.Replace"),
            Lang.Text("Dialog.Conflict.Skip"));
        return result switch
        {
            0 => ConflictDecision.KeepBoth,
            1 => ConflictDecision.Replace,
            _ => ConflictDecision.Skip
        };
    }

    private async Task ErrorAsync(string title, string message)
    {
        await ShowAlertAsync(title, message, MacAlertStyle.Critical, Lang.Text("Common.Action.OK"));
    }

    private Task<int> ShowAlertAsync(
        string title,
        string message,
        MacAlertStyle style,
        params string[] buttons)
    {
        return MacAlert.ShowSheetAsync(
            () =>
            {
                var handle = TryGetPlatformHandle();
                return handle?.HandleDescriptor == "NSWindow" ? handle.Handle : IntPtr.Zero;
            },
            title,
            message,
            style,
            buttons);
    }
 

}
