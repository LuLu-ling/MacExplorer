namespace MacExplorer.Native;

internal static class WindowChrome
{
    public const double TitleBarHeight = 48;
    public const double TrafficLightDiameter = 14.25;
    public const double TrafficLightGap = 8;
    public const double TrafficLightInset = 78;

    public static double TrafficLightClusterWidth =>
        TrafficLightDiameter * 3 + TrafficLightGap * 2;
}
