using MacExplorer.Localization;

namespace MacExplorer.Native;

internal static class MacOpenWith
{
    private const nuint OptionFlag = 1 << 19;
    private const nuint AliasWithoutUi = 1 << 8;

    public static MacMenuEntry Menu(IReadOnlyList<string> paths, Action<string, bool> open)
    {
        var always = OptionDown();
        var children = new List<MacMenuEntry>();
        foreach (var app in Apps(paths))
        {
            var path = app.Path;
            children.Add(new(app.Title, () => open(path, always), Icon: path));
        }

        if (children.Count > 0)
            children.Add(new("", Separator: true));
        children.Add(new(Lang.Text("Context.OpenWith.Other"), () =>
        {
            if (Choose(paths) is { } app)
                open(app, always);
        }, Symbol: MacMenuSymbol.Other));
        children.Add(new(Lang.Text("Context.OpenWith.AppStore"), () => SearchAppStore(paths),
            Icon: MacMenuSymbol.AppStoreApp));
        return new(Lang.Text(always ? "Context.AlwaysOpenWith" : "Context.OpenWith"), Children: [.. children]);
    }

    public static bool Open(IReadOnlyList<string> paths, string application)
    {
        using var pool = new AutoreleasePool();
        if (paths.Count == 0 || AppPath(ObjC.FileUrl(application)) is null)
            return false;
        var config = ObjC.Call(ObjC.Class("NSWorkspaceOpenConfiguration"), "configuration");
        if (config == IntPtr.Zero)
            return false;
        ObjC.SetBool(config, "setAllowsRunningApplicationSubstitution:", false);
        ObjC.MsgSend(Shared(), ObjC.Sel("openURLs:withApplicationAtURL:configuration:completionHandler:"),
            FileUrls(paths), ObjC.FileUrl(application), config, IntPtr.Zero);
        return true;
    }

