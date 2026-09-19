using Avalonia;
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


using MacExplorer.Localization;
namespace MacExplorer.Views;

public partial class MainWindow : FAAppWindow
{
    private readonly DragHoverOpen _dragHoverOpen = new();
    private bool _tabsReady;
    private readonly HashSet<ExplorerTabViewModel> _enteredTabs = [];

    public MainWindow()
    {
        InitializeComponent();
        AddressBar.MenuService = new BreadcrumbMenuService(AppServices.Get<VolumeService>());
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.Height = 48;
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

    private void OnWindowDragEnter(object? sender, DragEventArgs e) =>
        FileDrag.Begin(e.Source as Visual);

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
        ShowToolbarMenu(sender, e, static (vm, tab) =>
        {
            var field = Config.Layout.SortFieldValue;
            return
            [
                new(Lang.Text("Sort.By"), Children:
                [
                    new(Lang.Text("Sort.Name"), () => vm.SetSort("Name"), Checked: field is SortField.Name),
                    new(Lang.Text("Sort.DateModified"), () => vm.SetSort("DateModified"), Checked: field is SortField.DateModified),
                    new(Lang.Text("Sort.DateCreated"), () => vm.SetSort("DateCreated"), Checked: field is SortField.DateCreated),
                    new(Lang.Text("Sort.Type"), () => vm.SetSort("Type"), Checked: field is SortField.Type),
                    new(Lang.Text("Sort.Size"), () => vm.SetSort("Size"), Checked: field is SortField.Size),
                ], Symbol: MacMenuSymbol.Sort),
                new(Lang.Text("Group.By"), Children: FolderView.GroupByMenu(tab), Symbol: MacMenuSymbol.Group),
            ];
        });

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
    private double _tabPressX;
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
            new(Lang.Text("Tab.New"), () => VM.NewTab(), Symbol: MacMenuSymbol.NewTab),
            new(Lang.Text("Tab.NewWindow"), () => AppServices.Get<WindowService>().OpenWindow(), Symbol: MacMenuSymbol.NewWindow),
            new(Lang.Text("Tab.OpenInNewWindow"), () => AppServices.Get<WindowService>().OpenWindow(tab.CurrentPath), Symbol: MacMenuSymbol.NewWindow),
            new(Lang.Text("Tab.Duplicate"), () => VM.DuplicateTab(), Symbol: MacMenuSymbol.Duplicate),
            new(Lang.Text("Tab.Close"), () => _ = VM.CloseTab(tab), VM.CanCloseTab, Symbol: MacMenuSymbol.Close),
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
            if (Math.Abs(x - _tabPressX) < FileDrag.Threshold)
                return;
            _tabDragging = true;
            panel.Children[_tabDragFrom].ZIndex = 100;
            _tabDragSource.ZIndex = 100;
            ReorderShift.Item(panel.Children[_tabDragFrom], 0, horizontal: true, animate: false);
        }

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
        var dragged = _tabDragging;
        var panel = TabPanel();
        _tabDragFrom = -1;
        _tabDragHover = -1;
        _tabDragging = false;
        _tabDragSource = null;
        if (panel is null)
            return;

        if (dragged && to >= 0 && to != from)
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
        var chevrons = overflow ? 52 : 0;
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

    private void PlayTabEnter(Border border)
    {
        var target = TabSlotWidth(border);
        var transitions = border.Transitions;
        border.Transitions = null;
        border.MinWidth = 0;
        border.MaxWidth = 0;
        border.Width = 0;
        border.Opacity = 0;
        border.Transitions = transitions;
        Dispatcher.UIThread.Post(() =>
        {
            border.MinWidth = 0;
            border.MaxWidth = 240;
            border.Width = target;
            border.Opacity = 1;
            _ = ReleaseTabSize(border);
        }, DispatcherPriority.Render);
    }

    private static async Task ReleaseTabSize(Border border)
    {
        await Task.Delay(ReorderShift.Duration);
        if (border.DataContext is ExplorerTabViewModel { IsClosing: true })
            return;
        var transitions = border.Transitions;
        border.Transitions = null;
        border.ClearValue(WidthProperty);
        border.ClearValue(MinWidthProperty);
        border.ClearValue(MaxWidthProperty);
        border.Transitions = transitions;
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
        var width = border.Bounds.Width;
        var transitions = border.Transitions;
        border.Transitions = null;
        border.Width = width;
        border.MinWidth = 0;
        border.MaxWidth = width;
        border.Transitions = transitions;
        border.Width = 0;
        border.MaxWidth = 0;
        border.Opacity = 0;
        border.IsHitTestVisible = false;
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
