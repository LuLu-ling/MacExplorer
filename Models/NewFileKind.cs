namespace MacExplorer.Models;

public sealed class NewFileKind
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Extension { get; set; } = "";

    public NewFileKind Clone() => new() { Id = Id, Name = Name, Extension = Extension };
}
