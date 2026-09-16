using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using MacExplorer.Localization;
using MacExplorer.Models;
namespace MacExplorer.Native;

internal static class MacWorkspace
{
    public static bool Open(string path)
    {
        using var pool = new AutoreleasePool();
        return ObjC.MsgSendBool(Shared(), ObjC.Sel("openURL:"), ObjC.FileUrl(path));
    }

    public static bool Reveal(string path)
    {
        using var pool = new AutoreleasePool();
        var parent = Path.GetDirectoryName(path) ?? path;
        return ObjC.MsgSendBool(Shared(), ObjC.Sel("selectFile:inFileViewerRootedAtPath:"),
            ObjC.NsString(path), ObjC.NsString(parent));
    }

    public static bool ActivateFileViewer()
    {
        using var pool = new AutoreleasePool();
        return ObjC.MsgSendBool(Shared(), ObjC.Sel("openURL:"), ObjC.FileUrl("/"));
    }

    public static void NoteChanged(string path)
    {
        using var pool = new AutoreleasePool();
        ObjC.Call(Shared(), "noteFileSystemChanged:", ObjC.NsString(path));
    }


    public static string LocalizedType(string path)
    {
        using var pool = new AutoreleasePool();
        var error = IntPtr.Zero;
        var uti = ObjC.MsgSend(Shared(), ObjC.Sel("typeOfFile:error:"), ObjC.NsString(path), ref error);
        if (uti == IntPtr.Zero)
            return FallbackType(path);
        var description = ObjC.ToString(ObjC.Call(Shared(), "localizedDescriptionForType:", uti));
        return string.IsNullOrWhiteSpace(description) ? FallbackType(path) : description;
    }

    public static Bitmap? Icon(string path, int size)
    {
        using var pool = new AutoreleasePool();
        var pixels = Math.Max(16, size) * 2;
        var source = FetchIcon(path);
        if (source == IntPtr.Zero)
            return null;

        var icon = ObjC.Call(source, "copy");
        if (icon == IntPtr.Zero)
            return null;
        ObjC.Call(icon, "autorelease");
        ObjC.MsgSendVoid(icon, ObjC.Sel("setSize:"), new NSSize(pixels, pixels));

        var tint = FolderTint(path);
        var png = tint == FileTagColor.None
            ? EncodePng(icon) ?? EncodePngFromTiff(icon)
            : EncodeTintedPng(icon, tint) ?? EncodePng(icon) ?? EncodePngFromTiff(icon);
        if (png is null)
            return null;

        using var stream = new MemoryStream(png, writable: false);
        return new Bitmap(stream);
    }

    private static IntPtr FetchIcon(string path)
    {
        var icon = ObjC.Call(Shared(), "iconForFile:", ObjC.NsString(path));
        if (icon != IntPtr.Zero)
            return icon;

        var type = Directory.Exists(path) ? "public.folder" : "public.data";
        return ObjC.Call(Shared(), "iconForFileType:", ObjC.NsString(type));
    }

