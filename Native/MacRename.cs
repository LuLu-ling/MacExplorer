using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace MacExplorer.Native;

/// <summary>One SwiftUI filename field. Created only while a rename editor is open.</summary>
internal sealed class MacRenameField : IDisposable
{
    private const string Lib = MacSettingsLibrary.Name;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TextCallback(IntPtr context, IntPtr text);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DoneCallback(IntPtr context);

    private static readonly TextCallback TextNative = OnText;
    private static readonly DoneCallback CommitNative = OnCommit;
    private static readonly DoneCallback CancelNative = OnCancel;

    private readonly GCHandle _self;
    private IntPtr _view;
    private bool _disposed;

    static MacRenameField() => MacSettingsLibrary.Ensure();

    private MacRenameField() => _self = GCHandle.Alloc(this);

    public IntPtr View => _view;

    public event Action<string>? TextChanged;
    public event Action? CommitRequested;
    public event Action? CancelRequested;

    public static MacRenameField Open(string text, double fontSize, bool centered)
    {
        var field = new MacRenameField();
        var utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            field._view = Native.MXRenameCreate(
                GCHandle.ToIntPtr(field._self), utf8, fontSize, centered ? 1 : 0,
                TextNative, CommitNative, CancelNative);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }

        if (field._view != IntPtr.Zero)
            return field;
        field._self.Free();
        throw new InvalidOperationException("MXRenameCreate returned null.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_view != IntPtr.Zero)
        {
            Native.MXRenameSilence(_view);
            Native.MXRenameRelease(_view);
            _view = IntPtr.Zero;
        }

        if (_self.IsAllocated)
            _self.Free();
    }

    private static MacRenameField? From(IntPtr context) =>
        context == IntPtr.Zero ? null : GCHandle.FromIntPtr(context).Target as MacRenameField;

    private static void OnText(IntPtr context, IntPtr text)
    {
        var field = From(context);
        if (field is null || field._disposed)
            return;
        var value = text == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(text) ?? "";
        if (Dispatcher.UIThread.CheckAccess())
            field.TextChanged?.Invoke(value);
        else
            Dispatcher.UIThread.Post(() => field.TextChanged?.Invoke(value));
    }

    private static void OnCommit(IntPtr context) => Signal(context, static field => field.CommitRequested);

    private static void OnCancel(IntPtr context) => Signal(context, static field => field.CancelRequested);

    private static void Signal(IntPtr context, Func<MacRenameField, Action?> pick)
    {
        var field = From(context);
        if (field is null || field._disposed)
            return;
        var handler = pick(field);
        if (handler is null)
            return;
        Dispatcher.UIThread.Post(handler);
    }

    private static class Native
    {
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr MXRenameCreate(
            IntPtr context, IntPtr text, double fontSize, int centered,
            TextCallback changed, DoneCallback commit, DoneCallback cancel);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void MXRenameSilence(IntPtr view);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void MXRenameRelease(IntPtr view);
    }
}
