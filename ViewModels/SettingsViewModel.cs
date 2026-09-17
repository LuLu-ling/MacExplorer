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

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public int ThemeIndex
    {
        get => (int)Theme;
        set => Theme = (ThemeMode)value;
    }

    public string Version => "1.0.0";
    public string VersionText => Lang.Text("Settings.About.Version", Version);

    partial void OnThemeChanged(ThemeMode value)
    {
        Config.Appearance.Theme = value;
        OnPropertyChanged(nameof(ThemeIndex));
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

    [RelayCommand]
    private void SelectPage(string page) => SelectedPage = page;
}
