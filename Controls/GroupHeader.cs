using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MacExplorer.Converters;
using MacExplorer.Models;

namespace MacExplorer.Controls;

public sealed class GroupHeader : StackPanel
{
    public GroupHeader()
    {
        Margin = new Thickness(0, 8, 0, 4);
        Spacing = 0;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Hand);
        Classes.Add("GroupHeader");

        var title = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(FileGroup.Text)));

        var countInline = new TextBlock
        {
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        countInline.Classes.Add("ColSecondary");
        countInline.Bind(TextBlock.TextProperty, new Binding(nameof(FileGroup.CountText)));
        countInline.Bind(IsVisibleProperty, new Binding(nameof(FileGroup.ShowCountTextBelow))
        {
            Converter = InvertBoolConverter.Instance
        });

        var subtext = new TextBlock
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        subtext.Classes.Add("ColSecondary");
        subtext.Bind(TextBlock.TextProperty, new Binding(nameof(FileGroup.Subtext)));
        subtext.Bind(IsVisibleProperty, new Binding(nameof(FileGroup.ShowCountTextBelow)));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(title);
        row.Children.Add(countInline);
        row.Children.Add(subtext);
        Children.Add(row);

        var countBelow = new TextBlock { Margin = new Thickness(0, 2, 0, 0) };
        countBelow.Classes.Add("ColSecondary");
        countBelow.Bind(TextBlock.TextProperty, new Binding(nameof(FileGroup.CountText)));
        countBelow.Bind(IsVisibleProperty, new Binding(nameof(FileGroup.ShowCountTextBelow)));
        Children.Add(countBelow);
    }
}
