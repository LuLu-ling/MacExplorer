using System.Globalization;
using MacExplorer.Models;

namespace MacExplorer.Services;

internal static class Grouping
{
    internal static event Action? SettingsChanged;

    public static bool IsDate(GroupOption option) =>
        option is GroupOption.DateModified or GroupOption.DateCreated or GroupOption.DateDeleted;

    public static bool Allowed(GroupOption option, bool trash, bool mixedFolders) => option switch
    {
        GroupOption.SyncStatus => false,
        GroupOption.OriginalFolder or GroupOption.DateDeleted => trash,
        GroupOption.FolderPath => mixedFolders,
        _ => true
    };

    public static List<FileGroup> Arrange(
        IReadOnlyList<FileItem> items,
        GroupOption option,
        GroupByDateUnit unit,
        SortDirection direction)
    {
        var keyOf = KeySelector(option, unit);
        if (keyOf is null)
            return [];

        var groups = new Dictionary<string, FileGroup>(StringComparer.Ordinal);
        var order = new List<FileGroup>();
        foreach (var item in items)
        {
            var key = keyOf(item) ?? "";
            item.GroupKey = key;
            if (!groups.TryGetValue(key, out var group))
            {
                group = new FileGroup { Key = key, Text = key };
                groups[key] = group;
                order.Add(group);
            }

            group.Items.Add(item);
        }

        foreach (var group in order)
        {
            ApplyHeader(group, option, unit);
            group.UpdateCount();
        }

        IOrderedEnumerable<FileGroup> sorted;
        if (option is GroupOption.Size)
        {
            sorted = direction is SortDirection.Ascending
                ? order.OrderBy(FolderRank).ThenBy(static g => g.SortIndexOverride).ThenBy(static g => g.Text)
                : order.OrderBy(FolderRank).ThenByDescending(static g => g.SortIndexOverride).ThenByDescending(static g => g.Text);
        }
        else
        {
            sorted = direction is SortDirection.Ascending
                ? order.OrderBy(static g => g.SortIndexOverride).ThenBy(static g => g.Text)
                : order.OrderByDescending(static g => g.SortIndexOverride).ThenByDescending(static g => g.Text);
        }

        return sorted.ToList();
    }

    public static void NotifySettingsChanged() => SettingsChanged?.Invoke();

    private static int FolderRank(FileGroup group) =>
        group.Items.Count > 0 && group.Items[0].IsDirectory ? 0 : 1;

    private static Func<FileItem, string?>? KeySelector(GroupOption option, GroupByDateUnit unit) => option switch
    {
        GroupOption.Name => static x =>
        {
            var name = x.Name;
            return string.IsNullOrEmpty(name)
                ? "#"
                : char.ToUpperInvariant(name[0]).ToString();
        },
        GroupOption.Size => static x => x.IsDirectory
            ? x.SizeKnown ? x.SizeText : "Item size not calculated"
            : SizeKey(x.Size),
        GroupOption.DateCreated => x => ToTimeSpanLabel(x.Created, unit).Text,
        GroupOption.DateModified => x => ToTimeSpanLabel(x.Modified, unit).Text,
        GroupOption.FileType => static x => x.IsDirectory
            ? x.ItemType
            : string.IsNullOrEmpty(x.Extension) ? " " : x.Extension.ToLowerInvariant(),
        GroupOption.FileTag => static x => x.Tags.Count > 0 ? x.Tags[0].Name : "Untagged",
        GroupOption.OriginalFolder => static x => x.OriginalFolder,
        GroupOption.DateDeleted => x => ToTimeSpanLabel(x.DateDeleted, unit).Text,
        GroupOption.FolderPath => static x => ParentDir(x.Path),
        _ => null
    };

    private static void ApplyHeader(FileGroup group, GroupOption option, GroupByDateUnit unit)
    {
        if (group.Items.Count == 0)
            return;
        var first = group.Items[0];
        switch (option)
        {
            case GroupOption.FileType:
                group.Text = first.ItemType;
                group.Subtext = first.Extension;
                group.ShowImage = true;
                if (!first.IsDirectory)
                    group.SortIndexOverride = 1;
                break;
            case GroupOption.Size when !first.IsDirectory:
                var size = SizeInfo(first.Size);
                group.Text = size.Range;
                group.Subtext = size.Range;
                group.SortIndexOverride = size.Index;
                break;
            case GroupOption.DateCreated:
                ApplyDate(group, first.Created, unit);
                break;
            case GroupOption.DateModified:
                ApplyDate(group, first.Modified, unit);
                break;
            case GroupOption.DateDeleted:
                ApplyDate(group, first.DateDeleted, unit);
                break;
            case GroupOption.FileTag:
                if (first.Tags.Count > 0)
                {
                    group.Text = first.Tags[0].Name;
                    group.Marker = first.Tags[0].Brush;
                }
                else
                {
                    group.Text = "Untagged";
                    group.Marker = FileTagPalette.Brush(FileTagColor.Gray);
                }
                break;
            case GroupOption.OriginalFolder:
                group.ShowCountTextBelow = true;
                group.Text = first.OriginalFolderName;
                group.Subtext = first.OriginalFolder;
                break;
            case GroupOption.FolderPath:
                group.ShowCountTextBelow = true;
                var parent = ParentDir(first.Path);
                group.Text = FolderName(parent) ?? "";
                group.Subtext = parent;
                break;
        }
    }

