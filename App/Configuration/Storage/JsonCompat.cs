using System.Text.Json;
using System.Text.Json.Nodes;

namespace MacExplorer.Configuration.Storage;

internal static class JsonCompat
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static JsonDocumentOptions DocumentOptions { get; } = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static JsonNodeOptions NodeOptions { get; } = new();
}
