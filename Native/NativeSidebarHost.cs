using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using MacExplorer.Localization;
using MacExplorer.Models;
using MacExplorer.Services;
using MacExplorer.Input;
using MacExplorer.ViewModels;

namespace MacExplorer.Native;

public enum NativeSidebarMode
{
    Explorer,
    Settings
}

public sealed class NativeSidebarHost : NativeControlHost
{
    public static readonly StyledProperty<NativeSidebarMode> ModeProperty =
        AvaloniaProperty.Register<NativeSidebarHost, NativeSidebarMode>(nameof(Mode));

    private MacSidebarPane? _pane;
    private MainViewModel? _explorer;
    private SettingsViewModel? _settings;
    private readonly List<SidebarItem> _hooked = [];
    private int _push;
    private readonly DragHoverOpen _hoverOpen = new();

    public NativeSidebarMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public NativeSidebarHost()
    {
        Focusable = true;
        ClipToBounds = true;
        LayoutUpdated += (_, _) =>
        {
            if (IsEffectivelyVisible && Bounds.Width > 0 && Bounds.Height > 0)
                TryUpdateNativeControlPosition();
        };
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        if (arranged.Width > 0 && arranged.Height > 0)
            TryUpdateNativeControlPosition();
        return arranged;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _pane = MacSidebarPane.Create();
        _pane.Selected += OnSelected;
        _pane.Moved += OnMoved;
        _pane.Context += OnContext;
        _pane.Dropped += OnDropped;
        _pane.Hovered += OnHovered;
        _pane.Renamed += OnRenamed;
        _pane.Settings += OnSettings;
        Attach(DataContext);
        return new PlatformHandle(_pane.View, "NSView");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_pane is null)
        {
            base.DestroyNativeControlCore(control);
            return;
        }

