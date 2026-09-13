using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using MacExplorer.Services;

namespace MacExplorer.Controls;

internal static class FileDragTip
{
    private static OverlayLayer? _layer;
    private static Border? _root;
    private static TextBlock? _label;
    private static DispatcherTimer? _hide;

    public static void Show(DragEventArgs e, DragDropEffects effect, string? destination)
    {
        if (e.Source is not Visual visual)
            return;
        var layer = OverlayLayer.GetOverlayLayer(visual);
        if (layer is null)
            return;

        Ensure(layer);
        _hide?.Stop();

        var text = FileDrag.Caption(effect, destination);
        if (_label!.Text != text)
            _label.Text = text;

        _root!.Margin = default;
        _root.IsVisible = true;
        _root.Measure(Size.Infinity);
        var size = _root.DesiredSize;
        var pos = e.GetPosition(layer);
        var bounds = layer.Bounds.Size;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            bounds = layer.AvailableSize;
        var maxX = Math.Max(8, bounds.Width - size.Width - 8);
        var maxY = Math.Max(8, bounds.Height - size.Height - 8);
        Canvas.SetLeft(_root, Math.Clamp(pos.X + 16, 8, maxX));
        Canvas.SetTop(_root, Math.Clamp(pos.Y + 18, 8, maxY));
    }



    public static void Hide()
    {
        if (_root is null || !_root.IsVisible)
            return;
        _hide ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _hide.Tick -= OnHideTick;
        _hide.Tick += OnHideTick;
        _hide.Stop();
        _hide.Start();
    }

    private static void OnHideTick(object? sender, EventArgs e)
    {
        _hide?.Stop();
        if (_root is not null)
            _root.IsVisible = false;
    }

    private static void Ensure(OverlayLayer layer)
    {
        if (ReferenceEquals(_layer, layer) && _root is not null)
            return;

        if (_root is not null)
            _layer?.Children.Remove(_root);

        _layer = layer;
        _label = new TextBlock { FontSize = 12 };
        _root = new Border
        {
            Classes = { "FileDragTip" },
            Child = _label,
            IsHitTestVisible = false,
            ZIndex = 1000,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsVisible = false
        };
        layer.Children.Add(_root);
    }
}
