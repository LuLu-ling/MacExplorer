using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace MacExplorer.Controls;

public sealed class FileDragHandle
{
    private FileDragHandle() { }

    public static readonly AttachedProperty<bool> IsHandleProperty =
        AvaloniaProperty.RegisterAttached<FileDragHandle, Control, bool>("IsHandle");

    public static bool GetIsHandle(AvaloniaObject element) => element.GetValue(IsHandleProperty);

    public static void SetIsHandle(AvaloniaObject element, bool value) => element.SetValue(IsHandleProperty, value);

    public static bool Hit(Visual? source, Point pointInSource)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is ListBox)
                return false;
            if (visual is not Control control || !GetIsHandle(control))
                continue;
            if (control is not TextBlock text)
                return true;
            return source?.TranslatePoint(pointInSource, text) is { } local && HitsText(text, local);
        }

        return false;
    }

    private static bool HitsText(TextBlock text, Point local)
    {
        var padding = text.Padding;
        if (text.UseLayoutRounding)
            padding = LayoutHelper.RoundLayoutThickness(padding, LayoutHelper.GetLayoutScale(text));

        var layout = text.TextLayout;
        var top = padding.Top;
        if (text.Bounds.Height < layout.Height)
        {
            top += text.VerticalAlignment switch
            {
                VerticalAlignment.Center => (text.Bounds.Height - layout.Height) / 2,
                VerticalAlignment.Bottom => text.Bounds.Height - layout.Height,
                _ => 0
            };
        }

        var x = local.X - padding.Left;
        var y = local.Y - top;
        var lineTop = 0d;
        foreach (var line in layout.TextLines)
        {
            var bottom = lineTop + line.Height;
            if (line.Width > 0 && y >= lineTop && y <= bottom && x >= line.Start && x <= line.Start + line.Width)
                return true;
            lineTop = bottom;
        }

        return false;
    }
}
