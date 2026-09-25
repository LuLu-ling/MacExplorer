using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace MacExplorer.Controls;

public class SectionExpander : ContentControl
{
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<SectionExpander, bool>(nameof(IsExpanded), true);

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SectionExpander, string?>(nameof(Title));

    private Border? _header;

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_header is not null)
            _header.PointerReleased -= OnHeaderReleased;
        base.OnApplyTemplate(e);
        _header = e.NameScope.Find<Border>("PART_Header");
        if (_header is null)
            return;
        _header.Margin = new Thickness(1.6, 2, 1.6, 2);
        _header.PointerReleased += OnHeaderReleased;
    }

    private void OnHeaderReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        e.Handled = true;
        IsExpanded = !IsExpanded;
    }
}
