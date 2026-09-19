using Avalonia;
using MacExplorer.ViewModels;

namespace MacExplorer.Services;

internal static class TabDrag
{
    public static ExplorerTabViewModel? Tab { get; private set; }
    public static MainViewModel? Source { get; private set; }
    public static MainViewModel? Dest { get; private set; }
    public static int Index { get; private set; } = -1;
    public static PixelPoint Screen { get; set; }

    public static bool Active => Tab is not null;
    public static bool Is(object? _ = null) => Tab is not null;
    public static void Begin(ExplorerTabViewModel tab, MainViewModel source, PixelPoint screen)
    {
        Tab = tab;
        Source = source;
        Dest = null;
        Index = -1;
        Screen = screen;
    }

    public static void Offer(MainViewModel dest, int index)
    {
        Dest = dest;
        Index = index;
    }

    public static void ClearOffer()
    {
        Dest = null;
        Index = -1;
    }

    public static void End()
    {
        Tab = null;
        Source = null;
        Dest = null;
        Index = -1;
    }
}
