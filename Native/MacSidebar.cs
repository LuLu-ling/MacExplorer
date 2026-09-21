using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace MacExplorer.Native;

internal readonly record struct MacSidebarSnapshot(
    string Rows,
    string FooterTitle,
    string RenamingId,
    string RenameText,
    uint SelectArgb,
    uint HoverArgb,
    uint AccentArgb,
    uint SecondaryArgb);

internal sealed class MacSidebarPane : IDisposable
{
    private const string Lib = "MacExplorerSettings";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StrCb(IntPtr context, IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MoveCb(IntPtr context, int from, int to);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DropCb(IntPtr context, IntPtr id, IntPtr paths, int modifiers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RenameCb(IntPtr context, IntPtr id, IntPtr text);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VoidCb(IntPtr context);

    private static readonly StrCb SelectNative = OnSelectNative;
    private static readonly MoveCb MoveNative = OnMoveNative;
    private static readonly StrCb ContextNative = OnContextNative;
    private static readonly DropCb DropNative = OnDropNative;
    private static readonly StrCb HoverNative = OnHoverNative;
    private static readonly RenameCb RenameNative = OnRenameNative;
    private static readonly VoidCb SettingsNative = OnSettingsNative;

    private IntPtr _handle;
    private GCHandle _self;
    private MacSidebarSnapshot _applied;
    private bool _disposed;

    static MacSidebarPane() => NativeLib.Touch();

    public static void Warmup() => Native.MXSidebarWarmup();

    private MacSidebarPane() { }

    public IntPtr View => _handle;

    public event Action<string>? Selected;
    public event Action<int, int>? Moved;
    public event Action<string>? Context;
    public event Action<string, string, int>? Dropped;
    public event Action<string>? Hovered;
    public event Action<string, string>? Renamed;
    public event Action? Settings;

    public static MacSidebarPane Create()
    {
        var pane = new MacSidebarPane();
        pane._self = GCHandle.Alloc(pane);
        pane._handle = Native.MXSidebarCreate(
            GCHandle.ToIntPtr(pane._self),
            SelectNative, MoveNative, ContextNative, DropNative, HoverNative, RenameNative, SettingsNative);
        if (pane._handle == IntPtr.Zero)
        {
            pane._self.Free();
            throw new InvalidOperationException("MXSidebarCreate returned null.");
        }

        return pane;
    }

    public void Apply(in MacSidebarSnapshot snapshot)
    {
        if (_disposed || _handle == IntPtr.Zero || snapshot == _applied)
            return;
        _applied = snapshot;
        using var utf8 = new Utf8(snapshot.Rows, snapshot.FooterTitle, snapshot.RenamingId, snapshot.RenameText);
        var payload = new Payload
        {
            SelectArgb = snapshot.SelectArgb,
            HoverArgb = snapshot.HoverArgb,
            AccentArgb = snapshot.AccentArgb,
            SecondaryArgb = snapshot.SecondaryArgb,
            Rows = utf8[0],
            FooterTitle = utf8[1],
            RenamingId = utf8[2],
            RenameText = utf8[3]
        };
        Native.MXSidebarApply(_handle, ref payload);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Selected = null;
        Moved = null;
        Context = null;
        Dropped = null;
        Hovered = null;
        Renamed = null;
        Settings = null;
        if (_handle != IntPtr.Zero)
        {
            Native.MXSidebarRelease(_handle);
            _handle = IntPtr.Zero;
        }

        if (_self.IsAllocated)
            _self.Free();
    }

    private static MacSidebarPane? From(IntPtr context)
    {
        if (context == IntPtr.Zero)
            return null;
        try
        {
            var handle = GCHandle.FromIntPtr(context);
            return handle.IsAllocated ? handle.Target as MacSidebarPane : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void OnSelectNative(IntPtr context, IntPtr value)
    {
        var id = Read(value);
        Dispatch(context, pane => pane.Selected?.Invoke(id));
    }

    private static void OnMoveNative(IntPtr context, int from, int to) =>
        Dispatch(context, pane => pane.Moved?.Invoke(from, to));

    private static void OnContextNative(IntPtr context, IntPtr value)
    {
        var id = Read(value);
        Dispatch(context, pane => pane.Context?.Invoke(id));
    }

    private static void OnDropNative(IntPtr context, IntPtr id, IntPtr paths, int modifiers)
    {
        var item = Read(id);
        var list = Read(paths);
        Dispatch(context, pane => pane.Dropped?.Invoke(item, list, modifiers));
    }

    private static void OnHoverNative(IntPtr context, IntPtr value)
    {
        var id = Read(value);
        Dispatch(context, pane => pane.Hovered?.Invoke(id));
    }

    private static void OnRenameNative(IntPtr context, IntPtr id, IntPtr text)
    {
        var item = Read(id);
        var value = Read(text);
        Dispatch(context, pane => pane.Renamed?.Invoke(item, value));
    }

    private static void OnSettingsNative(IntPtr context) =>
        Dispatch(context, pane => pane.Settings?.Invoke());

    private static string Read(IntPtr ptr) => ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";

    private static void Dispatch(IntPtr context, Action<MacSidebarPane> action)
    {
        if (From(context) is not { } pane)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!pane._disposed)
                action(pane);
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Payload
    {
        public uint SelectArgb, HoverArgb, AccentArgb, SecondaryArgb;
        public IntPtr Rows, FooterTitle, RenamingId, RenameText;
    }

    private readonly struct Utf8 : IDisposable
    {
        private readonly IntPtr[] _ptrs;
        public Utf8(params string[] values)
        {
            _ptrs = new IntPtr[values.Length];
            for (var i = 0; i < values.Length; i++)
                _ptrs[i] = Marshal.StringToCoTaskMemUTF8(values[i]);
        }

        public IntPtr this[int i] => _ptrs[i];

        public void Dispose()
        {
            foreach (var ptr in _ptrs)
                Marshal.FreeCoTaskMem(ptr);
        }
    }

    private static class Native
    {
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr MXSidebarCreate(
            IntPtr context, StrCb select, MoveCb move, StrCb contextMenu, DropCb drop, StrCb hover, RenameCb rename, VoidCb settings);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void MXSidebarApply(IntPtr view, ref Payload data);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void MXSidebarRelease(IntPtr view);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void MXSidebarWarmup();
    }
}
