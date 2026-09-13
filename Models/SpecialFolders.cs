namespace MacExplorer.Models;

internal static class SpecialFolders
{
    public const string HomeKey = "home:";
    public const string SettingsKey = "settings:";
    public const string TagPrefix = "tag:";

    public static string UserHome =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Desktop =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string Documents =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public static string Downloads =>
        Path.Combine(UserHome, "Downloads");

    public static string Pictures =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    public static string Music =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

    public static string Movies =>
        Path.Combine(UserHome, "Movies");

    public static string Applications => "/Applications";

    public static string Computer => "/";

    public static string Trash => Path.Combine(UserHome, ".Trash");

    public static string ICloud =>
        Path.Combine(UserHome, "Library/Mobile Documents/com~apple~CloudDocs");

    public static bool IsTag(string path) =>
        path.StartsWith(TagPrefix, StringComparison.Ordinal);

    public static string TagPath(string name) => TagPrefix + name;

    public static string TagName(string path) =>
        IsTag(path) ? path[TagPrefix.Length..] : path;

    public static bool IsVirtual(string path) =>
        path is HomeKey or SettingsKey || IsTag(path);

    public static bool IsTrash(string path) =>
        !IsVirtual(path) && Path.GetFullPath(path).TrimEnd('/')
            .Equals(Path.GetFullPath(Trash).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    public static bool ICloudExists() => Directory.Exists(ICloud);
}
