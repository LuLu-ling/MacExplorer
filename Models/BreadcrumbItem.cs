namespace MacExplorer.Models;

public sealed class BreadcrumbItem
{
    public required string Title { get; init; }
    public required string Path { get; init; }
    public bool IsRoot { get; init; }
    public bool ShowChevron { get; init; } = true;
}