    private static byte[]? EncodePng(IntPtr icon)
    {
        var cg = ObjC.MsgSend(icon, ObjC.Sel("CGImageForProposedRect:context:hints:"), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (cg == IntPtr.Zero)
            return null;
        var rep = ObjC.Call(ObjC.Call(ObjC.Class("NSBitmapImageRep"), "alloc"), "initWithCGImage:", cg);
        if (rep == IntPtr.Zero)
            return null;
        ObjC.Call(rep, "autorelease");
        var data = ObjC.MsgSend(rep, ObjC.Sel("representationUsingType:properties:"), 4, IntPtr.Zero);
        return ObjC.NsDataToBytes(data);
    }

    private static byte[]? EncodePngFromTiff(IntPtr icon)
    {
        var tiff = ObjC.Call(icon, "TIFFRepresentation");
        if (tiff == IntPtr.Zero)
            return null;
        var rep = ObjC.Call(ObjC.Class("NSBitmapImageRep"), "imageRepWithData:", tiff);
        if (rep == IntPtr.Zero)
            return null;
        var data = ObjC.MsgSend(rep, ObjC.Sel("representationUsingType:properties:"), 4, IntPtr.Zero);
        return ObjC.NsDataToBytes(data);
    }

    private static FileTagColor FolderTint(string path)
    {
        if (!Directory.Exists(path))
            return FileTagColor.None;
        var ext = Path.GetExtension(path);
        if (ext is ".app" or ".framework" or ".bundle" or ".plugin" or ".kext")
            return FileTagColor.None;
        if (File.Exists(Path.Combine(path, "Contents", "Info.plist")))
            return FileTagColor.None;

        foreach (var tag in MacTags.Read(path))
        {
            if (tag.Color != FileTagColor.None)
                return tag.Color;
        }

        return FileTagColor.None;
    }

    private static byte[]? EncodeTintedPng(IntPtr icon, FileTagColor tint)
    {
        var tiff = ObjC.Call(icon, "TIFFRepresentation");
        if (tiff == IntPtr.Zero)
            return null;
        var source = ObjC.Call(ObjC.Class("NSBitmapImageRep"), "imageRepWithData:", tiff);
        if (source == IntPtr.Zero)
            return null;
        var rep = ObjC.Call(source, "copy");
        if (rep == IntPtr.Zero)
            return null;
        ObjC.Call(rep, "autorelease");
        if (ObjC.MsgSendBool(rep, ObjC.Sel("isPlanar")) || !Colorize(rep, tint))
            return null;
        var data = ObjC.MsgSend(rep, ObjC.Sel("representationUsingType:properties:"), 4, IntPtr.Zero);
        return ObjC.NsDataToBytes(data);
    }

    private static bool Colorize(IntPtr rep, FileTagColor tint)
    {
        var width = (int)ObjC.MsgSendNuint(rep, ObjC.Sel("pixelsWide"));
        var height = (int)ObjC.MsgSendNuint(rep, ObjC.Sel("pixelsHigh"));
        var spp = (int)ObjC.MsgSendNuint(rep, ObjC.Sel("samplesPerPixel"));
        var stride = (int)ObjC.MsgSendNuint(rep, ObjC.Sel("bytesPerRow"));
        var format = ObjC.MsgSendNuint(rep, ObjC.Sel("bitmapFormat"));
        var ptr = ObjC.Call(rep, "bitmapData");
        if (ptr == IntPtr.Zero || width <= 0 || height <= 0 || spp < 3 || stride < width * 3)
            return false;

        var argb = FileTagPalette.Argb(tint);
        var tr = (int)((argb >> 16) & 255);
        var tg = (int)((argb >> 8) & 255);
        var tb = (int)(argb & 255);
        var alphaFirst = (format & 1) != 0;
        var buffer = new byte[stride * height];
        Marshal.Copy(ptr, buffer, 0, buffer.Length);

        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * spp;
                int r, g, b, a;
                if (alphaFirst && spp >= 4)
                {
                    a = buffer[i];
                    r = buffer[i + 1];
                    g = buffer[i + 2];
                    b = buffer[i + 3];
                }
                else
                {
                    r = buffer[i];
                    g = buffer[i + 1];
                    b = buffer[i + 2];
                    a = spp >= 4 ? buffer[i + 3] : 255;
                }

                if (a == 0)
                    continue;

                var lum = 40 + ((r * 54 + g * 183 + b * 19) >> 8) * 215 / 255;
                r = tr * lum / 255;
                g = tg * lum / 255;
                b = tb * lum / 255;
                if (alphaFirst && spp >= 4)
                {
                    buffer[i + 1] = (byte)r;
                    buffer[i + 2] = (byte)g;
                    buffer[i + 3] = (byte)b;
                }
                else
                {
                    buffer[i] = (byte)r;
                    buffer[i + 1] = (byte)g;
                    buffer[i + 2] = (byte)b;
                }
            }
        }

