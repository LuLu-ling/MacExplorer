using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacExplorer.Infrastructure;
using MacExplorer.Localization;
using MacExplorer.Models;

namespace MacExplorer.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel()
    {
        Theme = Config.Appearance.Theme;
        ShowHidden = Config.Files.ShowHidden;
        ShowExtensions = Config.Files.ShowExtensions;
        ShowQuickAccess = Config.Home.ShowQuickAccess;
        ShowVolumes = Config.Home.ShowVolumes;
        ShowRecents = Config.Home.ShowRecents;
        NavWidth = Config.Settings.NavWidth;
        LanguageOptions =
        [
            new LanguageOption(LocalizationService.Auto),
            .. LocalizationService.SupportedLanguages.Select(language =>
                new LanguageOption(language.Code, language.NativeName))
        ];
        var code = Config.Localization.Language;
        SelectedLanguage = LanguageOptions.FirstOrDefault(option =>
                               string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase))
                           ?? LanguageOptions[0];
    }

    [ObservableProperty] public partial ThemeMode Theme { get; set; }
    [ObservableProperty] public partial bool ShowHidden { get; set; }
    [ObservableProperty] public partial bool ShowExtensions { get; set; }
    [ObservableProperty] public partial bool ShowQuickAccess { get; set; }
    [ObservableProperty] public partial bool ShowVolumes { get; set; }
    [ObservableProperty] public partial bool ShowRecents { get; set; }
    [ObservableProperty] public partial string SelectedPage { get; set; } = "Appearance";
    [ObservableProperty] public partial LanguageOption SelectedLanguage { get; set; } = null!;
    [ObservableProperty] public partial double NavWidth { get; set; }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public string Version => "1.0.0";
    public string VersionText => Lang.Text("Settings.About.Version", Version);

    partial void OnThemeChanged(ThemeMode value)
    {
        Config.Appearance.Theme = value;
        App.ApplyTheme(value);
    }

    partial void OnShowHiddenChanged(bool value) => Config.Files.ShowHidden = value;
    partial void OnShowExtensionsChanged(bool value) => Config.Files.ShowExtensions = value;
    partial void OnShowQuickAccessChanged(bool value) => Config.Home.ShowQuickAccess = value;
    partial void OnShowVolumesChanged(bool value) => Config.Home.ShowVolumes = value;
    partial void OnShowRecentsChanged(bool value) => Config.Home.ShowRecents = value;

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value is null) return;
        Config.Localization.Language = value.Code;
    }

    protected override void OnLanguageChanged() => OnPropertyChanged(nameof(VersionText));

    public const double NavMin = 160;
    public const double NavMax = 480;

    public GridLength NavColumn
    {
        get => new(Math.Clamp(NavWidth, NavMin, NavMax));
        set
        {
            if (value.IsAbsolute)
                NavWidth = Math.Clamp(value.Value, NavMin, NavMax);
        }
    }

    partial void OnNavWidthChanged(double value)
    {
        var width = Math.Clamp(value, NavMin, NavMax);
        if (Math.Abs(width - value) > 0.5)
        {
            NavWidth = width;
            return;
        }
        Config.Settings.NavWidth = width;
        OnPropertyChanged(nameof(NavColumn));
    }

    [RelayCommand]
    private void SelectPage(string page) => SelectedPage = page;
}
