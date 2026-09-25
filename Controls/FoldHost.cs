using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MacExplorer.Controls;

public sealed class FoldHost : Decorator
{
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<FoldHost, bool>(nameof(IsExpanded), true);

    private double _openHeight;
    private Thickness _openMargin;
    private bool _marginCaptured;
    private bool _moving;
    private bool _snapQueued;
    private DispatcherTimer? _settle;
    private EventHandler? _settleHandler;
    private int _motion;

    public FoldHost()
    {
        ClipToBounds = true;
        MinHeight = 0;
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        CaptureMargin();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExpandedProperty)
            Apply(change.GetNewValue<bool>());
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var child = Child;
        if (child is null)
            return default;

        child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        SyncOpenHeight(child.DesiredSize.Height);
        var height = double.IsNaN(Height) ? (IsExpanded ? _openHeight : 0) : Math.Max(0, Height);
        var width = double.IsInfinity(availableSize.Width) ? child.DesiredSize.Width : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width, Math.Max(finalSize.Height, _openHeight)));
        return finalSize;
    }

    private void Apply(bool expanded)
    {
        CaptureMargin();
        if (!expanded && Bounds.Height > 0)
            _openHeight = Bounds.Height;
        BeginMotion();
        if (expanded)
        {
            Opacity = 1;
            IsHitTestVisible = true;
            Margin = _openMargin;
            if (_openHeight > 0)
                Height = _openHeight;
            return;
        }

        Opacity = 0;
        IsHitTestVisible = false;
        Margin = new Thickness(_openMargin.Left, 0, _openMargin.Right, 0);
        Height = 0;
    }

    private void SyncOpenHeight(double open)
    {
        if (open <= 0)
            return;
        var settled = !_moving && (double.IsNaN(Height) || _openHeight <= 0 || Math.Abs(Height - _openHeight) < 0.5);
        _openHeight = open;
        if (IsExpanded && settled && (double.IsNaN(Height) || Math.Abs(Height - open) > 0.5))
            SnapHeight(open);
    }

    private void SnapHeight(double height)
    {
        if (_snapQueued || (!double.IsNaN(Height) && Math.Abs(Height - height) < 0.5))
            return;
        _snapQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _snapQueued = false;
            if (_moving || !IsExpanded)
                return;
            var transitions = Transitions;
            Transitions = null;
            Height = height;
            Transitions = transitions;
        }, DispatcherPriority.Send);
    }

    private void CaptureMargin()
    {
        if (_marginCaptured)
            return;
        _openMargin = Margin;
        _marginCaptured = true;
    }

    private void BeginMotion()
    {
        var id = ++_motion;
        _moving = true;
        var timer = _settle ??= new DispatcherTimer { Interval = SectionMotion.Move };
        if (_settleHandler is not null)
            timer.Tick -= _settleHandler;
        _settleHandler = (_, _) =>
        {
            if (id != _motion)
                return;
            timer.Stop();
            _moving = false;
            InvalidateMeasure();
        };
        timer.Tick += _settleHandler;
        timer.Stop();
        timer.Start();
    }
}
