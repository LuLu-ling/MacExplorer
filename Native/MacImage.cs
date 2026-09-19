using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MacExplorer.Models;

namespace MacExplorer.Native;

internal static class MacImage
{
    private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint BgraPremul = 2 | (2u << 12);

    public static Bitmap? FromCGImage(IntPtr image, FileTagColor tint = FileTagColor.None)
    {
        if (image == IntPtr.Zero)
            return null;
        var width = (int)CGImageGetWidth(image);
        var height = (int)CGImageGetHeight(image);
        if (width <= 0 || height <= 0)
            return null;

        var stride = width * 4;
        var buffer = Marshal.AllocHGlobal(stride * height);
        try
        {
            Clear(buffer, stride * height);
            var space = CGColorSpaceCreateDeviceRGB();
            if (space == IntPtr.Zero)
                return null;
            var ctx = CGBitmapContextCreate(buffer, width, height, 8, stride, space, BgraPremul);
            CGColorSpaceRelease(space);
            if (ctx == IntPtr.Zero)
                return null;
            CGContextDrawImage(ctx, new NSRect(0, 0, width, height), image);
            CGContextRelease(ctx);
            if (tint != FileTagColor.None)
                Tint(buffer, width, height, stride, tint);
            Unpremultiply(buffer, width, height, stride);
            return new WriteableBitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, buffer,
                new PixelSize(width, height), new Vector(96, 96), stride);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static Bitmap? FromNSImage(IntPtr image, FileTagColor tint = FileTagColor.None)
    {
        if (image == IntPtr.Zero)
            return null;
        var cg = ObjC.MsgSend(image, ObjC.Sel("CGImageForProposedRect:context:hints:"),
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return FromCGImage(cg, tint);
    }

    private static unsafe void Clear(IntPtr pixels, int length) =>
        new Span<byte>((void*)pixels, length).Clear();

    private static unsafe void Unpremultiply(IntPtr pixels, int width, int height, int stride)
    {
        var p = (byte*)pixels;
        for (var y = 0; y < height; y++)
        {
            var row = p + y * stride;
            for (var x = 0; x < width; x++)
            {
                var i = x * 4;
                var a = row[i + 3];
                if (a == 0)
                {
                    row[i] = 0;
                    row[i + 1] = 0;
                    row[i + 2] = 0;
                    continue;
                }

                if (a == 255)
                    continue;
                row[i] = (byte)Math.Min(255, row[i] * 255 / a);
                row[i + 1] = (byte)Math.Min(255, row[i + 1] * 255 / a);
                row[i + 2] = (byte)Math.Min(255, row[i + 2] * 255 / a);
            }
        }
    }

    private static unsafe void Tint(IntPtr pixels, int width, int height, int stride, FileTagColor tint)
    {
        var argb = FileTagPalette.Argb(tint);
        var tr = (int)((argb >> 16) & 255);
        var tg = (int)((argb >> 8) & 255);
        var tb = (int)(argb & 255);
        var p = (byte*)pixels;
        for (var y = 0; y < height; y++)
        {
            var row = p + y * stride;
            for (var x = 0; x < width; x++)
            {
                var i = x * 4;
                var a = row[i + 3];
                if (a == 0)
                    continue;
                var b = row[i] * 255 / a;
                var g = row[i + 1] * 255 / a;
                var r = row[i + 2] * 255 / a;
                var lum = 40 + ((r * 54 + g * 183 + b * 19) >> 8) * 215 / 255;
                row[i] = (byte)(tb * lum * a / 65025);
                row[i + 1] = (byte)(tg * lum * a / 65025);
                row[i + 2] = (byte)(tr * lum * a / 65025);
            }
        }
    }

    [DllImport(Graphics)]
    private static extern nint CGImageGetWidth(IntPtr image);

    [DllImport(Graphics)]
    private static extern nint CGImageGetHeight(IntPtr image);

    [DllImport(Graphics)]
    private static extern IntPtr CGColorSpaceCreateDeviceRGB();

    [DllImport(Graphics)]
    private static extern void CGColorSpaceRelease(IntPtr space);

    [DllImport(Graphics)]
    private static extern IntPtr CGBitmapContextCreate(
        IntPtr data, nint width, nint height, nint bitsPerComponent, nint bytesPerRow, IntPtr space, uint bitmapInfo);

    [DllImport(Graphics)]
    private static extern void CGContextDrawImage(IntPtr context, NSRect rect, IntPtr image);

    [DllImport(Graphics)]
    private static extern void CGContextRelease(IntPtr context);

    [DllImport(Foundation)]
    internal static extern void CFRelease(IntPtr cf);
}
