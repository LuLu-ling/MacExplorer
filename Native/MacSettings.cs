using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace MacExplorer.Native;

internal sealed class MacSettingsSnapshot
{
    public int Page { get; init; }
    public int Theme { get; init; }
    public int LanguageIndex { get; init; }
    public int Flags { get; init; }
    public string AppearanceTitle { get; init; } = "";
    public string LanguageTitle { get; init; } = "";
    public string FoldersTitle { get; init; } = "";
    public string AboutTitle { get; init; } = "";
    public string ThemeLabel { get; init; } = "";
    public string ThemeSystem { get; init; } = "";
    public string ThemeLight { get; init; } = "";
    public string ThemeDark { get; init; } = "";
    public string LanguageLabel { get; init; } = "";
    public string[] Languages { get; init; } = [];
    public string ShowHidden { get; init; } = "";
    public string ShowExtensions { get; init; } = "";
    public string ShowQuickAccess { get; init; } = "";
    public string ShowVolumes { get; init; } = "";
    public string ShowRecents { get; init; } = "";
    public string AppName { get; init; } = "";
    public string Version { get; init; } = "";
    public string Description { get; init; } = "";
}

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

/// <summary>Owns one Swift AppKit settings pane and forwards mutations to C#.</summary>
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

    public void Apply(MacSettingsSnapshot snapshot)
    {
        if (_disposed || _handle == IntPtr.Zero)
            return;
        using var appearanceTitle = new Utf8(snapshot.AppearanceTitle);
        using var languageTitle = new Utf8(snapshot.LanguageTitle);
        using var foldersTitle = new Utf8(snapshot.FoldersTitle);
        using var aboutTitle = new Utf8(snapshot.AboutTitle);
        using var themeLabel = new Utf8(snapshot.ThemeLabel);
        using var themeSystem = new Utf8(snapshot.ThemeSystem);
        using var themeLight = new Utf8(snapshot.ThemeLight);
        using var themeDark = new Utf8(snapshot.ThemeDark);
        using var languageLabel = new Utf8(snapshot.LanguageLabel);
        using var languages = new Utf8(string.Join('\n', snapshot.Languages));
        using var showHidden = new Utf8(snapshot.ShowHidden);
        using var showExtensions = new Utf8(snapshot.ShowExtensions);
        using var showQuickAccess = new Utf8(snapshot.ShowQuickAccess);
        using var showVolumes = new Utf8(snapshot.ShowVolumes);
        using var showRecents = new Utf8(snapshot.ShowRecents);
        using var appName = new Utf8(snapshot.AppName);
        using var version = new Utf8(snapshot.Version);
        using var description = new Utf8(snapshot.Description);
        Native.MXSettingsApply(
            _handle,
            snapshot.Page,
            snapshot.Theme,
            snapshot.LanguageIndex,
            snapshot.Flags,
            appearanceTitle.Ptr,
            languageTitle.Ptr,
            foldersTitle.Ptr,
            aboutTitle.Ptr,
            themeLabel.Ptr,
            themeSystem.Ptr,
            themeLight.Ptr,
            themeDark.Ptr,
            languageLabel.Ptr,
            languages.Ptr,
            showHidden.Ptr,
            showExtensions.Ptr,
            showQuickAccess.Ptr,
            showVolumes.Ptr,
            showRecents.Ptr,
            appName.Ptr,
            version.Ptr,
            description.Ptr);
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

    private readonly struct Utf8 : IDisposable
    {
        public IntPtr Ptr { get; }
        public Utf8(string? value) => Ptr = Marshal.StringToCoTaskMemUTF8(value ?? "");
        public void Dispose() => Marshal.FreeCoTaskMem(Ptr);
    }

    private static class Native
    {
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr MXSettingsCreate(
            IntPtr context, IntCallback themeChanged, IntCallback languageChanged, ToggleCallback toggleChanged);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void MXSettingsApply(
            IntPtr view,
            int page,
            int theme,
            int languageIndex,
            int flags,
            IntPtr appearanceTitle,
            IntPtr languageTitle,
            IntPtr foldersTitle,
            IntPtr aboutTitle,
            IntPtr themeLabel,
            IntPtr themeSystem,
            IntPtr themeLight,
            IntPtr themeDark,
            IntPtr languageLabel,
            IntPtr languages,
            IntPtr showHidden,
            IntPtr showExtensions,
            IntPtr showQuickAccess,
            IntPtr showVolumes,
            IntPtr showRecents,
            IntPtr appName,
            IntPtr version,
            IntPtr description);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void MXSettingsRelease(IntPtr view);
    }
}
