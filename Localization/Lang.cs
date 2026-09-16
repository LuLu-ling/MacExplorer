using System.Globalization;
using Avalonia;
using Avalonia.Styling;

namespace MacExplorer.Localization;

/// <summary>
/// Code-side lookup for localized strings and display formatting.
/// XAML static text should use <c>DynamicResource</c>; this type is for C#.
/// </summary>
public static class Lang
{
    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentCulture;

    internal static void SyncCulture(CultureInfo culture) => Culture = culture;

    public static string Text(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var app = Application.Current;
        if (app is not null &&
            app.TryGetResource(key, app.ActualThemeVariant, out var themed) &&
            themed is string themedText)
            return themedText;

        if (app is not null &&
            app.TryGetResource(key, ThemeVariant.Default, out var fallback) &&
            fallback is string fallbackText)
            return fallbackText;

#if DEBUG
        return $"!{key}!";
#else
        return key;
#endif
    }

    public static string Text(string key, params object?[] args) =>
        string.Format(Culture, Text(key), args);

    public static string Date(DateTime value, string format = "g") =>
        value.ToString(format, Culture);

    public static string Number<T>(T value, string? format = null) where T : IFormattable =>
        value.ToString(format, Culture);

    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => Text("File.Size.Bytes", bytes),
        < 1024 * 1024 => Text("File.Size.KB", Number(bytes / 1024.0, "0.##")),
        < 1024L * 1024 * 1024 => Text("File.Size.MB", Number(bytes / (1024.0 * 1024), "0.##")),
        _ => Text("File.Size.GB", Number(bytes / (1024.0 * 1024 * 1024), "0.##"))
    };

    public static bool EqualsText(string? value, string key) =>
        !string.IsNullOrEmpty(value) &&
        (value.Equals(Text(key), StringComparison.CurrentCultureIgnoreCase) ||
         value.Equals(key, StringComparison.OrdinalIgnoreCase));
}
