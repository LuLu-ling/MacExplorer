namespace MacExplorer.Models;

internal static class Glyphs
{
    public const string GlobalNav = "\uE700";
    public const string Back = "\uE76B";
    public const string Forward = "\uE76C";
    public const string Up = "\uE74A";
    public const string Refresh = "\uE72C";
    public const string Add = "\uE710";
    public const string More = "\uE712";
    public const string Settings = "\uE713";
    public const string Search = "\uE721";
    public const string Share = "\uE72D";
    public const string Cut = "\uE8C6";
    public const string Copy = "\uE8C8";
    public const string Paste = "\uE77F";
    public const string Rename = "\uE8AC";
    public const string Delete = "\uE74D";
    public const string Properties = "\uE946";
    public const string Filter = "\uE71C";
    public const string Select = "\uE8B3";
    public const string Sort = "\uE8CB";
    public const string Group = "\uF168";
    public const string Layout = "\uE8A9";
    public const string Details = "\uE8A9";
    public const string List = "\uE8A4";
    public const string Cards = "\uE8A1";
    public const string Grid = "\uE80A";
    public const string Columns = "\uE8A0";
    public const string PanelRight = "\uEA49";
    public const string Home = "\uE80F";
    public const string Folder = "\uE8B7";
    public const string NewFolder = "\uE8F4";
    public const string NewFile = "\uE8A5";
    public const string Pin = "\uE840";
    public const string Cloud = "\uE753";
    public const string Drive = "\uEDA2";
    public const string Network = "\uE968";
    public const string Tag = "\uE8EC";
    public const string Command = "\uE756";
    public const string Path = "\uE8B5";
    public const string Info = "\uE946";
    public const string ChevronRight = "\uE76C";
    public const string ChevronDown = "\uE70D";
    public const string Open = "\uE8A7";
    public const string Panes = "\uE89F";
    public const string Status = "\uEA37";
    public const string EmptyTrash = "\uE74D";
    public const string Restore = "\uE777";
    public const string Desktop = "\uE7F4";
    public const string Download = "\uE896";
    public const string Picture = "\uEB9F";
    public const string Music = "\uE8D6";
    public const string Movie = "\uE8B2";
    public const string Applications = "\uE71D";
    public const string Computer = "\uE770";
    public const string Recycle = "\uE74D";
    public const string Document = "\uE8A5";
    public const string Calendar = "\uE787";
    public const string Size = "\uE9D2";

    public static string ForPath(string path)
    {
        if (path == SpecialFolders.HomeKey) return Home;
        if (path == SpecialFolders.SettingsKey) return Settings;
        if (SpecialFolders.IsTag(path)) return Tag;
        if (SpecialFolders.IsTrash(path)) return Recycle;
        if (Same(path, SpecialFolders.ICloud)) return Cloud;
        if (path is "/" or "") return Computer;
        if (IsVolumeRoot(path)) return Drive;
        if (Same(path, SpecialFolders.Desktop)) return Desktop;
        if (Same(path, SpecialFolders.Documents)) return Document;
        if (Same(path, SpecialFolders.Downloads)) return Download;
        if (Same(path, SpecialFolders.Pictures)) return Picture;
        if (Same(path, SpecialFolders.Music)) return Music;
        if (Same(path, SpecialFolders.Movies)) return Movie;
        if (Same(path, SpecialFolders.Applications)) return Applications;
        return Folder;
    }

    private static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static bool IsVolumeRoot(string path)
    {
        var full = path.TrimEnd('/');
        const string prefix = "/Volumes/";
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && full.IndexOf('/', prefix.Length) < 0;
    }
}
