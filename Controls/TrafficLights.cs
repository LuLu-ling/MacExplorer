using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacExplorer.Native;

namespace MacExplorer.Controls;

public sealed class TrafficLights : Control
{
    private static readonly IBrush CloseFill = Brush(0xFF5F57);
    private static readonly IBrush ClosePress = Brush(0xBF4A42);
    private static readonly IBrush MiniFill = Brush(0xFEBC2E);
    private static readonly IBrush MiniPress = Brush(0xBF8E1F);
    private static readonly IBrush ZoomFill = Brush(0x28C840);
    private static readonly IBrush ZoomPress = Brush(0x1D9A2E);
    private static readonly IBrush CloseGlyph = Brush(0x4D0000);
    private static readonly IBrush MiniGlyph = Brush(0x995700);
    private static readonly IBrush ZoomGlyph = Brush(0x006400);
    private static readonly IBrush IdleLight = Brush(0xDEDEDE);
    private static readonly IBrush IdleDark = Brush(0x5A5A5C);
    private static readonly IBrush IdleStrokeLight = Brush(0xC8C8C8);
    private static readonly IBrush IdleStrokeDark = Brush(0x3A3A3C);

    private Window? _window;
    private bool _groupHover;
    private int _pressed = -1;
    private Rect _zoomClient;

    public TrafficLights()
    {
        Focusable = false;
        ClipToBounds = false;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(WindowChrome.TrafficLightClusterWidth, WindowChrome.TrafficLightDiameter);

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        Dispatcher.UIThread.Post(SyncNative, DispatcherPriority.Loaded);
        return size;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = this.FindAncestorOfType<Window>() ?? TopLevel.GetTopLevel(this) as Window;
        if (_window is null)
            return;
        _window.Opened += OnChrome;
        _window.Activated += OnActive;
        _window.Deactivated += OnDeactivated;
        _window.SizeChanged += OnSize;
        _window.LayoutUpdated += OnLayout;
        _window.PropertyChanged += OnWindowProperty;
        _window.AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel, true);
        SyncNative();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
        {
            _window.Opened -= OnChrome;
            _window.Activated -= OnActive;
            _window.Deactivated -= OnDeactivated;
            _window.SizeChanged -= OnSize;
            _window.LayoutUpdated -= OnLayout;
            _window.PropertyChanged -= OnWindowProperty;
            _window.RemoveHandler(PointerMovedEvent, OnWindowPointerMoved);
            MacCaptionButtons.HideZoom(_window);
        }

        _window = null;
        _zoomClient = default;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerEntered(PointerEventArgs e) =>
        SetGroupHover(InLightGroup(e.GetPosition(this)));

    protected override void OnPointerMoved(PointerEventArgs e) =>
        SetGroupHover(InLightGroup(e.GetPosition(this)));

