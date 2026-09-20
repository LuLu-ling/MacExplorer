using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using MacExplorer.Localization;
using MacExplorer.Models;
using MacExplorer.ViewModels;

namespace MacExplorer.Native;

public sealed class NativeSettingsHost : NativeControlHost
{
    private MacSettingsPane? _pane;
    private SettingsViewModel? _model;
    private bool _owns;

    public NativeSettingsHost()
    {
        Focusable = true;
        ClipToBounds = true;
        LayoutUpdated += (_, _) =>
        {
            if (IsEffectivelyVisible && Bounds.Width > 0 && Bounds.Height > 0)
                TryUpdateNativeControlPosition();
        };
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        if (arranged.Width > 0 && arranged.Height > 0)
            TryUpdateNativeControlPosition();
        return arranged;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _pane = MacSettingsPane.Create();
        _pane.ThemeChanged += OnThemeChanged;
        _pane.LanguageChanged += OnLanguageChanged;
        _pane.ToggleChanged += OnToggleChanged;
        _owns = true;
        AttachModel(DataContext as SettingsViewModel);
        Push();
        MacAppearance.ApplyTo(_pane.View);
        return new PlatformHandle(_pane.View, "NSView");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (!_owns)
        {
            base.DestroyNativeControlCore(control);
            return;
        }

        if (_pane is { } pane)
        {
            pane.ThemeChanged -= OnThemeChanged;
            pane.LanguageChanged -= OnLanguageChanged;
            pane.ToggleChanged -= OnToggleChanged;
            pane.Dispose();
            _pane = null;
        }

        _owns = false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LocalizationService.LanguageChanged += OnLanguageResourcesChanged;
        base.OnAttachedToVisualTree(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LocalizationService.LanguageChanged -= OnLanguageResourcesChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty)
            AttachModel(change.NewValue as SettingsViewModel);
    }

    private void AttachModel(SettingsViewModel? model)
    {
        if (ReferenceEquals(_model, model))
            return;
        if (_model is not null)
            _model.PropertyChanged -= OnModelChanged;
        _model = model;
        if (_model is not null)
            _model.PropertyChanged += OnModelChanged;
        Push();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Push();

    private void OnLanguageResourcesChanged() => Push();

    private void OnThemeChanged(int theme)
    {
        if (_model is not null && Enum.IsDefined(typeof(ThemeMode), theme))
            _model.Theme = (ThemeMode)theme;
    }

    private void OnLanguageChanged(int index)
    {
        if (_model is null || index < 0 || index >= _model.LanguageOptions.Count)
            return;
        _model.SelectedLanguage = _model.LanguageOptions[index];
    }

    private void OnToggleChanged(int id, bool on)
    {
        if (_model is null)
            return;
        switch (id)
        {
            case MacSettingsToggle.Hidden:
                _model.ShowHidden = on;
                break;
            case MacSettingsToggle.Extensions:
                _model.ShowExtensions = on;
                break;
            case MacSettingsToggle.QuickAccess:
                _model.ShowQuickAccess = on;
                break;
            case MacSettingsToggle.Volumes:
                _model.ShowVolumes = on;
                break;
            case MacSettingsToggle.Recents:
                _model.ShowRecents = on;
                break;
        }
    }

    private void Push()
    {
        if (_pane is null || _model is null)
            return;
        _pane.Apply(Capture(_model));
        MacAppearance.ApplyTo(_pane.View);
    }

    private static MacSettingsSnapshot Capture(SettingsViewModel vm)
    {
        var languages = vm.LanguageOptions;
        var languageIndex = 0;
        for (var i = 0; i < languages.Count; i++)
        {
            if (ReferenceEquals(languages[i], vm.SelectedLanguage))
            {
                languageIndex = i;
                break;
            }
        }

        var titles = new string[languages.Count];
        for (var i = 0; i < languages.Count; i++)
            titles[i] = languages[i].Title;

        return new MacSettingsSnapshot
        {
            Page = vm.SelectedPage switch
            {
                "Language" => 1,
                "Folders" => 2,
                "About" => 3,
                _ => 0
            },
            Theme = (int)vm.Theme,
            LanguageIndex = languageIndex,
            Flags = MacSettingsToggle.Pack(
                vm.ShowHidden, vm.ShowExtensions, vm.ShowQuickAccess, vm.ShowVolumes, vm.ShowRecents),
            AppearanceTitle = Lang.Text("Settings.Nav.Appearance"),
            LanguageTitle = Lang.Text("Settings.Language.Title"),
            FoldersTitle = Lang.Text("Settings.Nav.Folders"),
            AboutTitle = Lang.Text("Settings.Nav.About"),
            ThemeLabel = Lang.Text("Settings.Theme"),
            ThemeSystem = Lang.Text("Settings.Theme.System"),
            ThemeLight = Lang.Text("Settings.Theme.Light"),
            ThemeDark = Lang.Text("Settings.Theme.Dark"),
            LanguageLabel = Lang.Text("Settings.Language.UiLanguage"),
            Languages = titles,
            ShowHidden = Lang.Text("Settings.Folders.ShowHidden"),
            ShowExtensions = Lang.Text("Settings.Folders.ShowExtensions"),
            ShowQuickAccess = Lang.Text("Settings.Folders.ShowQuickAccess"),
            ShowVolumes = Lang.Text("Settings.Folders.ShowVolumes"),
            ShowRecents = Lang.Text("Settings.Folders.ShowRecents"),
            AppName = "MacExplorer",
            Version = vm.VersionText,
            Description = Lang.Text("Settings.About.Description")
        };
    }
}