        Marshal.Copy(buffer, 0, ptr, buffer.Length);
        return true;
    }


    public static void Share(IReadOnlyList<string> paths, string serviceName)
    {
        using var pool = new AutoreleasePool();
        var service = ObjC.Call(ObjC.Class("NSSharingService"), "sharingServiceNamed:", ObjC.NsString(serviceName));
        if (service == IntPtr.Zero)
            return;

        var items = ObjC.MutableArray(paths.Count);
        foreach (var path in paths)
            ObjC.AddObject(items, ObjC.FileUrl(path));
        ObjC.Call(service, "performWithItems:", items);
    }

    public static IReadOnlyList<string> MountedVolumePaths()
    {
        using var pool = new AutoreleasePool();
        var urls = ObjC.Call(ObjC.Call(ObjC.Class("NSFileManager"), "defaultManager"),
            "mountedVolumeURLsIncludingResourceValuesForKeys:options:", IntPtr.Zero, (IntPtr)2);
        if (urls == IntPtr.Zero)
            return Array.Empty<string>();

        var count = ObjC.ArrayCount(urls);
        var paths = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var path = ObjC.ToString(ObjC.Call(ObjC.ArrayAt(urls, i), "path"));
            if (!string.IsNullOrEmpty(path))
                paths.Add(path);
        }

        return paths;
    }

    public static string VolumeName(string path)
    {
        using var pool = new AutoreleasePool();
        var url = ObjC.FileUrl(path);
        var name = ObjC.Call(url, "resourceValuesForKeys:error:",
            ObjC.Call(ObjC.Class("NSArray"), "arrayWithObject:", ObjC.NsString("NSURLVolumeNameKey")), IntPtr.Zero);
        if (name == IntPtr.Zero)
            return Path.GetFileName(path.TrimEnd('/')) is { Length: > 0 } leaf ? leaf : path;
        var value = ObjC.Call(name, "objectForKey:", ObjC.NsString("NSURLVolumeNameKey"));
        return ObjC.ToString(value) ?? Path.GetFileName(path.TrimEnd('/')) ?? path;
    }

    public static bool SameVolume(string a, string b)
    {
        var idA = VolumeUuid(a);
        var idB = VolumeUuid(b);
        if (!string.IsNullOrEmpty(idA) && !string.IsNullOrEmpty(idB))
            return string.Equals(idA, idB, StringComparison.Ordinal);
        return VolumeRoot(a) == VolumeRoot(b);
    }

    private static string? VolumeUuid(string path)
    {
        using var pool = new AutoreleasePool();
        var values = ObjC.Call(ObjC.FileUrl(path), "resourceValuesForKeys:error:",
            ObjC.Call(ObjC.Class("NSArray"), "arrayWithObject:", ObjC.NsString("NSURLVolumeUUIDStringKey")),
            IntPtr.Zero);
        if (values == IntPtr.Zero)
            return null;
        return ObjC.ToString(ObjC.Call(values, "objectForKey:", ObjC.NsString("NSURLVolumeUUIDStringKey")));
    }

    private static string VolumeRoot(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return "/";
        }

        var best = "/";
        foreach (var vol in MountedVolumePaths())
        {
            if (vol is "/" or "")
                continue;
            var prefix = vol.TrimEnd('/');
            if (full.Equals(prefix, StringComparison.Ordinal) ||
                full.StartsWith(prefix + "/", StringComparison.Ordinal))
            {
                if (prefix.Length > best.Length)
                    best = prefix;
            }
        }

        return best;
    }

    public static bool IsDiskImage(string path)
    {
        using var pool = new AutoreleasePool();
        if (string.IsNullOrEmpty(path) || path is "/" or "/System/Volumes/Data")
            return false;
        var protocol = DiskProtocol(path);
        return protocol is "Disk Image" or "Virtual Interface"
            && VolumeBool(path, "NSURLVolumeIsEjec tableKey");
    }

    public static bool Eject(string path)
    {
        using var pool = new AutoreleasePool();
        var error = IntPtr.Zero;
        if (ObjC.MsgSendBool(Shared(), ObjC.Sel("unmountAndEjectDeviceAtURL:error:"), ObjC.FileUrl(path), ref error))
            return true;
        return ObjC.MsgSendBool(Shared(), ObjC.Sel("unmountAndEjectDeviceAtPath:"), ObjC.NsString(path));
    }

    public static bool PathOnVolume(string path, string volume)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(volume) || SpecialFolders.IsVirtual(path))
            return false;
        string full;
        string root;
        try
        {
            full = Path.GetFullPath(path).TrimEnd('/');
            root = Path.GetFullPath(volume).TrimEnd('/');
        }
        catch
        {
            return false;
        }
        if (full.Length == 0) full = "/";
        if (root.Length == 0) root = "/";
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool VolumeBool(string path, string key)
    {
        var values = ObjC.Call(ObjC.FileUrl(path), "resourceValuesForKeys:error:",
            ObjC.Call(ObjC.Class("NSArray"), "arrayWithObject:", ObjC.NsString(key)), IntPtr.Zero);
        if (values == IntPtr.Zero)
            return false;
        var num = ObjC.Call(values, "objectForKey:", ObjC.NsString(key));
        return num != IntPtr.Zero && ObjC.MsgSendBool(num, ObjC.Sel("boolValue"));
    }

    private static string? DiskProtocol(string path)
    {
        var session = DASessionCreate(IntPtr.Zero);
        if (session == IntPtr.Zero)
            return null;
        try
        {
            var disk = DADiskCreateFromVolumePath(IntPtr.Zero, session, ObjC.FileUrl(path));
            if (disk == IntPtr.Zero)
                return null;
            try
            {
                var desc = DADiskCopyDescription(disk);
                if (desc == IntPtr.Zero)
                    return null;
                try
                {
                    return ObjC.ToString(ObjC.Call(desc, "objectForKey:", ObjC.NsString("DADeviceProtocol")));
                }
                finally
                {
                    CFRelease(desc);
                }
            }
            finally
            {
                CFRelease(disk);
            }
        }
        finally
        {
            CFRelease(session);
        }
    }

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string DiskArbitration = "/System/Library/Frameworks/DiskArbitration.framework/DiskArbitration";

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport(DiskArbitration)]
    private static extern IntPtr DASessionCreate(IntPtr allocator);

    [DllImport(DiskArbitration)]
    private static extern IntPtr DADiskCreateFromVolumePath(IntPtr allocator, IntPtr session, IntPtr path);

    [DllImport(DiskArbitration)]
    private static extern IntPtr DADiskCopyDescription(IntPtr disk);

    private static IntPtr Shared() => ObjC.Call(ObjC.Class("NSWorkspace"), "sharedWorkspace");

    private static string FallbackType(string path)
    {
        if (Directory.Exists(path))
            return Lang.Text("File.Type.Folder");
        var ext = Path.GetExtension(path);
        return string.IsNullOrEmpty(ext)
            ? Lang.Text("File.Type.Document")
            : Lang.Text("File.Type.ExtensionFile", ext.TrimStart('.').ToUpperInvariant());
    }
}
