using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using MacExplorer.Models;
using MacExplorer.ViewModels;

namespace MacExplorer.Controls;

public sealed class ColumnStripPanel : Panel
{
    public static readonly AttachedProperty<DetailsColumnKind> ColumnProperty =
        AvaloniaProperty.RegisterAttached<ColumnStripPanel, Control, DetailsColumnKind>("Column");

    public static readonly StyledProperty<DetailsColumns?> ColumnsProperty =
        AvaloniaProperty.Register<ColumnStripPanel, DetailsColumns?>(nameof(Columns));

    public DetailsColumns? Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public static DetailsColumnKind GetColumn(AvaloniaObject obj) => obj.GetValue(ColumnProperty);

    public static void SetColumn(AvaloniaObject obj, DetailsColumnKind value) => obj.SetValue(ColumnProperty, value);

    private DetailsColumns? _hooked;

    public ColumnStripPanel()
    {
        ClipToBounds = false;
        Focusable = false;
        Children.CollectionChanged += (_, _) => InvalidateMeasure();
    }

    public Control[] Units()
    {
        var units = new List<Control>(Children.Count);
        foreach (var child in VisibleChildren())
        {
            if (child is ColumnUnit { IsShown: false })
                continue;
            units.Add(child);
        }

        return units.ToArray();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Hook(Columns);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Hook(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColumnsProperty)
        {
            Hook(Columns);
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var constraint = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in Children)
            child.Measure(constraint);

        var width = 0.0;
        var height = 0.0;
        foreach (var child in VisibleChildren())
        {
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var placed = new HashSet<Control>();
        foreach (var child in VisibleChildren())
        {
            placed.Add(child);
            var width = child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        foreach (var child in Children)
        {
            if (placed.Contains(child))
                continue;
            child.Arrange(default);
        }

        return new Size(Math.Max(x, finalSize.Width), finalSize.Height);
    }

    private IEnumerable<Control> VisibleChildren()
    {
        if (Columns is { } columns)
        {
            foreach (var kind in columns.Order)
            {
                if (Child(kind) is { } child)
                    yield return child;
            }

            yield break;
        }

        foreach (var child in Children)
            yield return child;
    }

    private Control? Child(DetailsColumnKind kind)
    {
        foreach (var child in Children)
        {
            if (GetColumn(child) == kind)
                return child;
        }

        return null;
    }

    private void Hook(DetailsColumns? columns)
    {
        if (ReferenceEquals(_hooked, columns))
            return;
        if (_hooked is not null)
            _hooked.PropertyChanged -= OnColumnsChanged;
        _hooked = columns;
        if (_hooked is not null)
            _hooked.PropertyChanged += OnColumnsChanged;
    }

    private void OnColumnsChanged(object? sender, PropertyChangedEventArgs e) => InvalidateMeasure();
}
