using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MacExplorer.Controls;

internal sealed class RenameTextBox : TextBox
{
    private bool _activationQueued;

    public static bool IsSource(object? source) =>
        source is Visual visual && visual.FindAncestorOfType<RenameTextBox>(includeSelf: true) is not null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        QueueActivation();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
            QueueActivation();
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

    private void QueueActivation()
    {
        if (_activationQueued || !IsVisible)
            return;
        _activationQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _activationQueued = false;
            if (!IsEffectivelyVisible || IsKeyboardFocusWithin)
                return;
            Focus();
            SelectAll();
        }, DispatcherPriority.Loaded);
    }
}
