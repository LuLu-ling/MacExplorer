using MacExplorer.Models;

namespace MacExplorer.Native;

/// <summary>Keeps AppKit chrome on the application theme instead of the system appearance.</summary>
internal static class MacAppearance
{
    public static void Apply(ThemeMode mode)
    {
        using var pool = new AutoreleasePool();
        ObjC.Call(App(), "setAppearance:", Named(mode));
    }

    public static void ApplyTo(IntPtr target)
    {
        if (target == IntPtr.Zero)
            return;
        ObjC.Call(target, "setAppearance:", ObjC.Call(App(), "effectiveAppearance"));
    }

    private static IntPtr App() => ObjC.Call(ObjC.Class("NSApplication"), "sharedApplication");

    private static IntPtr Named(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => ObjC.Call(ObjC.Class("NSAppearance"), "appearanceNamed:", ObjC.NsString("NSAppearanceNameAqua")),
        ThemeMode.Dark => ObjC.Call(ObjC.Class("NSAppearance"), "appearanceNamed:", ObjC.NsString("NSAppearanceNameDarkAqua")),
        _ => IntPtr.Zero
    };
}
