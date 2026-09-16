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
    private readonly List<(WeakReference<NativeMenuItem> Item, Func<string> Title)> _titles = [];
    private IReadOnlyList<string> _favorites = [];

    private MacApplicationMenu(WindowService windows)
    {
        _windows = windows;
        _windowOpened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            Guard(() => AttachCore(window)));
    }

    public static void Install(WindowService windows)
    {
        if (!OperatingSystem.IsMacOS() || _instance is not null)
            return;
        Dispatcher.UIThread.VerifyAccess();
        var application = Application.Current ?? throw new InvalidOperationException("Application is not initialized.");
        var menus = _instance = new MacApplicationMenu(windows);
        menus.InstallApplicationMenu(application);
        menus.Add(menus._dock, "Menu.App.NewWindow", () => windows.OpenWindow());
        menus._dock.NeedsUpdate += (_, _) => Guard(menus.RefreshDock);
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
        Add(additions, "Menu.App.NewWindow", () => _windows.OpenWindow(), "Meta+N");
        Add(additions, "Menu.App.Settings", OpenSettings, "Meta+OemComma");
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
        Add(menu, "Menu.App.Quit", _windows.Quit, "Meta+Q");
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
        Add(menu, "Menu.File.NewWindow", () => _windows.OpenWindow(), "Meta+N");
        Command(menu, "Menu.File.NewTab", () => Model?.NewTabCommand, "Meta+T");
        Command(menu, "Menu.File.NewFolder", () => Tab?.NewFolderCommand, "Meta+Shift+N", CanWriteFolder);
        Command(menu, "Menu.File.NewFile", () => Tab?.NewFileCommand, enabled: CanWriteFolder);
        Separator(menu);
        Command(menu, "Menu.File.Open", () => Tab?.OpenCommand, "Meta+O", () => FileSelection && Tab!.HasSingleSelection);
        Command(menu, "Menu.File.Rename", () => Tab?.RenameCommand, enabled: () => FileSelection && Tab!.HasSingleSelection);
        Command(menu, "Menu.File.GetInfo", () => Model?.OpenPropertiesCommand, "Meta+I", () => FileSelection);
        Command(menu, () => Lang.Text(Tab is { IsTrash: true } ? "Menu.File.DeletePermanently" : "Menu.File.MoveToTrash"),
            () => Execute(Tab?.DeleteCommand), "Meta+Back",
            () => FileSelection);
        Command(menu, "Menu.File.EmptyTrash", () => Tab?.EmptyTrashCommand,
            enabled: () => CanUseFiles && Tab is { IsTrash: true, Items.Count: > 0 });
        Separator(menu);
        var close = Add(menu, () => Lang.Text(
                ActiveWindow is MainWindow && Model is { Tabs.Count: > 1 }
                    ? "Menu.File.CloseTab"
                    : "Menu.File.CloseWindow"),
            Close, "Meta+W", () => ActiveWindow is not null);
        var closeWindow = Add(menu, "Menu.File.CloseWindow", () => ActiveWindow?.Close(), "Meta+Shift+W",
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
            foreach (var item in menu.Items.OfType<NativeMenuItem>())
                if (item.Gesture is { Key: Key.Back, KeyModifiers: KeyModifiers.Meta })
                    item.Header = Lang.Text(Tab is { IsTrash: true } ? "Menu.File.DeletePermanently" : "Menu.File.MoveToTrash");
        });
    }

    private void BuildEdit(NativeMenu menu)
    {
        TextAction(menu, "Common.Action.Undo", "Meta+Z", box => box.Undo(), box => box.CanUndo);
        TextAction(menu, "Common.Action.Redo", "Meta+Shift+Z", box => box.Redo(), box => box.CanRedo);
        Separator(menu);
        TextAction(menu, "Common.Action.Cut", "Meta+X", box => box.Cut(), box => box.CanCut,
            () => Tab?.CutCommand, () => FileSelection);
        TextAction(menu, "Common.Action.Copy", "Meta+C", box => box.Copy(), box => box.CanCopy,
            () => Tab?.CopyCommand, () => FileSelection,
            block => block.Copy(), block => block.CanCopy);
        TextAction(menu, "Common.Action.Paste", "Meta+V", box => box.Paste(), box => box.CanPaste,
            () => Tab?.PasteCommand, () => CanWriteFolder() && MacPasteboard.HasFiles());
        Separator(menu);
        TextAction(menu, "Common.Action.SelectAll", "Meta+A", box => box.SelectAll(), box => box.Text is { Length: > 0 },
            () => Tab?.SelectAllCommand, () => CanUseFiles && Tab is { Items.Count: > 0 },
            block => block.SelectAll(), block => block.Text is { Length: > 0 });
        Separator(menu);
        Command(menu, "Menu.Edit.Find", () => Model?.FocusSearchCommand, "Meta+F");
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
        Layout(menu, "Menu.View.AsDetails", "Details", "Meta+D1", LayoutKind.Details);
        Layout(menu, "Menu.View.AsList", "List", "Meta+D2", LayoutKind.List);
        Layout(menu, "Menu.View.AsCards", "Cards", "Meta+D3", LayoutKind.Cards);
        Layout(menu, "Menu.View.AsGrid", "Grid", "Meta+D4", LayoutKind.Grid);
        Separator(menu);
        Command(menu, "Menu.View.ShowInfoPane", () => Model?.ToggleInfoPaneCommand, "Meta+P",
            check: () => Model?.ShowInfoPane == true);
        Command(menu, "Menu.View.ShowHidden", () => Model?.ToggleHiddenCommand, "Meta+Shift+OemPeriod",
            check: () => Model?.ShowHidden == true);
        Command(menu, "Menu.View.ShowExtensions", () => Model?.ToggleExtensionsCommand,
            check: () => Model?.ShowExtensions == true);
        Separator(menu);
        Command(menu, "Common.Action.Refresh", () => Tab?.RefreshCommand, "Meta+R", () => Tab is { IsBusy: false });
    }

    private void BuildGo(NativeMenu menu)
    {
        Command(menu, "Menu.Go.Back", () => Tab?.BackCommand, "Meta+Left", () => !HasTextFocus && Tab is { CanGoBack: true });
        Command(menu, "Menu.Go.Forward", () => Tab?.ForwardCommand, "Meta+Right", () => !HasTextFocus && Tab is { CanGoForward: true });
        Command(menu, "Menu.Go.EnclosingFolder", () => Tab?.UpCommand, "Meta+Up", () => !HasTextFocus && Tab is { CanGoUp: true });
        Separator(menu);
        Location(menu, "Places.Home", SpecialFolders.UserHome, "Meta+Shift+H");
        Location(menu, "Places.Desktop", SpecialFolders.Desktop, "Meta+Shift+D");
        Location(menu, "Places.Documents", SpecialFolders.Documents, "Meta+Shift+O");
        Location(menu, "Places.Downloads", SpecialFolders.Downloads, "Meta+Alt+L");
        Location(menu, "Places.Applications", SpecialFolders.Applications, "Meta+Shift+A");
        Location(menu, "Places.Computer", SpecialFolders.Computer, "Meta+Shift+C");
        Location(menu, "Places.Trash", SpecialFolders.Trash);
    }

    private void BuildWindow(NativeMenu menu)
    {
        Add(menu, "Menu.Window.Minimize", () => { if (ActiveWindow is { } window) window.WindowState = WindowState.Minimized; },
            "Meta+M", () => ActiveWindow is { CanMinimize: true });
        Add(menu, "Menu.Window.Zoom", () =>
        {
            if (ActiveWindow is { } window)
                window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }, enabled: () => ActiveWindow is { CanMaximize: true });
        Separator(menu);
        Add(menu, "Menu.Window.ShowPreviousTab", () => SelectTab(-1), "Meta+Shift+OemOpenBrackets", () => Model is { Tabs.Count: > 1 });
        Add(menu, "Menu.Window.ShowNextTab", () => SelectTab(1), "Meta+Shift+OemCloseBrackets", () => Model is { Tabs.Count: > 1 });
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
            {
                var target = new WeakReference<MainWindow>(window);
                var item = new NativeMenuItem(window.Title ?? "MacExplorer")
                {
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = ReferenceEquals(window, _windows.ActiveWindow)
                };
                item.Click += (_, _) => Guard(() =>
                {
                    if (target.TryGetTarget(out var live) && _windows.Windows.Contains(live))
                        Focus(live);
                });
                menu.Add(item);
            }
        });
    }

    private void RefreshDock()
    {
        var favorites = MacFinder.FavoriteFolders();
        if (_favorites.SequenceEqual(favorites, StringComparer.Ordinal))
            return;
        _favorites = favorites;
        while (_dock.Items.Count > 1)
            _dock.Items.RemoveAt(_dock.Items.Count - 1);
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

    private void Location(NativeMenu menu, string key, string path, string? gesture = null) =>
        Command(menu, key, () => Model?.OpenPathCommand, gesture, parameter: path);

    private void Layout(NativeMenu menu, string key, string parameter, string gesture, LayoutKind layout) =>
        Command(menu, key, () => Model?.SetLayoutCommand, gesture, () => Tab is { ShowFolder: true },
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

    private void Command(NativeMenu menu, string key, Func<ICommand?> command, string? gesture = null,
        Func<bool>? enabled = null, object? parameter = null, Func<bool>? check = null) =>
        Add(menu, key, () => Execute(command(), parameter), gesture,
            () => (enabled?.Invoke() ?? true) && command()?.CanExecute(parameter) == true, check);

    private void Command(NativeMenu menu, Func<string> title, Action action, string? gesture = null,
        Func<bool>? enabled = null) =>
        Add(menu, title, action, gesture, enabled);

    private void TextAction(NativeMenu menu, string key, string gesture, Action<TextBox> action,
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
        }, gesture, () =>
        {
            if (Focused<TextBox>() is { } box)
                return enabled(box);
            if (Focused<SelectableTextBlock>() is { } block)
                return selectableEnabled?.Invoke(block) == true;
            return fileEnabled?.Invoke() == true && fileCommand?.Invoke()?.CanExecute(null) == true;
        });
    }

    private NativeMenuItem Add(NativeMenu menu, string key, Action action, string? gesture = null,
        Func<bool>? enabled = null, Func<bool>? check = null) =>
        Add(menu, () => Lang.Text(key), action, gesture, enabled, check);

    private NativeMenuItem Add(NativeMenu menu, Func<string> title, Action action, string? gesture = null,
        Func<bool>? enabled = null, Func<bool>? check = null)
    {
        var item = new NativeMenuItem(title())
        {
            Gesture = gesture is null ? null : KeyGesture.Parse(gesture),
            ToggleType = check is null ? MenuItemToggleType.None : MenuItemToggleType.CheckBox
        };
        Remember(item, title);
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

    private void Remember(NativeMenuItem item, Func<string> title) =>
        _titles.Add((new WeakReference<NativeMenuItem>(item), title));

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
