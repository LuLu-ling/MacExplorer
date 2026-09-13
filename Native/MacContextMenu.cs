using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace MacExplorer.Native;

internal readonly record struct MacMenuEntry(
    string Title,
    Action? Action = null,
    bool Enabled = true,
    MacMenuEntry[]? Children = null,
    bool Separator = false,
    bool Checked = false,
    uint? Dot = null);

internal static class MacContextMenu
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MenuImp(IntPtr self, IntPtr cmd, IntPtr sender);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    private static readonly MenuImp InvokeImp = Invoke;
    private static List<Action?> _actions = [];
    private static IntPtr _targetClass;
    private static bool _registered;
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;
        Control.ContextRequestedEvent.AddClassHandler<TextBox>(OnTextBox);
    }

    public static void Show(IReadOnlyList<MacMenuEntry> entries)
    {
        Install();
        Dispatcher.UIThread.Post(() => ShowCore(entries), DispatcherPriority.Input);
    }

    private static void ShowCore(IReadOnlyList<MacMenuEntry> entries)
    {
        using var pool = new AutoreleasePool();
        EnsureTarget();
        _actions = [];
        var target = ObjC.Call(_targetClass, "new");
        var menu = BuildMenu(entries, target);
        var location = MouseLocation(ObjC.Class("NSEvent"), ObjC.Sel("mouseLocation"));
        PopUp(menu, ObjC.Sel("popUpMenuPositioningItem:atLocation:inView:"), IntPtr.Zero, location, IntPtr.Zero);
    }

    private static IntPtr BuildMenu(IReadOnlyList<MacMenuEntry> entries, IntPtr target)
    {
        var menu = ObjC.Call(ObjC.Call(ObjC.Class("NSMenu"), "alloc"), "initWithTitle:", ObjC.NsString(""));
        SetBool(menu, ObjC.Sel("setAutoenablesItems:"), false);
        foreach (var entry in entries)
            ObjC.Call(menu, "addItem:", CreateItem(entry, target));
        return menu;
    }

    private static IntPtr CreateItem(MacMenuEntry entry, IntPtr target)
    {
        if (entry.Separator)
            return ObjC.Call(ObjC.Class("NSMenuItem"), "separatorItem");

        var item = ObjC.MsgSend(
            ObjC.Call(ObjC.Class("NSMenuItem"), "alloc"),
            ObjC.Sel("initWithTitle:action:keyEquivalent:"),
            ObjC.NsString(entry.Title),
            entry.Children is { Length: > 0 } ? IntPtr.Zero : ObjC.Sel("invoke:"),
            ObjC.NsString(""));
        ObjC.Call(item, "setTarget:", target);
        SetBool(item, ObjC.Sel("setEnabled:"), entry.Enabled);
        if (entry.Checked)
            SetTag(item, ObjC.Sel("setState:"), 1);

        if (entry.Dot is uint argb)
        {
            var image = DotImage(argb);
            if (image != IntPtr.Zero)
                ObjC.Call(item, "setImage:", image);
        }
        if (entry.Children is { Length: > 0 })
        {
            ObjC.Call(item, "setSubmenu:", BuildMenu(entry.Children, target));
            return item;
        }

        var tag = _actions.Count;
        _actions.Add(entry.Action);
        SetTag(item, ObjC.Sel("setTag:"), tag);
        return item;
    }

    private static void Invoke(IntPtr self, IntPtr cmd, IntPtr sender)
    {
        var tag = (int)ObjC.MsgSendNuint(sender, ObjC.Sel("tag"));
        if (tag < 0 || tag >= _actions.Count)
            return;
        var action = _actions[tag];
        if (action is null)
            return;
        Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
    }

    private static void OnTextBox(TextBox box, ContextRequestedEventArgs e)
    {
        if (e.Handled)
            return;
        e.Handled = true;
        Show(
        [
            new("Undo", box.Undo, box.CanUndo),
            new("Redo", box.Redo, box.CanRedo),
            new("", Separator: true),
            new("Cut", box.Cut, box.CanCut),
            new("Copy", box.Copy, box.CanCopy),
            new("Paste", box.Paste, box.CanPaste),
            new("", Separator: true),
            new("Select All", box.SelectAll, box.Text is { Length: > 0 }),
        ]);
    }

    private static void EnsureTarget()
    {
        if (_registered)
            return;
        _targetClass = objc_lookUpClass("MXMenuTarget");
        if (_targetClass == IntPtr.Zero)
        {
            _targetClass = objc_allocateClassPair(ObjC.Class("NSObject"), "MXMenuTarget", 0);
            class_addMethod(_targetClass, ObjC.Sel("invoke:"), Marshal.GetFunctionPointerForDelegate(InvokeImp), "v@:@");
            objc_registerClassPair(_targetClass);
        }

        _registered = true;
    }

    [DllImport(ObjC.Lib)]
    private static extern IntPtr objc_lookUpClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC.Lib)]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint extraBytes);

    [DllImport(ObjC.Lib)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

    [DllImport(ObjC.Lib)]
    private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(ObjC.Lib, EntryPoint = "objc_msgSend")]
    private static extern CGPoint MouseLocation(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC.Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool PopUp(IntPtr receiver, IntPtr selector, IntPtr item, CGPoint location, IntPtr view);

    [DllImport(ObjC.Lib, EntryPoint = "objc_msgSend")]
    private static extern void SetBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(ObjC.Lib, EntryPoint = "objc_msgSend")]
    private static extern void SetTag(IntPtr receiver, IntPtr selector, nint tag);


    private static IntPtr DotImage(uint argb)
    {
        var image = ObjC.MsgSend(ObjC.Call(ObjC.Class("NSImage"), "alloc"), ObjC.Sel("initWithSize:"), new NSSize(16, 16));
        if (image == IntPtr.Zero)
            return IntPtr.Zero;
        ObjC.Call(image, "lockFocus");
        var a = ((argb >> 24) & 255) / 255.0;
        if (a == 0)
            a = 1;
        var color = ObjC.MsgSend(
            ObjC.Class("NSColor"),
            ObjC.Sel("colorWithSRGBRed:green:blue:alpha:"),
            ((argb >> 16) & 255) / 255.0,
            ((argb >> 8) & 255) / 255.0,
            (argb & 255) / 255.0,
            a);
        ObjC.Call(color, "setFill");
        var path = ObjC.MsgSend(
            ObjC.Class("NSBezierPath"),
            ObjC.Sel("bezierPathWithOvalInRect:"),
            new NSRect(3, 3, 10, 10));
        ObjC.Call(path, "fill");
        ObjC.Call(image, "unlockFocus");
        SetBool(image, ObjC.Sel("setTemplate:"), false);
        return image;
    }
}
