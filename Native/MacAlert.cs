using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace MacExplorer.Native;

internal enum MacAlertStyle
{
    Warning = 0,
    Informational = 1,
    Critical = 2
}

/// <summary>Bridges asynchronous NSAlert sheets without coupling view models to AppKit.</summary>
internal static class MacAlert
{
    private const nint FirstButtonResponse = 1000;
    private static readonly AlertDidEndImp AlertDidEnd = OnAlertDidEnd;
    private static IntPtr _targetClass;
    private static bool _targetRegistered;

    public static Task<int> ShowSheetAsync(
        Func<IntPtr> windowHandle,
        string title,
        string message,
        MacAlertStyle style,
        IReadOnlyList<string> buttons)
    {
        ArgumentNullException.ThrowIfNull(windowHandle);
        ArgumentNullException.ThrowIfNull(buttons);
        if (buttons.Count == 0)
            throw new ArgumentException("At least one alert button is required.", nameof(buttons));

        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Show()
        {
            try
            {
                var window = windowHandle();
                if (window == IntPtr.Zero)
                    throw new InvalidOperationException("The native window handle is unavailable.");
                ShowCore(window, title, message, style, buttons, completion);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
            Show();
        else
            Dispatcher.UIThread.Post(Show, DispatcherPriority.Normal);

        return completion.Task;
    }

    private static void ShowCore(
        IntPtr window,
        string title,
        string message,
        MacAlertStyle style,
        IReadOnlyList<string> buttons,
        TaskCompletionSource<int> completion)
    {
        using var pool = new AutoreleasePool();
        IntPtr alert = IntPtr.Zero;
        IntPtr target = IntPtr.Zero;
        PendingAlert? pending = null;
        var handle = default(GCHandle);
        try
        {
            EnsureTargetClass();
            alert = CreateAlert(title, message, style, buttons);
            if (alert == IntPtr.Zero)
                throw new InvalidOperationException("NSAlert could not be created.");

            target = ObjC.Call(ObjC.Call(_targetClass, "alloc"), "init");
            if (target == IntPtr.Zero)
                throw new InvalidOperationException("The native alert target could not be created.");

            pending = new PendingAlert(alert, target, buttons.Count, completion);
            handle = GCHandle.Alloc(pending);
            ObjC.MsgSend(
                alert,
                ObjC.Sel("beginSheetModalForWindow:modalDelegate:didEndSelector:contextInfo:"),
                window,
                target,
                ObjC.Sel("alertDidEnd:returnCode:contextInfo:"),
                GCHandle.ToIntPtr(handle));
        }
        catch (Exception exception)
        {
            if (handle.IsAllocated)
                handle.Free();
            if (pending is not null)
                pending.Dispose();
            else
            {
                if (target != IntPtr.Zero)
                    ObjC.Call(target, "release");
                if (alert != IntPtr.Zero)
                    ObjC.Call(alert, "release");
            }

            completion.TrySetException(exception);
        }
    }

    private static IntPtr CreateAlert(
        string title,
        string message,
        MacAlertStyle style,
        IReadOnlyList<string> buttons)
    {
        var alert = ObjC.Call(ObjC.Call(ObjC.Class("NSAlert"), "alloc"), "init");
        if (alert == IntPtr.Zero)
            return IntPtr.Zero;

        ObjC.Call(alert, "setMessageText:", ObjC.NsString(title));
        ObjC.Call(alert, "setInformativeText:", ObjC.NsString(message));
        ObjC.Call(alert, "setAlertStyle:", (IntPtr)style);
        foreach (var button in buttons)
            ObjC.Call(alert, "addButtonWithTitle:", ObjC.NsString(button));
        return alert;
    }

    private static void OnAlertDidEnd(
        IntPtr self,
        IntPtr command,
        IntPtr alert,
        nint returnCode,
        IntPtr contextInfo)
    {
        using var pool = new AutoreleasePool();
        if (contextInfo == IntPtr.Zero)
            return;

        var handle = GCHandle.FromIntPtr(contextInfo);
        try
        {
            if (handle.Target is PendingAlert pending)
                pending.Complete(returnCode);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void EnsureTargetClass()
    {
        if (_targetRegistered)
            return;

        _targetClass = objc_lookUpClass("MXAlertTarget");
        if (_targetClass == IntPtr.Zero)
        {
            _targetClass = objc_allocateClassPair(ObjC.Class("NSObject"), "MXAlertTarget", 0);
            if (_targetClass == IntPtr.Zero)
                throw new InvalidOperationException("The native alert target class could not be created.");
            if (!class_addMethod(
                    _targetClass,
                    ObjC.Sel("alertDidEnd:returnCode:contextInfo:"),
                    Marshal.GetFunctionPointerForDelegate(AlertDidEnd),
                    "v@:@q^v"))
                throw new InvalidOperationException("The native alert callback could not be registered.");
            objc_registerClassPair(_targetClass);
        }

        _targetRegistered = true;
    }

    private sealed class PendingAlert(
        IntPtr alert,
        IntPtr target,
        int buttonCount,
        TaskCompletionSource<int> completion)
    {
        private int _completed;
        private int _disposed;

        public void Complete(nint returnCode)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
                return;

            Dispose();
            var selected = returnCode >= FirstButtonResponse &&
                           returnCode < FirstButtonResponse + buttonCount
                ? (int)(returnCode - FirstButtonResponse)
                : -1;
            completion.TrySetResult(selected);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            if (alert != IntPtr.Zero)
                ObjC.Call(alert, "release");
            if (target != IntPtr.Zero)
                ObjC.Call(target, "release");
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AlertDidEndImp(
        IntPtr self,
        IntPtr command,
        IntPtr alert,
        nint returnCode,
        IntPtr contextInfo);

    [DllImport(ObjC.Lib)]
    private static extern IntPtr objc_lookUpClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC.Lib)]
    private static extern IntPtr objc_allocateClassPair(
        IntPtr superclass,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        nint extraBytes);

    [DllImport(ObjC.Lib)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(
        IntPtr cls,
        IntPtr selector,
        IntPtr implementation,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

    [DllImport(ObjC.Lib)]
    private static extern void objc_registerClassPair(IntPtr cls);
}
