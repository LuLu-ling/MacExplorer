using MacExplorer.Infrastructure;
using MacExplorer.Models;
using MacExplorer.Native;

namespace MacExplorer.Services;

public sealed class ListingService
{
    public IReadOnlyList<FileItem> List(string path)
    {
        if (SpecialFolders.IsTag(path))
            return ListTagged(SpecialFolders.TagName(path));
        if (SpecialFolders.IsVirtual(path) || !Directory.Exists(path))
            return [];

        var showHidden = Config.Files.ShowHidden;
        var items = new List<FileItem>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var name = Path.GetFileName(entry);
            if (name is "." or ".." or ".DS_Store")
                continue;
            if (name.StartsWith('.') && !showHidden)
                continue;

            try { items.Add(Create(entry, name)); }
            catch { /* skip unreadable entries */ }
        }

        MacTags.NotifyLearned();
        return Sort(items);
    }

    private IReadOnlyList<FileItem> ListTagged(string tag)
    {
        var showHidden = Config.Files.ShowHidden;
        var items = new List<FileItem>();
        foreach (var entry in MacFinder.FilesWithTag(tag))
        {
            var name = Path.GetFileName(entry);
            if (string.IsNullOrEmpty(name) || name is ".DS_Store")
                continue;
            if (name.StartsWith('.') && !showHidden)
                continue;
            if (!PathUtil.Exists(entry))
                continue;
            try { items.Add(Create(entry, name)); }
            catch { /* skip unreadable entries */ }
        }

        MacTags.NotifyLearned();
        return Sort(items);
    }

    public IReadOnlyList<FileItem> Filter(IReadOnlyList<FileItem> items, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return items;
        return items.Where(i => i.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static FileItem Create(string path, string? name = null)
    {
        name ??= Path.GetFileName(path);
        var isDir = Directory.Exists(path);
        var bundle = isDir && PathUtil.IsBundle(path);
        var info = isDir ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        long size = 0;
        var sizeKnown = !isDir || bundle;
        if (sizeKnown)
        {
            try { size = new FileInfo(path).Exists ? new FileInfo(path).Length : 0; }
            catch { /* ignored */ }
        }

        var item = new FileItem
        {
            Path = path,
            Name = name,
            DisplayName = PathUtil.DisplayName(name, isDir && !bundle, Config.Files.ShowExtensions),
            IsDirectory = isDir && !bundle,
            IsBundle = bundle,
            IsHidden = name.StartsWith('.'),
            Size = size,
            SizeKnown = sizeKnown,
            Modified = info.LastWriteTime,
            Created = info.CreationTime,
            OriginalFolder = Path.GetDirectoryName(path)?.Replace('\\', '/'),
            OriginalFolderName = Path.GetFileName((Path.GetDirectoryName(path) ?? "").TrimEnd('/')) ?? "",
            DateDeleted = info.LastWriteTime,
            ItemType = MacWorkspace.LocalizedType(path),
            Extension = Path.GetExtension(name)
        };
        item.Tags = MacTags.Read(path);
        return item;
    }

    public static IReadOnlyList<FileItem> Sort(IEnumerable<FileItem> items) =>
        items.OrderBy(static i => i, FileItemComparer.Instance).ToList();

    private sealed class FileItemComparer : IComparer<FileItem>
    {
        public static readonly FileItemComparer Instance = new();

        public int Compare(FileItem? x, FileItem? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var priority = Config.Layout.FolderPriorityValue;
            if (priority is FolderPriority.FoldersFirst && x.IsDirectory != y.IsDirectory)
                return x.IsDirectory ? -1 : 1;
            if (priority is FolderPriority.FilesFirst && x.IsDirectory != y.IsDirectory)
                return x.IsDirectory ? 1 : -1;

            var cmp = Config.Layout.SortFieldValue switch
            {
                SortField.DateModified => x.Modified.CompareTo(y.Modified),
                SortField.DateCreated => x.Created.CompareTo(y.Created),
                SortField.Size => x.Size.CompareTo(y.Size),
                SortField.Type => string.Compare(x.ItemType, y.ItemType, StringComparison.CurrentCultureIgnoreCase),
                _ => string.Compare(x.DisplayName, y.DisplayName, StringComparison.CurrentCultureIgnoreCase)
            };
            if (cmp == 0)
                cmp = string.Compare(x.DisplayName, y.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            return Config.Layout.SortDirectionValue is SortDirection.Descending ? -cmp : cmp;
        }
    }
}
