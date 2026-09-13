namespace MacExplorer.Configuration;

public interface IConfigScope
{
    IEnumerable<string> CheckScope(IReadOnlySet<string> keys);
    bool Reset(object? argument = null);
    bool IsDefault(object? argument = null);
}
