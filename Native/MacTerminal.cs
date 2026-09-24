using MacExplorer.Logging;
using MacExplorer.Models;
namespace MacExplorer.Native;

internal static class MacTerminal
{
    private const string FallbackApp = "/System/Applications/Utilities/Terminal.app";
    private const string ShellScriptType = "com.apple.terminal.shell-script";

    public static bool CanOpen(string? path) =>
        path is { Length: > 0 } && !SpecialFolders.IsVirtual(path) && Directory.Exists(path);

    public static string? DefaultApp()
    {
        using var pool = new AutoreleasePool();
        var command = HandlerForType(ShellScriptType);
        var ssh = HandlerForUrl("ssh://localhost");
        var shell = HandlerForType("public.shell-script");
        var shellTerminal = IsTerminalApp(shell);

        string? chosen;
        string via;
        if (Chosen(command) is { } commandApp)
            (chosen, via) = (commandApp, ".command");
        else if (Chosen(ssh) is { } sshApp)
            (chosen, via) = (sshApp, "ssh");
        else if (shellTerminal && Chosen(shell) is { } shellApp)
            (chosen, via) = (shellApp, ".sh");
        else
            (chosen, via) = (command ?? ssh ?? Existing(FallbackApp), "fallback");

        LogWrapper.Debug("Terminal",
            $"scan .command={Label(command)} ssh={Label(ssh)} .sh={Label(shell)} shellTerminal={shellTerminal} -> {Label(chosen)} via {via}");
        return chosen;
    }

    public static void Open(string path)
    {
        if (!CanOpen(path))
            return;
        var app = DefaultApp() ?? FallbackApp;
        if (!MacOpenWith.Open([path], app) && !string.Equals(app, FallbackApp, StringComparison.Ordinal))
            MacOpenWith.Open([path], FallbackApp);
    }

    private static string? Chosen(string? path) =>
        path is { Length: > 0 } && !IsStockTerminal(path) ? path : null;

    private static bool IsStockTerminal(string path) =>
        string.Equals(path, FallbackApp, StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminalApp(string? path)
    {
        if (Existing(path) is not { } app || IsStockTerminal(app))
            return false;
        var info = ObjC.Call(ObjC.Class("NSDictionary"), "dictionaryWithContentsOfFile:",
            ObjC.NsString(Path.Combine(app, "Contents", "Info.plist")));
        return info != IntPtr.Zero &&
            (Claims(info, "CFBundleDocumentTypes", "CFBundleTypeRole", "Shell") ||
             Claims(info, "CFBundleDocumentTypes", "LSItemContentTypes", ShellScriptType) ||
             Claims(info, "CFBundleURLTypes", "CFBundleURLSchemes", "ssh"));
    }

    private static bool Claims(IntPtr info, string arrayKey, string field, string expected)
    {
        var items = ObjC.Call(info, "objectForKey:", ObjC.NsString(arrayKey));
        if (!IsKind(items, "NSArray"))
            return false;
        var count = ObjC.ArrayCount(items);
        for (var i = 0; i < count; i++)
        {
            var value = ObjC.Call(ObjC.ArrayAt(items, i), "objectForKey:", ObjC.NsString(field));
            if (Matches(value, expected))
                return true;
        }
        return false;
    }

    private static bool Matches(IntPtr value, string expected)
    {
        if (IsKind(value, "NSString"))
            return string.Equals(ObjC.ToString(value), expected, StringComparison.OrdinalIgnoreCase);
        if (!IsKind(value, "NSArray"))
            return false;
        var count = ObjC.ArrayCount(value);
        for (var i = 0; i < count; i++)
        {
            var item = ObjC.ArrayAt(value, i);
            if (IsKind(item, "NSString") &&
                string.Equals(ObjC.ToString(item), expected, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsKind(IntPtr value, string className) =>
        value != IntPtr.Zero && ObjC.MsgSendBool(value, ObjC.Sel("isKindOfClass:"), ObjC.Class(className));

    private static string? HandlerForUrl(string url)
    {
        var target = ObjC.Call(ObjC.Class("NSURL"), "URLWithString:", ObjC.NsString(url));
        return target == IntPtr.Zero
            ? null
            : AppPath(ObjC.Call(Workspace(), "URLForApplicationToOpenURL:", target));
    }

    private static string? HandlerForType(string identifier)
    {
        var type = ObjC.Call(ObjC.Class("UTType"), "typeWithIdentifier:", ObjC.NsString(identifier));
        return type == IntPtr.Zero
            ? null
            : AppPath(ObjC.Call(Workspace(), "URLForApplicationToOpenContentType:", type));
    }

    private static string? AppPath(IntPtr url)
    {
        if (url == IntPtr.Zero)
            return null;
        return Existing(ObjC.ToString(ObjC.Call(url, "path")));
    }

    private static string? Existing(string? path) =>
        path is { Length: > 0 } && Directory.Exists(path) ? path : null;

    private static string Label(string? path) =>
        string.IsNullOrEmpty(path) ? "none" : Path.GetFileName(path);

    private static IntPtr Workspace() => ObjC.Call(ObjC.Class("NSWorkspace"), "sharedWorkspace");
}
