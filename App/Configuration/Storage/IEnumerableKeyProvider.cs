namespace MacExplorer.Configuration.Storage;

public interface IEnumerableKeyProvider
{
    IEnumerable<string> Keys { get; }
}
