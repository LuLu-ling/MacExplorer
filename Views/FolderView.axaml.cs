using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using MacExplorer.Controls;
using MacExplorer.Lifecycle;
using MacExplorer.Localization;
using MacExplorer.Models;
using MacExplorer.Native;
using MacExplorer.Services;
using MacExplorer.ViewModels;


namespace MacExplorer.Views;

public partial class FolderView : UserControl
{
    public static readonly StyledProperty<LayoutMetrics> MetricsProperty =
        AvaloniaProperty.Register<FolderView, LayoutMetrics>(nameof(Metrics), LayoutMetrics.For(3));

    public LayoutMetrics Metrics
    {
        get => GetValue(MetricsProperty);
        set => SetValue(MetricsProperty, value);
    }

    public static readonly StyledProperty<DetailsColumns> ColumnsProperty =
        AvaloniaProperty.Register<FolderView, DetailsColumns>(nameof(Columns), DetailsColumns.Shared);

    public DetailsColumns Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    private ExplorerTabViewModel? _boundTab;
    private TopLevel? _root;
    private FileItem? _anchor;
    private FileItem? _pressedItem;
    private FileItem? _dropTarget;
    private bool _marqueeArmed;
    private bool _marqueeActive;
    private bool _pointerSelecting;
    private bool _dragArmed;
    private bool _dragging;
    private bool _deferSingleSelect;
    private bool _syncing;
    private bool _listSyncPosted;
    private Point _marqueeOrigin;
    private Point _marqueePointer;
    private KeyModifiers _marqueeModifiers;
    private DispatcherTimer? _marqueeScroll;
    private FileItem[] _selectionSnapshot = [];
    private IPointer? _captured;
    private PointerPressedEventArgs? _pressArgs;
    private int _colFrom = -1;
    private int _colHover = -1;
    private bool _colDragging;
    private bool _colLayoutHooked;
    private double _colPressX;
    private double _colDelta;
    private double _colSlot;


