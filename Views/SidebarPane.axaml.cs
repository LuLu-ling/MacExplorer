using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MacExplorer.Controls;
using MacExplorer.Localization;
using MacExplorer.Models;
using MacExplorer.Native;
using MacExplorer.Services;
using MacExplorer.ViewModels;

namespace MacExplorer.Views;

public partial class SidebarPane : UserControl
{
    private readonly DragHoverOpen _hoverOpen = new();
    private int _from = -1;
    private int _fromRun;
    private int _hoverRun;
    private (int Start, int Count)[]? _units;
    private double _pressY;
    private bool _dragging;
    private Control? _source;
    private TopLevel? _root;

    public SidebarPane()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _root = TopLevel.GetTopLevel(this);
        ClickOutside.Attach(_root, OnRenameOutsidePointerPressed);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClickOutside.Detach(_root, OnRenameOutsidePointerPressed);
        _root = null;
        base.OnDetachedFromVisualTree(e);
    }

    private MainViewModel? VM => DataContext as MainViewModel;

    private Panel? ListPanel => SidebarList.ItemsPanelRoot as Panel;

    private void Row_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: SidebarItem item } row || VM is null)
            return;
        if (item.IsRenaming || RenameField.IsSource(e.Source))
            return;
        if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            return;
        if (ListPanel is not { } panel)
            return;

        _from = VM.Sidebar.Items.IndexOf(item);
        if (_from < 0)
            return;
        _units = VM.Sidebar.ReorderUnits(_from);
        _fromRun = _hoverRun = IndexOf(_units, _from);
        _dragging = false;
        _source = row;
        _pressY = e.GetPosition(panel).Y;
        e.Handled = true;
    }

    private void Row_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_from < 0 || _source is null || VM is null || ListPanel is not { } panel)
            return;
        if (RenameField.IsSource(e.Source))
            return;
        if (!e.GetCurrentPoint(_source).Properties.IsLeftButtonPressed)
            return;
        if (_units is not { Length: > 1 } units || (uint)_fromRun >= (uint)units.Length)
            return;

        var from = units[_fromRun];
        var last = units[^1];
        if ((uint)from.Start >= (uint)panel.Children.Count ||
            (uint)(last.Start + last.Count - 1) >= (uint)panel.Children.Count)
            return;

        var y = e.GetPosition(panel).Y;
        var slot = ReorderShift.RunSize(panel.Children, from.Start, from.Count, horizontal: false);
        if (!_dragging)
        {
            if (Math.Abs(y - _pressY) < FileDrag.Threshold)
                return;
            _dragging = true;
            MarkRun(panel, from, dragging: true);
            ReorderShift.Offset(panel.Children, from.Start, from.Count, 0, horizontal: false, animate: false);
            e.Pointer.Capture(_source);
        }

        var origin = panel.Children[from.Start].Bounds.Y;
        var first = units[0];
        var min = panel.Children[first.Start].Bounds.Y - origin;
        var max = panel.Children[last.Start].Bounds.Y
            + ReorderShift.RunSize(panel.Children, last.Start, last.Count, horizontal: false)
            - origin - slot;
        var delta = Math.Clamp(y - _pressY, min, max);
        var hover = ReorderShift.HoverRun(
            panel.Children, _fromRun, origin + slot / 2 + delta, horizontal: false, units);
        if (delta <= min + 0.5)
            hover = 0;
        else if (delta >= max - 0.5)
            hover = units.Length - 1;
        ReorderShift.Offset(panel.Children, from.Start, from.Count, delta, horizontal: false, animate: false);
        if (hover != _hoverRun)
        {
            ReorderShift.SiblingRuns(panel.Children, _fromRun, hover, units, slot, horizontal: false);
            _hoverRun = hover;
        }
    }

    private void Row_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (RenameField.IsSource(e.Source))
        {
            FinishReorder();
            e.Pointer.Capture(null);
            return;
        }

        var item = _source is Border { Tag: SidebarItem found } ? found : null;
        var dragged = _dragging;
        FinishReorder();
        e.Pointer.Capture(null);
        if (!dragged && item is not null)
            _ = VM?.NavigateSidebarAsync(item);
    }

    private void Settings_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        e.Handled = true;
        _ = VM?.OpenSettingsAsync();
    }


    private void Row_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => FinishReorder();

    private void FinishReorder()
    {
        if (_from < 0 || VM is null)
            return;

        var fromRun = _fromRun;
        var toRun = _hoverRun;
        var units = _units;
        var dragged = _dragging;
        _from = -1;
        _fromRun = 0;
        _hoverRun = 0;
        _units = null;
        _dragging = false;
        _source = null;
        if (ListPanel is { } panel)
        {
            foreach (var child in panel.Children)
            {
                child.ZIndex = 0;
                RowOf(child)?.Classes.Set("dragging", false);
            }
            ReorderShift.Reset(panel);
        }

        if (dragged && units is { Length: > 1 } && toRun != fromRun)
            VM.Sidebar.TryMoveUnits(units, fromRun, toRun);
    }

    private static void MarkRun(Panel panel, (int Start, int Count) run, bool dragging)
    {
        var last = Math.Min(run.Start + run.Count, panel.Children.Count);
        for (var i = run.Start; i < last; i++)
        {
            var child = panel.Children[i];
            child.ZIndex = dragging ? 100 : 0;
            if (RowOf(child) is { } row && !row.Classes.Contains("collapsed"))
                row.Classes.Set("dragging", dragging);
        }
    }

    private static int IndexOf((int Start, int Count)[]? units, int index)
    {
        if (units is null)
            return 0;
        for (var i = 0; i < units.Length; i++)
        {
            var (start, count) = units[i];
            if (index >= start && index < start + count)
                return i;
        }

        return 0;
    }

    private static Border? RowOf(Control child) =>
        child as Border ?? child.FindDescendantOfType<Border>();

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (VM is null || e.Handled)
            return;
        var item = ItemAt(e.Source as Visual);
        if (item is null)
            return;
        var entries = Menu(item);
        if (entries.Length == 0)
            return;
        e.Handled = true;
        MacContextMenu.Show(entries);
    }

    private MacMenuEntry[] Menu(SidebarItem item)
    {
        if (item.Path is not { Length: > 0 } path || item.IsSection)
            return [];
        if (item.Kind == SidebarKind.Tag)
        {
            return
            [
                PlaceMenu.OpenWindow(path),
                new("", Separator: true),
                new(Lang.Text("Common.Action.Rename"), () => BeginTagRename(item), Symbol: MacMenuSymbol.Rename),
                new(Lang.Text("Common.Action.Delete"), () => _ = VM!.DeleteTagAsync(item.Title), Symbol: MacMenuSymbol.Trash),
            ];
        }

        return PlaceMenu.For(
            path,
            unfavorite: item.Kind == SidebarKind.Favorite,
            eject: item.Kind == SidebarKind.Location ? VM!.EjectVolumeAsync : null);
    }

    private void BeginTagRename(SidebarItem item)
    {
        if (VM is null)
            return;
        foreach (var other in VM.Sidebar.Items)
            other.IsRenaming = false;
        item.RenameText = item.Title;
        item.IsRenaming = true;
    }

    private void OnRenameOutsidePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (VM?.Sidebar.Items.FirstOrDefault(static item => item.IsRenaming) is not { } item)
            return;
        if (e.Source is Visual source &&
            source.FindAncestorOfType<RenameField>(includeSelf: true) is
                { DataContext: SidebarItem editor } && ReferenceEquals(editor, item))
            return;
        _ = CommitTagRename(item);
    }

    private async void OnTagRenameCommit(object? sender, RoutedEventArgs e)
    {
        if (sender is RenameField { DataContext: SidebarItem item })
            await CommitTagRename(item);
    }

    private void OnTagRenameCancel(object? sender, RoutedEventArgs e)
    {
        if (sender is RenameField { DataContext: SidebarItem item })
            item.IsRenaming = false;
    }

    private async Task CommitTagRename(SidebarItem item)
    {
        if (!item.IsRenaming)
            return;
        item.IsRenaming = false;
        var name = item.RenameText.Trim();
        if (string.IsNullOrEmpty(name) || name == item.Title || VM is null)
            return;
        await VM.RenameTagAsync(item.Title, name);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (TabDrag.Is(e.DataTransfer))
            return;
        var paths = FileDrag.Paths(e.DataTransfer);
        var item = ItemAt(e.Source as Visual);
        if (FavoriteDrop(e, item, paths, e.DragEffects, out var effect, out var dest))
        {
            _hoverOpen.Cancel();
            e.DragEffects = effect;
            FileDragTip.Show(e, dest is null ? DragDropEffects.None : DragDropEffects.Link, dest);
            e.Handled = true;
            return;
        }

        dest = DropPath(item);
        _hoverOpen.Update(dest, VM?.SelectedTab?.CurrentPath, target => _ = VM?.OpenPathAsync(target));
        e.DragEffects = dest is null || paths is null
            ? DragDropEffects.None
            : FileDrag.Effect(paths, dest, e.DragEffects, e.KeyModifiers);
        FileDragTip.Show(e, e.DragEffects, dest);
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (Inside(this, e))
            return;
        _hoverOpen.Cancel();
        FileDragTip.Hide();
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (TabDrag.Is(e.DataTransfer))
            return;
        var paths = FileDrag.Paths(e.DataTransfer);
        var item = ItemAt(e.Source as Visual);
        _hoverOpen.Cancel();
        FileDragTip.Hide();

        if (FavoriteDrop(e, item, paths, e.DragEffects, out var effect, out _))
        {
            e.DragEffects = effect;
            e.Handled = true;
            if (effect != DragDropEffects.None && paths is not null)
                MacFinder.AddFavorites(paths);
            return;
        }

        var dest = DropPath(item);
        if (VM?.SelectedTab is null || dest is null || paths is null)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        effect = FileDrag.Effect(paths, dest, e.DragEffects, e.KeyModifiers);
        e.DragEffects = effect;
        e.Handled = true;
        if (effect != DragDropEffects.None)
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
        if (paths is null || !OverFavorites(e, item))
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

    private bool OverFavorites(DragEventArgs e, SidebarItem? item)
    {
        if (item is { Id: "favorites" })
            return true;
        return item is null && InFavoritesBand(e);
    }

    private bool InFavoritesBand(DragEventArgs e)
    {
        if (ListPanel is not { } panel || VM is null)
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

    private static SidebarItem? ItemAt(Visual? start)
    {
        for (var visual = start; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Control { Tag: SidebarItem item })
                return item;
        }

        return null;
    }

    private static string? DropPath(SidebarItem? item) =>
        item is { Path.Length: > 0 } && Directory.Exists(item.Path) ? item.Path : null;

    private static bool Inside(Visual host, DragEventArgs e)
    {
        var p = e.GetPosition(host);
        return p.X >= 0 && p.Y >= 0 && p.X <= host.Bounds.Width && p.Y <= host.Bounds.Height;
    }



}
