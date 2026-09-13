using System.Globalization;
using Avalonia.Data.Converters;
using MacExplorer.Models;

namespace MacExplorer.Converters;

public sealed class InvertBoolConverter : IValueConverter
{
    public static readonly InvertBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is false;
}

public sealed class NullToBoolConverter : IValueConverter
{
    public static readonly NullToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EqualityConverter : IValueConverter
{
    public static readonly EqualityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;
        if (value is Enum && parameter is string text)
            return string.Equals(value.ToString(), text, StringComparison.Ordinal);
        return Equals(value, parameter) || string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class LayoutEqConverter : IValueConverter
{
    public static readonly LayoutEqConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not LayoutKind layout || parameter is not string name)
            return false;
        return layout.ToString() == name;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class GroupDateEqConverter : IMultiValueConverter
{
    public static readonly GroupDateEqConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string spec)
            return false;
        var split = spec.Split(':');
        if (split.Length != 2 || values.Count < 2)
            return false;
        return string.Equals(values[0]?.ToString(), split[0], StringComparison.Ordinal)
            && string.Equals(values[1]?.ToString(), split[1], StringComparison.Ordinal);
    }
}
