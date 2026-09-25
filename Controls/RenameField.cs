using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using MacExplorer.Native;

namespace MacExplorer.Controls;

internal sealed class RenameField : Panel
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<RenameField, string>(nameof(Text), "", defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> FontSizeProperty =
        TextBlock.FontSizeProperty.AddOwner<RenameField>();

    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        TextBlock.TextAlignmentProperty.AddOwner<RenameField>();

    public static readonly RoutedEvent<RoutedEventArgs> CommitEvent =
        RoutedEvent.Register<RenameField, RoutedEventArgs>(nameof(Commit), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> CancelEvent =
        RoutedEvent.Register<RenameField, RoutedEventArgs>(nameof(Cancel), RoutingStrategies.Bubble);

    private Host? _host;

    public RenameField()
    {
        Focusable = false;
        IsHitTestVisible = true;
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? Commit
    {
        add => AddHandler(CommitEvent, value);
        remove => RemoveHandler(CommitEvent, value);
    }

    public event EventHandler<RoutedEventArgs>? Cancel
    {
        add => AddHandler(CancelEvent, value);
        remove => RemoveHandler(CancelEvent, value);
    }

    public static bool IsSource(object? source) =>
        source is Visual visual && visual.FindAncestorOfType<RenameField>(includeSelf: true) is not null;

    protected override Size MeasureOverride(Size available)
    {
        var font = FontSize is > 0 and var size && !double.IsNaN(size) ? size : 13;
        var preferred = font + 8;
        var height = double.IsPositiveInfinity(available.Height) || available.Height <= 0
            ? preferred
            : Math.Min(available.Height, preferred);
        var width = double.IsPositiveInfinity(available.Width) ? 0 : available.Width;
        var slot = new Size(width, height);
        foreach (var child in Children)
            child.Measure(slot);
        return slot;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rect = new Rect(finalSize);
        foreach (var child in Children)
            child.Arrange(rect);
        return finalSize;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncHost();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        var commit = _host is not null && IsVisible;
        UntrackRoot();
        DetachHost();
        base.OnDetachedFromVisualTree(e);
        if (commit)
            RaiseEvent(new RoutedEventArgs(CommitEvent));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            SyncHost();
    }

    private Control? _layoutRoot;

    private void OnRootLayout(object? sender, EventArgs e) => SyncHost();

    private void TrackRoot()
    {
        if (VisualRoot is not Control root || ReferenceEquals(_layoutRoot, root))
            return;
        UntrackRoot();
        _layoutRoot = root;
        root.LayoutUpdated += OnRootLayout;
    }

    private void UntrackRoot()
    {
        if (_layoutRoot is null)
            return;
        _layoutRoot.LayoutUpdated -= OnRootLayout;
        _layoutRoot = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            e.Handled = true;
    }

    private void SyncHost()
    {
        if (IsEffectivelyVisible && VisualRoot is not null)
        {
            AttachHost();
            TrackRoot();
        }
        else
        {
            UntrackRoot();
            DetachHost();
        }
    }

    private void AttachHost()
    {
        if (_host is not null)
            return;
        _host = new Host(this);
        Children.Add(_host);
    }

    private void DetachHost()
    {
        if (_host is null)
            return;
        Children.Remove(_host);
        _host = null;
    }

    private sealed class Host : NativeControlHost
    {
        private readonly RenameField _slot;
        private MacRenameField? _field;

        public Host(RenameField slot)
        {
            _slot = slot;
            Focusable = false;
            LayoutUpdated += (_, _) => SyncFrame();
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var arranged = base.ArrangeOverride(finalSize);
            SyncFrame();
            return arranged;
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            var font = _slot.FontSize is > 0 and var size && !double.IsNaN(size) ? size : 13;
            _field = MacRenameField.Open(_slot.Text ?? "", font, _slot.TextAlignment == TextAlignment.Center);
            _field.TextChanged += OnText;
            _field.CommitRequested += OnCommit;
            _field.CancelRequested += OnCancel;
            MacAppearance.ApplyTo(_field.View);
            return new PlatformHandle(_field.View, "NSView");
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            if (_field is null)
            {
                base.DestroyNativeControlCore(control);
                return;
            }

            _field.TextChanged -= OnText;
            _field.CommitRequested -= OnCommit;
            _field.CancelRequested -= OnCancel;
            _field.Dispose();
            _field = null;
        }

        private void OnText(string value)
        {
            if (_slot.Text != value)
                _slot.Text = value;
        }

        private void OnCommit()
        {
            if (_slot.IsVisible)
                _slot.RaiseEvent(new RoutedEventArgs(CommitEvent));
        }

        private void OnCancel()
        {
            if (_slot.IsVisible)
                _slot.RaiseEvent(new RoutedEventArgs(CancelEvent));
        }

        private void SyncFrame()
        {
            if (_field is null || VisualRoot is not Visual root || _slot.Bounds is { Width: <= 0 } or { Height: <= 0 })
                return;
            TryUpdateNativeControlPosition();
            var origin = _slot.TranslatePoint(new Point(), root);
            var clipped = origin is not { } point || !InView(new Rect(point, _slot.Bounds.Size), root);
            ObjC.MsgSendVoid(_field.View, ObjC.Sel("setHidden:"), clipped);
        }

        private bool InView(Rect rect, Visual root)
        {
            if (!rect.Intersects(new Rect(root.Bounds.Size)))
                return false;
            for (Visual? node = this; node is not null && !ReferenceEquals(node, root); node = node.GetVisualParent())
            {
                if (node is not ScrollViewer scroll || scroll.TranslatePoint(new Point(), root) is not { } origin)
                    continue;
                if (!rect.Intersects(new Rect(origin, scroll.Bounds.Size)))
                    return false;
            }

            return true;
        }
    }
}
