using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using MacExplorer.Logging;

namespace MacExplorer.Configuration.Storage;

public abstract class ConfigStorage : IConfigProvider
{
    protected abstract bool OnAccess<TKey, TValue>(
        StorageAction action,
        ref TKey key,
        [NotNullWhen(true)] ref TValue value,
        object? argument);

    protected virtual void OnStop() { }

    public void Stop() => OnStop();

    public bool Access<TKey, TValue>(
        StorageAction action,
        ref TKey key,
        [NotNullWhen(true)] ref TValue value,
        object? argument)
    {
        try
        {
            return OnAccess(action, ref key, ref value, argument);
        }
        catch (Exception ex)
        {
            LogWrapper.Fatal(ex, "Config",
                $"Config storage error: {action} {ToString()} key={key}");
            throw;
        }
    }

    public bool GetValue<T>(string key, [NotNullWhen(true)] out T? value, object? argument = null)
    {
        var keyRef = key;
        T? valueRef = default;
        var hasValue = Access(StorageAction.Get, ref keyRef, ref valueRef, argument);
        value = valueRef;
        return hasValue;
    }

    public void SetValue<T>(string key, T value, object? argument = null)
    {
        var keyRef = key;
        var valueRef = value;
        Access(StorageAction.Set, ref keyRef, ref valueRef, argument);
    }

    public void Delete(string key, object? argument = null)
    {
        var keyRef = key;
        object? valueRef = null;
        Access(StorageAction.Delete, ref keyRef, ref valueRef, argument);
    }

    public bool Exists(string key, object? argument = null)
    {
        var keyRef = key;
        var resultRef = false;
        return Access(StorageAction.Exists, ref keyRef, ref resultRef, argument) && resultRef;
    }

    public override string ToString() => $"{GetType().Name}@{GetHashCode()}";
}
