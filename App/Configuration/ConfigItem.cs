using System.Collections.Specialized;
using System.ComponentModel;

namespace MacExplorer.Configuration;

public class ConfigItem<TValue>(string key, Func<TValue> defaultValue, ConfigSource source) : IConfigScope, ConfigItem
{
    public string Key { get; } = key;
    public ConfigSource Source { get; } = source;
    public Type Type => typeof(TValue);

    private Func<TValue>? _defaultValueConstructor = defaultValue;
    private TValue? _defaultValue;
    private bool _defaultValueHasSet;
    private IConfigProvider? _provider;
    private ConfigValueCache<TValue> _valueCache = new();
    private readonly HashSet<ConfigObserver> _observers = [];
    private readonly HashSet<ConfigObserver> _previewObservers = [];

    public ConfigItem(string key, TValue defaultValue, ConfigSource source)
        : this(key, () => defaultValue, source) { }

    public TValue DefaultValue => GetDefaultValue();
    public object DefaultValueNoType => DefaultValue ?? default!;

    public bool EnableCache
    {
        get;
        set
        {
            if (!value) _valueCache.InvalidateAll();
            field = value;
        }
    } = true;

    public IEnumerable<string> CheckScope(IReadOnlySet<string> keys) => keys.Contains(Key) ? [Key] : [];

    public TValue GetValue(object? argument = null)
    {
        TValue? value = default;
        var exists = EnableCache && _valueCache.TryRead(out value, argument);
        var newValue = false;
        if (!exists)
        {
            newValue = true;
            exists = Provider.GetValue(Key, out value, argument);
        }

        var e = TriggerInternal(ConfigEvent.Get, argument, value, true);
        if (e is not null)
        {
            if (e.Cancelled) return DefaultValue;
            if (e.NewValueReplacement is not null) return (TValue)e.NewValueReplacement;
        }

        if (!exists) value = DefaultValue;
        if (newValue) ProcessNewCache(value!, argument);
        return value!;
    }

    public object GetValueNoType(object? argument = null) => GetValue(argument) ?? default!;

    public bool SetValue(TValue value, object? argument = null, bool forceNewValue = false, bool bypassCache = false)
    {
        var e = TriggerInternal(ConfigEvent.Set, argument, value, isPreview: true);
        if (e is not null)
        {
            if (e.Cancelled) return false;
            if (e.NewValueReplacement is not null) value = (TValue)e.NewValueReplacement;
        }

        if (bypassCache || ProcessNewCache(value, argument, forceNewValue))
            Provider.SetValue(Key, value, argument);
        TriggerInternal(ConfigEvent.Set, argument, value, e: e, isPreview: false);
        return true;
    }

    public bool SetValueNoType(object value, object? argument = null)
    {
        try
        {
            return SetValue((TValue)value, argument);
        }
        catch (InvalidCastException)
        {
            if (value is string v) return SetValue(v.Convert<TValue>(), argument);
            throw new InvalidCastException(
                $"Value convert failed (required: {Type.FullName}, provided: {value.GetType().FullName})");
        }
    }

    public bool SetDefaultValue(object? argument = null, bool? forceNewValue = null) =>
        SetValue(DefaultValue, argument, forceNewValue ?? IsDefault(argument));

    public bool Reset(object? argument = null)
    {
        var e = TriggerInternal(ConfigEvent.Reset, argument, null, isPreview: true);
        if (e is { Cancelled: true }) return false;
        Provider.Delete(Key, argument);
        if (EnableCache) _valueCache.Invalidate(argument);
        TriggerInternal(ConfigEvent.Reset, argument, DefaultValueNoType, isPreview: false);
        return true;
    }

    public bool IsDefault(object? argument = null)
    {
        var result = !Provider.Exists(Key, argument);
        var e = TriggerInternal(ConfigEvent.CheckDefault, argument, result);
        if (e is { NewValueReplacement: not null }) result = (bool)e.NewValueReplacement;
        return result;
    }

    public void Observe(ConfigObserver observer)
    {
        if (observer.IsPreview) _previewObservers.Add(observer);
        else _observers.Add(observer);
    }

