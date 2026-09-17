using System.Runtime.InteropServices;

namespace MacExplorer.Native;

internal readonly record struct NSSize(double Width, double Height);

internal readonly record struct NSRect(double X, double Y, double Width, double Height);

internal sealed class AutoreleasePool : IDisposable
{
    private IntPtr _pool = objc_autoreleasePoolPush();

    public void Dispose()
    {
        if (_pool == IntPtr.Zero)
            return;
        objc_autoreleasePoolPop(_pool);
        _pool = IntPtr.Zero;
    }

    [DllImport(ObjC.Lib)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjC.Lib)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);
}

internal static class ObjC
{
    internal const string Lib = "/usr/lib/libobjc.A.dylib";

    [DllImport(Lib)]
    public static extern IntPtr objc_getClass(string name);

    [DllImport(Lib)]
    public static extern IntPtr sel_registerName(string name);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector, IntPtr a1);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector, IntPtr a1, IntPtr a2);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector, IntPtr a1, IntPtr a2, IntPtr a3);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector, IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector, IntPtr a1, IntPtr a2, ref IntPtr a3);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector, IntPtr a1, ref IntPtr error);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool MsgSendBool(IntPtr receiver, IntPtr selector);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool MsgSendBool(IntPtr receiver, IntPtr selector, IntPtr a1);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool MsgSendBool(IntPtr receiver, IntPtr selector, IntPtr a1, IntPtr a2);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool MsgSendBool(IntPtr receiver, IntPtr selector, IntPtr a1, IntPtr a2, IntPtr a3);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool MsgSendBool(IntPtr receiver, IntPtr selector, IntPtr a1, ref IntPtr error);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool MsgSendBool(IntPtr receiver, IntPtr selector, IntPtr a1, IntPtr a2, ref IntPtr error);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool MsgSendBool(IntPtr receiver, IntPtr selector, IntPtr a1, IntPtr a2, IntPtr a3, ref IntPtr error);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool MsgSendBool(IntPtr receiver, IntPtr selector, IntPtr a1, ref IntPtr a2, ref IntPtr error);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void MsgSendVoid(IntPtr receiver, IntPtr selector, NSSize size);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void MsgSendVoid(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector, nuint type, IntPtr properties);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern nuint MsgSendNuint(IntPtr receiver, IntPtr selector);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector, NSSize size);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector, NSRect rect);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector, double a, double b, double c, double d);

    public static IntPtr Class(string name) => objc_getClass(name);

    public static IntPtr Sel(string name) => sel_registerName(name);

    public static IntPtr Call(IntPtr receiver, string selector) => MsgSend(receiver, Sel(selector));

    public static IntPtr Call(IntPtr receiver, string selector, IntPtr a1) => MsgSend(receiver, Sel(selector), a1);

    public static IntPtr Call(IntPtr receiver, string selector, IntPtr a1, IntPtr a2) =>
        MsgSend(receiver, Sel(selector), a1, a2);

    public static IntPtr NsString(string value)
    {
        var cls = Class("NSString");
        return MsgSend(cls, Sel("stringWithUTF8String:"), value);
    }

    public static string? ToString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
            return null;
        var utf8 = Call(nsString, "UTF8String");
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }

    public static string NsErrorDescription(IntPtr error)
    {
        if (error == IntPtr.Zero)
            return "Unknown error";
        return ToString(Call(error, "localizedDescription")) ?? "Unknown error";
    }

    public static IntPtr FileUrl(string path)
    {
        var nsPath = NsString(path);
        return Call(Class("NSURL"), "fileURLWithPath:", nsPath);
    }

    public static IntPtr MutableArray(int capacity = 8)
    {
        return Call(Class("NSMutableArray"), "arrayWithCapacity:", (IntPtr)capacity);
    }

    public static void AddObject(IntPtr array, IntPtr item) => Call(array, "addObject:", item);

    public static int ArrayCount(IntPtr array) => (int)MsgSendNuint(array, Sel("count"));

    public static IntPtr ArrayAt(IntPtr array, int index) => Call(array, "objectAtIndex:", (IntPtr)index);

    public static byte[]? NsDataToBytes(IntPtr data)
    {
        if (data == IntPtr.Zero)
            return null;
        var length = (int)MsgSendNuint(data, Sel("length"));
        if (length <= 0)
            return null;
        var ptr = Call(data, "bytes");
        var buffer = new byte[length];
        Marshal.Copy(ptr, buffer, 0, length);
        return buffer;
    }

    public static IntPtr NsData(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return Call(Class("NSData"), "data");
        unsafe
        {
            fixed (byte* ptr = bytes)
                return MsgSend(Class("NSData"), Sel("dataWithBytes:length:"), (IntPtr)ptr, (IntPtr)bytes.Length);
        }
    }

    public static void SetBool(IntPtr receiver, string selector, bool value) =>
        MsgSendVoid(receiver, Sel(selector), value);
}
