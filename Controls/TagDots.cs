using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using MacExplorer.Models;

namespace MacExplorer.Controls;

public sealed class TagDots : StackPanel
{
    public static readonly StyledProperty<IReadOnlyList<FileTag>?> ItemsSourceProperty =
        AvaloniaProperty.Register<TagDots, IReadOnlyList<FileTag>?>(nameof(ItemsSource));

    public IReadOnlyList<FileTag>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    static TagDots()
    {
        ItemsSourceProperty.Changed.AddClassHandler<TagDots>(static (c, _) => c.Rebuild());
    }

    public TagDots()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 3;
        MinHeight = 8;
        IsHitTestVisible = false;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    private void Rebuild()
    {
        Children.Clear();
        var tags = ItemsSource;
        if (tags is null || tags.Count == 0)
            return;
        foreach (var tag in tags)
        {
            Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = tag.Brush
            });
        }
    }
}
