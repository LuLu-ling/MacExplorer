using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using MacExplorer.Localization;
using MacExplorer.Logging;
using MacExplorer.Models;
using MacExplorer.Services;
using MacExplorer.ViewModels;
using MacExplorer.Input;
using MacExplorer.Views;

namespace MacExplorer.Native;

internal sealed class MacApplicationMenu
{
    // Avalonia owns the native menu objects and callback bridges. This application-lifetime
    // owner roots their managed menus; closed windows are removed from the attachment set.
    private static MacApplicationMenu? _instance;
    private readonly WindowService _windows;
    private readonly HashSet<Window> _attached = [];
    private readonly IDisposable _windowOpened;
    private readonly NativeMenu _dock = new();
    private readonly NativeMenuItem _dockNewWindow;
    private readonly List<(WeakReference<NativeMenuItem> Item, Func<string> Title, ShortcutId? Shortcut)> _titles = [];

    private MacApplicationMenu(WindowService windows)
    {
        _windows = windows;
        _windowOpened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            Guard(() => AttachCore(window)));
        _dockNewWindow = Add(_dock, "Menu.App.NewWindow", () => windows.OpenWindow());
        _dock.NeedsUpdate += (_, _) => Guard(RefreshDock);
        Shortcuts.Changed += () => Guard(RefreshGestures);
    }

    public static void Install(WindowService windows)
    {
        if (!OperatingSystem.IsMacOS() || _instance is not null)
            return;
        Dispatcher.UIThread.VerifyAccess();
        var application = Application.Current ?? throw new InvalidOperationException("Application is not initialized.");
        var menus = _instance = new MacApplicationMenu(windows);
        menus.InstallApplicationMenu(application);
        menus.RefreshDock();
        NativeDock.SetMenu(application, menus._dock);
        WeakLanguageChanged.Add(menus, static m => m.Relocalize());
        if (application.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
                menus.AttachCore(window);
            desktop.Exit += (_, _) => menus._windowOpened.Dispose();
        }
    }

    public static void Attach(MainWindow window) => _instance?.AttachCore(window);

    private void InstallApplicationMenu(Application application)
    {
        // Keep Avalonia's Services, Hide, Hide Others and Show All entries. Replacing
        // the menu would discard them: the exporter only inserts defaults once.
        var menu = NativeMenu.GetMenu(application) ?? new NativeMenu();
        for (var i = menu.Items.Count - 1; i >= 0; i--)
        {
            if (menu.Items[i] is not NativeMenuItem item)
                continue;
            if (item.Header == "About Avalonia")
                menu.Items.RemoveAt(i);
            else if (item.Gesture is { Key: Key.Q, KeyModifiers: KeyModifiers.Meta })
                menu.Items.RemoveAt(i);
        }

        var additions = new NativeMenu();
        Add(additions, "Menu.App.NewWindow", () => _windows.OpenWindow(), ShortcutId.NewWindow);
        Add(additions, "Menu.App.Settings", OpenSettings, ShortcutId.Settings);
        for (var i = additions.Items.Count - 1; i >= 0; i--)
        {
            var item = additions.Items[i];
            additions.Items.RemoveAt(i);
            menu.Items.Insert(0, item);
        }
        // The two application actions have no window-dependent enabled state.
        while (menu.Items.LastOrDefault() is NativeMenuItemSeparator)
            menu.Items.RemoveAt(menu.Items.Count - 1);
        menu.Add(new NativeMenuItemSeparator());
        Add(menu, "Menu.App.Quit", _windows.Quit, ShortcutId.Quit);
        NativeMenu.SetMenu(application, menu);
    }

    private void AttachCore(Window window)
    {
        if (!_attached.Add(window))
            return;
        var root = new NativeMenu();
        BuildFile(Submenu(root, "Menu.File"));
        BuildEdit(Submenu(root, "Menu.Edit"));
        BuildView(Submenu(root, "Menu.View"));
        BuildGo(Submenu(root, "Menu.Go"));
        BuildWindow(Submenu(root, "Menu.Window"));
        NativeMenu.SetMenu(window, root);
        window.Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;
        window.Closed -= OnWindowClosed;
        _attached.Remove(window);
    }

    private void BuildFile(NativeMenu menu)
    {
        Add(menu, "Menu.File.NewWindow", () => _windows.OpenWindow(), ShortcutId.NewWindow);
        Command(menu, "Menu.File.NewTab", () => Model?.NewTabCommand, ShortcutId.NewTab);
        Command(menu, "Menu.File.NewFolder", () => Tab?.NewFolderCommand, ShortcutId.NewFolder, CanWriteFolder);
        Command(menu, "Menu.File.NewFile", () => Tab?.NewFileCommand, enabled: CanWriteFolder);
        Separator(menu);
        Command(menu, "Menu.File.Open", () => Tab?.OpenCommand, ShortcutId.Open, () => FileSelection && Tab!.HasSingleSelection);
        Command(menu, "Menu.File.Rename", () => Tab?.RenameCommand, ShortcutId.Rename, () => FileSelection && Tab!.HasSingleSelection);
        Command(menu, "Menu.File.GetInfo", () => Model?.OpenPropertiesCommand, ShortcutId.GetInfo, () => FileSelection);
        Command(menu, () => Lang.Text(Tab is { IsTrash: true } ? "Menu.File.DeletePermanently" : "Menu.File.MoveToTrash"),
            () => Execute(Tab?.DeleteCommand), ShortcutId.MoveToTrash,
            () => FileSelection);
        Command(menu, "Menu.File.EmptyTrash", () => Tab?.EmptyTrashCommand,
            enabled: () => CanUseFiles && Tab is { IsTrash: true, Items.Count: > 0 });
        Separator(menu);
        var close = Add(menu, () => Lang.Text(
                ActiveWindow is MainWindow && Model is { Tabs.Count: > 1 }
                    ? "Menu.File.CloseTab"
                    : "Menu.File.CloseWindow"),
            Close, ShortcutId.CloseTab, () => ActiveWindow is not null);
        var closeWindow = Add(menu, "Menu.File.CloseWindow", () => ActiveWindow?.Close(), ShortcutId.CloseWindow,
            () => ActiveWindow is MainWindow && Model is { Tabs.Count: > 1 });
        void RefreshCloseItems()
        {
            var multipleTabs = ActiveWindow is MainWindow && Model is { Tabs.Count: > 1 };
            close.Header = Lang.Text(multipleTabs ? "Menu.File.CloseTab" : "Menu.File.CloseWindow");
            if (multipleTabs)
            {
                if (!menu.Items.Contains(closeWindow))
                    menu.Add(closeWindow);
            }
            else
                menu.Items.Remove(closeWindow);
        }
        RefreshCloseItems();
        menu.NeedsUpdate += (_, _) => Guard(() =>
        {
            RefreshCloseItems();
            foreach (var entry in _titles)
            {
                if (entry.Shortcut != ShortcutId.MoveToTrash)
                    continue;
                if (entry.Item.TryGetTarget(out var item))
                    item.Header = entry.Title();
            }
        });
    }

    private void BuildEdit(NativeMenu menu)
    {
        TextAction(menu, "Common.Action.Undo", ShortcutId.Undo, box => box.Undo(), box => box.CanUndo);
        TextAction(menu, "Common.Action.Redo", ShortcutId.Redo, box => box.Redo(), box => box.CanRedo);
        Separator(menu);
        TextAction(menu, "Common.Action.Cut", ShortcutId.Cut, box => box.Cut(), box => box.CanCut,
            () => Tab?.CutCommand, () => FileSelection);
        TextAction(menu, "Common.Action.Copy", ShortcutId.Copy, box => box.Copy(), box => box.CanCopy,
            () => Tab?.CopyCommand, () => FileSelection,
            block => block.Copy(), block => block.CanCopy);
        TextAction(menu, "Common.Action.Paste", ShortcutId.Paste, box => box.Paste(), box => box.CanPaste,
            () => Tab?.PasteCommand, () => CanWriteFolder() && MacPasteboard.HasFiles());
        Separator(menu);
        TextAction(menu, "Common.Action.SelectAll", ShortcutId.SelectAll, box => box.SelectAll(), box => box.Text is { Length: > 0 },
            () => Tab?.SelectAllCommand, () => CanUseFiles && Tab is { Items.Count: > 0 },
            block => block.SelectAll(), block => block.Text is { Length: > 0 });
        Separator(menu);
        Command(menu, "Menu.Edit.Find", () => Model?.FocusSearchCommand, ShortcutId.Find);
        menu.NeedsUpdate += (_, _) => Guard(RemoveAutomaticEditItems);
    }

    private static void RemoveAutomaticEditItems()
    {
        using var pool = new AutoreleasePool();
        var application = ObjC.Call(ObjC.Class("NSApplication"), "sharedApplication");
        RemoveAutomaticEditItems(ObjC.Call(application, "mainMenu"), ObjC.Sel("startDictation:"),
            ObjC.NsString("_NSMenuItemAutoFillIdentifier"));
    }

    private static void RemoveAutomaticEditItems(IntPtr menu, IntPtr dictationAction, IntPtr autoFillIdentifier)
    {
        // AppKit owns these additions, not Avalonia's managed menu collection. Match
        // native identity rather than localized titles, and leave every other item alone.
        // NeedsUpdate permits structural changes; menuWillOpen explicitly does not.
        for (var i = (int)ObjC.MsgSendNuint(menu, ObjC.Sel("numberOfItems")) - 1; i >= 0; i--)
        {
            var item = ObjC.Call(menu, "itemAtIndex:", (IntPtr)i);
            if (ObjC.Call(item, "action") == dictationAction ||
                ObjC.MsgSendBool(ObjC.Call(item, "identifier"), ObjC.Sel("isEqualToString:"), autoFillIdentifier))
                ObjC.Call(menu, "removeItemAtIndex:", (IntPtr)i);
            else if (ObjC.Call(item, "submenu") is var submenu && submenu != IntPtr.Zero)
                RemoveAutomaticEditItems(submenu, dictationAction, autoFillIdentifier);
        }
    }

    private void BuildView(NativeMenu menu)
    {
        Layout(menu, "Menu.View.AsDetails", "Details", ShortcutId.AsDetails, LayoutKind.Details);
        Layout(menu, "Menu.View.AsList", "List", ShortcutId.AsList, LayoutKind.List);
        Layout(menu, "Menu.View.AsCards", "Cards", ShortcutId.AsCards, LayoutKind.Cards);
        Layout(menu, "Menu.View.AsGrid", "Grid", ShortcutId.AsGrid, LayoutKind.Grid);
        Separator(menu);
        Command(menu, "Menu.View.ShowInfoPane", () => Model?.ToggleInfoPaneCommand, ShortcutId.ShowInfoPane,
            check: () => Model?.ShowInfoPane == true);
        Command(menu, "Menu.View.ShowHidden", () => Model?.ToggleHiddenCommand, ShortcutId.ShowHidden,
            check: () => Model?.ShowHidden == true);
        Command(menu, "Menu.View.ShowExtensions", () => Model?.ToggleExtensionsCommand,
            check: () => Model?.ShowExtensions == true);
        Separator(menu);
        Command(menu, "Common.Action.Refresh", () => Tab?.RefreshCommand, ShortcutId.Refresh, () => Tab is { IsBusy: false });
    }

    private void BuildGo(NativeMenu menu)
    {
        Command(menu, "Menu.Go.Back", () => Tab?.BackCommand, ShortcutId.GoBack, () => !HasTextFocus && Tab is { CanGoBack: true });
        Command(menu, "Menu.Go.Forward", () => Tab?.ForwardCommand, ShortcutId.GoForward, () => !HasTextFocus && Tab is { CanGoForward: true });
        Command(menu, "Menu.Go.EnclosingFolder", () => Tab?.UpCommand, ShortcutId.GoEnclosingFolder, () => !HasTextFocus && Tab is { CanGoUp: true });
        Command(menu, "Menu.Go.GoToFolder", () => Model?.FocusPathCommand, ShortcutId.FocusPath);
        Separator(menu);
        Location(menu, "Places.Home", SpecialFolders.UserHome, ShortcutId.GoHome);
        Location(menu, "Places.Desktop", SpecialFolders.Desktop, ShortcutId.GoDesktop);
        Location(menu, "Places.Documents", SpecialFolders.Documents, ShortcutId.GoDocuments);
        Location(menu, "Places.Downloads", SpecialFolders.Downloads, ShortcutId.GoDownloads);
        Location(menu, "Places.Applications", SpecialFolders.Applications, ShortcutId.GoApplications);
        Location(menu, "Places.Computer", SpecialFolders.Computer, ShortcutId.GoComputer);
        Location(menu, "Places.Trash", SpecialFolders.Trash);
    }

    private void BuildWindow(NativeMenu menu)
    {
        Add(menu, "Menu.Window.Minimize", () => { if (ActiveWindow is { } window) window.WindowState = WindowState.Minimized; },
            ShortcutId.Minimize, () => ActiveWindow is { CanMinimize: true });
        Add(menu, "Menu.Window.Zoom", () =>
        {
            if (ActiveWindow is { } window)
                window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }, enabled: () => ActiveWindow is { CanMaximize: true });
        Separator(menu);
        Add(menu, "Menu.Window.ShowPreviousTab", () => SelectTab(-1), ShortcutId.PreviousTab, () => Model is { Tabs.Count: > 1 });
        Add(menu, "Menu.Window.ShowNextTab", () => SelectTab(1), ShortcutId.NextTab, () => Model is { Tabs.Count: > 1 });
        Separator(menu);
        Add(menu, "Menu.Window.BringAllToFront", () =>
        {
            var active = _windows.ActiveWindow;
            foreach (var window in _windows.Windows)
                Focus(window);
            if (active is not null)
                Focus(active);
        }, enabled: () => _windows.Windows.Count > 0);
        Separator(menu);
        var fixedCount = menu.Items.Count;
        menu.NeedsUpdate += (_, _) => Guard(() =>
        {
            while (menu.Items.Count > fixedCount)
                menu.Items.RemoveAt(menu.Items.Count - 1);
            foreach (var window in _windows.Windows)
                menu.Add(WindowItem(window));
        });
    }

    private void RefreshDock()
    {
        for (var i = _dock.Items.Count - 1; i >= 0; i--)
            if (!ReferenceEquals(_dock.Items[i], _dockNewWindow))
                _dock.Items.RemoveAt(i);

        var insert = 0;
        foreach (var window in _windows.Windows)
            _dock.Items.Insert(insert++, WindowItem(window));
        if (_windows.Windows.Count > 0)
            _dock.Items.Insert(insert, new NativeMenuItemSeparator());

        var favorites = MacFinder.FavoriteFolders();
        if (favorites.Count > 0)
            Separator(_dock);
        foreach (var path in favorites)
        {
            var name = Path.GetFileName(path.TrimEnd('/'));
            var item = new NativeMenuItem(string.IsNullOrEmpty(name) ? path : name) { ToolTip = path };
            item.Click += (_, _) => Guard(() => _windows.OpenWindow(path));
            _dock.Add(item);
        }
    }

    private NativeMenuItem WindowItem(MainWindow window)
    {
        var target = new WeakReference<MainWindow>(window);
        var header = window.DataContext is MainViewModel { SelectedTab.Title: { Length: > 0 } tab }
            ? tab
            : window.Title is { Length: > 0 } title ? title : "MacExplorer";
        var item = new NativeMenuItem(header)
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = ReferenceEquals(window, _windows.ActiveWindow)
        };
        item.Click += (_, _) => Guard(() =>
        {
            if (target.TryGetTarget(out var live) && _windows.Windows.Contains(live))
                Focus(live);
        });
        return item;
    }

    private Window? ActiveWindow
    {
        get
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return null;
            foreach (var window in desktop.Windows)
                if (window.IsActive && window.IsEnabled)
                    return window;
            return null;
        }
    }

    private MainViewModel? Model => (ActiveWindow as MainWindow)?.DataContext as MainViewModel;
    private ExplorerTabViewModel? Tab => Model?.SelectedTab;
    private bool HasTextFocus => Focused<TextBox>() is not null || Focused<SelectableTextBlock>() is not null;
    private bool CanUseFiles => !HasTextFocus && Tab is { ShowFolder: true, IsBusy: false };
    private bool FileSelection => CanUseFiles && Tab is { HasSelection: true };
    private bool CanWriteFolder() => CanUseFiles && Tab is { IsTag: false, IsTrash: false };

    private T? Focused<T>() where T : Control
    {
        for (var visual = ActiveWindow?.FocusManager?.GetFocusedElement() as Visual; visual is not null; visual = visual.GetVisualParent())
            if (visual is T control)
                return control;
        return null;
    }

    private void Close()
    {
        if (ActiveWindow is MainWindow)
            _windows.CloseActiveTabOrWindow();
        else
            ActiveWindow?.Close();
    }

    private void OpenSettings()
    {
        var window = _windows.ActiveWindow ?? _windows.OpenWindow();
        Focus(window);
        if (window.DataContext is MainViewModel model)
            Execute(model.OpenSettingsCommand);
    }

    private void SelectTab(int offset)
    {
        if (Model is { Tabs.Count: > 1 } model)
            model.SelectedTabIndex = (model.SelectedTabIndex + offset + model.Tabs.Count) % model.Tabs.Count;
    }

    private static void Focus(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
    }

    private void Location(NativeMenu menu, string key, string path, ShortcutId? shortcut = null) =>
        Command(menu, key, () => Model?.OpenPathCommand, shortcut, parameter: path);

    private void Layout(NativeMenu menu, string key, string parameter, ShortcutId shortcut, LayoutKind layout) =>
        Command(menu, key, () => Model?.SetLayoutCommand, shortcut, () => Tab is { ShowFolder: true },
            parameter, () => Tab?.Layout == layout);

    private NativeMenu Submenu(NativeMenu root, string key)
    {
        var menu = new NativeMenu();
        var item = new NativeMenuItem(Lang.Text(key)) { Menu = menu };
        Remember(item, () => Lang.Text(key));
        root.Add(item);
        return menu;
    }

    private static void Separator(NativeMenu menu) => menu.Add(new NativeMenuItemSeparator());

    private void Command(NativeMenu menu, string key, Func<ICommand?> command, ShortcutId? shortcut = null,
        Func<bool>? enabled = null, object? parameter = null, Func<bool>? check = null) =>
        Add(menu, key, () => Execute(command(), parameter), shortcut,
            () => (enabled?.Invoke() ?? true) && command()?.CanExecute(parameter) == true, check);

    private void Command(NativeMenu menu, Func<string> title, Action action, ShortcutId? shortcut = null,
        Func<bool>? enabled = null) =>
        Add(menu, title, action, shortcut, enabled);

    private void TextAction(NativeMenu menu, string key, ShortcutId shortcut, Action<TextBox> action,
        Func<TextBox, bool> enabled, Func<ICommand?>? fileCommand = null, Func<bool>? fileEnabled = null,
        Action<SelectableTextBlock>? selectableAction = null, Func<SelectableTextBlock, bool>? selectableEnabled = null)
    {
        Add(menu, key, () =>
        {
            if (Focused<TextBox>() is { } box)
                action(box);
            else if (Focused<SelectableTextBlock>() is { } block)
                selectableAction?.Invoke(block);
            else
                Execute(fileCommand?.Invoke());
        }, shortcut, () =>
        {
            if (Focused<TextBox>() is { } box)
                return enabled(box);
            if (Focused<SelectableTextBlock>() is { } block)
                return selectableEnabled?.Invoke(block) == true;
            return fileEnabled?.Invoke() == true && fileCommand?.Invoke()?.CanExecute(null) == true;
        });
    }

    private NativeMenuItem Add(NativeMenu menu, string key, Action action, ShortcutId? shortcut = null,
        Func<bool>? enabled = null, Func<bool>? check = null) =>
        Add(menu, () => Lang.Text(key), action, shortcut, enabled, check);

    private NativeMenuItem Add(NativeMenu menu, Func<string> title, Action action, ShortcutId? shortcut = null,
        Func<bool>? enabled = null, Func<bool>? check = null)
    {
        var item = new NativeMenuItem(title())
        {
            Gesture = shortcut is { } id ? Shortcuts.Gesture(id) : null,
            ToggleType = check is null ? MenuItemToggleType.None : MenuItemToggleType.CheckBox
        };
        Remember(item, title, shortcut);
        void Refresh()
        {
            item.IsEnabled = enabled?.Invoke() ?? true;
            item.IsChecked = check?.Invoke() ?? false;
        }
        Refresh();
        menu.NeedsUpdate += (_, _) => Guard(Refresh);
        item.Click += (_, _) => Guard(() =>
        {
            if (enabled?.Invoke() ?? true)
                action();
        });
        menu.Add(item);
        return item;
    }

    private void Remember(NativeMenuItem item, Func<string> title, ShortcutId? shortcut = null) =>
        _titles.Add((new WeakReference<NativeMenuItem>(item), title, shortcut));

    private void RefreshGestures()
    {
        for (var i = _titles.Count - 1; i >= 0; i--)
        {
            var entry = _titles[i];
            if (!entry.Item.TryGetTarget(out var item))
            {
                _titles.RemoveAt(i);
                continue;
            }

            if (entry.Shortcut is { } id)
                item.Gesture = Shortcuts.Gesture(id);
        }
    }

    private void Relocalize()
    {
        for (var i = _titles.Count - 1; i >= 0; i--)
        {
            if (!_titles[i].Item.TryGetTarget(out var item))
            {
                _titles.RemoveAt(i);
                continue;
            }

            item.Header = _titles[i].Title();
        }
    }

    private static async void Execute(ICommand? command, object? parameter = null)
    {
        try
        {
            if (command?.CanExecute(parameter) != true)
                return;
            if (command is IAsyncRelayCommand asyncCommand)
                await asyncCommand.ExecuteAsync(parameter);
            else
                command.Execute(parameter);
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    private static void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { Report(ex); }
    }

    private static void Report(Exception ex)
    {
        // A logger subscriber must not allow an exception through a native callback either.
        try { LogWrapper.Error(ex, "NativeMenu", "Native menu action failed"); }
        catch { }
    }
}
