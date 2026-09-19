using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using MacExplorer.Native;

namespace MacExplorer.Services;

public sealed class IconService
{
    private readonly ConcurrentDictionary<(string Path, int Size, long Stamp), Bitmap> _cache = new();

    public Bitmap? Get(string path, int size)
    {
        if (string.IsNullOrEmpty(path) || size <= 0)
            return null;

        var key = (path, size, Stamp(path));
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var bitmap = MacWorkspace.Icon(path, size);
            if (bitmap is null)
                return null;
            if (_cache.TryAdd(key, bitmap) && _cache.Count > 1024)
                Trim();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public void Invalidate(string path)
    {
        foreach (var key in _cache.Keys)
        {
            if (string.Equals(key.Path, path, StringComparison.Ordinal))
                _cache.TryRemove(key, out _);
        }
    }

    public Task<Bitmap?> GetAsync(string path, int size, CancellationToken token = default) =>
        Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            return Get(path, size);
        }, token);

    private static long Stamp(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch
        {
            return 0;
        }
    }

    private void Trim()
    {
        foreach (var key in _cache.Keys.Take(_cache.Count - 768).ToArray())
            _cache.TryRemove(key, out _);
    }
}
