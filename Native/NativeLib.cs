using System.Runtime.InteropServices;

namespace MacExplorer.Native;

internal static class NativeLib
{
    public const string Name = "MacExplorerSettings";

    static NativeLib()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLib).Assembly, static (name, _, _) =>
        {
            if (name != Name)
                return IntPtr.Zero;
            var path = Path.Combine(AppContext.BaseDirectory, "libMacExplorerSettings.dylib");
            return File.Exists(path) ? NativeLibrary.Load(path) : IntPtr.Zero;
        });
    }

    public static void Touch() { }
}
