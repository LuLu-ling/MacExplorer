using System.Collections.Frozen;
using System.Runtime.InteropServices;

namespace MacExplorer.Native;

internal static class MacThumbnail
{
    private const string QuickLook = "/System/Library/Frameworks/QuickLook.framework/QuickLook";
    private const uint AttrDirEntryCount = 2;
    private static readonly FrozenSet<string> PreviewExtensions = FrozenSet.ToFrozenSet(
        [
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".heic", ".heif", ".bmp", ".tif", ".tiff", ".svg",
            ".pdf", ".ai", ".psd", ".eps",
            ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".mp3", ".m4a",
            ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx", ".pages", ".key", ".numbers",
            ".rtf", ".html", ".htm", ".txt"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static IntPtr Create(string path, int pixels, bool directory, bool bundle)
    {
        if (pixels <= 0 || bundle)
            return IntPtr.Zero;
        if (directory)
        {
            if (!HasEntries(path))
                return IntPtr.Zero;
        }
        else if (!PreviewExtensions.Contains(Path.GetExtension(path)))
        {
            return IntPtr.Zero;
        }

        var url = ObjC.FileUrl(path);
        if (url == IntPtr.Zero)
            return IntPtr.Zero;
        return QLThumbnailImageCreate(IntPtr.Zero, url, new NSSize(pixels, pixels), IntPtr.Zero);
    }

    public static bool HasEntries(string path)
    {
        if (!HasEntryCount(path))
            return false;
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                var name = Path.GetFileName(entry);
                if (name is ".DS_Store" or ".localized")
                    continue;
                return true;
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    private static bool HasEntryCount(string path)
    {
        var attrs = new AttrList { bitmapcount = 5, dirattr = AttrDirEntryCount };
        Span<byte> buffer = stackalloc byte[16];
        unsafe
        {
            fixed (byte* ptr = buffer)
            {
                if (getattrlist(path, ref attrs, (IntPtr)ptr, buffer.Length, 0) != 0)
                    return true;
            }
        }

        return MemoryMarshal.Read<uint>(buffer[4..]) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttrList
    {
        public ushort bitmapcount;
        public ushort reserved;
        public uint commonattr;
        public uint volattr;
        public uint dirattr;
        public uint fileattr;
        public uint forkattr;
    }

    [DllImport("/usr/lib/libSystem.B.dylib", SetLastError = true)]
    private static extern int getattrlist(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        ref AttrList attrList,
        IntPtr attrBuf,
        nint attrBufSize,
        uint options);

    [DllImport(QuickLook)]
    private static extern IntPtr QLThumbnailImageCreate(IntPtr allocator, IntPtr url, NSSize size, IntPtr options);
}