    public FolderView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPreviewPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPreviewPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        DetailsHeaderStrip.AddHandler(PointerPressedEvent, OnColumnPressed, RoutingStrategies.Tunnel);
        DetailsHeaderStrip.AddHandler(PointerMovedEvent, OnColumnMoved);
        DetailsHeaderStrip.AddHandler(PointerReleasedEvent, OnColumnReleased);
        DetailsHeaderStrip.AddHandler(PointerCaptureLostEvent, OnColumnCaptureLost);

    }

    private ExplorerTabViewModel? Tab => DataContext as ExplorerTabViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindTab(Tab);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _root = TopLevel.GetTopLevel(this);
        ClickOutside.Attach(_root, OnRenameOutsidePointerPressed);
        BindTab(Tab);
        HookFileLists();
        ApplyGroupOverview(Tab?.IsGroupOverview == true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClickOutside.Detach(_root, OnRenameOutsidePointerPressed);
        _root = null;
        EndMarquee();
        FinishColumnDrag();
        BindTab(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void BindTab(ExplorerTabViewModel? tab)
    {
        if (ReferenceEquals(_boundTab, tab))
            return;
        if (_boundTab is not null)
        {
            _boundTab.SelectedItems.CollectionChanged -= OnTabSelectedItemsChanged;
            _boundTab.ViewItems.CollectionChanged -= OnTabSelectedItemsChanged;
            _boundTab.PropertyChanged -= OnTabPropertyChanged;
        }

        _boundTab = tab;
        _anchor = null;
        _pointerSelecting = false;
        if (_boundTab is not null)
        {
            _boundTab.SelectedItems.CollectionChanged += OnTabSelectedItemsChanged;
            _boundTab.ViewItems.CollectionChanged += OnTabSelectedItemsChanged;
            _boundTab.PropertyChanged += OnTabPropertyChanged;
            QueueListSync();
            ApplyGroupOverview(_boundTab.IsGroupOverview);
        }
    }

    private void OnTabSelectedItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueListSync();

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerTabViewModel.Layout))
            QueueListSync();
        else if (e.PropertyName == nameof(ExplorerTabViewModel.IsGroupOverview))
            ApplyGroupOverview(_boundTab?.IsGroupOverview == true);
    }

    private void QueueListSync()
    {
        if (_syncing || _listSyncPosted)
            return;
        _listSyncPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _listSyncPosted = false;
            SyncListFromTab();
        }, DispatcherPriority.Loaded);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _marqueeArmed || _marqueeActive || _pointerSelecting)
            return;
        if (sender is not ListBox list || Tab is null)
            return;
        ApplySelection(list.SelectedItems?.OfType<FileItem>().ToList() ?? []);
        CaptureAnchor(list);
    }

    private async void OnOpen(object? sender, TappedEventArgs e)
    {
        if (Tab is null || _marqueeActive) return;
        if (FindFileGroup(e.Source as Visual) is not null) return;
        await Tab.OpenAsync();
    }

    private async void OnRenameKey(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not FileItem item || Tab is null)
            return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await Tab.CommitRenameAsync(item);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            item.IsRenaming = false;
        }
    }

    private async void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not FileItem item || Tab is null)
            return;
        if (item.IsRenaming)
            await Tab.CommitRenameAsync(item);
    }
    private void OnRenameOutsidePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Tab is not { } tab)
            return;
        var item = tab.ViewItems.OfType<FileItem>().FirstOrDefault(static file => file.IsRenaming);
        if (item is null)
            return;
        if (e.Source is Visual source &&
            source.FindAncestorOfType<RenameTextBox>(includeSelf: true) is
                { DataContext: FileItem editor } && ReferenceEquals(editor, item))
            return;
        _ = tab.CommitRenameAsync(item);
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Tab is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (RenameTextBox.IsSource(e.Source) || e.Source is not Visual visual ||
            !IsInsideFileList(visual) || IsScrollChrome(visual))
            return;
        if (FindFileGroup(visual) is not null)
        {
            e.Handled = true;
            Tab.IsGroupOverview = true;
            return;
        }

        _selectionSnapshot = Tab.SelectedItems.ToArray();
        var item = FindFileItem(visual);
        _pressedItem = item;
        _pointerSelecting = item is not null;
        _deferSingleSelect = false;
        _dragArmed = false;
        _pressArgs = null;
        _marqueeArmed = false;

        if (item is not null && !item.IsRenaming && item.IsSelected
            && !IsToggle(e.KeyModifiers) && !IsRange(e.KeyModifiers))
        {
            _deferSingleSelect = true;
            _dragArmed = true;
            _pressArgs = e;
        }
        else
        {
            if (item is not null)
                ApplyItemPointer(item, e.KeyModifiers);
            _marqueeArmed = true;
        }

        _marqueeActive = false;
        _marqueeOrigin = e.GetPosition(MarqueeHost);
    }


    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (Tab is null)
            return;

        if (_dragArmed && _pressArgs is not null)
        {
            var pos = e.GetPosition(MarqueeHost);
            if (Math.Abs(pos.X - _marqueeOrigin.X) < FileDrag.Threshold &&
                Math.Abs(pos.Y - _marqueeOrigin.Y) < FileDrag.Threshold)
                return;

            var press = _pressArgs;
            _dragArmed = false;
            _marqueeArmed = false;
            _pressArgs = null;
            _deferSingleSelect = false;
            _ = StartFileDragAsync(press);
            e.Handled = true;
            return;
        }

        if (!_marqueeArmed)
            return;

        var marqueePos = e.GetPosition(MarqueeHost);
        if (!_marqueeActive)
        {
            if (Math.Abs(marqueePos.X - _marqueeOrigin.X) < FileDrag.Threshold &&
                Math.Abs(marqueePos.Y - _marqueeOrigin.Y) < FileDrag.Threshold)
                return;
            _marqueeActive = true;
            MarqueeRect.IsVisible = true;
            _captured = e.Pointer;
            e.Pointer.Capture(this);
            StartMarqueeScroll();
        }

        UpdateMarquee(marqueePos, e.KeyModifiers);
        e.Handled = true;
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var startedOnItem = _pointerSelecting;
        var defer = _deferSingleSelect;
        var pressed = _pressedItem;
        _pointerSelecting = false;
        _dragArmed = false;
        _pressArgs = null;
        _pressedItem = null;
        _deferSingleSelect = false;

        if (defer && !_dragging && pressed is not null && Tab is not null)
        {
            ApplySelection([pressed]);
            _anchor = pressed;
        }

        if (_marqueeArmed)
        {
            if (_marqueeActive)
                e.Handled = true;
            else if (!startedOnItem && !IsToggle(e.KeyModifiers) && !IsRange(e.KeyModifiers))
            {
                ApplySelection([]);
                _anchor = null;
            }
        }

        EndMarquee();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        EndMarquee();
        base.OnPointerCaptureLost(e);
    }

    private async Task StartFileDragAsync(PointerPressedEventArgs e)
    {
        if (Tab is null || _dragging)
            return;
        var paths = Tab.SelectedItems.Select(static i => i.Path).ToList();
        if (paths.Count == 0)
            return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;

        var transfer = await FileDrag.Create(top.StorageProvider, paths);
        if (transfer is null)
            return;

        _dragging = true;
        FileDrag.Begin(e.Source as Visual);
        try
        {
            var effect = await DragDrop.DoDragDropAsync(
                e, transfer, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            if (effect == DragDropEffects.Move)
                await Tab.ReloadAsync();
        }
        finally
        {
            _dragging = false;
            FileDrag.End();
            SetDropTarget(null);
            FileDragTip.Hide();
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var paths = FileDrag.Paths(e.DataTransfer);
        var dest = DropDestination(e, paths);
        SetDropTarget(dest?.Item);
        if (dest is null || paths is null)
        {
            e.DragEffects = DragDropEffects.None;
            FileDragTip.Hide();
            e.Handled = true;
            return;
        }

        e.DragEffects = FileDrag.Effect(paths, dest.Value.Path, e.DragEffects, e.KeyModifiers);
        FileDragTip.Show(e, e.DragEffects, dest.Value.Path);
        e.Handled = true;
    }


    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        var p = e.GetPosition(this);
        if (p.X >= 0 && p.Y >= 0 && p.X <= Bounds.Width && p.Y <= Bounds.Height)
            return;
        SetDropTarget(null);
        FileDragTip.Hide();
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = FileDrag.Paths(e.DataTransfer);
        var dest = DropDestination(e, paths);
        SetDropTarget(null);
        FileDragTip.Hide();
        if (Tab is null || paths is null || dest is null)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var effect = FileDrag.Effect(paths, dest.Value.Path, e.DragEffects, e.KeyModifiers);
        e.DragEffects = effect;
        e.Handled = true;
        if (effect == DragDropEffects.None)
            return;
        await Tab.DropFilesAsync(paths, dest.Value.Path, effect == DragDropEffects.Move);
    }

    private (string Path, FileItem? Item)? DropDestination(DragEventArgs e, IReadOnlyList<string>? paths)
    {
        if (Tab is null || paths is null)
            return null;

        var item = FindFileItem(e.Source as Visual);
        if (item is { IsNavigable: true } && FileDrag.CanAccept(paths, item.Path))
            return (item.Path, item);
        if (Tab.ShowFolder && FileDrag.CanAccept(paths, Tab.CurrentPath))
            return (Tab.CurrentPath, null);
        return null;
    }

    private void SetDropTarget(FileItem? item)
    {
        if (ReferenceEquals(_dropTarget, item))
            return;
        if (_dropTarget is not null)
            _dropTarget.IsDropTarget = false;
        _dropTarget = item;
        if (_dropTarget is not null)
            _dropTarget.IsDropTarget = true;
    }


    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Tab is null)
            return;
        if ((e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
            return;
        if (e.Source is Visual source && IsInsideDetailsHeader(source))
        {
            e.Handled = true;
            MacContextMenu.Show(ColumnMenu());
            return;
        }
        e.Handled = true;
        _marqueeArmed = false;
        _marqueeActive = false;
        _pointerSelecting = false;
        _dragArmed = false;
        _pressArgs = null;
        _pressedItem = null;
        _deferSingleSelect = false;
        MarqueeRect.IsVisible = false;
        _captured?.Capture(null);
        _captured = null;


        var item = FindFileItem(e.Source as Visual);
        if (item is not null)
        {
            if (!Tab.SelectedItems.Contains(item))
            {
                ApplySelection([item]);
                _anchor = item;
            }
        }
        else
        {
            ApplySelection([]);
            _anchor = null;
        }

        var tab = Tab;
        MacContextMenu.Show(item is not null ? ItemMenu(tab) : BackgroundMenu(tab));
    }

    private MacMenuEntry[] ItemMenu(ExplorerTabViewModel tab)
    {
        var selected = tab.SelectedItems;
        var common = selected
            .Select(i => i.Tags.Select(t => t.Name))
            .DefaultIfEmpty([])
            .Aggregate((a, b) => a.Intersect(b, StringComparer.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var tagItems = MacTags.All()
            .Select(tag => new MacMenuEntry(
                tag.Name,
                () => tab.ToggleTag(tag.Name),
                Checked: common.Contains(tag.Name),
                Dot: tag.Argb))
            .ToArray();

        return
        [
            new(Lang.Text("Common.Action.Open"), () => tab.OpenCommand.Execute(null), Symbol: MacMenuSymbol.Open),
            ..OpenWindowEntry(selected),
            ..OpenWithEntry(tab, selected),
            new(Lang.Text("Context.ShowInFinder"), () => tab.RevealCommand.Execute(null), Icon: MacMenuSymbol.FinderApp),
            ..FavoriteEntry(selected),
            new("", Separator: true),
            new(Lang.Text("Common.Action.Cut"), () => tab.CutCommand.Execute(null), Symbol: MacMenuSymbol.Cut),
            new(Lang.Text("Common.Action.Copy"), () => tab.CopyCommand.Execute(null), Symbol: MacMenuSymbol.Copy),
            new(Lang.Text("Common.Action.Paste"), () => tab.PasteCommand.Execute(null), tab.PasteCommand.CanExecute(null), Symbol: MacMenuSymbol.Paste),
            new("", Separator: true),
            new(Lang.Text("Common.Action.Rename"), () => tab.RenameCommand.Execute(null), Symbol: MacMenuSymbol.Rename),
            new(Lang.Text("Common.Action.Delete"), () => tab.DeleteCommand.Execute(null), Symbol: MacMenuSymbol.Trash),
            new("", Separator: true),
            new(Lang.Text("Context.Tags"), Children:
            [
                ..tagItems,
                new("", Separator: true),
                new(Lang.Text("Context.RemoveTags"), () => _ = tab.RemoveTagsAsync(), selected.Any(i => i.HasTags), Symbol: MacMenuSymbol.RemoveTags),
            ], Symbol: MacMenuSymbol.Tags),
            new(Lang.Text("Context.Share"), Children:
            [
                new(Lang.Text("Context.AirDrop"), () => tab.ShareCommand.Execute("com.apple.share.AirDrop.send"), Symbol: MacMenuSymbol.AirDrop),
                new(Lang.Text("Context.Mail"), () => tab.ShareCommand.Execute("com.apple.share.Mail.compose"), Icon: MacMenuSymbol.MailApp),
                new(Lang.Text("Context.Messages"), () => tab.ShareCommand.Execute("com.apple.share.Messages.compose"), Icon: MacMenuSymbol.MessagesApp),
            ], Symbol: MacMenuSymbol.Share),
            new("", Separator: true),
            new(Lang.Text("Menu.File.GetInfo"), OpenProperties, Symbol: MacMenuSymbol.Info),
        ];
    }

    private static MacMenuEntry[] OpenWindowEntry(IList<FileItem> selected)
    {
        var folders = selected.Where(i => i.IsDirectory).Select(i => i.Path).ToArray();
        return folders.Length == 0 ? [] :
        [
            new(Lang.Text("Tab.OpenInNewWindow"), () =>
            {
                foreach (var path in folders)
                    AppServices.Get<WindowService>().OpenWindow(path);
            }, Symbol: MacMenuSymbol.NewWindow)
        ];
    }

    private static MacMenuEntry[] OpenWithEntry(ExplorerTabViewModel tab, IList<FileItem> selected)
    {
        if (selected.Count == 0 ||
            selected.Any(static i => i.IsDirectory || i.Extension.Equals(".app", StringComparison.OrdinalIgnoreCase)))
            return [];
        var paths = selected.Select(static i => i.Path).ToArray();
        return [MacOpenWith.Menu(paths, (app, always) => _ = tab.OpenWithAsync(app, always))];
    }

    private static MacMenuEntry[] FavoriteEntry(IList<FileItem> selected)
    {
        if (selected.Count != 1 || !selected[0].IsDirectory)
            return [];
        var path = selected[0].Path;
        return
        [
            new("", Separator: true),
            MacFinder.IsFavorite(path)
                ? new(Lang.Text("Context.Unfavorite"), () => MacFinder.RemoveFavorite(path), Symbol: MacMenuSymbol.Unfavorite)
                : new(Lang.Text("Context.Favorite"), () => MacFinder.AddFavorite(path), Symbol: MacMenuSymbol.Favorite),
        ];
    }

    private MacMenuEntry[] BackgroundMenu(ExplorerTabViewModel tab) =>
    [
        new(Lang.Text("Tab.OpenInNewWindow"), () => AppServices.Get<WindowService>().OpenWindow(tab.CurrentPath), Symbol: MacMenuSymbol.NewWindow),
        new("", Separator: true),
        new(Lang.Text("Context.NewFolder"), () => tab.NewFolderCommand.Execute(null), Symbol: MacMenuSymbol.NewFolder),
        new(Lang.Text("Context.NewFile"), () => tab.NewFileCommand.Execute(null), Symbol: MacMenuSymbol.NewFile),
        new("", Separator: true),
        new(Lang.Text("Common.Action.Paste"), () => tab.PasteCommand.Execute(null), tab.PasteCommand.CanExecute(null), Symbol: MacMenuSymbol.Paste),
        new(Lang.Text("Group.By"), Children: GroupByMenu(tab), Symbol: MacMenuSymbol.Group),
        new(Lang.Text("Common.Action.Refresh"), () => tab.RefreshCommand.Execute(null), Symbol: MacMenuSymbol.Refresh),
        new("", Separator: true),
        new(Lang.Text("Menu.File.GetInfo"), OpenProperties, Symbol: MacMenuSymbol.Info),
    ];

    internal static MacMenuEntry[] SortByMenu(ExplorerTabViewModel tab)
    {
        var field = tab.SortField;
        var direction = tab.SortDirection;
        return
        [
            new(Lang.Text("Sort.Name"), () => tab.SetSort("Name"), Checked: field is SortField.Name),
            new(Lang.Text("Sort.DateModified"), () => tab.SetSort("DateModified"), Checked: field is SortField.DateModified),
            new(Lang.Text("Sort.DateCreated"), () => tab.SetSort("DateCreated"), Checked: field is SortField.DateCreated),
            new(Lang.Text("Sort.Type"), () => tab.SetSort("Type"), Checked: field is SortField.Type),
            new(Lang.Text("Sort.Size"), () => tab.SetSort("Size"), Checked: field is SortField.Size),
            new("", Separator: true),
            new(Lang.Text("Sort.Ascending"), () => tab.SetSortDirection("Ascending"), Checked: direction is SortDirection.Ascending),
            new(Lang.Text("Sort.Descending"), () => tab.SetSortDirection("Descending"), Checked: direction is SortDirection.Descending),
        ];
    }

    internal static MacMenuEntry[] GroupByMenu(ExplorerTabViewModel tab)
    {
        var option = tab.GroupOption;
        var unit = tab.GroupByDateUnit;
        var grouped = option is not GroupOption.None;
        var items = new List<MacMenuEntry>
        {
            new(Lang.Text("Group.None"), () => tab.SetGroup("None"), Checked: option is GroupOption.None),
            new(Lang.Text("Group.Name"), () => tab.SetGroup("Name"), Checked: option is GroupOption.Name),
            new(Lang.Text("Group.DateModified"), Children:
            [
                new(Lang.Text("Group.Year"), () => tab.SetGroup("DateModified:Year"), Checked: option is GroupOption.DateModified && unit is GroupByDateUnit.Year),
                new(Lang.Text("Group.Month"), () => tab.SetGroup("DateModified:Month"), Checked: option is GroupOption.DateModified && unit is GroupByDateUnit.Month),
                new(Lang.Text("Group.Day"), () => tab.SetGroup("DateModified:Day"), Checked: option is GroupOption.DateModified && unit is GroupByDateUnit.Day),
            ]),
            new(Lang.Text("Group.DateCreated"), Children:
            [
                new(Lang.Text("Group.Year"), () => tab.SetGroup("DateCreated:Year"), Checked: option is GroupOption.DateCreated && unit is GroupByDateUnit.Year),
                new(Lang.Text("Group.Month"), () => tab.SetGroup("DateCreated:Month"), Checked: option is GroupOption.DateCreated && unit is GroupByDateUnit.Month),
                new(Lang.Text("Group.Day"), () => tab.SetGroup("DateCreated:Day"), Checked: option is GroupOption.DateCreated && unit is GroupByDateUnit.Day),
            ]),
            new(Lang.Text("Group.Type"), () => tab.SetGroup("FileType"), Checked: option is GroupOption.FileType),
            new(Lang.Text("Group.Size"), () => tab.SetGroup("Size"), Checked: option is GroupOption.Size),
            new(Lang.Text("Group.FileTags"), () => tab.SetGroup("FileTag"), Checked: option is GroupOption.FileTag),
        };
        if (tab.CanGroupByOriginalFolder)
            items.Add(new(Lang.Text("Group.OriginalFolder"), () => tab.SetGroup("OriginalFolder"), Checked: option is GroupOption.OriginalFolder));
        if (tab.CanGroupByDateDeleted)
        {
            items.Add(new(Lang.Text("Group.DateDeleted"), Children:
            [
                new(Lang.Text("Group.Year"), () => tab.SetGroup("DateDeleted:Year"), Checked: option is GroupOption.DateDeleted && unit is GroupByDateUnit.Year),
                new(Lang.Text("Group.Month"), () => tab.SetGroup("DateDeleted:Month"), Checked: option is GroupOption.DateDeleted && unit is GroupByDateUnit.Month),
                new(Lang.Text("Group.Day"), () => tab.SetGroup("DateDeleted:Day"), Checked: option is GroupOption.DateDeleted && unit is GroupByDateUnit.Day),
            ]));
        }
        if (tab.CanGroupByFolderPath)
            items.Add(new(Lang.Text("Group.FolderPath"), () => tab.SetGroup("FolderPath"), Checked: option is GroupOption.FolderPath));
        items.Add(new("", Separator: true));
        items.Add(new(Lang.Text("Group.Ascending"), () => tab.SetGroupDirection("Ascending"), grouped, Checked: tab.GroupDirection is SortDirection.Ascending));
        items.Add(new(Lang.Text("Group.Descending"), () => tab.SetGroupDirection("Descending"), grouped, Checked: tab.GroupDirection is SortDirection.Descending));
        return [.. items];
    }

    private static MacMenuEntry[] ColumnMenu()
    {
        var columns = DetailsColumns.Shared;
        return
        [
            new(Lang.Text("Column.Tags"), () => columns.ShowTags = !columns.ShowTags, Checked: columns.ShowTags),
            new(Lang.Text("Column.DateModified"), () => columns.ShowDateModified = !columns.ShowDateModified, Checked: columns.ShowDateModified),
            new(Lang.Text("Column.DateCreated"), () => columns.ShowDateCreated = !columns.ShowDateCreated, Checked: columns.ShowDateCreated),
            new(Lang.Text("Column.Type"), () => columns.ShowType = !columns.ShowType, Checked: columns.ShowType),
            new(Lang.Text("Column.Size"), () => columns.ShowSize = !columns.ShowSize, Checked: columns.ShowSize),
        ];
    }

    private void OpenProperties()
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainViewModel vm)
            vm.ShowInfo();
    }

    private void UpdateMarquee(Point pos, KeyModifiers modifiers)
    {
        _marqueePointer = pos;
        _marqueeModifiers = modifiers;
        ScrollMarquee(pos);
        DrawMarquee(pos);
        ApplyMarqueeSelection(modifiers);
    }

    private void StartMarqueeScroll()
    {
        _marqueeScroll ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _marqueeScroll.Tick -= OnMarqueeScrollTick;
        _marqueeScroll.Tick += OnMarqueeScrollTick;
        _marqueeScroll.Start();
    }

    private void OnMarqueeScrollTick(object? sender, EventArgs e)
    {
        if (!_marqueeActive)
        {
            _marqueeScroll?.Stop();
            return;
        }

        if (ScrollMarquee(_marqueePointer))
        {
            DrawMarquee(_marqueePointer);
            ApplyMarqueeSelection(_marqueeModifiers);
        }
    }

    private void EndMarquee()
    {
        _marqueeArmed = false;
        _marqueeActive = false;
        MarqueeRect.IsVisible = false;
        _marqueeScroll?.Stop();
        _captured?.Capture(null);
        _captured = null;
    }

    private bool ScrollMarquee(Point pointer)
    {
        var list = VisibleList();
        if (list is null)
            return false;

        var inner = ScrollerOf(list);
        var horizontal = list.Classes.Contains("details") ? DetailsScroller : inner;
        var vertical = inner;
        var applied = default(Vector);
        if (ReferenceEquals(horizontal, vertical))
        {
            if (horizontal is not null)
                applied = ScrollTowardEdge(horizontal, pointer, x: true, y: true);
        }
        else
        {
            if (horizontal is not null)
                applied += ScrollTowardEdge(horizontal, pointer, x: true, y: false);
            if (vertical is not null)
                applied += ScrollTowardEdge(vertical, pointer, x: false, y: true);
        }

        if (applied == default)
            return false;
        _marqueeOrigin = new Point(_marqueeOrigin.X - applied.X, _marqueeOrigin.Y - applied.Y);
        return true;
    }

    private Vector ScrollTowardEdge(ScrollViewer sv, Point pointerInHost, bool x, bool y)
    {
        if (sv.TranslatePoint(default, MarqueeHost) is not { } origin)
            return default;

        var viewport = new Rect(origin, sv.Bounds.Size);
        var dx = x ? EdgeDelta(pointerInHost.X, viewport.X, viewport.Width) : 0;
        var dy = y ? EdgeDelta(pointerInHost.Y, viewport.Y, viewport.Height) : 0;
        if (dx == 0 && dy == 0)
            return default;

        var maxX = Math.Max(0, sv.Extent.Width - sv.Viewport.Width);
        var maxY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        var next = new Vector(
            Math.Clamp(sv.Offset.X + dx, 0, maxX),
            Math.Clamp(sv.Offset.Y + dy, 0, maxY));
        var applied = next - sv.Offset;
        if (applied != default)
            sv.Offset = next;
        return applied;
    }

    private static double EdgeDelta(double pointer, double start, double length)
    {
        if (length <= 1)
            return 0;
        var zone = Math.Min(36, length / 3);
        var before = start + zone;
        var after = start + length - zone;
        if (pointer < before)
            return -ScrollStep(before - pointer, zone);
        if (pointer > after)
            return ScrollStep(pointer - after, zone);
        return 0;
    }

    private static double ScrollStep(double overshoot, double zone)
    {
        var t = overshoot / Math.Max(zone, 1);
        return Math.Clamp(3 + t * 10, 3, 28);
    }

    private static ScrollViewer? ScrollerOf(Control root) =>
        root as ScrollViewer ?? root.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void DrawMarquee(Point pos)
    {
        var rect = ClipMarquee(RawMarquee(pos));
        Canvas.SetLeft(MarqueeRect, rect.X);
        Canvas.SetTop(MarqueeRect, rect.Y);
        MarqueeRect.Width = rect.Width;
        MarqueeRect.Height = rect.Height;
    }

    private Rect RawMarquee(Point pos) => new(
        Math.Min(_marqueeOrigin.X, pos.X),
        Math.Min(_marqueeOrigin.Y, pos.Y),
        Math.Abs(pos.X - _marqueeOrigin.X),
        Math.Abs(pos.Y - _marqueeOrigin.Y));


    private Rect ClipMarquee(Rect marquee)
    {
        if (VisibleList() is not { } list || !list.Classes.Contains("details"))
            return marquee;
        if (list.TranslatePoint(default, MarqueeHost) is not { } origin)
            return marquee;
        return marquee.Intersect(new Rect(origin, list.Bounds.Size));
    }

    private void ApplyMarqueeSelection(KeyModifiers modifiers)
    {
        var list = VisibleList();
        if (list is null || Tab is null)
            return;

        var hits = MarqueeHits(list, RawMarquee(_marqueePointer));
        ApplySelection(MergeSelection(_selectionSnapshot, hits, modifiers));
        if (!IsRange(modifiers) && !IsToggle(modifiers))
            _anchor = hits.Count > 0 ? hits[^1] : null;
    }

    private List<FileItem> MarqueeHits(ListBox list, Rect marquee)
    {
        var count = list.ItemCount;
        var hits = new List<FileItem>();
        var details = list.Classes.Contains("details");
        Control? realized = null;
        var realizedIndex = -1;
        var fileHeight = 0.0;
        var groupHeight = 0.0;
        for (var i = 0; i < count; i++)
        {
            if (list.ContainerFromIndex(i) is not Control container)
                continue;
            realized ??= container;
            if (realizedIndex < 0)
                realizedIndex = i;
            if (list.Items[i] is FileGroup)
                groupHeight = groupHeight == 0 ? container.Bounds.Height : groupHeight;
            else if (fileHeight == 0)
                fileHeight = container.Bounds.Height;
        }

        if (realized is null || realized.TranslatePoint(default, MarqueeHost) is not { } origin)
            return hits;

        var y = origin.Y;
        if (details)
        {
            for (var i = realizedIndex - 1; i >= 0; i--)
                y -= MarqueeSlotHeight(list.Items[i], fileHeight, groupHeight);
        }

        for (var i = 0; i < count; i++)
        {
            var height = MarqueeSlotHeight(list.Items[i], fileHeight, groupHeight);
            Rect rect;
            if (list.ContainerFromIndex(i) is Control container &&
                container.TranslatePoint(default, MarqueeHost) is { } topLeft)
            {
                rect = new Rect(topLeft, container.Bounds.Size);
                y = topLeft.Y;
            }
            else if (details && height > 0)
                rect = new Rect(origin.X, y, realized.Bounds.Width, height);
            else
            {
                y += height;
                continue;
            }

            if (list.Items[i] is FileItem file && marquee.Intersects(rect))
                hits.Add(file);
            y += height;
        }

        return hits;
    }

    private static double MarqueeSlotHeight(object? item, double fileHeight, double groupHeight) =>
        item is FileGroup
            ? (groupHeight > 0 ? groupHeight : fileHeight)
            : fileHeight;

    private void ApplyItemPointer(FileItem item, KeyModifiers modifiers)
    {
        if (Tab is null)
            return;

        var items = Tab.Items;
        var index = items.IndexOf(item);
        if (index < 0)
            return;

        if (IsRange(modifiers))
        {
            var from = _anchor is null ? index : items.IndexOf(_anchor);
            if (from < 0)
                from = index;
            var range = RangeInclusive(items, from, index);
            ApplySelection(IsToggle(modifiers) ? _selectionSnapshot.Union(range).ToList() : range);
            return;
        }

        if (IsToggle(modifiers))
        {
            var set = _selectionSnapshot.ToHashSet();
            if (!set.Add(item))
                set.Remove(item);
            ApplySelection(set.ToList());
            _anchor = item;
            return;
        }

        ApplySelection([item]);
        _anchor = item;
    }

    private void ApplySelection(IReadOnlyList<FileItem> selected)
    {
        Tab?.SetSelection(selected);
    }

    private static List<FileItem> RangeInclusive(IList<FileItem> items, int a, int b)
    {
        if (items.Count == 0)
            return [];
        var lo = Math.Clamp(Math.Min(a, b), 0, items.Count - 1);
        var hi = Math.Clamp(Math.Max(a, b), 0, items.Count - 1);
        var result = new List<FileItem>(hi - lo + 1);
        for (var i = lo; i <= hi; i++)
            result.Add(items[i]);
        return result;
    }

    private static bool IsToggle(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Meta) || modifiers.HasFlag(KeyModifiers.Control);

    private static bool IsRange(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Shift);

    private static List<FileItem> MergeSelection(
        IReadOnlyList<FileItem> snapshot,
        IReadOnlyList<FileItem> hits,
        KeyModifiers modifiers)
    {
        if (IsToggle(modifiers))
        {
            var set = snapshot.ToHashSet();
            foreach (var hit in hits)
            {
                if (!set.Add(hit))
                    set.Remove(hit);
            }

            return set.ToList();
        }

        if (IsRange(modifiers))
            return snapshot.Concat(hits).Distinct().ToList();

        return hits.ToList();
    }

    private void SyncListFromTab()
    {
        if (Tab is null)
            return;
        var list = VisibleList();
        if (list is null)
            return;
        SelectOnList(list, Tab.SelectedItems);
    }

    private void SelectOnList(ListBox list, IReadOnlyList<FileItem> selected)
    {
        var set = selected.ToHashSet();
        if (!ListMatches(list, set))
        {
            _syncing = true;
            try
            {
                list.SelectionMode = SelectionMode.Multiple;
                list.Selection.Clear();
                var count = list.ItemCount;
                for (var i = 0; i < count; i++)
                {
                    if (list.Items[i] is FileItem item && set.Contains(item))
                        list.Selection.Select(i);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        SetListAnchor(list);
    }

    private void CaptureAnchor(ListBox list)
    {
        var index = list.Selection.AnchorIndex;
        if ((uint)index < (uint)list.ItemCount && list.Items[index] is FileItem item)
            _anchor = item;
    }

    private void SetListAnchor(ListBox list)
    {
        if (_anchor is null)
            return;
        var count = list.ItemCount;
        for (var i = 0; i < count; i++)
        {
            if (!ReferenceEquals(list.Items[i], _anchor))
                continue;
            list.Selection.AnchorIndex = i;
            return;
        }
    }

    private static bool ListMatches(ListBox list, HashSet<FileItem> selected)
    {
        var current = list.SelectedItems;
        if (current is null)
            return selected.Count == 0;
        if (current.Count != selected.Count)
            return false;
        foreach (var item in current)
        {
            if (item is not FileItem file || !selected.Contains(file))
                return false;
        }

        return true;
    }

    private ListBox? VisibleList() =>
        this.GetVisualDescendants().OfType<ListBox>().FirstOrDefault(static l => l.IsEffectivelyVisible && l.Classes.Contains("FileList"));
    private void HookFileLists()
    {
        foreach (var list in this.GetVisualDescendants().OfType<ListBox>())
        {
            if (!list.Classes.Contains("FileList"))
                continue;
            list.ContainerPrepared -= OnFileContainerPrepared;
            list.ContainerPrepared += OnFileContainerPrepared;
        }
    }

    private void OnFileContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not ListBoxItem item)
            return;
        var data = sender is ListBox list && (uint)e.Index < (uint)list.ItemCount
            ? list.Items[e.Index]
            : item.DataContext;
        var group = data is FileGroup;
        item.Classes.Set("group", group);
        item.Focusable = true;
        item.IsHitTestVisible = true;
        if (item.FindDescendantOfType<ColumnStripPanel>() is not { } strip)
            return;
        ReorderShift.Reset(strip);
        foreach (var child in strip.Children)
            child.ZIndex = 0;
    }

    private void OnOverviewTapped(object? sender, TappedEventArgs e)
    {
        if (Tab is null || FindFileGroup(e.Source as Visual) is not FileGroup group)
            return;
        Tab.IsGroupOverview = false;
        Dispatcher.UIThread.Post(() => VisibleList()?.ScrollIntoView(group), DispatcherPriority.Loaded);
    }

    private void ApplyGroupOverview(bool overview)
    {
        if (FileContentHost.RenderTransform is not ScaleTransform fileScale
            || GroupOverviewHost.RenderTransform is not ScaleTransform overviewScale)
            return;

        if (overview)
        {
            GroupOverviewHost.IsHitTestVisible = true;
            GroupOverviewHost.Opacity = 1;
            overviewScale.ScaleX = 1;
            overviewScale.ScaleY = 1;
            FileContentHost.IsHitTestVisible = false;
            FileContentHost.Opacity = 0;
            fileScale.ScaleX = 0.92;
            fileScale.ScaleY = 0.92;
            return;
        }

        FileContentHost.IsHitTestVisible = true;
        FileContentHost.Opacity = 1;
        fileScale.ScaleX = 1;
        fileScale.ScaleY = 1;
        GroupOverviewHost.IsHitTestVisible = false;
        GroupOverviewHost.Opacity = 0;
        overviewScale.ScaleX = 1.08;
        overviewScale.ScaleY = 1.08;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape || Tab is not { IsGroupOverview: true })
            return;
        Tab.IsGroupOverview = false;
        e.Handled = true;
    }

    private static FileGroup? FindFileGroup(Visual? start)
    {
        for (var visual = start; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is ListBox list && list.Classes.Contains("FileList"))
                return null;
            if (visual is Control { DataContext: FileGroup group })
                return group;
        }

        return null;
    }

    private static FileItem? FindFileItem(Visual? start)
    {
        for (var visual = start; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Control { DataContext: FileItem item })
                return item;
        }

        return null;
    }

    private static bool IsInsideFileList(Visual start)
    {
        for (var visual = start; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is ListBox list && list.Classes.Contains("FileList"))
                return true;
        }

        return false;
    }

    private static bool IsScrollChrome(Visual start)
    {
        for (var visual = start; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is ScrollBar)
                return true;
        }

        return false;
    }

    private bool IsInsideDetailsHeader(Visual start)
    {
        for (var visual = start; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, DetailsHeader))
                return true;
        }

        return false;
    }

    private void OnColumnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source &&
            source.FindAncestorOfType<ColumnSplitter>(includeSelf: true) is not null)
            return;
        if (!e.GetCurrentPoint(DetailsHeaderStrip).Properties.IsLeftButtonPressed)
            return;

        var unit = ColumnUnit(DetailsHeaderStrip, e.Source);
        if (unit is null)
            return;
        var units = DetailsHeaderStrip.Units();
        var from = Array.IndexOf(units, unit);
        if (from < 0)
            return;

        _colFrom = from;
        _colHover = from;
        _colDragging = false;
        _colDelta = 0;
        _colSlot = units[from].Bounds.Width;
        _colPressX = e.GetPosition(DetailsHeaderStrip).X;
        e.Pointer.Capture(DetailsHeaderStrip);
        e.Handled = true;
    }

    private void OnColumnMoved(object? sender, PointerEventArgs e)
    {
        if (_colFrom < 0)
            return;
        if (!e.GetCurrentPoint(DetailsHeaderStrip).Properties.IsLeftButtonPressed)
            return;

        var header = DetailsHeaderStrip.Units();
        if ((uint)_colFrom >= (uint)header.Length)
            return;

        var x = e.GetPosition(DetailsHeaderStrip).X;
        if (!_colDragging)
        {
            if (Math.Abs(x - _colPressX) < FileDrag.Threshold)
                return;
            if (header.Length < 2)
                return;
            _colDragging = true;
            _colSlot = header[_colFrom].Bounds.Width;
            HookColumnLayout(true);
            ShiftColumns(_colFrom, _colHover, 0, siblings: true);
        }

        var origin = ReorderShift.Origin(header, _colFrom, horizontal: true);
        var width = header[_colFrom].Bounds.Width;
        var min = -origin;
        var max = Math.Max(min, ReorderShift.Origin(header, header.Length, horizontal: true) - origin - width);
        var delta = Math.Clamp(x - _colPressX, min, max);
        var hover = ReorderShift.HoverAt(
            header, _colFrom, origin + width / 2 + delta, horizontal: true, lo: 0, hi: header.Length - 1);
        if (delta <= min + 0.5)
            hover = 0;
        else if (delta >= max - 0.5)
            hover = header.Length - 1;
        _colDelta = delta;
        var siblings = hover != _colHover;
        if (siblings)
            _colHover = hover;
        ShiftColumns(_colFrom, _colHover, delta, siblings);
    }

    private void OnColumnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var from = _colFrom;
        var dragged = _colDragging;
        FinishColumnDrag();
        e.Pointer.Capture(null);
        if (dragged || from < 0 || Tab is null)
            return;
        var units = DetailsHeaderStrip.Units();
        if ((uint)from >= (uint)units.Length)
            return;
        var kind = ColumnStripPanel.GetColumn(units[from]);
        if (kind is not DetailsColumnKind.Tags)
            Tab.ToggleSort(kind.ToString());
    }

    private void OnColumnCaptureLost(object? sender, PointerCaptureLostEventArgs e) => FinishColumnDrag();

    private void FinishColumnDrag()
    {
        if (_colFrom < 0)
            return;

        var from = _colFrom;
        var to = _colHover;
        var dragged = _colDragging;
        _colFrom = -1;
        _colHover = -1;
        _colDragging = false;
        _colDelta = 0;
        HookColumnLayout(false);

        if (dragged && to >= 0 && to != from)
        {
            foreach (var strip in ColumnStrips())
                ReorderShift.Settle(strip.Units(), from, to, horizontal: true);
            _ = CommitColumnDrop(from, to);
            return;
        }

        ResetColumnShift(animate: dragged);
    }

    private async Task CommitColumnDrop(int from, int to)
    {
        await Task.Delay(ReorderShift.Duration);
        Columns.TryMoveVisible(from, to);
        foreach (var strip in ColumnStrips())
            strip.UpdateLayout();
        ResetColumnShift(animate: false);
    }

    private void OnColumnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_colDragging || _colFrom < 0)
            return;
        ShiftColumns(_colFrom, _colHover, _colDelta, siblings: true, freshOnly: true);
    }

    private void ShiftColumns(int from, int hover, double delta, bool siblings, bool freshOnly = false)
    {
        foreach (var strip in ColumnStrips())
        {
            var units = strip.Units();
            if ((uint)from >= (uint)units.Length)
                continue;
            var dragged = units[from];
            var fresh = dragged.ZIndex != 100;
            if (freshOnly && !fresh)
            {
                ReorderShift.Item(dragged, delta, horizontal: true, animate: false);
                continue;
            }

            dragged.ZIndex = 100;
            ReorderShift.Item(dragged, delta, horizontal: true, animate: false);
            if (siblings || fresh)
                ReorderShift.Siblings(units, from, hover, _colSlot, horizontal: true);
        }
    }

    private void ResetColumnShift(bool animate)
    {
        foreach (var strip in ColumnStrips())
        {
            foreach (var child in strip.Children)
                child.ZIndex = 0;
            ReorderShift.Reset(strip, animate);
        }
    }

    private void HookColumnLayout(bool on)
    {
        if (on == _colLayoutHooked)
            return;
        _colLayoutHooked = on;
        if (on)
            DetailsList.LayoutUpdated += OnColumnLayoutUpdated;
        else
            DetailsList.LayoutUpdated -= OnColumnLayoutUpdated;
    }

    private IEnumerable<ColumnStripPanel> ColumnStrips()
    {
        yield return DetailsHeaderStrip;
        if (DetailsList.ItemsPanelRoot is not Panel rows)
            yield break;
        foreach (var row in rows.Children)
        {
            if (row.FindDescendantOfType<ColumnStripPanel>() is { } strip)
                yield return strip;
        }
    }

    private static Control? ColumnUnit(ColumnStripPanel strip, object? source)
    {
        for (var visual = source as Visual; visual is not null && !ReferenceEquals(visual, strip); visual = visual.GetVisualParent())
        {
            if (visual is Control control && strip.Children.Contains(control))
                return control;
        }

        return null;
    }
}
