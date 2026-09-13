using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace MacExplorer.Controls;

public sealed class ColumnSplitter : Border
{
    public static readonly StyledProperty<double> ColumnWidthProperty =
        AvaloniaProperty.Register<ColumnSplitter, double>(nameof(ColumnWidth), 100, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<ColumnSplitter, double>(nameof(MinColumnWidth), 56);

    public static readonly StyledProperty<double> MaxColumnWidthProperty =
        AvaloniaProperty.Register<ColumnSplitter, double>(nameof(MaxColumnWidth), 800);

    public double ColumnWidth
    {
        get => GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double MaxColumnWidth
    {
        get => GetValue(MaxColumnWidthProperty);
        set => SetValue(MaxColumnWidthProperty, value);
    }

    private bool _drag;
    private double _start;
    private double _origin;
    public ColumnSplitter()
    {
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        Focusable = false;
        Background = Brushes.Transparent;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _drag = true;
        _start = ColumnWidth;
        _origin = e.GetPosition(null).X;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag)
            return;
        ColumnWidth = Math.Clamp(_start + e.GetPosition(null).X - _origin, MinColumnWidth, MaxColumnWidth);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_drag)
            return;
        _drag = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _drag = false;
        base.OnPointerCaptureLost(e);
    }
}
