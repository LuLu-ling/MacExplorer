namespace MacExplorer.Configuration;

public record ConfigEventArgs(
    ConfigItem Item,
    ConfigEvent Event,
    object? Argument,
    object? OldValue,
    object? NewValue)
{
    public object? NewValueReplacement { get; set; }
    public bool Cancelled { get; set; }
    public object? Value => NewValueReplacement ?? NewValue;
}
