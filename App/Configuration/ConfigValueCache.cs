using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace MacExplorer.Configuration;

public struct ConfigValueCache<TValue>()
{
    private TValue? _cachedValue;
    private bool _hasCachedValue;
    private readonly ConcurrentDictionary<object, TValue> _cacheWithContext = [];

    public bool TryRead([NotNullWhen(true)] out TValue? value, object? argument = null)
    {
        if (argument is not null)
        {
            if (_cacheWithContext.TryGetValue(argument, out var cached))
            {
                value = cached!;
                return true;
            }
            value = default;
            return false;
        }

        if (_hasCachedValue)
        {
            value = _cachedValue!;
            return true;
        }

        value = default;
        return false;
    }

    public void Write(TValue value, object? argument = null)
    {
        if (argument is not null)
        {
            _cacheWithContext[argument] = value;
            return;
        }

        _hasCachedValue = true;
        _cachedValue = value;
    }

    public bool Invalidate(object? argument)
    {
        if (argument is not null)
            return _cacheWithContext.TryRemove(argument, out _);
        if (!_hasCachedValue) return false;
        _cachedValue = default;
        _hasCachedValue = false;
        return true;
    }

    public void InvalidateAll()
    {
        _cacheWithContext.Clear();
        _cachedValue = default;
        _hasCachedValue = false;
    }
}
