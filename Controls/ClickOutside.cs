using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace MacExplorer.Controls;

internal static class ClickOutside
{
    public static void Attach(TopLevel? root, EventHandler<PointerPressedEventArgs> handler) =>
        root?.AddHandler(InputElement.PointerPressedEvent, handler, RoutingStrategies.Tunnel);

    public static void Detach(TopLevel? root, EventHandler<PointerPressedEventArgs> handler) =>
        root?.RemoveHandler(InputElement.PointerPressedEvent, handler);

    public static bool Hits(Control host, object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, host))
                return true;
        }

        return false;
    }
}