    public bool Unobserve(ConfigObserver observer) =>
        observer.IsPreview ? _previewObservers.Remove(observer) : _observers.Remove(observer);

    public ConfigEventArgs? TriggerEvent(ConfigEvent trigger, object? argument, bool bypassOldValue = false, bool fillNewValue = false) =>
        TriggerInternal(trigger, argument, null, bypassOldValue, fillNewValue);

    private IConfigProvider Provider => _provider ??= ConfigService.GetProvider(Source);

    private TValue GetDefaultValue()
    {
        if (_defaultValueHasSet) return _defaultValue!;
        _defaultValue = _defaultValueConstructor!();
        _defaultValueHasSet = true;
        _defaultValueConstructor = null;
        return _defaultValue;
    }

    private bool ProcessNewCache(TValue newCache, object? argument, bool force = false)
    {
        if (!EnableCache) return true;
        if (!force)
        {
            var existsOld = _valueCache.TryRead(out var oldCache, argument);
            if (existsOld && EqualityComparer<TValue>.Default.Equals(oldCache, newCache)) return false;
        }

        if (newCache is INotifyPropertyChanged reactive)
            reactive.PropertyChanged += (_, _) => SetValue(newCache, argument, bypassCache: true);
        else if (newCache is INotifyCollectionChanged reactiveCollection)
            reactiveCollection.CollectionChanged += (_, _) => SetValue(newCache, argument, bypassCache: true);

        _valueCache.Write(newCache, argument);
        return true;
    }

    private object? GetValueOrNull(object? argument)
    {
        var exists = Provider.GetValue<TValue>(Key, out var value, argument);
        return exists ? value : null;
    }

    private ConfigEventArgs? TriggerInternal(
        ConfigEvent trigger, object? argument, object? newValue,
        bool bypassOldValue = false, bool fillNewValue = false,
        ConfigEventArgs? e = null, bool? isPreview = null)
    {
        var replaceNewValue = false;
        var observers = isPreview is { } preview
            ? (preview ? _previewObservers : _observers)
            : _previewObservers.Concat(_observers);

        foreach (var observer in observers.Where(o => ((int)o.Event & (int)trigger) > 0))
        {
            if (e is null)
            {
                if (isPreview == false && !bypassOldValue) bypassOldValue = true;
                var currentValue = fillNewValue || !bypassOldValue ? GetValueOrNull(argument) : null;
                if (newValue is null && fillNewValue) newValue = currentValue ?? DefaultValue;
                e = new ConfigEventArgs(this, trigger, argument, bypassOldValue ? null : currentValue, newValue);
            }

            observer.Handler(e);
            if (observer.IsPreview)
            {
                if (e.NewValueReplacement is not null) replaceNewValue = true;
                if (e.Cancelled) return e;
            }
            else if (!replaceNewValue && e.NewValueReplacement is not null)
                e.NewValueReplacement = null;
        }

        if (e is { Cancelled: true }) e.Cancelled = false;
        return e;
    }
}

public interface ConfigItem
{
    string Key { get; }
    ConfigSource Source { get; }
    Type Type { get; }
    void Observe(ConfigObserver observer);
    bool Unobserve(ConfigObserver observer);
    ConfigEventArgs? TriggerEvent(ConfigEvent trigger, object? argument, bool bypassOldValue = false, bool fillNewValue = false);

    ConfigObserver Observe(ConfigEvent trigger, ConfigEventHandler handler, bool isPreview = false)
    {
        var observer = new ConfigObserver(trigger, handler, isPreview);
        Observe(observer);
        return observer;
    }

    event ConfigEventHandler Changed
    {
        add => Observe(ConfigEvent.Changed, value);
        remove => throw new NotSupportedException("Use Observe() and Unobserve().");
    }

    bool Reset(object? argument = null);
    bool IsDefault(object? argument = null);
    bool SetDefaultValue(object? argument = null, bool? forceNewValue = null);
    object GetValueNoType(object? argument = null);
    bool SetValueNoType(object value, object? argument = null);
    object DefaultValueNoType { get; }
    bool EnableCache { get; set; }
}
