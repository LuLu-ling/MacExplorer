using Avalonia;
using Avalonia.Controls;

namespace MacExplorer.Controls;

// Files' BreadcrumbBar keeps the root and the newest path components visible.
// This Avalonia layout also keeps an overlong final component available, with text ellipsis.
public sealed class BreadcrumbOverflowPanel : Panel
{
    // Child zero is the overflow button; remaining children are path components.
    public int FirstVisibleIndex { get; private set; } = 1;
    public bool HasOverflow => FirstVisibleIndex > 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (i > 0)
                width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
            return finalSize;

        double total = 0;
        for (var i = 1; i < Children.Count; i++)
            total += Children[i].DesiredSize.Width;

        FirstVisibleIndex = 1;
        if (total > finalSize.Width && Children.Count > 2)
        {
            var remaining = Math.Max(0, finalSize.Width - Children[0].DesiredSize.Width);
            FirstVisibleIndex = Children.Count - 1;
            var width = Math.Min(Children[FirstVisibleIndex].DesiredSize.Width, remaining);
            while (FirstVisibleIndex > 1 && width + Children[FirstVisibleIndex - 1].DesiredSize.Width <= remaining)
            {
                FirstVisibleIndex--;
                width += Children[FirstVisibleIndex].DesiredSize.Width;
            }
        }

        double x = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var visible = i == 0 ? HasOverflow : i >= FirstVisibleIndex;
            // Preserve intrinsic measurements, but exclude collapsed ancestors from input and rendering.
            child.Opacity = visible ? 1 : 0;
            child.IsHitTestVisible = visible;
            child.IsEnabled = visible;
            var width = visible ? Math.Min(child.DesiredSize.Width, Math.Max(0, finalSize.Width - x)) : 0;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
