using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MacExplorer.Configuration;
using MacExplorer.Infrastructure;
using MacExplorer.Logging;

namespace MacExplorer.Localization;

public static class LocalizationService
{
    public const string Auto = "auto";
    public const string DefaultLanguageCode = "zh-CN";

    private static readonly LocalizationLanguage DefaultLanguage = new(
        DefaultLanguageCode,
        "简体中文",
        "zh-CN");

    private static ResourceDictionary? _baseLanguageDictionary;
    private static ResourceDictionary? _currentLanguageDictionary;
    private static CultureInfo _systemUiCulture = CultureInfo.CurrentUICulture;
    private static bool _initialized;

    public static LocalizationLanguage CurrentLanguage { get; private set; } = DefaultLanguage;

    public static IReadOnlyList<LocalizationLanguage> SupportedLanguages { get; } =
    [
        DefaultLanguage,
        new("en-US", "English", "en-US")
    ];

    public static event Action? LanguageChanged;

    internal static bool TryGetText(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? text)
    {
        text = Lookup(_currentLanguageDictionary, key) ?? Lookup(_baseLanguageDictionary, key);
        return text is not null;
    }

    private static string? Lookup(ResourceDictionary? dictionary, string key) =>
        dictionary is not null && dictionary.TryGetValue(key, out var value) ? value as string : null;

    public static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;
        _systemUiCulture = CultureInfo.CurrentUICulture;
        Config.Localization.LanguageConfig.Observe(
            new ConfigObserver(ConfigEvent.Update, _ => ApplyFromConfig()));
        ApplyFromConfig();
    }

    public static void ApplyFromConfig()
    {
        var code = ConfigService.IsInitialized ? Config.Localization.Language : Auto;
        Apply(code, save: false);
    }

    public static void Apply(string languageCode, bool save = true)
    {
        var normalized = NormalizeConfigValue(languageCode);
        var language = ResolveLanguage(normalized);
        var uiCulture = CultureInfo.GetCultureInfo(language.CultureName);

        var languageChanged = !string.Equals(CurrentLanguage.Code, language.Code, StringComparison.OrdinalIgnoreCase);
        if (_baseLanguageDictionary is not null && !languageChanged)
        {
            SaveConfigIfNeeded(save, normalized, language);
            return;
        }

        ApplyCultures(uiCulture);
        ApplyLanguageResources(language.Code);

        CurrentLanguage = language;
        SaveConfigIfNeeded(save, normalized, language);
        LogWrapper.Info("Localization", $"UI language {language.Code}");
        LanguageChanged?.Invoke();
    }

    public static LocalizationLanguage ResolveLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode) ||
            string.Equals(languageCode, Auto, StringComparison.OrdinalIgnoreCase))
            return ResolveSystemLanguage();

        var normalized = NormalizeCultureCode(languageCode);
        return SupportedLanguages.FirstOrDefault(language =>
                   string.Equals(language.Code, normalized, StringComparison.OrdinalIgnoreCase))
               ?? DefaultLanguage;
    }

    private static void SaveConfigIfNeeded(bool save, string normalized, LocalizationLanguage language)
    {
        if (!save || !ConfigService.IsInitialized)
            return;

        var configCode = string.Equals(normalized, Auto, StringComparison.OrdinalIgnoreCase)
            ? Auto
            : language.Code;
        if (Config.Localization.Language != configCode)
            Config.Localization.Language = configCode;
    }

    private static LocalizationLanguage ResolveSystemLanguage()
    {
        var name = NormalizeCultureCode(_systemUiCulture.Name);
        var exact = SupportedLanguages.FirstOrDefault(language =>
            string.Equals(language.Code, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var neutral = _systemUiCulture.TwoLetterISOLanguageName;
        return SupportedLanguages.FirstOrDefault(language =>
                   language.Code.StartsWith(neutral + "-", StringComparison.OrdinalIgnoreCase))
               ?? DefaultLanguage;
    }

    private static void ApplyCultures(CultureInfo uiCulture)
    {
        CultureInfo.CurrentUICulture = uiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        Thread.CurrentThread.CurrentUICulture = uiCulture;

        CultureInfo.CurrentCulture = uiCulture;
        CultureInfo.DefaultThreadCurrentCulture = uiCulture;
        Thread.CurrentThread.CurrentCulture = uiCulture;
        Lang.SyncCulture(uiCulture);
    }

    private static void ApplyLanguageResources(string languageCode)
    {
        var app = Application.Current;
        if (app is null)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => ApplyLanguageResourcesCore(app, languageCode));
            return;
        }

        ApplyLanguageResourcesCore(app, languageCode);
    }

    private static void ApplyLanguageResourcesCore(Application app, string languageCode)
    {
        var dictionaries = app.Resources.MergedDictionaries;

        if (_baseLanguageDictionary is not null)
            dictionaries.Remove(_baseLanguageDictionary);
        if (_currentLanguageDictionary is not null)
            dictionaries.Remove(_currentLanguageDictionary);

        _baseLanguageDictionary = LoadLanguageDictionary(DefaultLanguageCode);
        dictionaries.Add(_baseLanguageDictionary);

        if (string.Equals(languageCode, DefaultLanguageCode, StringComparison.OrdinalIgnoreCase))
        {
            _currentLanguageDictionary = null;
            return;
        }

        _currentLanguageDictionary = LoadLanguageDictionary(languageCode);
        dictionaries.Add(_currentLanguageDictionary);
    }

    private static ResourceDictionary LoadLanguageDictionary(string languageCode)
    {
        var uri = new Uri($"avares://MacExplorer/Localization/Languages/{languageCode}.axaml");
        return AvaloniaXamlLoader.Load(uri) as ResourceDictionary
               ?? throw new InvalidOperationException($"Language dictionary '{languageCode}' is missing.");
    }

    private static string NormalizeConfigValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Auto : NormalizeCultureCode(value);

    private static string NormalizeCultureCode(string value) => value.Replace('_', '-').Trim();
}