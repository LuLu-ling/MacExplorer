namespace MacExplorer.Native;

internal static class MacPasteboard
{
    public const string FilenamesType = "NSFilenamesPboardType";
    public const string FileUrlType = "public.file-url";
    public const string CutType = "org.nspasteboard.CutType";

    public static void WriteFiles(IReadOnlyList<string> paths, bool cut)
    {
        using var pool = new AutoreleasePool();
        var pasteboard = General();
        ObjC.Call(pasteboard, "clearContents");

        var urls = ObjC.MutableArray(paths.Count);
        foreach (var path in paths)
            ObjC.AddObject(urls, ObjC.FileUrl(path));

        ObjC.MsgSendBool(pasteboard, ObjC.Sel("writeObjects:"), urls);

        var names = ObjC.MutableArray(paths.Count);
        foreach (var path in paths)
            ObjC.AddObject(names, ObjC.NsString(path));
        ObjC.Call(pasteboard, "setPropertyList:forType:", names, ObjC.NsString(FilenamesType));

        if (cut)
            ObjC.Call(pasteboard, "setString:forType:", ObjC.NsString(string.Empty), ObjC.NsString(CutType));
    }

    public static (IReadOnlyList<string> Paths, bool Cut) ReadFiles()
    {
        using var pool = new AutoreleasePool();
        var pasteboard = General();
        var cut = ObjC.Call(pasteboard, "stringForType:", ObjC.NsString(CutType)) != IntPtr.Zero;

        var list = ObjC.Call(pasteboard, "propertyListForType:", ObjC.NsString(FilenamesType));
        if (list != IntPtr.Zero)
        {
            var count = ObjC.ArrayCount(list);
            var paths = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                var path = ObjC.ToString(ObjC.ArrayAt(list, i));
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }

            if (paths.Count > 0)
                return (paths, cut);
        }

        return (Array.Empty<string>(), cut);
    }

    public static bool HasFiles()
    {
        using var pool = new AutoreleasePool();
        var pasteboard = General();
        var list = ObjC.Call(pasteboard, "propertyListForType:", ObjC.NsString(FilenamesType));
        return list != IntPtr.Zero && ObjC.ArrayCount(list) > 0;
    }

    public static void Clear()
    {
        using var pool = new AutoreleasePool();
        ObjC.Call(General(), "clearContents");
    }

    private static IntPtr General() => ObjC.Call(ObjC.Class("NSPasteboard"), "generalPasteboard");
}
