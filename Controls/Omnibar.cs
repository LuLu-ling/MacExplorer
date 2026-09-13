using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace MacExplorer.Controls;

public sealed class OmnibarQuerySubmittedEventArgs(OmnibarMode mode, string text) : EventArgs
{
    public OmnibarMode Mode { get; } = mode;
    public string Text { get; } = text;
}

[PseudoClasses(":focused")]
public sealed class Omnibar : TemplatedControl
{
    public static readonly StyledProperty<AvaloniaList<OmnibarMode>> ModesProperty =
        AvaloniaProperty.Register<Omnibar, AvaloniaList<OmnibarMode>>(nameof(Modes));

    public static readonly StyledProperty<OmnibarMode?> CurrentSelectedModeProperty =
        AvaloniaProperty.Register<Omnibar, OmnibarMode?>(nameof(CurrentSelectedMode));

    public static readonly StyledProperty<string?> CurrentSelectedModeNameProperty =
        AvaloniaProperty.Register<Omnibar, string?>(nameof(CurrentSelectedModeName));

    public static readonly StyledProperty<Thickness> AutoSuggestBoxPaddingProperty =
        AvaloniaProperty.Register<Omnibar, Thickness>(nameof(AutoSuggestBoxPadding));

    public static readonly StyledProperty<bool> IsOmnibarFocusedProperty =
        AvaloniaProperty.Register<Omnibar, bool>(nameof(IsOmnibarFocused));

    private TextBox? _textBox;
    private Grid? _modesHost;
    private Popup? _suggestionsPopup;
    private Border? _suggestionsBorder;
    private ListBox? _suggestionsList;
    private bool _populated;

    public event EventHandler<OmnibarQuerySubmittedEventArgs>? QuerySubmitted;
    public event EventHandler<OmnibarMode?>? ModeChanged;

    public Omnibar()
    {
        Modes = [];
    }

    public AvaloniaList<OmnibarMode> Modes
    {
        get => GetValue(ModesProperty);
        set => SetValue(ModesProperty, value);
    }

    public OmnibarMode? CurrentSelectedMode
    {
        get => GetValue(CurrentSelectedModeProperty);
        set => SetValue(CurrentSelectedModeProperty, value);
    }

    public string? CurrentSelectedModeName
    {
        get => GetValue(CurrentSelectedModeNameProperty);
        set => SetValue(CurrentSelectedModeNameProperty, value);
    }

    public Thickness AutoSuggestBoxPadding
    {
        get => GetValue(AutoSuggestBoxPaddingProperty);
        set => SetValue(AutoSuggestBoxPaddingProperty, value);
    }

    public bool IsOmnibarFocused
    {
        get => GetValue(IsOmnibarFocusedProperty);
        set => SetValue(IsOmnibarFocusedProperty, value);
    }

    public void FocusMode(string name)
    {
        var mode = Modes.FirstOrDefault(m => m.Name == name || m.ModeName == name);
        if (mode is not null)
            CurrentSelectedMode = mode;
        FocusTextBox();
    }

