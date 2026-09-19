using System.Runtime.InteropServices;

namespace MacExplorer.Native;

internal static class MacDock
{
    private const string Domain = "com.apple.dock";
    private const string Apps = "persistent-apps";
    private const string Others = "persistent-others";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public static bool Contains(string path)
    {
        using var pool = new AutoreleasePool();
        return IndexOf(Apps, path) >= 0 || IndexOf(Others, path) >= 0;
    }

    public static bool Add(string path)
    {
        if (string.IsNullOrEmpty(path) || !(File.Exists(path) || Directory.Exists(path)))
            return false;
        using var pool = new AutoreleasePool();
        if (IndexOf(Apps, path) >= 0 || IndexOf(Others, path) >= 0)
            return true;
        var key = IsApp(path) ? Apps : Others;
        var list = MutableList(key);
        ObjC.AddObject(list, Tile(path));
        Save(key, list);
        Reload();
        return true;
    }

    public static bool Remove(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        using var pool = new AutoreleasePool();
        var removed = RemoveFrom(Apps, path) | RemoveFrom(Others, path);
        if (removed)
            Reload();
        return removed;
    }

    private static bool RemoveFrom(string key, string path)
    {
        var list = MutableList(key);
        var removed = false;
        for (var i = ObjC.ArrayCount(list) - 1; i >= 0; i--)
        {
            if (!Same(TilePath(ObjC.ArrayAt(list, i)), path))
                continue;
            ObjC.Call(list, "removeObjectAtIndex:", (IntPtr)i);
            removed = true;
        }

        if (removed)
            Save(key, list);
        return removed;
    }

    private static int IndexOf(string key, string path)
    {
        var list = CopyList(key);
        if (list == IntPtr.Zero)
            return -1;
        var count = ObjC.ArrayCount(list);
        for (var i = 0; i < count; i++)
        {
            if (Same(TilePath(ObjC.ArrayAt(list, i)), path))
                return i;
        }

        return -1;
    }

    private static IntPtr Tile(string path)
    {
        var url = ObjC.FileUrl(path);
        var fileData = Dict();
        Set(fileData, "_CFURLString", ObjC.Call(url, "absoluteString"));
        Set(fileData, "_CFURLStringType", Number(15));

        var app = IsApp(path);
        var data = Dict();
        Set(data, "file-data", fileData);
        Set(data, "file-label", ObjC.NsString(Label(path)));
        Set(data, "file-type", Number(app ? 41 : 2));
        if (!app)
        {
            Set(data, "arrangement", Number(2));
            Set(data, "displayas", Number(0));
            Set(data, "showas", Number(1));
            Set(data, "directory", Number(1));
        }

        var bookmark = Bookmark(url);
        if (bookmark != IntPtr.Zero)
            Set(data, "book", bookmark);

        var tile = Dict();
        Set(tile, "GUID", Number((int)Random.Shared.Next(1, int.MaxValue)));
        Set(tile, "tile-data", data);
        Set(tile, "tile-type", ObjC.NsString(app ? "file-tile" : "directory-tile"));
        return tile;
    }

    private static string? TilePath(IntPtr tile)
    {
        var data = ObjC.Call(tile, "objectForKey:", ObjC.NsString("tile-data"));
        if (data == IntPtr.Zero)
            return null;
        var fileData = ObjC.Call(data, "objectForKey:", ObjC.NsString("file-data"));
        if (fileData == IntPtr.Zero)
            return null;
        var text = ObjC.ToString(ObjC.Call(fileData, "objectForKey:", ObjC.NsString("_CFURLString")));
        if (string.IsNullOrEmpty(text))
            return null;
        var url = text.Contains("://", StringComparison.Ordinal)
            ? ObjC.Call(ObjC.Class("NSURL"), "URLWithString:", ObjC.NsString(text))
            : ObjC.FileUrl(text);
        return url == IntPtr.Zero ? null : ObjC.ToString(ObjC.Call(url, "path"));
    }

    private static string Label(string path)
    {
        if (path is "/" or "")
            return MacWorkspace.VolumeName(path);
        var name = Path.GetFileName(path.TrimEnd('/'));
        if (name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static bool IsApp(string path) =>
        Directory.Exists(path) && path.TrimEnd('/').EndsWith(".app", StringComparison.OrdinalIgnoreCase);

    private static bool Same(string? a, string b) =>
        a is not null && string.Equals(Canon(a), Canon(b), StringComparison.OrdinalIgnoreCase);

    private static string Canon(string path)
    {
        try { path = Path.GetFullPath(path); }
        catch { /* keep original */ }
        var trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    private static IntPtr CopyList(string key)
    {
        var value = CFPreferencesCopyAppValue(ObjC.NsString(key), ObjC.NsString(Domain));
        if (value != IntPtr.Zero)
            ObjC.Call(value, "autorelease");
        return value;
    }

    private static IntPtr MutableList(string key)
    {
        var value = CopyList(key);
        if (value == IntPtr.Zero)
            return ObjC.Call(ObjC.Class("NSMutableArray"), "array");
        var copy = ObjC.Call(value, "mutableCopy");
        ObjC.Call(copy, "autorelease");
        return copy;
    }

    private static void Save(string key, IntPtr list)
    {
        CFPreferencesSetAppValue(ObjC.NsString(key), list, ObjC.NsString(Domain));
        CFPreferencesAppSynchronize(ObjC.NsString(Domain));
    }

    private static void Reload()
    {
        var running = ObjC.Call(ObjC.Class("NSRunningApplication"),
            "runningApplicationsWithBundleIdentifier:", ObjC.NsString("com.apple.dock"));
        if (running == IntPtr.Zero || ObjC.ArrayCount(running) == 0)
            return;
        ObjC.Call(ObjC.ArrayAt(running, 0), "terminate");
    }

    private static IntPtr Dict() => ObjC.Call(ObjC.Class("NSMutableDictionary"), "dictionary");

    private static void Set(IntPtr dict, string key, IntPtr value)
    {
        if (value != IntPtr.Zero)
            ObjC.Call(dict, "setObject:forKey:", value, ObjC.NsString(key));
    }

    private static IntPtr Number(int value) =>
        ObjC.Call(ObjC.Class("NSNumber"), "numberWithInt:", (IntPtr)value);

    private static IntPtr Bookmark(IntPtr url)
    {
        var error = IntPtr.Zero;
        return BookmarkData(url,
            ObjC.Sel("bookmarkDataWithOptions:includingResourceValuesForKeys:relativeToURL:error:"),
            0, IntPtr.Zero, IntPtr.Zero, ref error);
    }

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFPreferencesCopyAppValue(IntPtr key, IntPtr applicationId);

    [DllImport(CoreFoundation)]
    private static extern void CFPreferencesSetAppValue(IntPtr key, IntPtr value, IntPtr applicationId);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFPreferencesAppSynchronize(IntPtr applicationId);

    [DllImport(ObjC.Lib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr BookmarkData(
        IntPtr receiver, IntPtr selector, nuint options, IntPtr keys, IntPtr relative, ref IntPtr error);
}