    public static void SetDefault(IReadOnlyList<string> paths, string application)
    {
        using var pool = new AutoreleasePool();
        if (paths.Count == 0 || string.IsNullOrEmpty(application))
            return;
        var appUrl = ObjC.FileUrl(application);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var file = ResolvedFileUrl(path);
            var error = IntPtr.Zero;
            var uti = ObjC.ToString(ObjC.MsgSend(Shared(), ObjC.Sel("typeOfFile:error:"),
                ObjC.Call(file, "path"), ref error)) ?? path;
            if (!seen.Add(uti))
                continue;
            ObjC.MsgSend(Shared(),
                ObjC.Sel("setDefaultApplicationAtURL:toOpenContentTypeOfFileAtURL:completionHandler:"),
                appUrl, file, IntPtr.Zero);
        }
    }

    private readonly record struct App(string Path, string Title);

    private static List<App> Apps(IReadOnlyList<string> paths)
    {
        using var pool = new AutoreleasePool();
        HashSet<string>? common = null;
        string? defaultPath = null;
        foreach (var path in DistinctTypes(paths))
        {
            var set = AppsFor(path);
            var def = DefaultFor(path);
            if (def is not null)
                set.Add(def);
            if (common is null)
            {
                common = set;
                defaultPath = def;
            }
            else
            {
                common.IntersectWith(set);
                if (!string.Equals(defaultPath, def, StringComparison.OrdinalIgnoreCase))
                    defaultPath = null;
            }
        }

        if (common is null or { Count: 0 })
            return [];

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var versions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var nameCount = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var path in common)
        {
            var name = DisplayName(path);
            names[path] = name;
            versions[path] = Version(path);
            nameCount[name] = nameCount.GetValueOrDefault(name) + 1;
        }

        return common
            .Select(path =>
            {
                var title = names[path];
                if (nameCount[title] > 1 && versions[path] is { Length: > 0 } version)
                    title = $"{title} ({version})";
                var isDefault = string.Equals(path, defaultPath, StringComparison.OrdinalIgnoreCase);
                if (isDefault)
                    title = Lang.Text("Context.OpenWith.Default", title);
                return (path, title, isDefault);
            })
            .OrderByDescending(app => app.isDefault)
            .ThenBy(app => app.title, StringComparer.CurrentCultureIgnoreCase)
            .Select(app => new App(app.path, app.title))
            .ToList();
    }

    private static HashSet<string> AppsFor(string path)
    {
        var listed = ObjC.Call(Shared(), "URLsForApplicationsToOpenURL:", ResolvedFileUrl(path));
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = listed == IntPtr.Zero ? 0 : ObjC.ArrayCount(listed);
        for (var i = 0; i < count; i++)
        {
            if (AppPath(ObjC.ArrayAt(listed, i)) is { } app)
                set.Add(app);
        }

        return set;
    }

    private static string? DefaultFor(string path) =>
        AppPath(ObjC.Call(Shared(), "URLForApplicationToOpenURL:", ResolvedFileUrl(path)));

    private static IEnumerable<string> DistinctTypes(IReadOnlyList<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var file = ResolvedFileUrl(path);
            var error = IntPtr.Zero;
            var uti = ObjC.ToString(ObjC.MsgSend(Shared(), ObjC.Sel("typeOfFile:error:"),
                ObjC.Call(file, "path"), ref error)) ?? "";
            if (seen.Add(uti))
                yield return path;
        }
    }

    private static string? Choose(IReadOnlyList<string> paths)
    {
        using var pool = new AutoreleasePool();
        var panel = ObjC.Call(ObjC.Class("NSOpenPanel"), "openPanel");
        ObjC.SetBool(panel, "setCanChooseFiles:", true);
        ObjC.SetBool(panel, "setCanChooseDirectories:", false);
        ObjC.SetBool(panel, "setAllowsMultipleSelection:", false);
        ObjC.SetBool(panel, "setCanCreateDirectories:", false);
        ObjC.SetBool(panel, "setTreatsFilePackagesAsDirectories:", false);
        var utType = ObjC.Call(ObjC.Class("UTType"), "typeWithIdentifier:", ObjC.NsString("com.apple.application"));
        if (utType != IntPtr.Zero)
            ObjC.Call(panel, "setAllowedContentTypes:", ObjC.Call(ObjC.Class("NSArray"), "arrayWithObject:", utType));
        else
            ObjC.Call(panel, "setAllowedFileTypes:",
                ObjC.Call(ObjC.Class("NSArray"), "arrayWithObject:", ObjC.NsString("app")));
        ObjC.Call(panel, "setDirectoryURL:", ObjC.FileUrl("/Applications"));
        ObjC.Call(panel, "setTitle:", ObjC.NsString(Lang.Text("Context.OpenWith.ChooseApp")));
        var message = paths.Count == 1
            ? Lang.Text("Context.OpenWith.ChooseAppMessage", Path.GetFileName(paths[0]))
            : Lang.Text("Context.OpenWith.ChooseAppMessageMany");
        ObjC.Call(panel, "setMessage:", ObjC.NsString(message));
        ObjC.SetBool(ObjC.Call(ObjC.Class("NSApplication"), "sharedApplication"), "activateIgnoringOtherApps:", true);
        if (ObjC.MsgSendNuint(panel, ObjC.Sel("runModal")) != 1)
            return null;
        var urls = ObjC.Call(panel, "URLs");
        return ObjC.ArrayCount(urls) == 0 ? null : AppPath(ObjC.ArrayAt(urls, 0));
    }

    private static void SearchAppStore(IReadOnlyList<string> paths)
    {
        using var pool = new AutoreleasePool();
        var term = paths.Count > 0 ? Path.GetExtension(paths[0]).TrimStart('.') : "";
        if (string.IsNullOrEmpty(term) && paths.Count > 0)
            term = MacWorkspace.LocalizedType(paths[0]);
        if (string.IsNullOrEmpty(term))
            return;
        var query = Uri.EscapeDataString(term);
        var url = ObjC.Call(ObjC.Class("NSURL"), "URLWithString:",
            ObjC.NsString("macappstore://itunes.apple.com/search?term=" + query));
        if (url != IntPtr.Zero && ObjC.MsgSendBool(Shared(), ObjC.Sel("openURL:"), url))
            return;
        url = ObjC.Call(ObjC.Class("NSURL"), "URLWithString:",
            ObjC.NsString("https://apps.apple.com/search?term=" + query));
        if (url != IntPtr.Zero)
            ObjC.MsgSendBool(Shared(), ObjC.Sel("openURL:"), url);
    }

    private static IntPtr FileUrls(IReadOnlyList<string> paths)
    {
        var array = ObjC.MutableArray(paths.Count);
        foreach (var path in paths)
            ObjC.AddObject(array, ResolvedFileUrl(path));
        return array;
    }

    private static IntPtr ResolvedFileUrl(string path)
    {
        var url = ObjC.FileUrl(path);
        var error = IntPtr.Zero;
        var resolved = ObjC.MsgSend(ObjC.Class("NSURL"),
            ObjC.Sel("URLByResolvingAliasFileAtURL:options:error:"),
            url, (IntPtr)AliasWithoutUi, ref error);
        return resolved == IntPtr.Zero ? url : resolved;
    }

    private static string? AppPath(IntPtr url)
    {
        if (url == IntPtr.Zero)
            return null;
        var standardized = ObjC.Call(url, "URLByStandardizingPath");
        var path = ObjC.ToString(ObjC.Call(standardized == IntPtr.Zero ? url : standardized, "path"));
        return !string.IsNullOrEmpty(path) && (Directory.Exists(path) || File.Exists(path)) ? path : null;
    }

    private static string DisplayName(string path) =>
        ObjC.ToString(ObjC.Call(ObjC.Call(ObjC.Class("NSFileManager"), "defaultManager"),
            "displayNameAtPath:", ObjC.NsString(path)))
        ?? Path.GetFileNameWithoutExtension(path);

    private static string? Version(string path)
    {
        var bundle = ObjC.Call(ObjC.Class("NSBundle"), "bundleWithPath:", ObjC.NsString(path));
        return bundle == IntPtr.Zero
            ? null
            : ObjC.ToString(ObjC.Call(bundle, "objectForInfoDictionaryKey:",
                ObjC.NsString("CFBundleShortVersionString")));
    }

    private static bool OptionDown() =>
        (ObjC.MsgSendNuint(ObjC.Class("NSEvent"), ObjC.Sel("modifierFlags")) & OptionFlag) != 0;

    private static IntPtr Shared() => ObjC.Call(ObjC.Class("NSWorkspace"), "sharedWorkspace");
}
