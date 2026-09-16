using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MacExplorer.Localization;
namespace MacExplorer.Controls;

[TemplatePart("PART_Editor", typeof(TextBox), IsRequired = true)]
[TemplatePart("PART_Clear", typeof(Button))]
[PseudoClasses(":empty")]
public sealed class SearchField : TemplatedControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SearchField, string>(nameof(Text), string.Empty,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<SearchField, string>(nameof(Placeholder), string.Empty);

    private TextBox? _editor;
    private Button? _clear;
    private TopLevel? _root;

    public SearchField()
    {
        Focusable = true;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Once);
        AutomationProperties.SetName(this, Lang.Text("Search.AutomationName"));
        WeakLanguageChanged.Add(this, static field =>
            AutomationProperties.SetName(field, Lang.Text("Search.AutomationName")));
        UpdateEmpty();
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public void FocusEditor()
    {
        ApplyTemplate();
        _editor?.Focus();
        _editor?.SelectAll();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_editor is not null)
            _editor.RemoveHandler(KeyDownEvent, OnEditorKeyDown);
        if (_clear is not null)
            _clear.Click -= OnClear;

        base.OnApplyTemplate(e);
        _editor = e.NameScope.Find<TextBox>("PART_Editor");
        _clear = e.NameScope.Find<Button>("PART_Clear");
        if (_editor is not null)
            _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        if (_clear is not null)
            _clear.Click += OnClear;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            UpdateEmpty();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _root = TopLevel.GetTopLevel(this);
        ClickOutside.Attach(_root, OnRootPointerPressed);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClickOutside.Detach(_root, OnRootPointerPressed);
        _root = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (e.Source == this)
            FocusEditor();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        for (var visual = e.Source as Visual; visual is not null && visual != this; visual = visual.GetVisualParent())
        {
            if (visual is Button)
                return;
        }

        FocusEditor();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape)
            return;
        e.Handled = true;
        Text = string.Empty;
    }

    private void OnClear(object? sender, RoutedEventArgs e) => Text = string.Empty;

    private void Blur()
    {
        if (IsKeyboardFocusWithin)
            TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsKeyboardFocusWithin || ClickOutside.Hits(this, e.Source))
            return;
        Blur();
    }

    private void UpdateEmpty() => PseudoClasses.Set(":empty", string.IsNullOrEmpty(Text));
}
