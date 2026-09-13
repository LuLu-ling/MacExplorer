namespace MacExplorer.Native;

public readonly record struct MacFileResult(bool Ok, string? Error, string? ResultPath = null);

internal static class MacFileManager
{
    public static MacFileResult Copy(string source, string destination) =>
        Invoke("copyItemAtPath:toPath:error:", source, destination);

    public static MacFileResult Move(string source, string destination) =>
        Invoke("moveItemAtPath:toPath:error:", source, destination);

    public static MacFileResult Rename(string source, string destination) => Move(source, destination);

    public static MacFileResult Trash(string path)
    {
        using var pool = new AutoreleasePool();
        var error = IntPtr.Zero;
        var resulting = IntPtr.Zero;
        var ok = ObjC.MsgSendBool(Default(), ObjC.Sel("trashItemAtURL:resultingItemURL:error:"),
            ObjC.FileUrl(path), ref resulting, ref error);
        if (ok)
        {
            var resultPath = resulting == IntPtr.Zero ? null : ObjC.ToString(ObjC.Call(resulting, "path"));
            return new MacFileResult(true, null, resultPath);
        }

        return new MacFileResult(false, ObjC.NsErrorDescription(error));
    }

    public static MacFileResult Remove(string path)
    {
        using var pool = new AutoreleasePool();
        var error = IntPtr.Zero;
        var ok = ObjC.MsgSendBool(Default(), ObjC.Sel("removeItemAtPath:error:"), ObjC.NsString(path),
            ref error);
        return ok ? new MacFileResult(true, null) : new MacFileResult(false, ObjC.NsErrorDescription(error));
    }

    public static MacFileResult CreateDirectory(string path)
    {
        using var pool = new AutoreleasePool();
        var error = IntPtr.Zero;
        var ok = ObjC.MsgSendBool(Default(), ObjC.Sel("createDirectoryAtPath:withIntermediateDirectories:attributes:error:"),
            ObjC.NsString(path), (IntPtr)1, IntPtr.Zero, ref error);
        return ok ? new MacFileResult(true, null, path) : new MacFileResult(false, ObjC.NsErrorDescription(error));
    }

    public static MacFileResult CreateFile(string path)
    {
        using var pool = new AutoreleasePool();
        var data = ObjC.Call(ObjC.Class("NSData"), "data");
        var ok = ObjC.MsgSendBool(Default(), ObjC.Sel("createFileAtPath:contents:attributes:"),
            ObjC.NsString(path), data, IntPtr.Zero);
        return ok
            ? new MacFileResult(true, null, path)
            : new MacFileResult(false, "Unable to create the file.");
    }

    private static MacFileResult Invoke(string selector, string source, string destination)
    {
        using var pool = new AutoreleasePool();
        var error = IntPtr.Zero;
        var ok = ObjC.MsgSendBool(Default(), ObjC.Sel(selector), ObjC.NsString(source), ObjC.NsString(destination),
            ref error);
        return ok
            ? new MacFileResult(true, null, destination)
            : new MacFileResult(false, ObjC.NsErrorDescription(error));
    }

    private static IntPtr Default() => ObjC.Call(ObjC.Class("NSFileManager"), "defaultManager");
}
