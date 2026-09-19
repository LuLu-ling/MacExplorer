using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using MacExplorer.ViewModels;
namespace MacExplorer.Controls;

internal static class TabDragPreview
{
    private static Window? _window;
    private static PixelPoint _grab;

    public static void Show(ExplorerTabViewModel tab, PixelPoint screen, PixelPoint grab, double width)
    {
        _grab = grab;
        Ensure();
        _window!.Content = Build(tab, width);
        _window.Width = width;
        _window.Height = 32;
        if (!_window.IsVisible)
            _window.Show();
        Move(screen);
    }

    public static void Move(PixelPoint screen)
    {
        if (_window is null)
            return;
        _window.Position = new PixelPoint(screen.X - _grab.X, screen.Y - _grab.Y);
    }

    public static void Hide()
    {
        if (_window is null)
            return;
        _window.Hide();
        _window.Content = null;
    }

    private static void Ensure()
    {
        if (_window is not null)
            return;
        _window = new Window
        {
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            IsHitTestVisible = false,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            SizeToContent = SizeToContent.Manual
        };
    }

    private static Control Build(ExplorerTabViewModel tab, double width)
    {
        var symbols = Application.Current?.TryGetResource("SymbolThemeFontFamily", null, out var font) == true
            && font is FontFamily family
            ? family
            : FontFamily.Default;

        var icon = tab.HasMarker
            ? new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = tab.Marker,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
            : (Control)new TextBlock
            {
                FontFamily = symbols,
                FontSize = 16,
                Width = 16,
                Text = tab.Glyph,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#808080"))
            };
        var title = new TextBlock
        {
            Classes = { "TabTitle" },
            Text = tab.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(10, 0, 6, 0)
        };
        grid.Children.Add(new Panel { Width = 16, Height = 16, Children = { icon } });
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        return new Border
        {
            Classes = { "TabItem", "selected" },
            Width = width,
            Height = 32,
            Opacity = 0.92,
            IsHitTestVisible = false,
            Child = grid
        };
    }
}
