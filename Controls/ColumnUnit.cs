using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace MacExplorer.Controls;

public sealed class ColumnUnit : StackPanel
{
    public static readonly StyledProperty<bool> IsShownProperty =
        AvaloniaProperty.Register<ColumnUnit, bool>(nameof(IsShown), true);

    public bool IsShown
    {
        get => GetValue(IsShownProperty);
        set => SetValue(IsShownProperty, value);
    }

    private bool _live;
    private int _gen;
    private double _slot;

    public ColumnUnit()
    {
        Orientation = Orientation.Horizontal;
        ClipToBounds = true;
        Background = Brushes.Transparent;
        Transitions =
        [
            new DoubleTransition
            {
                Property = WidthProperty,
                Duration = ReorderShift.Duration,
                Easing = ReorderShift.Ease
            },
            new DoubleTransition
            {
                Property = MinWidthProperty,
                Duration = ReorderShift.Duration,
                Easing = ReorderShift.Ease
            },
            new DoubleTransition
            {
                Property = MaxWidthProperty,
                Duration = ReorderShift.Duration,
                Easing = ReorderShift.Ease
            },
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = ReorderShift.Duration,
                Easing = ReorderShift.Ease
            }
        ];
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!IsShown)
            Collapse(animate: false);
        Dispatcher.UIThread.Post(() => _live = true, DispatcherPriority.Loaded);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsShownProperty)
            return;
        if (!_live)
        {
            if (!IsShown)
                Collapse(animate: false);
            return;
        }

        if (IsShown)
            Expand();
        else
            Collapse(animate: true);
    }

    private void Collapse(bool animate)
    {
        _gen++;
        _slot = Slot();
        if (!animate)
        {
            Apply(0, 0, hit: false, animate: false);
            return;
        }

        Apply(_slot, 1, hit: true, animate: false);
        Apply(0, 0, hit: false, animate: true);
    }

    private void Expand()
    {
        var gen = ++_gen;
        var slot = Slot();
        Apply(0, 0, hit: true, animate: false);
        Dispatcher.UIThread.Post(() =>
        {
            if (gen != _gen || !IsShown)
                return;
            Apply(slot, 1, hit: true, animate: true);
            _ = Release(gen);
        }, DispatcherPriority.Render);
    }

    private async Task Release(int gen)
    {
        await Task.Delay(ReorderShift.Duration);
        if (gen != _gen || !IsShown)
            return;
        var saved = Transitions;
        Transitions = null;
        ClearValue(WidthProperty);
        ClearValue(MinWidthProperty);
        ClearValue(MaxWidthProperty);
        Transitions = saved;
    }

    private void Apply(double width, double opacity, bool hit, bool animate)
    {
        if (!animate)
        {
            var saved = Transitions;
            Transitions = null;
            Width = width;
            MinWidth = width;
            MaxWidth = width;
            Opacity = opacity;
            IsHitTestVisible = hit;
            Transitions = saved;
            return;
        }

        Width = width;
        MinWidth = 0;
        MaxWidth = width;
        Opacity = opacity;
        IsHitTestVisible = hit;
    }

    private double Slot()
    {
        if (Bounds.Width > 1)
            return Bounds.Width;
        if (_slot > 1)
            return _slot;
        var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
        var width = 0.0;
        foreach (var child in Children)
        {
            child.Measure(inf);
            width += child.DesiredSize.Width;
        }

        return width;
    }
}
