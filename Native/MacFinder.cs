using System.Runtime.InteropServices;

namespace MacExplorer.Native;

internal static class MacFinder
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const nuint BookmarkWithoutUiOrMount = 256 | 512;
    private const nuint MdQuerySynchronous = 1;
    private const uint ResolveFavoriteFlags = 1 | 2;
    private const int MaxTagResults = 10_000;

    public static event Action? FavoritesChanged;

    public static IReadOnlyList<string> FavoriteFolders()
    {
        using var pool = new AutoreleasePool();
        try
        {
            return SnapshotFavorites() ?? ReadFavoriteFile();
        }
        catch
        {
            return [];
        }
    }

    public static bool IsFavorite(string path) =>
        !string.IsNullOrEmpty(path) && FavoriteFolders().Any(p => SamePath(p, path));

    public static bool AddFavorite(string path) => AddFavorites([path]);

    public static bool AddFavorites(IReadOnlyList<string> paths)
    {
        var added = false;
        foreach (var path in paths)
            added |= InsertFavorite(path);
        if (added)
            NotifyFavoritesChanged();
        return added;
    }

    private static bool InsertFavorite(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path) || IsFavorite(path))
            return false;

        using var pool = new AutoreleasePool();
        var list = CreateFavoriteList();
        if (list == IntPtr.Zero)
            return false;
        try
        {
            var item = LSSharedFileListInsertItemURL(
                list, ItemLast(), IntPtr.Zero, IntPtr.Zero, ObjC.FileUrl(path), IntPtr.Zero, IntPtr.Zero);
            return item != IntPtr.Zero;
        }
        finally
        {
            CFRelease(list);
        }
    }

    public static bool RemoveFavorite(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        using var pool = new AutoreleasePool();
        var list = CreateFavoriteList();
        if (list == IntPtr.Zero)
            return false;
        try
        {
            uint seed = 0;
            var snapshot = LSSharedFileListCopySnapshot(list, ref seed);
            if (snapshot == IntPtr.Zero)
                return false;
            try
            {
                var removed = false;
                var count = CFArrayGetCount(snapshot);
                for (long i = 0; i < count; i++)
                {
                    var item = CFArrayGetValueAtIndex(snapshot, i);
                    if (!SamePath(ResolveListItem(item), path))
                        continue;
                    if (LSSharedFileListItemRemove(list, item) == 0)
                        removed = true;
                }

                if (removed)
                    NotifyFavoritesChanged();
                return removed;
            }
            finally
            {
                CFRelease(snapshot);
            }
        }
        finally
        {
            CFRelease(list);
        }
    }

    public static bool ToggleFavorite(string path) =>
        IsFavorite(path) ? RemoveFavorite(path) : AddFavorite(path);

    public static void ShowInfo(IReadOnlyList<string> paths)
    {
        using var pool = new AutoreleasePool();
        var lines = paths
            .Where(static p => !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p)))
            .Select(static p => $"open information window of (POSIX file \"{MdEscape(p)}\" as alias)")
            .ToArray();
        if (lines.Length == 0)
            return;
        var source = $"tell application \"Finder\"\n{string.Join("\n", lines)}\nactivate\nend tell";
        var script = ObjC.Call(ObjC.Call(ObjC.Class("NSAppleScript"), "alloc"),
            "initWithSource:", ObjC.NsString(source));
        if (script == IntPtr.Zero)
            return;
        ObjC.Call(script, "autorelease");
        ObjC.Call(script, "executeAndReturnError:", IntPtr.Zero);
    }

    public static IReadOnlyList<string> FilesWithTag(string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return [];

        using var pool = new AutoreleasePool();
        var query = ObjC.NsString($"kMDItemUserTags == \"{MdEscape(tag)}\"");
        var mdq = MDQueryCreate(IntPtr.Zero, query, IntPtr.Zero, IntPtr.Zero);
        if (mdq == IntPtr.Zero)
            return [];

        try
        {
            if (!MDQueryExecute(mdq, MdQuerySynchronous))
                return [];

            var count = Math.Min(MDQueryGetResultCount(mdq), MaxTagResults);
            var paths = new List<string>((int)count);
            var pathKey = ObjC.NsString("kMDItemPath");
            for (long i = 0; i < count; i++)
            {
                var item = MDQueryGetResultAtIndex(mdq, i);
                if (item == IntPtr.Zero)
                    continue;
                var pathRef = MDItemCopyAttribute(item, pathKey);
                if (pathRef == IntPtr.Zero)
                    continue;
                var path = ObjC.ToString(pathRef);
                CFRelease(pathRef);
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }

            return paths;
        }
        finally
        {
            CFRelease(mdq);
        }
    }

    private static IReadOnlyList<string>? SnapshotFavorites()
    {
        var list = CreateFavoriteList();
        if (list == IntPtr.Zero)
            return null;
        try
        {
            uint seed = 0;
            var snapshot = LSSharedFileListCopySnapshot(list, ref seed);
            if (snapshot == IntPtr.Zero)
                return [];
            try
            {
                var count = CFArrayGetCount(snapshot);
                var paths = new List<string>((int)count);
                for (long i = 0; i < count; i++)
                {
                    var path = ResolveListItem(CFArrayGetValueAtIndex(snapshot, i));
                    if (path is not null)
                        paths.Add(path);
                }

                return paths;
            }
            finally
            {
                CFRelease(snapshot);
            }
        }
        finally
        {
            CFRelease(list);
        }
    }

    private static IReadOnlyList<string> ReadFavoriteFile()
    {
        var file = FavoriteListFile();
        if (file is null)
            return [];

        var data = ObjC.Call(ObjC.Class("NSData"), "dataWithContentsOfFile:", ObjC.NsString(file));
        if (data == IntPtr.Zero)
            return [];

        var error = IntPtr.Zero;
        var root = ObjC.MsgSend(ObjC.Class("NSKeyedUnarchiver"),
            ObjC.Sel("unarchivedObjectOfClasses:fromData:error:"),
            AllowedArchiveClasses(), data, ref error);
        if (root == IntPtr.Zero)
            root = ObjC.Call(ObjC.Class("NSKeyedUnarchiver"), "unarchiveObjectWithData:", data);
        if (root == IntPtr.Zero)
            return [];

        var items = ObjC.Call(root, "objectForKey:", ObjC.NsString("items"));
        if (items == IntPtr.Zero)
            return [];

        var count = ObjC.ArrayCount(items);
        var paths = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var item = ObjC.ArrayAt(items, i);
            if (NsInt(ObjC.Call(item, "objectForKey:", ObjC.NsString("visibility"))) != 0)
                continue;
            var path = ResolveBookmark(ObjC.Call(item, "objectForKey:", ObjC.NsString("Bookmark")));
            if (path is not null)
                paths.Add(path);
        }

        return paths;
    }

    private static string? FavoriteListFile()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library/Application Support/com.apple.sharedfilelist");
        foreach (var name in new[]
                 {
                     "com.apple.LSSharedFileList.FavoriteItems.sfl4",
                     "com.apple.LSSharedFileList.FavoriteItems.sfl3",
                     "com.apple.LSSharedFileList.FavoriteItems.sfl2"
                 })
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static IntPtr AllowedArchiveClasses()
    {
        var array = ObjC.MutableArray(10);
        foreach (var name in new[]
                 {
                     "NSDictionary", "NSMutableDictionary", "NSArray", "NSMutableArray",
                     "NSString", "NSMutableString", "NSNumber", "NSData", "NSMutableData", "NSUUID"
                 })
            ObjC.AddObject(array, ObjC.Class(name));
        return ObjC.Call(ObjC.Class("NSSet"), "setWithArray:", array);
    }

    private static string? ResolveBookmark(IntPtr bookmark)
    {
        if (bookmark == IntPtr.Zero)
            return null;
        byte stale = 0;
        var error = IntPtr.Zero;
        var url = UrlByResolvingBookmark(
            ObjC.Class("NSURL"),
            ObjC.Sel("URLByResolvingBookmarkData:options:relativeToURL:bookmarkDataIsStale:error:"),
            bookmark, BookmarkWithoutUiOrMount, IntPtr.Zero, ref stale, ref error);
        return url == IntPtr.Zero ? null : ObjC.ToString(ObjC.Call(url, "path"));
    }

    private static IntPtr CreateFavoriteList()
    {
        var type = Symbol("kLSSharedFileListFavoriteItems");
        if (type == IntPtr.Zero)
            type = ObjC.NsString("com.apple.LSSharedFileList.FavoriteItems");
        return LSSharedFileListCreate(IntPtr.Zero, type, IntPtr.Zero);
    }

    private static IntPtr ItemLast()
    {
        var last = Symbol("kLSSharedFileListItemLast");
        return last == IntPtr.Zero ? new IntPtr(-1) : last;
    }

    private static string? ResolveListItem(IntPtr item)
    {
        if (item == IntPtr.Zero)
            return null;
        var error = IntPtr.Zero;
        var url = LSSharedFileListItemCopyResolvedURL(item, ResolveFavoriteFlags, ref error);
        if (url == IntPtr.Zero)
            return null;
        try
        {
            return ObjC.ToString(ObjC.Call(url, "path"));
        }
        finally
        {
            CFRelease(url);
        }
    }

    private static bool SamePath(string? a, string b)
    {
        if (a is null)
            return false;
        return string.Equals(Canon(a), Canon(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string Canon(string path)
    {
        try { path = Path.GetFullPath(path); }
        catch { /* keep original */ }
        var trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    private static void NotifyFavoritesChanged() => FavoritesChanged?.Invoke();

    private static readonly IntPtr CoreServicesLib = NativeLibrary.Load(CoreServices);

    private static IntPtr Symbol(string name) =>
        NativeLibrary.TryGetExport(CoreServicesLib, name, out var addr)
            ? Marshal.ReadIntPtr(addr)
            : IntPtr.Zero;

    private static int NsInt(IntPtr obj) =>
        obj == IntPtr.Zero ? 0 : ObjC.Call(obj, "intValue").ToInt32();

    private static string MdEscape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    [DllImport(ObjC.Lib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr UrlByResolvingBookmark(
        IntPtr receiver, IntPtr selector, IntPtr data, nuint options, IntPtr relative,
        ref byte stale, ref IntPtr error);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport(CoreFoundation)]
    private static extern long CFArrayGetCount(IntPtr array);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);

    [DllImport(CoreServices)]
    private static extern IntPtr LSSharedFileListCreate(IntPtr allocator, IntPtr listType, IntPtr options);

    [DllImport(CoreServices)]
    private static extern IntPtr LSSharedFileListCopySnapshot(IntPtr list, ref uint seed);

    [DllImport(CoreServices)]
    private static extern IntPtr LSSharedFileListInsertItemURL(
        IntPtr list, IntPtr after, IntPtr displayName, IntPtr iconRef,
        IntPtr url, IntPtr propertiesToSet, IntPtr propertiesToClear);

    [DllImport(CoreServices)]
    private static extern int LSSharedFileListItemRemove(IntPtr list, IntPtr item);

    [DllImport(CoreServices)]
    private static extern IntPtr LSSharedFileListItemCopyResolvedURL(IntPtr item, uint flags, ref IntPtr error);

    [DllImport(CoreServices)]
    private static extern IntPtr MDQueryCreate(IntPtr allocator, IntPtr query, IntPtr attrs, IntPtr sort);

    [DllImport(CoreServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool MDQueryExecute(IntPtr query, nuint options);

    [DllImport(CoreServices)]
    private static extern long MDQueryGetResultCount(IntPtr query);

    [DllImport(CoreServices)]
    private static extern IntPtr MDQueryGetResultAtIndex(IntPtr query, long index);

    [DllImport(CoreServices)]
    private static extern IntPtr MDItemCopyAttribute(IntPtr item, IntPtr name);
}
