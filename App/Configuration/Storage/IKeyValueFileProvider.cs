namespace MacExplorer.Configuration.Storage;

public interface IKeyValueFileProvider
{
    string FilePath { get; }
    T Get<T>(string key);
    void Set<T>(string key, T value);
    bool Exists(string key);
    void Remove(string key);
    void Sync();
}
