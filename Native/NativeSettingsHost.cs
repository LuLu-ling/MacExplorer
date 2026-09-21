using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using MacExplorer.Localization;
using MacExplorer.Models;
using MacExplorer.ViewModels;
using System.Text;
using MacExplorer.Input;

namespace MacExplorer.Native;

public sealed class NativeSettingsHost : NativeControlHost
{
    private MacSettingsPane? _pane;
    private SettingsViewModel? _model;
    private bool _echo;

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
        _pane.ShortcutChanged += OnShortcutChanged;
        AttachModel(DataContext as SettingsViewModel);
        MacAppearance.ApplyTo(_pane.View);
        return new PlatformHandle(_pane.View, "NSView");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_pane is null)
        {
            base.DestroyNativeControlCore(control);
            return;
        }

        _pane.ThemeChanged -= OnThemeChanged;
        _pane.LanguageChanged -= OnLanguageChanged;
        _pane.ToggleChanged -= OnToggleChanged;
        _pane.ShortcutChanged -= OnShortcutChanged;
        _pane.Dispose();
        _pane = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LocalizationService.LanguageChanged += OnLanguageResourcesChanged;
        MacExplorer.Input.Shortcuts.Changed += OnShortcutsChanged;
        ActualThemeVariantChanged += OnThemeVariantChanged;
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += OnThemeVariantChanged;
        base.OnAttachedToVisualTree(e);
        Push();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LocalizationService.LanguageChanged -= OnLanguageResourcesChanged;
        MacExplorer.Input.Shortcuts.Changed -= OnShortcutsChanged;
        ActualThemeVariantChanged -= OnThemeVariantChanged;
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged -= OnThemeVariantChanged;
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

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_echo)
            Push();
    }

    private void OnLanguageResourcesChanged() => Push();

    private void OnThemeVariantChanged(object? sender, EventArgs e) => Push();

    private void OnThemeChanged(int theme)
    {
        if (_model is not null && Enum.IsDefined(typeof(ThemeMode), theme))
            _model.Theme = (ThemeMode)theme;
    }

    private void OnLanguageChanged(int index) => FromNative(() =>
    {
        if (_model is null || index < 0 || index >= _model.LanguageOptions.Count)
            return;
        _model.SelectedLanguage = _model.LanguageOptions[index];
    });

    private void OnToggleChanged(int id, bool on) => FromNative(() =>
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
    });
    private void OnShortcutChanged(int id, int keyCode, int modifiers) =>
        MacExplorer.Input.Shortcuts.HandleNative(id, keyCode, modifiers);

    private void OnShortcutsChanged() => Push();

    private void FromNative(Action set)
    {
        _echo = true;
        try
        {
            set();
        }
        finally
        {
            _echo = false;
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
        var titles = new string[languages.Count];
        for (var i = 0; i < languages.Count; i++)
        {
            titles[i] = languages[i].Title;
            if (ReferenceEquals(languages[i], vm.SelectedLanguage))
                languageIndex = i;
        }

        return new MacSettingsSnapshot(
            Page: vm.SelectedPage switch
            {
                "Language" => 1,
                "Folders" => 2,
                "Shortcuts" => 3,
                "About" => 4,
                _ => 0
            },
            Theme: (int)vm.Theme,
            LanguageIndex: languageIndex,
            Flags: MacSettingsToggle.Pack(
                vm.ShowHidden, vm.ShowExtensions, vm.ShowQuickAccess, vm.ShowVolumes, vm.ShowRecents),
            PageTitles: Join(
                Lang.Text("Settings.Nav.Appearance"),
                Lang.Text("Settings.Language.Title"),
                Lang.Text("Settings.Nav.Folders"),
                Lang.Text("Settings.Nav.Shortcuts"),
                Lang.Text("Settings.Nav.About")),
            ThemeLabel: Lang.Text("Settings.Theme"),
            ThemeOptions: Join(
                Lang.Text("Settings.Theme.System"),
                Lang.Text("Settings.Theme.Light"),
                Lang.Text("Settings.Theme.Dark")),
            LanguageLabel: Lang.Text("Settings.Language.UiLanguage"),
            Languages: Join(titles),
            FolderLabels: Join(
                Lang.Text("Settings.Folders.ShowHidden"),
                Lang.Text("Settings.Folders.ShowExtensions"),
                Lang.Text("Settings.Folders.ShowQuickAccess"),
                Lang.Text("Settings.Folders.ShowVolumes"),
                Lang.Text("Settings.Folders.ShowRecents")),
            AppName: "MacExplorer",
            Version: vm.VersionText,
            Description: Lang.Text("Settings.About.Description"),
            Categories: CaptureShortcuts(out var shortcutRows),
            ShortcutRows: shortcutRows,
            ShortcutLabels: Join(
                Lang.Text("Settings.Shortcuts.TypePrompt"),
                Lang.Text("Settings.Shortcuts.None"),
                Lang.Text("Settings.Shortcuts.RestoreDefaults"),
                Lang.Text("Settings.Shortcuts.Restore")),
            CardArgb: Palette.Card,
            StrokeArgb: Palette.Stroke);
    }

    private static string Join(params string[] parts) => string.Join('\n', parts);
    private static string CaptureShortcuts(out string rows)
    {
        var categories = new List<string>();
        var text = new StringBuilder();
        string? last = null;
        foreach (var spec in ShortcutCatalog.All)
        {
            var category = Lang.Text(spec.CategoryKey);
            if (category != last)
            {
                categories.Add(category);
                last = category;
            }

            if (text.Length > 0)
                text.Append('\n');
            text.Append((int)spec.Id).Append('\t')
                .Append(categories.Count - 1).Append('\t')
                .Append(Sanitize(Lang.Text(spec.TitleKey))).Append('\t')
                .Append(Sanitize(MacExplorer.Input.Shortcuts.Display(spec.Id))).Append('\t')
                .Append(MacExplorer.Input.Shortcuts.IsCustom(spec.Id) ? '1' : '0');
        }

        rows = text.ToString();
        return string.Join('\n', categories);
    }

    private static string Sanitize(string value) =>
        value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');


    private static class Palette
    {
        public static uint Card => Resolve("LayerFillColorDefaultBrush", 0xFFF3F3F3);
        public static uint Stroke => Resolve("CardStrokeColorDefaultBrush", 0x26000000);

        private static uint Resolve(string key, uint fallback)
        {
            var app = Application.Current;
            if (app is null || !app.TryGetResource(key, app.ActualThemeVariant, out var value))
                return fallback;
            return value switch
            {
                ISolidColorBrush brush => Pack(brush.Color, brush.Opacity),
                Color color => Pack(color, 1),
                _ => fallback
            };
        }

        private static uint Pack(Color color, double opacity)
        {
            var alpha = (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255);
            return (uint)(alpha << 24 | color.R << 16 | color.G << 8 | color.B);
        }
    }
}
