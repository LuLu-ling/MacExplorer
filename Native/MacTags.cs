using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using MacExplorer.Models;

namespace MacExplorer.Native;

internal static class MacTags
{
    private const string UserTags = "com.apple.metadata:_kMDItemUserTags";
    private const string FinderInfo = "com.apple.FinderInfo";
    private const string TagViewSuffix = "_Tag_ViewSettings";
    private const nuint BinaryPlist = 200;
    private const int FinderInfoSize = 32;

    private static readonly ConcurrentDictionary<string, FileTagColor> Learned = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, FileTagColor> Standard = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Red"] = FileTagColor.Red, ["红色"] = FileTagColor.Red, ["紅色"] = FileTagColor.Red,
        ["Orange"] = FileTagColor.Orange, ["橙色"] = FileTagColor.Orange, ["橘色"] = FileTagColor.Orange,
        ["Yellow"] = FileTagColor.Yellow, ["黄色"] = FileTagColor.Yellow, ["黃色"] = FileTagColor.Yellow,
        ["Green"] = FileTagColor.Green, ["绿色"] = FileTagColor.Green, ["綠色"] = FileTagColor.Green,
        ["Blue"] = FileTagColor.Blue, ["蓝色"] = FileTagColor.Blue, ["藍色"] = FileTagColor.Blue,
        ["Purple"] = FileTagColor.Purple, ["紫色"] = FileTagColor.Purple,
        ["Gray"] = FileTagColor.Gray, ["Grey"] = FileTagColor.Gray, ["灰色"] = FileTagColor.Gray
    };

    public static FileTag Resolve(string name) => new(name, ColorOf(name));
    public static event Action? LearnedChanged;


    public static IReadOnlyList<FileTag> All()
    {
        using var pool = new AutoreleasePool();
        try
        {
            var domain = FinderDomain();
            if (domain == IntPtr.Zero)
                return [];

            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Add(string? name)
            {
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                    return;
                names.Add(name);
            }

            var favorites = ObjC.Call(domain, "objectForKey:", ObjC.NsString("FavoriteTagNames"));
            if (favorites != IntPtr.Zero)
            {
                var count = ObjC.ArrayCount(favorites);
                for (var i = 0; i < count; i++)
                    Add(ObjC.ToString(ObjC.ArrayAt(favorites, i)));
            }

            CollectViewSettingTags(domain, Add);
            return names.Select(Resolve).ToArray();
        }
        catch
        {
            return [];
        }
    }

    public static bool ReorderFavoriteNames(IReadOnlyList<string> names)
    {
        using var pool = new AutoreleasePool();
        try
        {
            var defaults = ObjC.Call(ObjC.Class("NSUserDefaults"), "standardUserDefaults");
            var domainName = ObjC.NsString("com.apple.finder");
            var domain = ObjC.Call(defaults, "persistentDomainForName:", domainName);
            if (domain == IntPtr.Zero)
                return false;
            var mutable = ObjC.Call(ObjC.Class("NSMutableDictionary"), "dictionaryWithDictionary:", domain);
            var array = ObjC.MutableArray(names.Count);
            foreach (var name in names)
                ObjC.AddObject(array, ObjC.NsString(name));
            ObjC.Call(mutable, "setObject:forKey:", array, ObjC.NsString("FavoriteTagNames"));
            ObjC.Call(defaults, "setPersistentDomain:forName:", mutable, domainName);
            ObjC.Call(defaults, "synchronize");
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<FileTag> Read(string path)
    {
        if (string.IsNullOrEmpty(path))
            return [];

        using var pool = new AutoreleasePool();
        try
        {
            var raw = GetXattr(path, UserTags);
            if (raw is { Length: > 0 })
            {
                var plist = ParsePlist(raw);
                if (plist != IntPtr.Zero)
                    return ArrayTags(plist);
            }

            return ResourceTagNames(path);
        }
        catch
        {
            return [];
        }
    }

    public static bool Write(string path, IReadOnlyList<FileTag> tags)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        using var pool = new AutoreleasePool();
        try
        {
            var url = ObjC.FileUrl(path);
            var names = ObjC.MutableArray(tags.Count);
            foreach (var tag in tags)
                ObjC.AddObject(names, ObjC.NsString(tag.Name));

            var error = IntPtr.Zero;
            if (!ObjC.MsgSendBool(url, ObjC.Sel("setResourceValue:forKey:error:"),
                    names, ObjC.NsString("NSURLTagNamesKey"), ref error))
                return false;

            foreach (var tag in tags)
                Learn(tag);

            WriteColoredXattr(path, tags);
            WriteLabel(path, FirstColor(tags));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static FileTagColor ColorOf(string name)
    {
        if (Standard.TryGetValue(name, out var color))
            return color;
        return Learned.GetValueOrDefault(name);
    }
    public static void NotifyLearned() => LearnedChanged?.Invoke();

    public static void LearnFromIndex()
    {
        foreach (var tag in All())
        {
            if (tag.Color != FileTagColor.None)
                continue;
            foreach (var path in MacFinder.FilesWithTag(tag.Name))
            {
                Read(path);
                if (ColorOf(tag.Name) != FileTagColor.None)
                    break;
            }
        }

        NotifyLearned();
    }


    private static void Learn(FileTag tag)
    {
        if (tag.Color != FileTagColor.None)
            Learned[tag.Name] = tag.Color;
    }

    private static FileTagColor FirstColor(IReadOnlyList<FileTag> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Color != FileTagColor.None)
                return tag.Color;
        }

        return FileTagColor.None;
    }

    private static IntPtr FinderDomain()
    {
        var defaults = ObjC.Call(ObjC.Class("NSUserDefaults"), "standardUserDefaults");
        var domain = ObjC.Call(defaults, "persistentDomainForName:", ObjC.NsString("com.apple.finder"));
        if (domain != IntPtr.Zero)
            return domain;

        var file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library/Preferences/com.apple.finder.plist");
        return ObjC.Call(ObjC.Class("NSDictionary"), "dictionaryWithContentsOfFile:", ObjC.NsString(file));
    }

    private static void CollectViewSettingTags(IntPtr dict, Action<string?> add)
    {
        if (dict == IntPtr.Zero ||
            !ObjC.MsgSendBool(dict, ObjC.Sel("isKindOfClass:"), ObjC.Class("NSDictionary")))
            return;

        var keys = ObjC.Call(dict, "allKeys");
        if (keys == IntPtr.Zero)
            return;

        var count = ObjC.ArrayCount(keys);
        for (var i = 0; i < count; i++)
        {
            var keyObj = ObjC.ArrayAt(keys, i);
            var key = ObjC.ToString(keyObj);
            if (key is not null && key.EndsWith(TagViewSuffix, StringComparison.Ordinal))
                add(key[..^TagViewSuffix.Length]);
            CollectViewSettingTags(ObjC.Call(dict, "objectForKey:", keyObj), add);
        }
    }

    private static IReadOnlyList<FileTag> ResourceTagNames(string path)
    {
        var url = ObjC.FileUrl(path);
        var value = IntPtr.Zero;
        var error = IntPtr.Zero;
        if (!GetResource(url, ObjC.Sel("getResourceValue:forKey:error:"),
                ref value, ObjC.NsString("NSURLTagNamesKey"), ref error) ||
            value == IntPtr.Zero)
            return [];
        return ArrayTags(value);
    }

    private static IReadOnlyList<FileTag> ArrayTags(IntPtr array)
    {
        var count = ObjC.ArrayCount(array);
        if (count <= 0)
            return [];

        var tags = new FileTag[count];
        var n = 0;
        for (var i = 0; i < count; i++)
        {
            var raw = ObjC.ToString(ObjC.ArrayAt(array, i));
            if (string.IsNullOrEmpty(raw))
                continue;
            tags[n++] = Parse(raw);
        }

        return n == tags.Length ? tags : tags.AsSpan(0, n).ToArray();
    }

    private static FileTag Parse(string raw)
    {
        var split = raw.IndexOf('\n');
        if (split < 0)
            return Resolve(raw);

        var name = raw[..split];
        var color = FileTagColor.None;
        if (split + 1 < raw.Length && raw[split + 1] is >= '1' and <= '7')
            color = (FileTagColor)(raw[split + 1] - '0');
        var tag = new FileTag(name, color);
        Learn(tag);
        return tag;
    }

    private static void WriteColoredXattr(string path, IReadOnlyList<FileTag> tags)
    {
        if (tags.Count == 0)
        {
            removexattr(path, UserTags, 0);
            return;
        }

        var array = ObjC.MutableArray(tags.Count);
        foreach (var tag in tags)
        {
            var encoded = tag.Color == FileTagColor.None
                ? tag.Name
                : $"{tag.Name}\n{(byte)tag.Color}";
            ObjC.AddObject(array, ObjC.NsString(encoded));
        }

        var error = IntPtr.Zero;
        var data = PlistWrite(ObjC.Class("NSPropertyListSerialization"),
            ObjC.Sel("dataWithPropertyList:format:options:error:"),
            array, BinaryPlist, 0, ref error);
        var bytes = ObjC.NsDataToBytes(data);
        if (bytes is { Length: > 0 })
            setxattr(path, UserTags, bytes, (nuint)bytes.Length, 0, 0);
    }

    private static void WriteLabel(string path, FileTagColor color)
    {
        var url = ObjC.FileUrl(path);
        var number = ObjC.Call(ObjC.Class("NSNumber"), "numberWithInt:", (IntPtr)(int)color);
        var error = IntPtr.Zero;
        ObjC.MsgSendBool(url, ObjC.Sel("setResourceValue:forKey:error:"),
            number, ObjC.NsString("NSURLLabelNumberKey"), ref error);

        var info = GetXattr(path, FinderInfo);
        if (info is null || info.Length < FinderInfoSize)
            info = new byte[FinderInfoSize];
        else if (info.Length > FinderInfoSize)
            info = info[..FinderInfoSize];

        var flags = (info[8] << 8) | info[9];
        flags = (flags & ~0x0E) | (((int)color << 1) & 0x0E);
        info[8] = (byte)(flags >> 8);
        info[9] = (byte)flags;
        setxattr(path, FinderInfo, info, (nuint)info.Length, 0, 0);
    }

    private static IntPtr ParsePlist(byte[] raw)
    {
        var data = ObjC.NsData(raw);
        if (data == IntPtr.Zero)
            return IntPtr.Zero;
        var error = IntPtr.Zero;
        return PlistRead(ObjC.Class("NSPropertyListSerialization"),
            ObjC.Sel("propertyListWithData:options:format:error:"),
            data, 0, IntPtr.Zero, ref error);
    }

    private static byte[]? GetXattr(string path, string name)
    {
        var size = getxattr(path, name, null, 0, 0, 0);
        if (size <= 0)
            return null;
        var buffer = new byte[size];
        var read = getxattr(path, name, buffer, (nuint)size, 0, 0);
        return read <= 0 ? null : buffer;
    }

    [DllImport(ObjC.Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool GetResource(IntPtr receiver, IntPtr selector, ref IntPtr value, IntPtr key, ref IntPtr error);

    [DllImport(ObjC.Lib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr PlistRead(IntPtr receiver, IntPtr selector, IntPtr data, nuint options, IntPtr format, ref IntPtr error);

    [DllImport(ObjC.Lib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr PlistWrite(IntPtr receiver, IntPtr selector, IntPtr plist, nuint format, nuint options, ref IntPtr error);

    [DllImport("/usr/lib/libSystem.B.dylib", SetLastError = true)]
    private static extern long getxattr(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        byte[]? value, nuint size, uint position, int options);

    [DllImport("/usr/lib/libSystem.B.dylib", SetLastError = true)]
    private static extern int setxattr(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        byte[] value, nuint size, uint position, int options);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int removexattr(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int options);
}
