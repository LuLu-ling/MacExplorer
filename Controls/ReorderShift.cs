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


    public static int HoverAt(Panel panel, int from, double center, bool horizontal, int lo, int hi, double spacing = 0)
    {
        var n = panel.Children.Count;
        if ((uint)from >= (uint)n)
            return from;

        double Size(int i) =>
            horizontal ? panel.Children[i].Bounds.Width : panel.Children[i].Bounds.Height;

        var mids = new double[n];
        var acc = 0.0;
        for (var i = 0; i < n; i++)
        {
            var size = Size(i);
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
            if (Size(i) < 1)
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
            if (Size(i) < 1)
                continue;
            if (center <= (mids[i] + mids[prev]) / 2)
                hover = i;
            else
                break;
            prev = i;
        }

        return hover;
    }

    public static void Siblings(Panel panel, int from, int hover, double slot, bool horizontal)
    {
        for (var i = 0; i < panel.Children.Count; i++)
        {
            if (i == from)
                continue;
            var shift = 0.0;
            if (from < hover && i > from && i <= hover)
                shift = -slot;
            else if (from > hover && i >= hover && i < from)
                shift = slot;
            Item(panel.Children[i], shift, horizontal, animate: true);
        }
    }

    public static void Reset(Panel panel, bool animate = false)
    {
        foreach (var child in panel.Children)
            Set(child, 0, 0, animate);
    }

    public static void Settle(Panel panel, int from, int to, double slot, bool horizontal)
    {
        for (var i = 0; i < panel.Children.Count; i++)
        {
            var shift = 0.0;
            if (i == from)
                shift = (to - from) * slot;
            else if (from < to && i > from && i <= to)
                shift = -slot;
            else if (from > to && i >= to && i < from)
                shift = slot;
            Item(panel.Children[i], shift, horizontal, animate: true);
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
