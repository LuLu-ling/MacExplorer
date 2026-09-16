using MacExplorer.Localization;
using MacExplorer.Models;
namespace MacExplorer.Services;

internal static class PathUtil
{
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public static bool IsBundle(string path)
    {
        if (!Directory.Exists(path))
            return false;
        var ext = Path.GetExtension(path);
        if (ext is ".app" or ".framework" or ".bundle" or ".plugin" or ".kext")
            return true;
        return File.Exists(Path.Combine(path, "Contents", "Info.plist"));
    }

    public static string UniquePath(string directory, string fileName)
    {
        var dest = Path.Combine(directory, fileName);
        if (!Exists(dest))
            return dest;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var n = 1; n < 10_000; n++)
        {
            var candidate = n == 1
                ? Lang.Text("File.CopyPattern", stem, ext)
                : Lang.Text("File.CopyPatternNumbered", stem, ext, n);
            dest = Path.Combine(directory, candidate);
            if (!Exists(dest))
                return dest;
        }

        return Path.Combine(directory, Lang.Text("File.CopyPatternNumbered", stem, ext, Guid.NewGuid().ToString("N")));
    }

    public static string DisplayName(string name, bool isDirectory, bool showExtensions)
    {
        if (showExtensions || isDirectory)
            return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrEmpty(stem) ? name : stem;
    }

    public static IReadOnlyList<BreadcrumbItem> Breadcrumbs(string path)
    {
        List<BreadcrumbItem> items;
        if (SpecialFolders.IsVirtual(path))
        {
            var title = path == SpecialFolders.SettingsKey
                ? Lang.Text("Places.Settings")
                : SpecialFolders.IsTag(path)
                    ? SpecialFolders.TagName(path)
                    : Lang.Text("Places.Home");
            items =
            [
                new BreadcrumbItem
                {
                    Title = title,
                    Path = path,
                    IsRoot = true,
                    ShowChevron = false
                }
            ];
        }
        else
        {
            var full = Path.GetFullPath(path);
            items =
            [
                new BreadcrumbItem { Title = Lang.Text("Places.MacintoshHD"), Path = "/", IsRoot = true }
            ];
            if (full is not ("/" or ""))
            {
                var current = "/";
                foreach (var segment in full.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    current = current == "/" ? "/" + segment : current + "/" + segment;
                    items.Add(new BreadcrumbItem { Title = segment, Path = current });
                }
            }
        }

        return items;
    }
}
