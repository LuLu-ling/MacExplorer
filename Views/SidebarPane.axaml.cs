using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using MacExplorer.Controls;
using MacExplorer.Lifecycle;
using MacExplorer.Localization;
using MacExplorer.Models;
using MacExplorer.Native;
using MacExplorer.Services;
using MacExplorer.ViewModels;

namespace MacExplorer.Views;

public partial class SidebarPane : UserControl
{
    private static readonly TimeSpan ShiftDuration = TimeSpan.FromMilliseconds(220);
    private static readonly SplineEasing ShiftEase = new(0.22, 1, 0.36, 1);

    private readonly DragHoverOpen _hoverOpen = new();
    private int _from = -1;
    private int _hover = -1;
    private int _lo;
    private int _hi;
    private double _pressY;
    private bool _dragging;
    private Control? _source;

    public SidebarPane()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
    }

    private MainViewModel? VM => DataContext as MainViewModel;

    private Panel? ListPanel => SidebarList.ItemsPanelRoot as Panel;

    private void Row_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: SidebarItem item } row || VM is null)
            return;
        if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            return;
        if (ListPanel is not { } panel)
            return;

        _from = VM.Sidebar.Items.IndexOf(item);
        if (_from < 0)
            return;
        _hover = _from;
        _dragging = false;
        _source = row;
        _pressY = e.GetPosition(panel).Y;
        if (VM.Sidebar.ReorderRange(_from) is { } range)
            (_lo, _hi) = range;
        else
            _lo = _hi = _from;
        e.Handled = true;
    }

    private void Row_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_from < 0 || _source is null || VM is null || ListPanel is not { } panel)
            return;
        if (!e.GetCurrentPoint(_source).Properties.IsLeftButtonPressed)
            return;
        if (_hi <= _lo || (uint)_from >= (uint)panel.Children.Count || (uint)_hi >= (uint)panel.Children.Count)
            return;

        var y = e.GetPosition(panel).Y;
        if (!_dragging)
        {
            if (Math.Abs(y - _pressY) < FileDrag.Threshold)
                return;
            _dragging = true;
            _source.Classes.Set("dragging", true);
            panel.Children[_from].ZIndex = 100;
            Shift(panel.Children[_from], 0, animate: false);
            e.Pointer.Capture(_source);
        }
        var origin = Top(panel, _from);
        var height = panel.Children[_from].Bounds.Height;
        var min = Top(panel, _lo) - origin;
        var max = Top(panel, _hi) + panel.Children[_hi].Bounds.Height - origin - height;
        var delta = Math.Clamp(y - _pressY, min, max);
        var hover = delta <= min + 0.5 ? _lo
            : delta >= max - 0.5 ? _hi
            : IndexAt(panel, origin + height / 2 + delta, _lo, _hi);
        Shift(panel.Children[_from], delta, animate: false);
        if (hover != _hover)
        {
            ShiftSiblings(panel, _from, hover, height);
            _hover = hover;
        }
    }

    private void Row_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var item = _source is Border { Tag: SidebarItem found } ? found : null;
        var dragged = _dragging;
        FinishReorder();
        e.Pointer.Capture(null);
        if (!dragged && item is not null)
            _ = VM?.NavigateSidebarAsync(item);
    }

    private void Row_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => FinishReorder();

    private void FinishReorder()
    {
        if (_from < 0 || VM is null)
            return;

        var from = _from;
        var to = _hover;
        var dragged = _dragging;
        var panel = ListPanel;
        _from = -1;
        _hover = -1;
        _dragging = false;
        if (_source is not null)
            _source.Classes.Set("dragging", false);
        _source = null;
        if (panel is not null)
        {
            foreach (var child in panel.Children)
                child.ZIndex = 0;
            ResetShifts(panel);
        }

        if (dragged && to >= 0 && to != from)
            VM.Sidebar.TryMove(from, to);
    }

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
        if (item.Path is not { Length: > 0 } path)
            return [];
        MacMenuEntry[] actions = item.Kind switch
        {
            SidebarKind.Favorite =>
            [
                new(Lang.Text("Context.Unfavorite"), () => MacFinder.RemoveFavorite(path), Symbol: MacMenuSymbol.Unfavorite),
                new("", Separator: true),
                new(Lang.Text("Menu.File.GetInfo"), () => VM!.ShowInfo([path]), Symbol: MacMenuSymbol.Info),
            ],
            SidebarKind.Location when MacWorkspace.IsDiskImage(path) =>
            [
                new(Lang.Text("Context.Eject"), () => _ = VM!.EjectVolumeAsync(path), Symbol: MacMenuSymbol.Eject),
                new("", Separator: true),
                new(Lang.Text("Menu.File.GetInfo"), () => VM!.ShowInfo([path]), Symbol: MacMenuSymbol.Info),
            ],
            _ => []
        };
        if (item.IsSection)
            return actions;
        return [new(Lang.Text("Tab.OpenInNewWindow"), () => AppServices.Get<WindowService>().OpenWindow(path), Symbol: MacMenuSymbol.NewWindow), ..actions];
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
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

    private static double Top(Panel panel, int index)
    {
        var y = 0.0;
        for (var i = 0; i < index && i < panel.Children.Count; i++)
            y += panel.Children[i].Bounds.Height;
        return y;
    }

    private static int IndexAt(Panel panel, double y, int lo, int hi)
    {
        var acc = 0.0;
        var last = lo;
        for (var i = 0; i < panel.Children.Count && i <= hi; i++)
        {
            var h = panel.Children[i].Bounds.Height;
            if (h < 1)
                continue;
            if (i < lo)
            {
                acc += h;
                continue;
            }

            if (y <= acc + h / 2)
                return i;
            acc += h;
            last = i;
        }

        return last;
    }

    private static void ShiftSiblings(Panel panel, int from, int hover, double height)
    {
        for (var i = 0; i < panel.Children.Count; i++)
        {
            if (i == from)
                continue;
            var shift = 0.0;
            if (from < hover && i > from && i <= hover)
                shift = -height;
            else if (from > hover && i >= hover && i < from)
                shift = height;
            Shift(panel.Children[i], shift, animate: true);
        }
    }

    private static void ResetShifts(Panel panel)
    {
        foreach (var child in panel.Children)
            Shift(child, 0, animate: false);
    }

    private static void Shift(Control child, double y, bool animate)
    {
        if (animate)
        {
            if (child.Transitions is not { Count: > 0 })
            {
                child.Transitions = new Transitions
                {
                    new TransformOperationsTransition
                    {
                        Property = Visual.RenderTransformProperty,
                        Duration = ShiftDuration,
                        Easing = ShiftEase
                    }
                };
            }
        }
        else
        {
            child.Transitions = null;
        }

        child.RenderTransform = y == 0
            ? null
            : TransformOperations.Parse(FormattableString.Invariant($"translateY({y}px)"));
    }

}
