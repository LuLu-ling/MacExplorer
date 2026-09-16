using MacExplorer.Logging;

namespace MacExplorer.Localization;

public static class WeakLanguageChanged
{
    private static readonly object Lock = new();
    private static readonly List<(WeakReference<object> Target, Action<object> Handler)> Handlers = [];
    private static bool _hooked;

    public static void Add<T>(T target, Action<T> handler) where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(handler);
        if (ReferenceEquals(handler.Target, target))
            throw new ArgumentException(
                "handler captures the target instance; use a static lambda: static t => t.Foo()",
                nameof(handler));

        lock (Lock)
        {
            if (!_hooked)
            {
                LocalizationService.LanguageChanged += OnLanguageChanged;
                _hooked = true;
            }

            Handlers.Add((new WeakReference<object>(target), o => handler((T)o)));
        }
    }

    private static void OnLanguageChanged()
    {
        (WeakReference<object> Target, Action<object> Handler)[] snapshot;
        lock (Lock)
        {
            Handlers.RemoveAll(h => !h.Target.TryGetTarget(out _));
            snapshot = Handlers.ToArray();
        }

        foreach (var (target, handler) in snapshot)
        {
            if (!target.TryGetTarget(out var live))
                continue;
            try
            {
                handler(live);
            }
            catch (Exception ex)
            {
                LogWrapper.Warn(ex, "Localization", "Language change handler failed");
            }
        }
    }
}
