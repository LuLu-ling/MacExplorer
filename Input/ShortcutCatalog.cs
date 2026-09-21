namespace MacExplorer.Input;

public readonly record struct ShortcutSpec(ShortcutId Id, string CategoryKey, string TitleKey, string DefaultChord);

public static class ShortcutCatalog
{
    private const string App = "Settings.Shortcuts.App";
    private const string File = "Menu.File";
    private const string Edit = "Menu.Edit";
    private const string View = "Menu.View";
    private const string Go = "Menu.Go";
    private const string Window = "Menu.Window";

    public static readonly ShortcutSpec[] All =
    [
        new(ShortcutId.NewWindow, App, "Menu.File.NewWindow", "Meta+N"),
        new(ShortcutId.Settings, App, "Menu.App.Settings", "Meta+OemComma"),
        new(ShortcutId.Quit, App, "Menu.App.Quit", "Meta+Q"),
        new(ShortcutId.NewTab, File, "Menu.File.NewTab", "Meta+T"),
        new(ShortcutId.NewFolder, File, "Menu.File.NewFolder", "Meta+Shift+N"),
        new(ShortcutId.Open, File, "Menu.File.Open", "Meta+O"),
        new(ShortcutId.Rename, File, "Menu.File.Rename", "F2"),
        new(ShortcutId.GetInfo, File, "Menu.File.GetInfo", "Meta+I"),
        new(ShortcutId.MoveToTrash, File, "Menu.File.MoveToTrash", "Meta+Back"),
        new(ShortcutId.CloseTab, File, "Menu.File.CloseTab", "Meta+W"),
        new(ShortcutId.CloseWindow, File, "Menu.File.CloseWindow", "Meta+Shift+W"),
        new(ShortcutId.Undo, Edit, "Common.Action.Undo", "Meta+Z"),
        new(ShortcutId.Redo, Edit, "Common.Action.Redo", "Meta+Shift+Z"),
        new(ShortcutId.Cut, Edit, "Common.Action.Cut", "Meta+X"),
        new(ShortcutId.Copy, Edit, "Common.Action.Copy", "Meta+C"),
        new(ShortcutId.Paste, Edit, "Common.Action.Paste", "Meta+V"),
        new(ShortcutId.SelectAll, Edit, "Common.Action.SelectAll", "Meta+A"),
        new(ShortcutId.Find, Edit, "Menu.Edit.Find", "Meta+F"),
        new(ShortcutId.AsDetails, View, "Menu.View.AsDetails", "Meta+D1"),
        new(ShortcutId.AsList, View, "Menu.View.AsList", "Meta+D2"),
        new(ShortcutId.AsCards, View, "Menu.View.AsCards", "Meta+D3"),
        new(ShortcutId.AsGrid, View, "Menu.View.AsGrid", "Meta+D4"),
        new(ShortcutId.ShowInfoPane, View, "Menu.View.ShowInfoPane", "Meta+P"),
        new(ShortcutId.ShowHidden, View, "Menu.View.ShowHidden", "Meta+Shift+OemPeriod"),
        new(ShortcutId.Refresh, View, "Common.Action.Refresh", "Meta+R"),
        new(ShortcutId.GoBack, Go, "Menu.Go.Back", "Meta+Left"),
        new(ShortcutId.GoForward, Go, "Menu.Go.Forward", "Meta+Right"),
        new(ShortcutId.GoEnclosingFolder, Go, "Menu.Go.EnclosingFolder", "Meta+Up"),
        new(ShortcutId.GoHome, Go, "Places.Home", "Meta+Shift+H"),
        new(ShortcutId.GoDesktop, Go, "Places.Desktop", "Meta+Shift+D"),
        new(ShortcutId.GoDocuments, Go, "Places.Documents", "Meta+Shift+O"),
        new(ShortcutId.GoDownloads, Go, "Places.Downloads", "Meta+Alt+L"),
        new(ShortcutId.GoApplications, Go, "Places.Applications", "Meta+Shift+A"),
        new(ShortcutId.GoComputer, Go, "Places.Computer", "Meta+Shift+C"),
        new(ShortcutId.FocusPath, Go, "Menu.Go.GoToFolder", "Meta+L"),
        new(ShortcutId.Minimize, Window, "Menu.Window.Minimize", "Meta+M"),
        new(ShortcutId.PreviousTab, Window, "Menu.Window.ShowPreviousTab", "Meta+Shift+OemOpenBrackets"),
        new(ShortcutId.NextTab, Window, "Menu.Window.ShowNextTab", "Meta+Shift+OemCloseBrackets")
    ];

    public static int Count => All.Length;

    static ShortcutCatalog()
    {
        if (All.Length != Enum.GetValues<ShortcutId>().Length)
            throw new InvalidOperationException("ShortcutCatalog.All must cover every ShortcutId.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < All.Length; i++)
        {
            if ((int)All[i].Id != i)
                throw new InvalidOperationException("ShortcutCatalog.All must be ordered by ShortcutId.");
            if (All[i].DefaultChord.Length > 0 && !seen.Add(All[i].DefaultChord))
                throw new InvalidOperationException($"Duplicate default shortcut {All[i].DefaultChord}.");
        }
    }

    public static ref readonly ShortcutSpec Spec(ShortcutId id) => ref All[(int)id];
}
