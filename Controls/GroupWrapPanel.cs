using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MacExplorer.Models;

namespace MacExplorer.Controls;

public sealed class GroupWrapPanel : WrapPanel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Orientation is Orientation.Vertical || double.IsInfinity(availableSize.Width))
            return base.MeasureOverride(availableSize);

        var width = availableSize.Width;
        double x = 0, y = 0, rowH = 0, maxW = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
                continue;
            if (IsGroup(child))
            {
                if (x > 0 || rowH > 0)
                {
                    y += rowH;
                    x = 0;
                    rowH = 0;
                }

                child.Measure(new Size(width, availableSize.Height));
                y += child.DesiredSize.Height;
                maxW = Math.Max(maxW, width);
                continue;
            }

            child.Measure(availableSize);
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                y += rowH;
                x = 0;
                rowH = 0;
            }

            x += size.Width;
            rowH = Math.Max(rowH, size.Height);
            maxW = Math.Max(maxW, x);
        }

        return new Size(maxW, y + rowH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Orientation is Orientation.Vertical)
            return base.ArrangeOverride(finalSize);

        double x = 0, y = 0, rowH = 0, maxW = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
                continue;
            var size = child.DesiredSize;
            if (IsGroup(child))
            {
                if (x > 0 || rowH > 0)
                {
                    y += rowH;
                    x = 0;
                    rowH = 0;
                }

                child.Arrange(new Rect(0, y, finalSize.Width, size.Height));
                y += size.Height;
                maxW = Math.Max(maxW, finalSize.Width);
                continue;
            }

            if (x > 0 && x + size.Width > finalSize.Width)
            {
                y += rowH;
                x = 0;
                rowH = 0;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width;
            rowH = Math.Max(rowH, size.Height);
            maxW = Math.Max(maxW, x);
        }

        return new Size(maxW, y + rowH);
    }

    private static bool IsGroup(Control child) =>
        child is ListBoxItem { DataContext: FileGroup } || child.DataContext is FileGroup;
}
