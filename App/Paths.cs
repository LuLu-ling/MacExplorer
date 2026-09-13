namespace MacExplorer.Infrastructure;

public static class Paths
{
    public static string Data { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MacExplorer");

    public static string Logs => Path.Combine(Data, "Log");

    public static string LocalConfig => Path.Combine(Data, "config.v1.json");

    public static string LegacySettings => Path.Combine(Data, "settings.json");
}
