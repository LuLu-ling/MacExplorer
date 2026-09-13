using System.Diagnostics.CodeAnalysis;

namespace MacExplorer.Configuration;

public interface IConfigProvider
{
    bool GetValue<T>(string key, [NotNullWhen(true)] out T? value, object? argument = null);
    void SetValue<T>(string key, T value, object? argument = null);
    void Delete(string key, object? argument = null);
    bool Exists(string key, object? argument = null);
}
