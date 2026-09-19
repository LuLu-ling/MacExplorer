using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;

namespace MacExplorer.Controls;

internal static class ReorderShift
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(220);
    public static readonly Easing Ease = new SplineEasing(0.22, 1, 0.36, 1);

    public static void Item(Control child, double delta, bool horizontal, bool animate) =>
        Set(child, horizontal ? delta : 0, horizontal ? 0 : delta, animate);

    public static double Extent(Control child, bool horizontal) =>
        horizontal ? child.Bounds.Width : child.Bounds.Height;

    public static double Origin(IReadOnlyList<Control> items, int index, bool horizontal, double spacing = 0)
    {
        var acc = 0.0;
        var last = Math.Clamp(index, 0, items.Count);
        for (var i = 0; i < last; i++)
            acc += Extent(items[i], horizontal) + spacing;
        return acc;
    }

    public static int HoverAt(Panel panel, int from, double center, bool horizontal, int lo, int hi, double spacing = 0) =>
        HoverAt(panel.Children, from, center, horizontal, lo, hi, spacing);

    public static int HoverAt(
        IReadOnlyList<Control> items, int from, double center, bool horizontal, int lo, int hi, double spacing = 0)
    {
        var n = items.Count;
        if ((uint)from >= (uint)n)
            return from;

        var mids = new double[n];
        var acc = 0.0;
        for (var i = 0; i < n; i++)
        {
            var size = Extent(items[i], horizontal);
            if (size < 1)
            {
                mids[i] = acc;
                continue;
            }

            mids[i] = acc + size / 2;
            acc += size + spacing;
        }

        var hover = from;
        var prev = from;
        for (var i = from + 1; i <= hi && i < n; i++)
        {
            if (Extent(items[i], horizontal) < 1)
                continue;
            if (center >= (mids[prev] + mids[i]) / 2)
                hover = i;
            else
                break;
            prev = i;
        }

        prev = from;
        for (var i = from - 1; i >= lo && i >= 0; i--)
        {
            if (Extent(items[i], horizontal) < 1)
                continue;
            if (center <= (mids[i] + mids[prev]) / 2)
                hover = i;
            else
                break;
            prev = i;
        }

        return hover;
    }

    public static void Siblings(Panel panel, int from, int hover, double slot, bool horizontal) =>
        Siblings(panel.Children, from, hover, slot, horizontal);

    public static void Siblings(IReadOnlyList<Control> items, int from, int hover, double slot, bool horizontal)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (i == from)
                continue;
            var shift = 0.0;
            if (from < hover && i > from && i <= hover)
                shift = -slot;
            else if (from > hover && i >= hover && i < from)
                shift = slot;
            Item(items[i], shift, horizontal, animate: true);
        }
    }

    public static void Reset(Panel panel, bool animate = false)
    {
        foreach (var child in panel.Children)
            Set(child, 0, 0, animate);
    }

    public static void Reset(IReadOnlyList<Control> items, bool animate = false)
    {
        foreach (var child in items)
            Set(child, 0, 0, animate);
    }

    public static void Settle(Panel panel, int from, int to, bool horizontal, double spacing = 0) =>
        Settle(panel.Children, from, to, horizontal, spacing);

    public static void Settle(IReadOnlyList<Control> items, int from, int to, bool horizontal, double spacing = 0)
    {
        if ((uint)from >= (uint)items.Count || (uint)to >= (uint)items.Count)
            return;

        var slot = Extent(items[from], horizontal) + spacing;
        var travel = 0.0;
        if (from < to)
        {
            for (var i = from + 1; i <= to; i++)
                travel += Extent(items[i], horizontal) + spacing;
        }
        else if (from > to)
        {
            for (var i = to; i < from; i++)
                travel -= Extent(items[i], horizontal) + spacing;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var shift = 0.0;
            if (i == from)
                shift = travel;
            else if (from < to && i > from && i <= to)
                shift = -slot;
            else if (from > to && i >= to && i < from)
                shift = slot;
            Item(items[i], shift, horizontal, animate: true);
        }
    }

    private static void Set(Control child, double x, double y, bool animate)
    {
        var transform = x == 0 && y == 0
            ? null
            : TransformOperations.Parse(FormattableString.Invariant($"translate({x}px, {y}px)"));
        if (animate)
        {
            Ensure(child);
            child.RenderTransform = transform;
            return;
        }

        var saved = child.Transitions;
        child.Transitions = null;
        child.RenderTransform = transform;
        child.Transitions = saved;
    }

    private static void Ensure(Control child)
    {
        var list = child.Transitions ??= new Transitions();
        foreach (var transition in list)
        {
            if (transition is TransformOperationsTransition)
                return;
        }

        list.Add(new TransformOperationsTransition
        {
            Property = Visual.RenderTransformProperty,
            Duration = Duration,
            Easing = Ease
        });
    }
}
