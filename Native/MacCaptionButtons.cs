using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace MacExplorer.Native;

internal static class MacCaptionButtons
{
    public static void HideCloseAndMini(Window window)
    {
        var ns = NsWindow(window);
        if (ns == IntPtr.Zero)
            return;

        using var pool = new AutoreleasePool();
        var off = new NSRect(-64, -64, 12, 12);
        for (var i = 0; i < 2; i++)
        {
            var button = Button(ns, i);
            if (button == IntPtr.Zero)
                continue;
            ObjC.SetBool(button, "setHidden:", true);
            ObjC.MsgSend(button, ObjC.Sel("setFrame:"), off);
        }
    }

    public static void PlaceZoom(Window window, Rect client)
    {
        var ns = NsWindow(window);
        if (ns == IntPtr.Zero)
            return;

        using var pool = new AutoreleasePool();
        var zoom = Button(ns, 2);
        var content = ObjC.Call(ns, "contentView");
        if (zoom == IntPtr.Zero || content == IntPtr.Zero)
            return;

        ObjC.SetBool(zoom, "setHidden:", false);
        ObjC.SetBool(zoom, "setEnabled:", true);
        SetAlpha(zoom, 0.02);
        if (ObjC.Call(zoom, "superview") != content)
            ObjC.Call(content, "addSubview:", zoom);

        var y = ObjC.MsgSendBool(content, ObjC.Sel("isFlipped"))
            ? client.Y
            : window.ClientSize.Height - client.Y - client.Height;
        ObjC.MsgSend(zoom, ObjC.Sel("setFrame:"), new NSRect(client.X, y, client.Width, client.Height));
    }

    public static void HideZoom(Window window)
    {
        var ns = NsWindow(window);
        if (ns == IntPtr.Zero)
            return;

        using var pool = new AutoreleasePool();
        var zoom = Button(ns, 2);
        if (zoom == IntPtr.Zero)
            return;
        ObjC.SetBool(zoom, "setHidden:", true);
        ObjC.MsgSend(zoom, ObjC.Sel("setFrame:"), new NSRect(-64, -64, 12, 12));
    }

    private static IntPtr NsWindow(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        return handle?.HandleDescriptor == "NSWindow" ? handle.Handle : IntPtr.Zero;
    }

    private static IntPtr Button(IntPtr nsWindow, int index) =>
        ObjC.MsgSend(nsWindow, ObjC.Sel("standardWindowButton:"), (IntPtr)index);

    [DllImport(ObjC.Lib, EntryPoint = "objc_msgSend")]
    private static extern void SetAlpha(IntPtr receiver, IntPtr selector, double value);

    private static void SetAlpha(IntPtr view, double value) =>
        SetAlpha(view, ObjC.Sel("setAlphaValue:"), value);
}
