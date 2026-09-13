using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacExplorer.Infrastructure;
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
    }

    [ObservableProperty] public partial ThemeMode Theme { get; set; }
    [ObservableProperty] public partial bool ShowHidden { get; set; }
    [ObservableProperty] public partial bool ShowExtensions { get; set; }
    [ObservableProperty] public partial bool ShowQuickAccess { get; set; }
    [ObservableProperty] public partial bool ShowVolumes { get; set; }
    [ObservableProperty] public partial bool ShowRecents { get; set; }
    [ObservableProperty] public partial string SelectedPage { get; set; } = "Appearance";

    public int ThemeIndex
    {
        get => (int)Theme;
        set => Theme = (ThemeMode)value;
    }

    public string Version => "1.0.0";

    partial void OnThemeChanged(ThemeMode value)
    {
        Config.Appearance.Theme = value;
        OnPropertyChanged(nameof(ThemeIndex));
        if (Application.Current is null) return;
        Application.Current.RequestedThemeVariant = value switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    partial void OnShowHiddenChanged(bool value) => Config.Files.ShowHidden = value;
    partial void OnShowExtensionsChanged(bool value) => Config.Files.ShowExtensions = value;
    partial void OnShowQuickAccessChanged(bool value) => Config.Home.ShowQuickAccess = value;
    partial void OnShowVolumesChanged(bool value) => Config.Home.ShowVolumes = value;
    partial void OnShowRecentsChanged(bool value) => Config.Home.ShowRecents = value;

    [RelayCommand]
    private void SelectPage(string page) => SelectedPage = page;
}