    public void FocusTextBox()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _textBox?.Focus();
            _textBox?.SelectAll();
        }, DispatcherPriority.Input);
    }

    public void SyncTextFromMode(OmnibarMode mode)
    {
        if (mode != CurrentSelectedMode || _textBox is null)
            return;
        if (!string.Equals(_textBox.Text, mode.Text, StringComparison.Ordinal))
            _textBox.Text = mode.Text ?? string.Empty;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_textBox is not null)
        {
            _textBox.GotFocus -= OnTextGotFocus;
            _textBox.LostFocus -= OnTextLostFocus;
            _textBox.KeyDown -= OnTextKeyDown;
            _textBox.TextChanged -= OnTextChanged;
        }

        _textBox = e.NameScope.Find<TextBox>("PART_TextBox");
        _modesHost = e.NameScope.Find<Grid>("PART_ModesHostGrid");
        _suggestionsPopup = e.NameScope.Find<Popup>("PART_SuggestionsPopup");
        _suggestionsBorder = e.NameScope.Find<Border>("PART_SuggestionsContainerBorder");
        _suggestionsList = e.NameScope.Find<ListBox>("PART_SuggestionsListView");

        if (_textBox is not null)
        {
            _textBox.GotFocus += OnTextGotFocus;
            _textBox.LostFocus += OnTextLostFocus;
            _textBox.KeyDown += OnTextKeyDown;
            _textBox.TextChanged += OnTextChanged;
        }

        SizeChanged += (_, _) =>
        {
            if (_suggestionsBorder is not null)
                _suggestionsBorder.Width = Bounds.Width;
        };

        PopulateModes();
        if (CurrentSelectedMode is not null)
            ChangeMode(null, CurrentSelectedMode);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CurrentSelectedModeProperty)
        {
            var next = change.NewValue as OmnibarMode;
            ChangeMode(change.OldValue as OmnibarMode, next);
            CurrentSelectedModeName = next?.Name ?? next?.ModeName;
            ModeChanged?.Invoke(this, next);
        }
        else if (change.Property == CurrentSelectedModeNameProperty)
        {
            var name = change.NewValue as string;
            if (string.IsNullOrEmpty(name) || Modes.Count == 0)
                return;
            var match = Modes.FirstOrDefault(m => m.Name == name || m.ModeName == name);
            if (match is not null && match != CurrentSelectedMode)
                CurrentSelectedMode = match;
        }
        else if (change.Property == IsOmnibarFocusedProperty)
        {
            PseudoClasses.Set(":focused", IsOmnibarFocused);
            ApplyFocusVisuals();
        }
        else if (change.Property == ModesProperty)
        {
            _populated = false;
            PopulateModes();
        }
    }

    private void PopulateModes()
    {
        if (_populated || _modesHost is null)
            return;
        _modesHost.Children.Clear();
        _modesHost.ColumnDefinitions.Clear();
        foreach (var mode in Modes)
        {
            if (_modesHost.Children.Count > 0)
            {
                _modesHost.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                var divider = new OmnibarModeSeparator();
                Grid.SetColumn(divider, _modesHost.Children.Count);
                _modesHost.Children.Add(divider);
            }

            _modesHost.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(mode, _modesHost.Children.Count);
            _modesHost.Children.Add(mode);
            mode.SetOwner(this);
        }

        _populated = true;
    }

    private void ChangeMode(OmnibarMode? oldMode, OmnibarMode? newMode)
    {
        if (_modesHost is null || newMode is null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_modesHost is null)
                return;
            foreach (var column in _modesHost.ColumnDefinitions)
                column.Width = GridLength.Auto;
            var index = _modesHost.Children.IndexOf(newMode);
            if (index >= 0)
                _modesHost.ColumnDefinitions[index].Width = new GridLength(1, GridUnitType.Star);
            UpdateTextPadding(newMode);
        }, DispatcherPriority.Loaded);

        if (_textBox is not null)
        {
            _textBox.PlaceholderText = newMode.PlaceholderText ?? string.Empty;
            _textBox.Text = newMode.Text ?? string.Empty;
        }

        if (_suggestionsList is not null)
        {
            _suggestionsList.ItemsSource = newMode.ItemsSource as System.Collections.IEnumerable;
            _suggestionsList.ItemTemplate = newMode.ItemTemplate;
        }

        ApplyFocusVisuals();

        if (newMode.IsAutoFocusEnabled)
            FocusTextBox();
    }

    private void UpdateTextPadding(OmnibarMode newMode)
    {
        if (_modesHost is null)
            return;
        const double modeButtonWidth = 46;
        var itemCount = Modes.Count;
        var itemIndex = Modes.IndexOf(newMode);
        if (itemIndex < 0)
            return;
        var separatorWidth = itemCount is 0 or 1
            ? 0
            : _modesHost.Children.Count > 1 && _modesHost.Children[1] is Layoutable sep
                ? sep.Bounds.Width
                : 9;
        var left = (itemIndex + 1) * modeButtonWidth + separatorWidth * itemIndex;
        var right = (itemCount - itemIndex - 1) * modeButtonWidth
                    + separatorWidth * (itemCount - itemIndex - 1) + 8;
        AutoSuggestBoxPadding = new Thickness(left, 0, right, 0);
    }

    private void ApplyFocusVisuals()
    {
        foreach (var mode in Modes)
            mode.SetModeVisualState(false, false);

        if (CurrentSelectedMode is null || _textBox is null)
            return;

        if (IsOmnibarFocused)
        {
            CurrentSelectedMode.SetModeVisualState(true, false);
            _textBox.Opacity = 1;
            _textBox.IsHitTestVisible = true;
        }
        else if (CurrentSelectedMode.ContentOnInactive is not null)
        {
            CurrentSelectedMode.SetModeVisualState(false, true);
            _textBox.Opacity = 0;
            _textBox.IsHitTestVisible = false;
        }
        else
        {
            _textBox.Opacity = 1;
            _textBox.IsHitTestVisible = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Source is Button || IsOmnibarFocused)
            return;
        FocusTextBox();
    }

    private void OnTextGotFocus(object? sender, EventArgs e)
    {
        IsOmnibarFocused = true;
        _textBox?.SelectAll();
    }

    private void OnTextLostFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsKeyboardFocusWithin)
                return;
            IsOmnibarFocused = false;
        });
    }

    private void OnTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (CurrentSelectedMode is not null)
                QuerySubmitted?.Invoke(this, new OmnibarQuerySubmittedEventArgs(CurrentSelectedMode, _textBox?.Text ?? string.Empty));
            IsOmnibarFocused = false;
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            IsOmnibarFocused = false;
        }
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (CurrentSelectedMode is null || _textBox is null)
            return;
        if (!string.Equals(CurrentSelectedMode.Text, _textBox.Text, StringComparison.Ordinal))
            CurrentSelectedMode.Text = _textBox.Text;
    }
}
