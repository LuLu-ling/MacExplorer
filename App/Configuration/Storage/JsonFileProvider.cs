using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MacExplorer.Configuration.Storage;

public class JsonFileProvider : CommonFileProvider, IEnumerableKeyProvider
{
    private readonly JsonObject _rootElement;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonCompat.SerializerOptions)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    public JsonFileProvider(string path) : base(path)
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var parseResult = JsonNode.Parse(stream, JsonCompat.NodeOptions, JsonCompat.DocumentOptions);
                if (parseResult is not JsonObject root)
                    throw new ConfigFileInitException(path,
                        $"Invalid root element type: {parseResult?.GetValueKind().ToString() ?? "Empty"}");
                _rootElement = root;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                using var stream = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
                _rootElement = [];
                JsonSerializer.Serialize(stream, _rootElement, SerializerOptions);
            }
        }
        catch (Exception ex) when (ex is not ConfigFileInitException)
        {
            throw new ConfigFileInitException(path, "Failed to read JSON file", ex);
        }
    }

    public override T Get<T>(string key)
    {
        var result = _rootElement[key];
        if (result is null) throw new KeyNotFoundException($"Not found: '{key}'");
        try
        {
            return result.Deserialize<T>(SerializerOptions) ?? throw NullException();
        }
        catch (JsonException)
        {
            T fallback;
            var type = typeof(T);
            if (type == typeof(string)) fallback = (T)(object)result.ToString();
            else
            {
                var jsonStr = result.Deserialize<string>(SerializerOptions)!;
                if (type == typeof(bool)) fallback = (T)(object)(jsonStr.ToLowerInvariant() is "true" or "1");
                else fallback = JsonSerializer.Deserialize<T>(jsonStr, SerializerOptions) ?? throw NullException();
            }

            Set(key, fallback);
            return fallback;
        }

        Exception NullException() => new InvalidDataException($"Deserialized value is null: '{key}'");
    }

    public override void Set<T>(string key, T value) =>
        _rootElement[key] = JsonSerializer.SerializeToNode(value, SerializerOptions);

    public override bool Exists(string key) => _rootElement.ContainsKey(key);

    public override void Remove(string key) => _rootElement.Remove(key);

    protected override void WriteToStream(Stream stream)
    {
        using var writer = new Utf8JsonWriter(stream, WriterOptions);
        _rootElement.WriteTo(writer, SerializerOptions);
        writer.Flush();
    }

    public IEnumerable<string> Keys => _rootElement.Select(pair => pair.Key);
}
