namespace MacExplorer.Configuration;

public record ConfigObserver(
    ConfigEvent Event,
    ConfigEventHandler Handler,
    bool IsPreview = false);