        _pane.Selected -= OnSelected;
        _pane.Moved -= OnMoved;
        _pane.Context -= OnContext;
        _pane.Dropped -= OnDropped;
        _pane.Hovered -= OnHovered;
        _pane.Renamed -= OnRenamed;
        _pane.Settings -= OnSettings;
        _pane.Dispose();
        _pane = null;
        _hoverOpen.Cancel();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LocalizationService.LanguageChanged += Push;
        ActualThemeVariantChanged += OnThemeChanged;
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += OnThemeChanged;
        base.OnAttachedToVisualTree(e);
        Push();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LocalizationService.LanguageChanged -= Push;
        ActualThemeVariantChanged -= OnThemeChanged;
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged -= OnThemeChanged;
        DetachExplorer();
        DetachSettings();
        _hoverOpen.Cancel();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty)
            Attach(change.NewValue);
        else if (change.Property == ModeProperty)
            Push();
    }

    private void Attach(object? data)
    {
        DetachExplorer();
        DetachSettings();
        if (Mode == NativeSidebarMode.Settings && data is SettingsViewModel settings)
        {
            _settings = settings;
            _settings.PropertyChanged += OnSettingsProperty;
        }
        else if (data is MainViewModel explorer)
        {
            _explorer = explorer;
            _explorer.PropertyChanged += OnExplorerProperty;
            _explorer.Sidebar.Items.CollectionChanged += OnItemsChanged;
            HookItems();
        }

        Push();
    }

    private void DetachExplorer()
    {
        if (_explorer is null)
            return;
        _explorer.PropertyChanged -= OnExplorerProperty;
        _explorer.Sidebar.Items.CollectionChanged -= OnItemsChanged;
        UnhookItems();
        _explorer = null;
    }

    private void DetachSettings()
    {
        if (_settings is null)
            return;
        _settings.PropertyChanged -= OnSettingsProperty;
        _settings = null;
    }

    private void HookItems()
    {
        UnhookItems();
        if (_explorer is null)
            return;
        foreach (var item in _explorer.Sidebar.Items)
        {
            item.PropertyChanged += OnItemProperty;
            _hooked.Add(item);
        }
    }

    private void UnhookItems()
    {
        foreach (var item in _hooked)
            item.PropertyChanged -= OnItemProperty;
        _hooked.Clear();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HookItems();
        Push();
    }

    private void OnItemProperty(object? sender, PropertyChangedEventArgs e) => Push();
    private void OnExplorerProperty(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ShowSettings) or null)
            Push();
    }
    private void OnSettingsProperty(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsViewModel.SelectedPage) or null)
            Push();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Push();

    private void Push()
    {
        if (_pane is null)
            return;
        var stamp = ++_push;
        Dispatcher.UIThread.Post(() =>
        {
            if (stamp != _push || _pane is null)
                return;
            _pane.Apply(Mode == NativeSidebarMode.Settings ? CaptureSettings() : CaptureExplorer());
        }, DispatcherPriority.Render);
    }

    private void OnSelected(string id)
    {
        if (Mode == NativeSidebarMode.Settings)
        {
            _settings?.SelectPageCommand.Execute(id);
            return;
        }

        var item = Item(id);
        if (item is not null)
            _ = _explorer?.NavigateSidebarAsync(item);
    }

    private void OnMoved(int from, int to) => _explorer?.Sidebar.TryMove(from, to);

    private void OnContext(string id)
    {
        var item = Item(id);
        if (item is null || _explorer is null)
            return;
        var entries = Menu(item);
        if (entries.Length > 0)
            MacContextMenu.Show(entries);
    }

    private void OnDropped(string id, string pathsText, int modifiers)
    {
        if (_explorer?.SelectedTab is null)
            return;
        var paths = pathsText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (paths.Length == 0)
            return;
        var item = Item(id);
        var mods = MacKeyCode.ToModifiers(modifiers);
        if (FavoriteDrop(item, paths, out var dest))
        {
            if (dest)
                MacFinder.AddFavorites(paths);
            return;
        }

        var path = DropPath(item);
        if (path is null)
            return;
        var effect = FileDrag.Effect(paths, path, DragDropEffects.Copy | DragDropEffects.Move, mods);
        if (effect != DragDropEffects.None)
            _ = _explorer.SelectedTab.DropFilesAsync(paths, path, effect == DragDropEffects.Move);
    }

    private void OnHovered(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            _hoverOpen.Cancel();
            return;
        }

        var path = DropPath(Item(id));
        _hoverOpen.Update(path, _explorer?.SelectedTab?.CurrentPath, target => _ = _explorer?.OpenPathAsync(target));
    }

    private async void OnRenamed(string id, string text)
    {
        var item = Item(id);
        if (item is null)
            return;
        item.IsRenaming = false;
        if (string.IsNullOrWhiteSpace(text) || text == item.Title || _explorer is null)
            return;
        await _explorer.RenameTagAsync(item.Title, text.Trim());
    }

    private void OnSettings() => _ = _explorer?.OpenSettingsAsync();

    private SidebarItem? Item(string id)
    {
        if (_explorer is null || string.IsNullOrEmpty(id))
            return null;
        foreach (var item in _explorer.Sidebar.Items)
            if (item.Id == id)
                return item;
        return null;
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
                new(Lang.Text("Common.Action.Delete"), () => _ = _explorer!.DeleteTagAsync(item.Title), Symbol: MacMenuSymbol.Trash),
            ];
        }

        return PlaceMenu.For(
            path,
            unfavorite: item.Kind == SidebarKind.Favorite,
            eject: item.Kind == SidebarKind.Location ? _explorer!.EjectVolumeAsync : null);
    }

    private void BeginTagRename(SidebarItem item)
    {
        if (_explorer is null)
            return;
        foreach (var other in _explorer.Sidebar.Items)
            other.IsRenaming = false;
        item.RenameText = item.Title;
        item.IsRenaming = true;
        Push();
    }

    private bool FavoriteDrop(SidebarItem? item, IReadOnlyList<string> paths, out bool add)
    {
        add = false;
        if (item is not { Id: "favorites" } && item is not { Kind: SidebarKind.Favorite })
            return false;
        if (paths.Any(static p => !Directory.Exists(p) || PathUtil.IsBundle(p)))
            return true;
        if (paths.Any(MacFinder.IsFavorite))
            return true;
        add = true;
        return true;
    }

    private static string? DropPath(SidebarItem? item) =>
        item is { Path.Length: > 0 } && Directory.Exists(item.Path) ? item.Path : null;

    private MacSidebarSnapshot CaptureExplorer()
    {
        var rows = new StringBuilder();
        var renamingId = "";
        var renameText = "";
        if (_explorer is not null)
        {
            foreach (var item in _explorer.Sidebar.Items)
            {
                if (item.IsRenaming)
                {
                    renamingId = item.Id;
                    renameText = item.RenameText;
                }

                AppendRow(rows, item.Id, item.Title, Symbol(item), Flags(item), Marker(item));
            }

            AppendRow(rows, "settings-footer", Lang.Text("Settings.Title"), "gearshape",
                _explorer.ShowSettings ? RowFlag.Selected : 0, 0);
        }

        return Snapshot(rows.ToString(), Lang.Text("Settings.Title"), renamingId, renameText);
    }

    private MacSidebarSnapshot CaptureSettings()
    {
        var rows = new StringBuilder();
        var selected = _settings?.SelectedPage ?? "Appearance";
        foreach (var page in Pages)
        {
            var flags = string.Equals(page.Id, selected, StringComparison.Ordinal) ? RowFlag.Selected : 0;
            AppendRow(rows, page.Id, Lang.Text(page.Title), page.Symbol, flags, 0);
        }

        return Snapshot(rows.ToString(), "", "", "");
    }

    private static MacSidebarSnapshot Snapshot(string rows, string footer, string renamingId, string renameText) =>
        new(
            Rows: rows,
            FooterTitle: footer,
            RenamingId: renamingId,
            RenameText: renameText,
            SelectArgb: Palette.Select,
            HoverArgb: Palette.Hover,
            AccentArgb: Palette.Accent,
            SecondaryArgb: Palette.Secondary);

    private static void AppendRow(StringBuilder rows, string id, string title, string symbol, int flags, uint argb)
    {
        if (rows.Length > 0)
            rows.Append('\n');
        rows.Append(Sanitize(id)).Append('\t')
            .Append(Sanitize(title)).Append('\t')
            .Append(Sanitize(symbol)).Append('\t')
            .Append(flags).Append('\t')
            .Append(argb.ToString("X8"));
    }

    private static int Flags(SidebarItem item)
    {
        var flags = 0;
        if (item.IsSelected) flags |= RowFlag.Selected;
        if (item.IsSection) flags |= RowFlag.Section;
        if (item.IsExpanded) flags |= RowFlag.Expanded;
        if (!item.IsVisible) flags |= RowFlag.Collapsed;
        if (item.Kind == SidebarKind.Tag) flags |= RowFlag.Marker;
        if (item.CanReorder || item.IsSection || item.Kind == SidebarKind.Home) flags |= RowFlag.Reorder;
        return flags;
    }

    private static uint Marker(SidebarItem item) =>
        item.Kind == SidebarKind.Tag ? MacTags.Resolve(item.Title).Argb : 0;

    private static string Symbol(SidebarItem item)
    {
        if (item.IsSection)
            return "";
        return item.Kind switch
        {
            SidebarKind.Home => "house",
            SidebarKind.Favorite => "folder",
            SidebarKind.Cloud => "icloud",
            SidebarKind.Tag => "",
            SidebarKind.Settings => "gearshape",
            SidebarKind.Location when item.Path is not null && SpecialFolders.IsTrash(item.Path) => "trash",
            SidebarKind.Location => "externaldrive",
            _ => "folder"
        };
    }

    private static string Sanitize(string value) =>
        value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    private static readonly (string Id, string Title, string Symbol)[] Pages =
    [
        ("Appearance", "Settings.Nav.Appearance", "paintpalette"),
        ("Language", "Settings.Nav.Language", "globe"),
        ("Folders", "Settings.Nav.Folders", "folder"),
        ("NewFiles", "Settings.Nav.NewFiles", "doc.badge.plus"),
        ("Shortcuts", "Settings.Nav.Shortcuts", "keyboard"),
        ("About", "Settings.Nav.About", "info.circle")
    ];

    private static class RowFlag
    {
        public const int Selected = 1;
        public const int Section = 2;
        public const int Expanded = 4;
        public const int Marker = 8;
        public const int Reorder = 16;
        public const int Collapsed = 32;
    }

    private static class Palette
    {
        public static uint Select => Resolve("SidebarSelectionFillBrush", 0xFFE8E8ED);
        public static uint Hover => Resolve("SidebarHoverFillBrush", 0xFFEEEFF3);
        public static uint Accent => Resolve("AccentFillColorDefaultBrush", 0xFF0078D4);
        public static uint Secondary => Resolve("TextFillColorSecondaryBrush", 0x99000000);

        private static uint Resolve(string key, uint fallback)
        {
            var app = Application.Current;
            if (app is null || !app.TryGetResource(key, app.ActualThemeVariant, out var value))
                return fallback;
            return value switch
            {
                ISolidColorBrush brush => Pack(brush.Color, brush.Opacity),
                Color color => Pack(color, 1),
                _ => fallback
            };
        }

        private static uint Pack(Color color, double opacity)
        {
            var alpha = (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255);
            return (uint)(alpha << 24 | color.R << 16 | color.G << 8 | color.B);
        }
    }
}