    protected override void OnPointerExited(PointerEventArgs e)
    {
        if (_pressed >= 0 || InLightGroup(e.GetPosition(this)))
            return;
        SetGroupHover(false);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var index = IndexAt(e.GetPosition(this));
        if (index < 0 || index == 2)
            return;
        e.Handled = true;
        _pressed = index;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_pressed < 0)
            return;
        e.Handled = true;
        var index = _pressed;
        var inside = IndexAt(e.GetPosition(this)) == index;
        _pressed = -1;
        e.Pointer.Capture(null);
        if (inside)
            Invoke(index);
        SetGroupHover(InLightGroup(e.GetPosition(this)));
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _pressed = -1;
        SetGroupHover(IsPointerOver);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, GroupRect());
        var colorize = _groupHover || _window is { IsActive: true };
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var idle = dark ? IdleDark : IdleLight;
        var idleStroke = dark ? IdleStrokeDark : IdleStrokeLight;

        for (var i = 0; i < 3; i++)
        {
            var rect = LightRect(i);
            var fill = colorize ? Fill(i, _pressed == i) : idle;
            var stroke = colorize ? null : new Pen(idleStroke, 0.5);
            var radius = rect.Width / 2;
            context.DrawEllipse(fill, stroke, rect.Center, radius, radius);
            if (_groupHover)
                DrawGlyph(context, i, rect);
        }
    }

    private void Invoke(int index)
    {
        if (_window is null)
            return;
        switch (index)
        {
            case 0:
                _window.Close();
                break;
            case 1:
                if (_window.CanMinimize)
                    _window.WindowState = WindowState.Minimized;
                break;
        }
    }

    private Rect LightRect(int index)
    {
        var diameter = WindowChrome.TrafficLightDiameter;
        var gap = WindowChrome.TrafficLightGap;
        var cluster = WindowChrome.TrafficLightClusterWidth;
        var x = (Bounds.Width - cluster) / 2 + index * (diameter + gap);
        var y = (Bounds.Height - diameter) / 2;
        return new Rect(x, y, diameter, diameter);
    }

    private Rect GroupRect()
    {
        var first = LightRect(0);
        var last = LightRect(2);
        const double slop = 5;
        return new Rect(
            first.X - slop,
            first.Y - slop,
            last.Right - first.X + slop * 2,
            first.Height + slop * 2);
    }

    private bool InLightGroup(Point point) => GroupRect().Contains(point);

    private int IndexAt(Point point)
    {
        var radius = WindowChrome.TrafficLightDiameter / 2 + 1;
        var r2 = radius * radius;
        for (var i = 0; i < 3; i++)
        {
            var c = LightRect(i).Center;
            var dx = point.X - c.X;
            var dy = point.Y - c.Y;
            if (dx * dx + dy * dy <= r2)
                return i;
        }

        return -1;
    }

    private void SetGroupHover(bool value)
    {
        if (_groupHover == value)
            return;
        _groupHover = value;
        InvalidateVisual();
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed >= 0)
            return;
        SetGroupHover(InLightGroup(e.GetPosition(this)));
    }

    private void OnChrome(object? sender, EventArgs e) => SyncNative();

    private void OnSize(object? sender, SizeChangedEventArgs e)
    {
        _zoomClient = default;
        SyncNative();
    }

    private void OnLayout(object? sender, EventArgs e)
    {
        if (_window is not null)
            MacCaptionButtons.HideCloseAndMini(_window);
    }

    private void OnActive(object? sender, EventArgs e) => InvalidateVisual();

    private void OnDeactivated(object? sender, EventArgs e)
    {
        SetGroupHover(false);
        InvalidateVisual();
    }

    private void OnWindowProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            _zoomClient = default;
            SyncNative();
        }
    }

    private void SyncNative()
    {
        if (_window is null)
            return;

        var fullScreen = _window.WindowState == WindowState.FullScreen;
        IsVisible = !fullScreen;
        MacCaptionButtons.HideCloseAndMini(_window);
        if (fullScreen || Bounds.Width <= 0)
        {
            MacCaptionButtons.HideZoom(_window);
            _zoomClient = default;
            SetGroupHover(false);
            return;
        }

        var green = LightRect(2);
        var origin = this.TranslatePoint(green.Position, _window);
        if (origin is null)
            return;
        var client = new Rect(origin.Value, green.Size);
        if (Near(client, _zoomClient))
            return;
        MacCaptionButtons.PlaceZoom(_window, client);
        _zoomClient = client;
    }

    private static bool Near(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) < 0.5 &&
        Math.Abs(a.Width - b.Width) < 0.5 && Math.Abs(a.Height - b.Height) < 0.5;

    private static IBrush Fill(int index, bool pressed) => index switch
    {
        0 => pressed ? ClosePress : CloseFill,
        1 => pressed ? MiniPress : MiniFill,
        _ => pressed ? ZoomPress : ZoomFill
    };

    private static void DrawGlyph(DrawingContext context, int index, Rect rect)
    {
        var inset = rect.Width * (3.2 / 12);
        var pen = new Pen(index switch
        {
            0 => CloseGlyph,
            1 => MiniGlyph,
            _ => ZoomGlyph
        }, rect.Width * (1.05 / 12), lineCap: PenLineCap.Round);
        var x = rect.X + inset;
        var y = rect.Y + inset;
        var s = rect.Width - inset * 2;
        var c = rect.Center;
        switch (index)
        {
            case 0:
                context.DrawLine(pen, new Point(x, y), new Point(x + s, y + s));
                context.DrawLine(pen, new Point(x + s, y), new Point(x, y + s));
                break;
            case 1:
                context.DrawLine(pen, new Point(x, c.Y), new Point(x + s, c.Y));
                break;
            default:
                context.DrawLine(pen, new Point(x, c.Y), new Point(x + s, c.Y));
                context.DrawLine(pen, new Point(c.X, y), new Point(c.X, y + s));
                break;
        }
    }

    private static SolidColorBrush Brush(uint rgb) =>
        new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
}
