using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace MacExplorer.Native;

internal readonly record struct MacSettingsSnapshot(
    int Page,
    int Theme,
    int LanguageIndex,
    int Flags,
    string PageTitles,
    string ThemeLabel,
    string ThemeOptions,
    string LanguageLabel,
    string Languages,
    string FolderLabels,
    string AppName,
    string Version,
    string Description,
    uint CardArgb,
    uint StrokeArgb);

internal static class MacSettingsToggle
{
    public const int Hidden = 0;
    public const int Extensions = 1;
    public const int QuickAccess = 2;
    public const int Volumes = 3;
    public const int Recents = 4;

    public static int Pack(bool hidden, bool extensions, bool quickAccess, bool volumes, bool recents)
    {
        var flags = 0;
        if (hidden) flags |= 1 << Hidden;
        if (extensions) flags |= 1 << Extensions;
        if (quickAccess) flags |= 1 << QuickAccess;
        if (volumes) flags |= 1 << Volumes;
        if (recents) flags |= 1 << Recents;
        return flags;
    }
}

/// <summary>Owns one AppKit settings pane and forwards mutations to C#.</summary>
internal sealed class MacSettingsPane : IDisposable
{
    private const string Lib = "MacExplorerSettings";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void IntCallback(IntPtr context, int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ToggleCallback(IntPtr context, int id, int isOn);

    private static readonly IntCallback ThemeNative = OnThemeNative;
    private static readonly IntCallback LanguageNative = OnLanguageNative;
    private static readonly ToggleCallback ToggleNative = OnToggleNative;

    private IntPtr _handle;
    private GCHandle _self;
    private MacSettingsSnapshot _applied;
    private bool _disposed;

    static MacSettingsPane()
    {
        NativeLibrary.SetDllImportResolver(typeof(MacSettingsPane).Assembly, static (name, _, _) =>
        {
            if (name != Lib)
                return IntPtr.Zero;
            var path = Path.Combine(AppContext.BaseDirectory, "libMacExplorerSettings.dylib");
            return File.Exists(path) ? NativeLibrary.Load(path) : IntPtr.Zero;
        });
    }

    private MacSettingsPane() { }

    public IntPtr View => _handle;

    public event Action<int>? ThemeChanged;
    public event Action<int>? LanguageChanged;
    public event Action<int, bool>? ToggleChanged;

    public static MacSettingsPane Create()
    {
        var pane = new MacSettingsPane();
        pane._self = GCHandle.Alloc(pane);
        pane._handle = Native.MXSettingsCreate(
            GCHandle.ToIntPtr(pane._self), ThemeNative, LanguageNative, ToggleNative);
        if (pane._handle == IntPtr.Zero)
        {
            pane._self.Free();
            throw new InvalidOperationException("MXSettingsCreate returned null.");
        }

        return pane;
    }

    public void Apply(in MacSettingsSnapshot snapshot)
    {
        if (_disposed || _handle == IntPtr.Zero || snapshot == _applied)
            return;
        _applied = snapshot;
        using var utf8 = new Utf8(
            snapshot.PageTitles,
            snapshot.ThemeLabel,
            snapshot.ThemeOptions,
            snapshot.LanguageLabel,
            snapshot.Languages,
            snapshot.FolderLabels,
            snapshot.AppName,
            snapshot.Version,
            snapshot.Description);
        var payload = new Payload
        {
            Page = snapshot.Page,
            Theme = snapshot.Theme,
            LanguageIndex = snapshot.LanguageIndex,
            Flags = snapshot.Flags,
            CardArgb = snapshot.CardArgb,
            StrokeArgb = snapshot.StrokeArgb,
            PageTitles = utf8[0],
            ThemeLabel = utf8[1],
            ThemeOptions = utf8[2],
            LanguageLabel = utf8[3],
            Languages = utf8[4],
            FolderLabels = utf8[5],
            AppName = utf8[6],
            Version = utf8[7],
            Description = utf8[8]
        };
        Native.MXSettingsApply(_handle, ref payload);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ThemeChanged = null;
        LanguageChanged = null;
        ToggleChanged = null;
        if (_handle != IntPtr.Zero)
        {
            Native.MXSettingsRelease(_handle);
            _handle = IntPtr.Zero;
        }

        if (_self.IsAllocated)
            _self.Free();
    }

    private static MacSettingsPane? From(IntPtr context)
    {
        if (context == IntPtr.Zero)
            return null;
        try
        {
            var handle = GCHandle.FromIntPtr(context);
            return handle.IsAllocated ? handle.Target as MacSettingsPane : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void OnThemeNative(IntPtr context, int value) =>
        Dispatch(context, pane => pane.ThemeChanged?.Invoke(value));

    private static void OnLanguageNative(IntPtr context, int value) =>
        Dispatch(context, pane => pane.LanguageChanged?.Invoke(value));

    private static void OnToggleNative(IntPtr context, int id, int isOn) =>
        Dispatch(context, pane => pane.ToggleChanged?.Invoke(id, isOn != 0));

    private static void Dispatch(IntPtr context, Action<MacSettingsPane> action)
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
        public int Page, Theme, LanguageIndex, Flags;
        public uint CardArgb, StrokeArgb;
        public IntPtr PageTitles, ThemeLabel, ThemeOptions, LanguageLabel, Languages, FolderLabels, AppName, Version, Description;
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
        public static extern IntPtr MXSettingsCreate(
            IntPtr context, IntCallback themeChanged, IntCallback languageChanged, ToggleCallback toggleChanged);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void MXSettingsApply(IntPtr view, ref Payload data);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void MXSettingsRelease(IntPtr view);
    }
}
