using MacExplorer.Infrastructure;
using MacExplorer.Logging;
using MacExplorer.Models;

namespace MacExplorer.Native;

internal static class MacTerminal
{
    internal const string BuiltInApp = "/System/Applications/Utilities/Terminal.app";
    private const string UnixExecutable = "public.unix-executable";

    internal readonly record struct Choice(string Path, string Title);

    public static bool CanOpen(string? path) =>
        path is { Length: > 0 } && !SpecialFolders.IsVirtual(path) && Directory.Exists(path);

    public static string? DefaultApp()
    {
        var app = Configured() ?? Existing(BuiltInApp);
        LogWrapper.Debug("Terminal", $"app={Label(app)} configured={Label(Config.Files.Terminal)}");
        return app;
    }

    public static IReadOnlyList<Choice> Choices()
    {
        using var pool = new AutoreleasePool();
        var type = ObjC.Call(ObjC.Class("UTType"), "typeWithIdentifier:", ObjC.NsString(UnixExecutable));
        var listed = type == IntPtr.Zero
            ? IntPtr.Zero
            : ObjC.Call(Workspace(), "URLsForApplicationsToOpenContentType:", type);
        var count = listed == IntPtr.Zero ? 0 : ObjC.ArrayCount(listed);
        var apps = new List<Choice>(count);
        for (var i = 0; i < count; i++)
        {
            if (AppPath(ObjC.ArrayAt(listed, i)) is not { } path || !DeclaresShellExecutable(path))
                continue;
            apps.Add(new(path, DisplayName(path)));
        }

        apps.Sort(static (a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
        return apps;
    }

    public static void Open(string path)
    {
        if (!CanOpen(path))
            return;
        var preferred = Configured();
        if (preferred is not null && MacOpenWith.Open([path], preferred))
            return;
        if (preferred is null || !Same(preferred, BuiltInApp))
            MacOpenWith.Open([path], BuiltInApp);
    }

    private static string? Configured()
    {
        var saved = Config.Files.Terminal;
        if (string.IsNullOrEmpty(saved))
            return null;
        foreach (var choice in Choices())
        {
            if (Same(choice.Path, saved))
                return choice.Path;
        }

        return null;
    }

    private static bool DeclaresShellExecutable(string path)
    {
        var info = ObjC.Call(ObjC.Class("NSDictionary"), "dictionaryWithContentsOfFile:",
            ObjC.NsString(Path.Combine(path, "Contents", "Info.plist")));
        if (!IsKind(info, "NSDictionary"))
            return false;
        var docs = ObjC.Call(info, "objectForKey:", ObjC.NsString("CFBundleDocumentTypes"));
        if (!IsKind(docs, "NSArray"))
            return false;
        var count = ObjC.ArrayCount(docs);
        for (var i = 0; i < count; i++)
        {
            var doc = ObjC.ArrayAt(docs, i);
            if (!IsKind(doc, "NSDictionary"))
                continue;
            var role = ObjC.Call(doc, "objectForKey:", ObjC.NsString("CFBundleTypeRole"));
            var types = ObjC.Call(doc, "objectForKey:", ObjC.NsString("LSItemContentTypes"));
            if (Matches(role, "Shell") && Matches(types, UnixExecutable))
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

    private static string? AppPath(IntPtr url)
    {
        if (url == IntPtr.Zero)
            return null;
        return Existing(ObjC.ToString(ObjC.Call(url, "path")));
    }

    private static string DisplayName(string path) =>
        ObjC.ToString(ObjC.Call(ObjC.Call(ObjC.Class("NSFileManager"), "defaultManager"),
            "displayNameAtPath:", ObjC.NsString(path)))
        ?? Path.GetFileNameWithoutExtension(path);

    private static string? Existing(string? path) =>
        path is { Length: > 0 } && Directory.Exists(path) ? path : null;

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Label(string? path) =>
        string.IsNullOrEmpty(path) ? "none" : Path.GetFileName(path);

    private static IntPtr Workspace() => ObjC.Call(ObjC.Class("NSWorkspace"), "sharedWorkspace");
}