    private static void ApplyDate(FileGroup group, DateTime time, GroupByDateUnit unit)
    {
        var label = ToTimeSpanLabel(time, unit);
        group.Text = label.Text;
        group.Subtext = label.Text;
        group.Icon = label.Glyph;
        group.SortIndexOverride = label.Index;
    }

    public static TimeSpanLabel ToTimeSpanLabel(DateTime time, GroupByDateUnit unit)
    {
        var now = DateTimeOffset.Now;
        var offset = new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Local));
        if (offset.Offset != now.Offset)
            offset = offset.ToLocalTime();
        var local = offset.ToLocalTime();
        var diff = now - offset;

        if (now.Date < local.Date)
            return new("Future", "\uED28", 1000000006);
        if (now.Date == local.Date)
            return new("Today", "\uE8D1", 1000000005);
        if (now.AddDays(-1).Date == local.Date)
            return new("Yesterday", "\uE8BF", 1000000004);
        if (unit is GroupByDateUnit.Day)
            return new(local.ToString("D", CultureInfo.CurrentCulture), "\uE8BF", local.Year * 10000 + local.Month * 100 + local.Day);
        if (diff.Days <= 7 && WeekOfYear(now) == WeekOfYear(local))
            return new("Earlier this week", "\uE8C0", 1000000003);
        if (diff.Days <= 14 && WeekOfYear(now.AddDays(-7)) == WeekOfYear(local))
            return new("Last week", "\uE8C0", 1000000002);
        if (now.Year == local.Year && now.Month == local.Month)
            return new("Earlier this month", "\uE787", 1000000001);
        if (now.AddMonths(-1).Year == local.Year && now.AddMonths(-1).Month == local.Month)
            return new("Last month", "\uE787", 1000000000);
        if (unit is GroupByDateUnit.Month)
            return new(local.ToString("Y", CultureInfo.CurrentCulture), "\uE787", local.Year * 10000 + local.Month * 100);
        if (now.Year == local.Year)
            return new("Earlier this year", "\uEC92", 10000001);
        if (now.AddYears(-1).Year == local.Year)
            return new("Last year", "\uEC92", 10000000);
        return new($"Year {local.Year}", "\uEC92", local.Year);
    }

    private static int WeekOfYear(DateTimeOffset t)
    {
        var culture = CultureInfo.CurrentCulture;
        return culture.Calendar.GetWeekOfYear(
            t.DateTime, CalendarWeekRule.FirstFullWeek, culture.DateTimeFormat.FirstDayOfWeek);
    }

    private static readonly (long Size, string Text, string SizeText)[] SizeGroups =
    [
        (5_000_000_000, "Huge", "5 GiB"),
        (1_000_000_000, "Very large", "1 GiB"),
        (128_000_000, "Large", "128 MiB"),
        (1_000_000, "Medium", "1 MiB"),
        (16_000, "Small", "16 KiB"),
    ];

    public static string SizeKey(long size)
    {
        foreach (var group in SizeGroups)
        {
            if (size > group.Size)
                return group.Size.ToString();
        }

        return "0";
    }

    public static (string Key, string Text, string Range, int Index) SizeInfo(long size)
    {
        var last = "";
        for (var i = 0; i < SizeGroups.Length; i++)
        {
            var group = SizeGroups[i];
            if (size > group.Size)
            {
                var range = i > 0 ? $"{group.SizeText} - {SizeGroups[i - 1].SizeText}" : $"{group.SizeText} +";
                return (group.Size.ToString(), group.Text, range, SizeGroups.Length - i);
            }

            last = group.SizeText;
        }

        return ("0", "Tiny", $"0 B - {last}", 0);
    }

    private static string? ParentDir(string path)
    {
        var parent = Path.GetDirectoryName(path.TrimEnd('/'));
        return string.IsNullOrEmpty(parent) ? "/" : parent.Replace('\\', '/');
    }

    private static string? FolderName(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        var name = Path.GetFileName(path.TrimEnd('/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    public readonly record struct TimeSpanLabel(string Text, string Glyph, int Index);

}
