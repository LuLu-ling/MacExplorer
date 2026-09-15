using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace MacExplorer.Services;

internal static class FolderSize
{
    private const int Workers = 2;
    private const int QosUtility = 0x11;
    private const int CacheCap = 4096;
    private const int BufferSize = 256 * 1024;
    private const int ORdOnly = 0;
    private const int ONoFollow = 0x100;
    private const int ODirectory = 0x100000;
    private const int OCloExec = 0x01000000;
    private const ushort AttrBitmapCount = 5;
    private const uint AttrCmnName = 0x00000001;
    private const uint AttrCmnObjType = 0x00000008;
    private const uint AttrCmnError = 0x20000000;
    private const uint AttrCmnReturnedAttrs = 0x80000000;
    private const uint AttrFileDataLength = 0x00000200;
    private const uint VDir = 2;

    private static readonly ConcurrentDictionary<string, long> Cache = new(StringComparer.Ordinal);
    private static readonly BlockingCollection<Work> Queue = new(new ConcurrentQueue<Work>());
    private static int _started;

    public static long Of(string path, CancellationToken token = default) =>
        ComputeAsync(path, token).GetAwaiter().GetResult();

    public static Task<long> ComputeAsync(string path, CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
            return Task.FromCanceled<long>(token);
        if (Cache.TryGetValue(path, out var cached))
            return Task.FromResult(cached);

        var job = new Job(path, token);
        Queue.Add(new Work(job, path, Follow: true));
        EnsureWorkers();
        return job.Completion.Task.WaitAsync(token);
    }

    public static void Invalidate(string path)
    {
        if (!string.IsNullOrEmpty(path))
            Cache.TryRemove(path, out _);
    }

    private static void EnsureWorkers()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;
        for (var i = 0; i < Workers; i++)
        {
            new Thread(Worker)
            {
                IsBackground = true,
                Name = "FolderSize"
            }.Start();
        }
    }

    private static void Worker()
    {
        pthread_set_qos_class_self_np(QosUtility, 0);
        var buffer = new byte[BufferSize];
        var subs = new List<string>();
        foreach (var work in Queue.GetConsumingEnumerable())
            Process(work, buffer, subs);
    }

    private static void Process(Work work, byte[] buffer, List<string> subs)
    {
        var job = work.Job;
        try
        {
            if (!job.Token.IsCancellationRequested)
                Walk(job, work.Path, work.Follow, buffer, subs);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Complete(job);
        }
    }

    private static unsafe void Walk(Job job, string path, bool follow, byte[] buffer, List<string> subs)
    {
        var flags = ORdOnly | ODirectory | OCloExec;
        if (!follow)
            flags |= ONoFollow;
        var fd = open(path, flags);
        if (fd < 0)
            return;

        subs.Clear();
        try
        {
            var list = new AttrList
            {
                BitmapCount = AttrBitmapCount,
                Common = AttrCmnReturnedAttrs | AttrCmnName | AttrCmnError | AttrCmnObjType,
                File = AttrFileDataLength
            };
            fixed (byte* buf = buffer)
            {
                while (!job.Token.IsCancellationRequested)
                {
                    var n = getattrlistbulk(fd, ref list, buf, (nuint)buffer.Length, 0);
                    if (n <= 0)
                        break;
                    Parse(job, path, buf, n, buffer.Length, subs);
                }
            }
        }
        finally
        {
            close(fd);
        }

        if (job.Token.IsCancellationRequested)
            return;
        foreach (var child in subs)
        {
            Interlocked.Increment(ref job.Pending);
            Queue.Add(new Work(job, child, Follow: false));
        }
    }

    private static unsafe void Parse(Job job, string parent, byte* buf, int count, int bufLen, List<string> subs)
    {
        var p = buf;
        var end = buf + bufLen;
        for (var i = 0; i < count; i++)
        {
            if (p + 8 > end)
                break;
            var length = *(uint*)p;
            if (length < 8)
                break;
            var record = p;
            var next = p + length;
            if (next > end || next <= record)
                break;
            var field = p + 4;
            var returned = *(AttributeSet*)field;
            field += sizeof(AttributeSet);

            var error = 0u;
            if ((returned.Common & AttrCmnError) != 0)
            {
                error = *(uint*)field;
                field += 4;
            }

            byte* namePtr = null;
            if ((returned.Common & AttrCmnName) != 0)
            {
                var nameRef = *(AttrReference*)field;
                namePtr = field + nameRef.DataOffset;
                field += sizeof(AttrReference);
            }

            if (error == 0)
            {
                var objType = 0u;
                if ((returned.Common & AttrCmnObjType) != 0)
                {
                    objType = *(uint*)field;
                    field += 4;
                }

                long size = 0;
                if ((returned.File & AttrFileDataLength) != 0)
                    size = *(long*)field;

                if (objType == VDir)
                {
                    if (namePtr is not null && namePtr < next && !IsDot(namePtr))
                    {
                        var name = Marshal.PtrToStringUTF8((IntPtr)namePtr);
                        if (!string.IsNullOrEmpty(name) && name.IndexOf('/') < 0)
                            subs.Add(Join(parent, name));
                    }
                }
                else if (size != 0)
                    Interlocked.Add(ref job.Total, size);
            }

            p = next;
        }
    }

    private static void Complete(Job job)
    {
        if (Interlocked.Decrement(ref job.Pending) != 0)
            return;
        if (job.Token.IsCancellationRequested)
        {
            job.Completion.TrySetCanceled(job.Token);
            return;
        }

        var total = Interlocked.Read(ref job.Total);
        if (Cache.Count > CacheCap)
            Cache.Clear();
        Cache[job.Root] = total;
        job.Completion.TrySetResult(total);
    }

    private static unsafe bool IsDot(byte* name) =>
        name[0] == '.' && (name[1] == 0 || (name[1] == '.' && name[2] == 0));

    private static string Join(string parent, string name) =>
        parent.Length == 1 && parent[0] == '/' ? "/" + name : parent + "/" + name;

    private sealed class Job
    {
        public readonly string Root;
        public readonly CancellationToken Token;
        public readonly TaskCompletionSource<long> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long Total;
        public int Pending = 1;

        public Job(string root, CancellationToken token)
        {
            Root = root;
            Token = token;
        }
    }

    private readonly record struct Work(Job Job, string Path, bool Follow);

    [StructLayout(LayoutKind.Sequential)]
    private struct AttrList
    {
        public ushort BitmapCount;
        public ushort Reserved;
        public uint Common, Vol, Dir, File, Fork;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeSet
    {
        public uint Common, Vol, Dir, File, Fork;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttrReference
    {
        public int DataOffset;
        public uint Length;
    }

    [DllImport("/usr/lib/libSystem.B.dylib", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int oflag);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int close(int fd);

    [DllImport("/usr/lib/libSystem.B.dylib", SetLastError = true)]
    private static unsafe extern int getattrlistbulk(int dirfd, ref AttrList attrList, byte* attrBuf, nuint attrBufSize, ulong options);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int pthread_set_qos_class_self_np(int qosClass, int relativePriority);
}
