namespace MacExplorer.Models;

public readonly record struct LayoutMetrics(
    double DetailsRowHeight,
    double DetailsIconSize,
    double ListRowHeight,
    double ListIconSize,
    double CardWidth,
    double CardHeight,
    double CardIconSize,
    double GridItemSize,
    double FontSize)
{
    public double ListIconBoxSize => ListIconSize + 4;

    public static LayoutMetrics For(int size) => Math.Clamp(size, 1, 5) switch
    {
        1 => new(20, 16, 24, 16, 148, 56, 32, 64, 11),
        2 => new(24, 16, 32, 16, 164, 64, 36, 80, 12),
        4 => new(40, 24, 40, 24, 200, 84, 48, 120, 13),
        5 => new(48, 28, 44, 32, 220, 96, 56, 160, 14),
        _ => new(32, 20, 36, 20, 180, 72, 40, 96, 12),
    };

    public int IconPixels(LayoutKind layout) => (int)(layout switch
    {
        LayoutKind.Grid => GridItemSize,
        LayoutKind.Cards => CardIconSize,
        LayoutKind.List => ListIconSize,
        _ => DetailsIconSize
    });
}
