using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacExplorer.Localization;
using MacExplorer.Models;
using MacExplorer.Native;
using MacExplorer.Services;

namespace MacExplorer.Controls;

[TemplatePart("PART_Editor", typeof(TextBox), IsRequired = true)]
[TemplatePart("PART_EditorHost", typeof(Control), IsRequired = true)]
[TemplatePart("PART_BreadcrumbHost", typeof(Control), IsRequired = true)]
[TemplatePart("PART_BreadcrumbPanel", typeof(BreadcrumbOverflowPanel), IsRequired = true)]
[PseudoClasses(":editing")]
public sealed class AddressBar : TemplatedControl
{
    public static readonly StyledProperty<string> PathProperty =
        AvaloniaProperty.Register<AddressBar, string>(nameof(Path), string.Empty);

    public static readonly StyledProperty<IReadOnlyList<BreadcrumbItem>> BreadcrumbsProperty =
        AvaloniaProperty.Register<AddressBar, IReadOnlyList<BreadcrumbItem>>(nameof(Breadcrumbs), Array.Empty<BreadcrumbItem>());

    public static readonly StyledProperty<string> RootPathProperty =
        AvaloniaProperty.Register<AddressBar, string>(nameof(RootPath), string.Empty);

    public static readonly DirectProperty<AddressBar, bool> IsEditingProperty =
        AvaloniaProperty.RegisterDirect<AddressBar, bool>(nameof(IsEditing), control => control.IsEditing);

    private TextBox? _editor;
    private Control? _editorHost;
    private Control? _breadcrumbHost;
    private Control? _rootHost;
    private Button? _rootButton;
    private Button? _rootArrow;
    private Button? _overflowButton;
    private BreadcrumbOverflowPanel? _breadcrumbPanel;
    private Window? _window;
    private WeakReference<IInputElement>? _previousFocus;
    private bool _isEditing;
    private bool _restoringFocus;
    private CancellationTokenSource? _menuRequest;

