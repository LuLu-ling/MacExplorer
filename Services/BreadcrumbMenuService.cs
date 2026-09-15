using System.Security;
using MacExplorer.Infrastructure;
using MacExplorer.Native;

namespace MacExplorer.Services;

internal sealed record BreadcrumbMenuSection(
    string? Title,
    IReadOnlyList<(string Title, string Path)> Folders,
    string? Message = null);

internal sealed class BreadcrumbMenuService(VolumeService volumes)
{
    public Task<IReadOnlyList<BreadcrumbMenuSection>> LoadAsync(string? path, CancellationToken cancellationToken)
    {
        var showHidden = Config.Files.ShowHidden;
        return Task.Run<IReadOnlyList<BreadcrumbMenuSection>>(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return path is null ? LoadRoot(cancellationToken) : LoadChildren(path, showHidden, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
            {
                return [new(null, [], e.Message)];
            }
        }, cancellationToken);
    }

    private IReadOnlyList<BreadcrumbMenuSection> LoadRoot(CancellationToken cancellationToken)
    {
        var favorites = new List<(string Title, string Path)>();
        foreach (var path in MacFinder.FavoriteFolders())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(path) || PathUtil.IsBundle(path))
                continue;
            var name = Path.GetFileName(path.TrimEnd('/'));
            favorites.Add((name.Length > 0 ? name : path, path));
        }

        var drives = new List<(string Title, string Path)>();
        foreach (var volume in volumes.List())
        {
            cancellationToken.ThrowIfCancellationRequested();
            drives.Add((volume.Name, volume.Path));
        }

        return
        [
            new("Quick Access", favorites, favorites.Count == 0 ? "No available favorite folders" : null),
            new("Drives", drives, drives.Count == 0 ? "No available drives" : null)
        ];
    }

    private static IReadOnlyList<BreadcrumbMenuSection> LoadChildren(
        string path, bool showHidden, CancellationToken cancellationToken)
    {
        var folders = new List<(string Title, string Path)>();
        // Enumerate directly so missing and inaccessible parents remain errors, not empty folders.
        foreach (var entry in Directory.EnumerateDirectories(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(entry);
            if (name is "." or ".." or ".DS_Store" || (!showHidden && name.StartsWith('.')))
                continue;
            if (!Directory.Exists(entry) || PathUtil.IsBundle(entry))
                continue;
            folders.Add((name, entry));
        }
        folders.Sort(static (a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
        return [new(null, folders, folders.Count == 0 ? "No subfolders to display" : null)];
    }
}
