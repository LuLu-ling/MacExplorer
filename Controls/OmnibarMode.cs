using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;

namespace MacExplorer.Controls;
[Avalonia.Controls.Metadata.PseudoClasses(":focused", ":current-unfocused")]
public sealed class OmnibarMode : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<OmnibarMode, string?>(nameof(Text));

    public static readonly StyledProperty<bool> IsDefaultProperty =
        AvaloniaProperty.Register<OmnibarMode, bool>(nameof(IsDefault));

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<OmnibarMode, string?>(nameof(PlaceholderText));

    public static readonly StyledProperty<string?> ModeNameProperty =
        AvaloniaProperty.Register<OmnibarMode, string?>(nameof(ModeName));

    public static readonly StyledProperty<object?> ContentOnInactiveProperty =
        AvaloniaProperty.Register<OmnibarMode, object?>(nameof(ContentOnInactive));

    public static readonly StyledProperty<object?> IconOnActiveProperty =
        AvaloniaProperty.Register<OmnibarMode, object?>(nameof(IconOnActive));

    public static readonly StyledProperty<object?> IconOnInactiveProperty =
        AvaloniaProperty.Register<OmnibarMode, object?>(nameof(IconOnInactive));

    public static readonly StyledProperty<object?> ItemsSourceProperty =
        AvaloniaProperty.Register<OmnibarMode, object?>(nameof(ItemsSource));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<OmnibarMode, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<bool> IsAutoFocusEnabledProperty =
        AvaloniaProperty.Register<OmnibarMode, bool>(nameof(IsAutoFocusEnabled));

    private WeakReference<Omnibar>? _owner;
    private Button? _modeButton;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsDefault
    {
        get => GetValue(IsDefaultProperty);
        set => SetValue(IsDefaultProperty, value);
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public string? ModeName
    {
        get => GetValue(ModeNameProperty);
        set => SetValue(ModeNameProperty, value);
    }

    public object? ContentOnInactive
    {
        get => GetValue(ContentOnInactiveProperty);
        set => SetValue(ContentOnInactiveProperty, value);
    }

    public object? IconOnActive
    {
        get => GetValue(IconOnActiveProperty);
        set => SetValue(IconOnActiveProperty, value);
    }

    public object? IconOnInactive
    {
        get => GetValue(IconOnInactiveProperty);
        set => SetValue(IconOnInactiveProperty, value);
    }

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public bool IsAutoFocusEnabled
    {
        get => GetValue(IsAutoFocusEnabledProperty);
        set => SetValue(IsAutoFocusEnabledProperty, value);
    }
    public void SetOwner(Omnibar owner) => _owner = new WeakReference<Omnibar>(owner);

    public void SetModeVisualState(bool focused, bool currentUnfocused)
    {
        PseudoClasses.Set(":focused", focused);
        PseudoClasses.Set(":current-unfocused", currentUnfocused);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_modeButton is not null)
            _modeButton.Click -= ModeButton_Click;
        _modeButton = e.NameScope.Find<Button>("PART_ModeButton");
        if (_modeButton is not null)
            _modeButton.Click += ModeButton_Click;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (IsDefault && _owner?.TryGetTarget(out var owner) == true)
            owner.CurrentSelectedMode = this;
    }

    private void ModeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_owner is null || !_owner.TryGetTarget(out var owner) || owner.CurrentSelectedMode == this)
            return;
        owner.CurrentSelectedMode = this;
        if (IsAutoFocusEnabled)
            owner.FocusTextBox();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && _owner?.TryGetTarget(out var owner) == true)
            owner.SyncTextFromMode(this);
    }
}
