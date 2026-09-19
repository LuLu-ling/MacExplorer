using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using MacExplorer.Models;

namespace MacExplorer.Native;

internal static class MacFolderIcon
{
    private const string IconServices = "/System/Library/PrivateFrameworks/IconServices.framework/IconServices";
    private const string IconFoundation = "/System/Library/PrivateFrameworks/IconFoundation.framework/IconFoundation";
    private static readonly ConcurrentDictionary<(int Pixels, bool Empty, FileTagColor Tint), Bitmap> Cache = new();

    static MacFolderIcon()
    {
        dlopen(IconFoundation, 1);
        dlopen(IconServices, 1);
    }

    public static Bitmap? Bitmap(int size, bool empty, FileTagColor tint)
    {
        var pixels = Math.Clamp(size, 16, 512) * 2;
        var key = (pixels, empty, tint);
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        using var pool = new AutoreleasePool();
        var image = Create(pixels, empty, tint);
        if (image == IntPtr.Zero)
            return null;
        var bitmap = MacImage.FromCGImage(image);
        if (bitmap is not null)
            Cache.TryAdd(key, bitmap);
        return bitmap;
    }

    private static IntPtr Create(int pixels, bool empty, FileTagColor tint)
    {
        var config = ObjC.Call(ObjC.Call(ObjC.Class("ISFolderIconConfiguration"), "alloc"), "init");
        if (config == IntPtr.Zero)
            return IntPtr.Zero;
        ObjC.SetBool(config, "setFolderEmpty:", empty);
        if (tint != FileTagColor.None)
        {
            var color = TintColor(tint);
            if (color != IntPtr.Zero)
                ObjC.Call(config, "setTintColor:", color);
        }

        var icon = ObjC.Call(
            ObjC.Call(ObjC.Class("ISIconFactory"), "alloc"),
            "initWithType:iconConfiguration:",
            ObjC.NsString("public.folder"),
            config);
        if (icon == IntPtr.Zero)
            return IntPtr.Zero;

        var descriptor = Descriptor(pixels / 2.0, 2);
        if (descriptor == IntPtr.Zero)
            return IntPtr.Zero;
        var generated = ObjC.Call(icon, "generateImageWithDescriptor:", descriptor);
        return generated == IntPtr.Zero ? IntPtr.Zero : ObjC.Call(generated, "CGImage");
    }

    private static IntPtr TintColor(FileTagColor tint)
    {
        var argb = FileTagPalette.Argb(tint);
        return ObjC.MsgSend(
            ObjC.Call(ObjC.Class("IFColor"), "alloc"),
            ObjC.Sel("initWithRed:green:blue:alpha:"),
            ((argb >> 16) & 255) / 255.0,
            ((argb >> 8) & 255) / 255.0,
            (argb & 255) / 255.0,
            1.0);
    }

    private static IntPtr Descriptor(double size, double scale) =>
        InitDescriptor(
            ObjC.Call(ObjC.Class("ISImageDescriptor"), "alloc"),
            ObjC.Sel("initWithSize:scale:"),
            new NSSize(size, size),
            scale);

    [DllImport(ObjC.Lib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr InitDescriptor(IntPtr receiver, IntPtr selector, NSSize size, double scale);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern IntPtr dlopen(string path, int mode);
}
