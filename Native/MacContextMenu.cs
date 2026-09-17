using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacExplorer.Localization;

namespace MacExplorer.Native;

internal readonly record struct MacMenuEntry(
    string Title,
    Action? Action = null,
    bool Enabled = true,
    MacMenuEntry[]? Children = null,
    bool Separator = false,
    bool Checked = false,
    uint? Dot = null,
    string? Icon = null);

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
    private static IntPtr _openMenu;
    private static Control? _anchor;

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

    public static void ShowAt(Control anchor, IReadOnlyList<MacMenuEntry> entries, Func<bool>? isCurrent = null)
    {
        Install();
        Dispatcher.UIThread.Post(() =>
        {
            if (anchor.IsEffectivelyVisible && anchor.IsEffectivelyEnabled &&
                TopLevel.GetTopLevel(anchor) is not null && (isCurrent?.Invoke() ?? true))
                ShowCore(entries, anchor);
        }, DispatcherPriority.Input);
    }

    public static void Close(Control owner)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_openMenu != IntPtr.Zero && _anchor is not null &&
            (owner == _anchor || owner.IsVisualAncestorOf(_anchor)))
            ObjC.Call(_openMenu, "cancelTracking");
    }

    private static void ShowCore(IReadOnlyList<MacMenuEntry> entries, Control? anchor = null)
    {
        if (_openMenu != IntPtr.Zero)
            return;
        using var pool = new AutoreleasePool();
        EnsureTarget();
        _actions.Clear();
        var target = ObjC.Call(ObjC.Call(_targetClass, "new"), "autorelease");
        try
        {
            var menu = BuildMenu(entries, target);
            var view = IntPtr.Zero;
            CGPoint location;
            if (anchor is null)
                location = MouseLocation(ObjC.Class("NSEvent"), ObjC.Sel("mouseLocation"));
            else
            {
                if (TopLevel.GetTopLevel(anchor) is not { } topLevel)
                    return;
                var point = anchor.TranslatePoint(new Point(0, anchor.Bounds.Height), topLevel);
                var handle = topLevel.TryGetPlatformHandle();
                view = handle?.HandleDescriptor switch
                {
                    "NSWindow" => ObjC.Call(handle.Handle, "contentView"),
                    "NSView" => handle.Handle,
                    _ => IntPtr.Zero
                };
                if (view == IntPtr.Zero || point is null)
                    return;
                location = new CGPoint
                {
                    X = point.Value.X,
                    Y = ObjC.MsgSendBool(view, ObjC.Sel("isFlipped"))
                        ? point.Value.Y : topLevel.ClientSize.Height - point.Value.Y
                };
            }
            _openMenu = menu;
            _anchor = anchor;
            PopUp(menu, ObjC.Sel("popUpMenuPositioningItem:atLocation:inView:"), IntPtr.Zero, location, view);
        }
        finally
        {
            _actions.Clear();
            _openMenu = IntPtr.Zero;
            _anchor = null;
        }
    }

    private static IntPtr BuildMenu(IReadOnlyList<MacMenuEntry> entries, IntPtr target)
    {
        var menu = ObjC.Call(ObjC.Call(ObjC.Class("NSMenu"), "alloc"), "initWithTitle:", ObjC.NsString(""));
        ObjC.Call(menu, "autorelease");
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
        ObjC.Call(item, "autorelease");
        ObjC.Call(item, "setTarget:", target);
        SetBool(item, ObjC.Sel("setEnabled:"), entry.Enabled);
        if (entry.Checked)
            SetTag(item, ObjC.Sel("setState:"), 1);

        if (!string.IsNullOrEmpty(entry.Icon))
        {
            var image = FileImage(entry.Icon);
            if (image != IntPtr.Zero)
                ObjC.Call(item, "setImage:", image);
        }
        else if (entry.Dot is uint argb)
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
            new(Lang.Text("Common.Action.Undo"), box.Undo, box.CanUndo),
            new(Lang.Text("Common.Action.Redo"), box.Redo, box.CanRedo),
            new("", Separator: true),
            new(Lang.Text("Common.Action.Cut"), box.Cut, box.CanCut),
            new(Lang.Text("Common.Action.Copy"), box.Copy, box.CanCopy),
            new(Lang.Text("Common.Action.Paste"), box.Paste, box.CanPaste),
            new("", Separator: true),
            new(Lang.Text("Common.Action.SelectAll"), box.SelectAll, box.Text is { Length: > 0 }),
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
        ObjC.Call(image, "autorelease");
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

    private static IntPtr FileImage(string path)
    {
        var source = ObjC.Call(ObjC.Call(ObjC.Class("NSWorkspace"), "sharedWorkspace"),
            "iconForFile:", ObjC.NsString(path));
        if (source == IntPtr.Zero)
            return IntPtr.Zero;
        var image = ObjC.Call(source, "copy");
        if (image == IntPtr.Zero)
            return IntPtr.Zero;
        ObjC.Call(image, "autorelease");
        ObjC.MsgSendVoid(image, ObjC.Sel("setSize:"), new NSSize(16, 16));
        return image;
    }
}
