namespace MacExplorer.Configuration;

internal static class ValueConvert
{
    public static T Convert<T>(this string value)
    {
        var type = typeof(T);
        if (type == typeof(string))
            return (T)(object)value;
        if (type.IsEnum)
            return (T)Enum.Parse(type, value, ignoreCase: true);
        return (T)System.Convert.ChangeType(value, type)!;
    }
}
