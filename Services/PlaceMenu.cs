using MacExplorer.Lifecycle;
using MacExplorer.Localization;
using MacExplorer.Models;
using MacExplorer.Native;

namespace MacExplorer.Services;

internal static class PlaceMenu
{
    public static MacMenuEntry OpenWindow(string path) =>
        new(Lang.Text("Tab.OpenInNewWindow"),
            () => AppServices.Get<WindowService>().OpenWindow(path),
            Symbol: MacMenuSymbol.NewWindow);

    public static MacMenuEntry[] For(string path, bool unfavorite = false, Func<string, Task>? eject = null)
    {
        List<MacMenuEntry> entries = [OpenWindow(path)];
        if (unfavorite)
            entries.Add(new(Lang.Text("Context.Unfavorite"),
                () => MacFinder.RemoveFavorite(path),
                Symbol: MacMenuSymbol.Unfavorite));
        if (eject is not null && MacWorkspace.IsEjectable(path))
            entries.Add(new(Lang.Text("Context.Eject"),
                () => _ = eject(path),
                Symbol: MacMenuSymbol.Eject));
        if (SpecialFolders.IsVirtual(path) || !PathUtil.Exists(path))
            return [..entries];

        entries.Add(new("", Separator: true));
        entries.Add(new(Lang.Text("Menu.File.GetInfo"),
            () => MacFinder.ShowInfo([path]),
            Symbol: MacMenuSymbol.Info));
        if (!SpecialFolders.IsTrash(path))
            entries.Add(Dock(path));
        return [..entries];
    }

    private static MacMenuEntry Dock(string path) =>
        MacDock.Contains(path)
            ? new(Lang.Text("Context.RemoveFromDock"), () => MacDock.Remove(path), Symbol: MacMenuSymbol.Dock)
            : new(Lang.Text("Context.AddToDock"), () => MacDock.Add(path), Symbol: MacMenuSymbol.Dock);
}