    public AddressBar()
    {
        Focusable = true;
        IsTabStop = true;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Once);
        AutomationProperties.SetName(this, Lang.Text("Address.AutomationName"));
        WeakLanguageChanged.Add(this, static bar =>
        {
            AutomationProperties.SetName(bar, Lang.Text("Address.AutomationName"));
            bar.RebuildBreadcrumbs();
        });
    }

    // Path is the host's committed location. Editing never writes back to its binding.
    public string Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs
    {
        get => GetValue(BreadcrumbsProperty);
        set => SetValue(BreadcrumbsProperty, value);
    }

    // Optional separate Home button, matching Files' NavigationToolbar root item.
    public string RootPath
    {
        get => GetValue(RootPathProperty);
        set => SetValue(RootPathProperty, value);
    }

    public bool IsEditing => _isEditing;

    internal BreadcrumbMenuService? MenuService { get; set; }

    public event EventHandler<string>? PathSubmitted;
    public event EventHandler<string>? OpenInNewWindowRequested;

    public void BeginEdit()
    {
        ApplyTemplate();
        if (_editor is null || !IsEffectivelyEnabled)
            return;

        CancelMenu();

        RememberFocus(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement());
        if (!IsEditing)
            _editor.Text = Path;
        SetEditing(true);
        _editor.Focus();
        _editor.SelectAll();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        CancelMenu();
        if (_editor is not null)
            _editor.RemoveHandler(KeyDownEvent, OnEditorKeyDown);
        if (_rootButton is not null)
        {
            _rootButton.Click -= OnRootClick;
            _rootButton.ContextRequested -= OnBreadcrumbContextRequested;
        }
        if (_rootArrow is not null)
        {
            _rootArrow.Click -= OnArrowClick;
            _rootArrow.ContextRequested -= OnArrowContextRequested;
        }

        base.OnApplyTemplate(e);
        _editor = e.NameScope.Find<TextBox>("PART_Editor");
        _editorHost = e.NameScope.Find<Control>("PART_EditorHost");
        _breadcrumbHost = e.NameScope.Find<Control>("PART_BreadcrumbHost");
        _rootHost = e.NameScope.Find<Control>("PART_RootHost");
        _rootButton = e.NameScope.Find<Button>("PART_RootButton");
        _rootArrow = e.NameScope.Find<Button>("PART_RootArrow");
        _breadcrumbPanel = e.NameScope.Find<BreadcrumbOverflowPanel>("PART_BreadcrumbPanel");
        if (_editor is not null)
        {
            _editor.Text = Path;
            _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        }
        if (_rootButton is not null)
        {
            _rootButton.Click += OnRootClick;
            _rootButton.ContextRequested += OnBreadcrumbContextRequested;
        }
        if (_rootArrow is not null)
        {
            _rootArrow.Click += OnArrowClick;
            _rootArrow.ContextRequested += OnArrowContextRequested;
        }
        RebuildBreadcrumbs();
        SetEditing(false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PathProperty)
        {
            CancelMenu();
            EndEdit(false);
            if (_editor is not null)
                _editor.Text = Path;
        }
        else if (change.Property == BreadcrumbsProperty || change.Property == RootPathProperty)
        {
            EndEdit(false);
            CancelMenu();
            RebuildBreadcrumbs();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
            _window.Deactivated += OnWindowDeactivated;
        ClickOutside.Attach(TopLevel.GetTopLevel(this), OnRootPointerPressed);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClickOutside.Detach(TopLevel.GetTopLevel(this), OnRootPointerPressed);
        CancelMenu();
        if (_window is not null)
            _window.Deactivated -= OnWindowDeactivated;
        _window = null;
        _previousFocus = null;
        SetEditing(false);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        RememberFocus(e.OldFocusedElement);
        if (e.Source == this && !_restoringFocus)
            BeginEdit();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        // Native menu tracking keeps Avalonia focus on the editor. Only a real focus move ends editing.
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsKeyboardFocusWithin)
                CancelMenu();
            if (IsEditing && !IsKeyboardFocusWithin)
                EndEdit(false);
        }, DispatcherPriority.Input);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsEditing || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        for (var visual = e.Source as Visual; visual is not null && visual != this; visual = visual.GetVisualParent())
        {
            if (visual is Button)
                return;
        }
        e.Handled = true;
        BeginEdit();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || IsEditing)
            return;
        if (e.Key is Key.F2 || (e.Key is Key.Enter && e.Source == this))
        {
            e.Handled = true;
            BeginEdit();
        }
        else if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            e.Handled = FocusBreadcrumb(e.Key);
        }
        else if (e.Key is Key.Down)
        {
            if (_overflowButton?.IsFocused == true)
            {
                e.Handled = true;
                ShowOverflow();
            }
            else if (FocusedArrow() is { } arrow)
            {
                e.Handled = true;
                ShowDropdown(arrow);
            }
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape)
        {
            e.Handled = true;
            EndEdit(true);
        }
        else if (e.Key is Key.Enter)
        {
            e.Handled = true;
            var path = _editor?.Text ?? string.Empty;
            EndEdit(true);
            PathSubmitted?.Invoke(this, path);
        }
        else if (e.Key is Key.Down && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            e.Handled = true;
            EndEdit(false);
            FocusBreadcrumb(Key.End);
        }
    }

    private void SetEditing(bool editing)
    {
        SetAndRaise(IsEditingProperty, ref _isEditing, editing);
        PseudoClasses.Set(":editing", editing);
        if (_editorHost is not null)
            _editorHost.IsVisible = editing;
        if (_breadcrumbHost is not null)
            _breadcrumbHost.IsVisible = !editing;
    }

    private void EndEdit(bool restoreFocus)
    {
        if (!IsEditing)
            return;
        SetEditing(false);
        if (_editor is not null)
            _editor.Text = Path;
        if (!restoreFocus)
            return;

        _restoringFocus = true;
        try
        {
            if (_previousFocus?.TryGetTarget(out var target) == true &&
                target is Control control && control.IsEffectivelyVisible && control.IsEffectivelyEnabled &&
                TopLevel.GetTopLevel(control) == TopLevel.GetTopLevel(this) && target.Focus())
                return;
            Focus();
        }
        finally
        {
            _restoringFocus = false;
        }
    }

    private void Blur()
    {
        CancelMenu();
        EndEdit(false);
        if (IsKeyboardFocusWithin)
            TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ClickOutside.Hits(this, e.Source) || (!IsEditing && !IsKeyboardFocusWithin))
            return;
        Blur();
    }

    private void RememberFocus(IInputElement? element)
    {
        if (element is Control control && control != this && !this.IsVisualAncestorOf(control) &&
            TopLevel.GetTopLevel(control) == TopLevel.GetTopLevel(this))
            _previousFocus = new WeakReference<IInputElement>(element);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        CancelMenu();
        EndEdit(true);
    }


    private void RebuildBreadcrumbs()
    {
        if (_rootHost is not null)
            _rootHost.IsVisible = !string.IsNullOrEmpty(RootPath);
        if (_rootButton is not null)
            _rootButton.Tag = RootPath;
        if (_breadcrumbPanel is null)
            return;

        _breadcrumbPanel.Children.Clear();
        _overflowButton = new Button { Content = "\u2026", Classes = { "AddressSegment", "AddressOverflow" } };
        AutomationProperties.SetName(_overflowButton, Lang.Text("Address.ShowHiddenComponents"));
        ToolTip.SetTip(_overflowButton, Lang.Text("Address.ShowHiddenComponents"));
        _overflowButton.Click += (_, _) => ShowOverflow();
        _breadcrumbPanel.Children.Add(_overflowButton);

        foreach (var item in Breadcrumbs)
        {
            var button = new Button
            {
                Tag = item.Path,
                Classes = { "AddressSegment" },
                Content = new TextBlock { Text = item.Title, TextTrimming = TextTrimming.CharacterEllipsis }
            };
            AutomationProperties.SetName(button, item.Title);
            ToolTip.SetTip(button, item.Path);
            button.Click += OnBreadcrumbClick;
            button.ContextRequested += OnBreadcrumbContextRequested;
            var segment = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ClipToBounds = true };
            segment.Children.Add(button);
            if (item.ShowChevron)
            {
                var arrow = new Button
                {
                    Tag = item.Path,
                    Classes = { "AddressSegment", "AddressArrow" },
                    Content = new PathIcon { Data = ChevronGeometry, Classes = { "AddressChevron" } }
                };
                AutomationProperties.SetName(arrow, Lang.Text("Address.ShowSubfolders", item.Title));
                ToolTip.SetTip(arrow, Lang.Text("Address.ShowSubfolders", item.Title));
                arrow.Click += OnArrowClick;
                arrow.ContextRequested += OnArrowContextRequested;
                Grid.SetColumn(arrow, 1);
                segment.Children.Add(arrow);
            }
            _breadcrumbPanel.Children.Add(segment);
        }
    }

    private static readonly Geometry ChevronGeometry = Geometry.Parse("M 1,1 L 5,5 L 1,9 L 2,10 L 7,5 L 2,0 Z");

    private void OnRootClick(object? sender, RoutedEventArgs e)
    {
        CancelMenu();
        PathSubmitted?.Invoke(this, RootPath);
    }

    private void OnBreadcrumbClick(object? sender, RoutedEventArgs e)
    {
        CancelMenu();
        if (sender is Control { Tag: string path })
            PathSubmitted?.Invoke(this, path);
    }

    private void OnBreadcrumbContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { Tag: string path } anchor)
            return;
        e.Handled = true;
        var cancellationToken = StartMenu();
        MacContextMenu.ShowAt(anchor,
            [new(Lang.Text("Tab.OpenInNewWindow"), () => InvokeMenuPath(path, true, cancellationToken))],
            () => IsCurrentMenu(cancellationToken));
    }

    private void ShowOverflow()
    {
        if (_breadcrumbPanel is null || !_breadcrumbPanel.HasOverflow || _overflowButton is null)
            return;
        var count = _breadcrumbPanel.FirstVisibleIndex - 1;
        var folders = new (string Title, string Path)[count];
        for (var i = 0; i < count; i++)
            folders[i] = (Breadcrumbs[i].Title, Breadcrumbs[i].Path);
        var cancellationToken = StartMenu();
        ShowMenu(_overflowButton, [new(null, folders)], cancellationToken);
    }

    private void OnArrowClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button arrow)
            ShowDropdown(arrow);
    }

    private static void OnArrowContextRequested(object? sender, ContextRequestedEventArgs e) => e.Handled = true;

    private async void ShowDropdown(Button arrow)
    {
        if (MenuService is null || !arrow.IsEffectivelyEnabled || IsEditing || _window is null)
            return;
        var cancellationToken = StartMenu();
        var path = arrow == _rootArrow ? null : arrow.Tag as string;
        try
        {
            var sections = await MenuService.LoadAsync(path, cancellationToken);
            if (IsCurrentMenu(cancellationToken))
                ShowMenu(arrow, sections, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ShowMenu(Control anchor, IReadOnlyList<BreadcrumbMenuSection> sections, CancellationToken cancellationToken)
    {
        var entries = new List<MacMenuEntry>();
        var newWindowEntries = new List<MacMenuEntry>();
        foreach (var section in sections)
        {
            if (entries.Count > 0)
                entries.Add(new("", Separator: true));
            if (section.Title is { } title)
                entries.Add(new(title, Enabled: false));
            if (section.Message is { } message)
                entries.Add(new(message, Enabled: false));
            if (section.Folders.Count == 0)
                continue;
            if (newWindowEntries.Count > 0)
                newWindowEntries.Add(new("", Separator: true));
            if (section.Title is { } newWindowTitle)
                newWindowEntries.Add(new(newWindowTitle, Enabled: false));
            foreach (var folder in section.Folders)
            {
                entries.Add(new(folder.Title, () => InvokeMenuPath(folder.Path, false, cancellationToken)));
                newWindowEntries.Add(new(folder.Title, () => InvokeMenuPath(folder.Path, true, cancellationToken)));
            }
        }
        if (newWindowEntries.Count > 0)
        {
            entries.Add(new("", Separator: true));
            entries.Add(new(Lang.Text("Tab.OpenInNewWindow"), Children: newWindowEntries.ToArray()));
        }
        MacContextMenu.ShowAt(anchor, entries, () => IsCurrentMenu(cancellationToken));
    }

    private CancellationToken StartMenu()
    {
        CancelMenu();
        _menuRequest = new CancellationTokenSource();
        return _menuRequest.Token;
    }

    private void CancelMenu()
    {
        _menuRequest?.Cancel();
        _menuRequest?.Dispose();
        _menuRequest = null;
        MacContextMenu.Close(this);
    }

    private bool IsCurrentMenu(CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && !IsEditing && _window is not null;

    private void InvokeMenuPath(string path, bool newWindow, CancellationToken cancellationToken)
    {
        if (!IsCurrentMenu(cancellationToken))
            return;
        CancelMenu();
        if (newWindow)
            OpenInNewWindowRequested?.Invoke(this, path);
        else
            PathSubmitted?.Invoke(this, path);
    }

    private Button? FocusedArrow()
    {
        if (_rootButton?.IsFocused == true || _rootArrow?.IsFocused == true)
            return _rootArrow;
        if (_breadcrumbPanel is not null)
        {
            foreach (var child in _breadcrumbPanel.Children)
            {
                if (child is Grid { Children.Count: 2 } grid && grid.IsKeyboardFocusWithin)
                    return grid.Children[1] as Button;
            }
        }
        return null;
    }

    private bool FocusBreadcrumb(Key key)
    {
        if (_breadcrumbPanel is null)
            return false;
        var buttons = new List<Button>();
        if (_rootHost?.IsVisible == true)
        {
            if (_rootButton is not null)
                buttons.Add(_rootButton);
            if (_rootArrow is not null)
                buttons.Add(_rootArrow);
        }
        if (_breadcrumbPanel.HasOverflow && _overflowButton is not null)
            buttons.Add(_overflowButton);
        for (var i = _breadcrumbPanel.FirstVisibleIndex; i < _breadcrumbPanel.Children.Count; i++)
        {
            if (_breadcrumbPanel.Children[i] is Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is Button button)
                        buttons.Add(button);
                }
            }
        }
        if (buttons.Count == 0)
            return false;
        var index = buttons.FindIndex(button => button.IsFocused);
        index = key switch
        {
            Key.Home => 0,
            Key.End => buttons.Count - 1,
            Key.Left => Math.Max(0, index - 1),
            _ => Math.Min(buttons.Count - 1, index + 1)
        };
        return buttons[index].Focus(NavigationMethod.Directional);
    }
}
