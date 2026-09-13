using Avalonia.Media;

namespace MacExplorer.Models;

public enum FileTagColor : byte
{
    None = 0,
    Gray = 1,
    Green = 2,
    Purple = 3,
    Blue = 4,
    Yellow = 5,
    Red = 6,
    Orange = 7
}

public readonly record struct FileTag(string Name, FileTagColor Color)
{
    public IBrush Brush => FileTagPalette.Brush(Color);
    public uint Argb => FileTagPalette.Argb(Color);
}

internal static class FileTagPalette
{
    private static readonly IBrush[] Brushes =
    [
        new SolidColorBrush(Color.FromRgb(0xA6, 0xA6, 0xA6)),
        new SolidColorBrush(Color.FromRgb(0xA6, 0xA6, 0xA6)),
        new SolidColorBrush(Color.FromRgb(0x30, 0xD0, 0x33)),
        new SolidColorBrush(Color.FromRgb(0xD1, 0x86, 0xD7)),
        new SolidColorBrush(Color.FromRgb(0x1A, 0x9F, 0xF6)),
        new SolidColorBrush(Color.FromRgb(0xFE, 0xC7, 0x0A)),
        new SolidColorBrush(Color.FromRgb(0xFC, 0x3A, 0x4A)),
        new SolidColorBrush(Color.FromRgb(0xFD, 0x9F, 0x0E))
    ];

    private static readonly uint[] Argbs =
    [
        0xFFA6A6A6,
        0xFFA6A6A6,
        0xFF30D033,
        0xFFD186D7,
        0xFF1A9FF6,
        0xFFFEC70A,
        0xFFFC3A4A,
        0xFFFD9F0E
    ];

    public static IBrush Brush(FileTagColor color)
    {
        var i = (int)color;
        return (uint)i < (uint)Brushes.Length ? Brushes[i] : Brushes[0];
    }

    public static uint Argb(FileTagColor color)
    {
        var i = (int)color;
        return (uint)i < (uint)Argbs.Length ? Argbs[i] : Argbs[0];
    }
}
