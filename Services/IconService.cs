using Avalonia.Media.Imaging;
using MacExplorer.Native;

namespace MacExplorer.Services;

public sealed class IconService
{
    private readonly Dictionary<(string Path, int Size), Bitmap> _cache = [];
    private readonly Lock _gate = new();

    public Bitmap? Get(string path, int size)
    {
        if (string.IsNullOrEmpty(path) || size <= 0)
            return null;

        var key = (path, size);
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            try
            {
                var bitmap = MacWorkspace.Icon(path, size);
                if (bitmap is null)
                    return null;
                _cache[key] = bitmap;
                if (_cache.Count > 1024)
                    Trim();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }

    public void Invalidate(string path)
    {
        lock (_gate)
        {
            foreach (var key in _cache.Keys.Where(k => string.Equals(k.Path, path, StringComparison.Ordinal)).ToArray())
                _cache.Remove(key);
        }
    }


    public Task<Bitmap?> GetAsync(string path, int size, CancellationToken token = default) =>
        Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            return Get(path, size);
        }, token);

    private void Trim()
    {
        foreach (var key in _cache.Keys.Take(_cache.Count - 768).ToArray())
            _cache.Remove(key);
    }
}
