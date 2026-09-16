using MacExplorer.Localization;
using MacExplorer.Logging;
using MacExplorer.Models;
using MacExplorer.Native;
namespace MacExplorer.Services;

public sealed class FileService
{
    public void Copy(IReadOnlyList<string> paths)
    {
        MacPasteboard.WriteFiles(paths, cut: false);
        LogWrapper.Info("File", $"Copy {paths.Count} item(s) to pasteboard");
    }

    public void Cut(IReadOnlyList<string> paths)
    {
        MacPasteboard.WriteFiles(paths, cut: true);
        LogWrapper.Info("File", $"Cut {paths.Count} item(s) to pasteboard");
    }

    public bool CanPaste => MacPasteboard.HasFiles();

    public (IReadOnlyList<string> Paths, bool Cut) PeekClipboard() => MacPasteboard.ReadFiles();

    public async Task<IReadOnlyList<string>> PasteAsync(
        string destination,
        Func<string, Task<ConflictDecision>> resolveConflict)
    {
        var (paths, cut) = MacPasteboard.ReadFiles();
        if (paths.Count == 0)
            return [];

        var completed = await TransferAsync(paths, destination, cut, resolveConflict);
        if (cut && completed.Count == paths.Count)
            MacPasteboard.Clear();
        return completed;
    }

    public async Task<IReadOnlyList<string>> TransferAsync(
        IReadOnlyList<string> paths,
        string destination,
        bool move,
        Func<string, Task<ConflictDecision>> resolveConflict)
    {
        if (paths.Count == 0 || !Directory.Exists(destination))
            return [];

        var destRoot = Path.GetFullPath(destination);
        var completed = new List<string>();

        foreach (var source in paths)
        {
            var name = Path.GetFileName(source);
            if (string.IsNullOrEmpty(name))
                continue;

            string sourceFull;
            try
            {
                sourceFull = Path.GetFullPath(source);
            }
            catch
            {
                continue;
            }

            var parent = Path.GetDirectoryName(sourceFull);
            if (move && parent is not null &&
                Path.GetFullPath(parent).TrimEnd('/').Equals(destRoot.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                continue;

            var dest = Path.Combine(destRoot, name);
            if (sourceFull.Equals(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                if (move)
                    continue;
                dest = PathUtil.UniquePath(destRoot, name);
            }
            else if (PathUtil.Exists(dest))
            {
                var decision = await resolveConflict(name);
                switch (decision)
                {
                    case ConflictDecision.Skip:
                        continue;
                    case ConflictDecision.Cancel:
                        LogWrapper.Info("File", $"Transfer {completed.Count}/{paths.Count} item(s) move={move}");
                        return completed;
                    case ConflictDecision.KeepBoth:
                        dest = PathUtil.UniquePath(destRoot, name);
                        break;
                    case ConflictDecision.Replace:
                        var remove = MacFileManager.Remove(dest);
                        if (!remove.Ok)
                            continue;
                        break;
                }
            }

            var result = move ? MacFileManager.Move(sourceFull, dest) : MacFileManager.Copy(sourceFull, dest);
            if (result.Ok)
                completed.Add(result.ResultPath ?? dest);
        }

        LogWrapper.Info("File", $"Transfer {completed.Count}/{paths.Count} item(s) move={move}");
        return completed;
    }

    public MacFileResult Rename(string path, string newName)
    {
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrWhiteSpace(newName))
            return new MacFileResult(false, Lang.Text("File.Error.InvalidName"));
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return new MacFileResult(false, Lang.Text("File.Error.InvalidCharacters"));
        var dest = Path.Combine(parent, newName);
        if (path == dest)
            return new MacFileResult(true, null, path);
        if (PathUtil.Exists(dest))
            return new MacFileResult(false, Lang.Text("File.Error.NameExists"));
        var renamed = MacFileManager.Rename(path, dest);
        if (renamed.Ok) LogWrapper.Info("File", $"Rename {path} -> {dest}");
        else LogWrapper.Warn("File", $"Rename failed: {renamed.Error}");
        return renamed;
    }

    public MacFileResult Trash(string path)
    {
        var result = MacFileManager.Trash(path);
        if (result.Ok) LogWrapper.Info("File", $"Trash {path}");
        else LogWrapper.Warn("File", $"Trash failed: {result.Error}");
        return result;
    }

    public MacFileResult Delete(string path)
    {
        var result = MacFileManager.Remove(path);
        if (result.Ok) LogWrapper.Info("File", $"Delete {path}");
        else LogWrapper.Warn("File", $"Delete failed: {result.Error}");
        return result;
    }

    public MacFileResult NewFolder(string directory)
    {
        var dest = PathUtil.UniquePath(directory, Lang.Text("File.NewFolder"));
        try
        {
            Directory.CreateDirectory(dest);
            return new MacFileResult(true, null, dest);
        }
        catch (Exception ex)
        {
            return new MacFileResult(false, ex.Message);
        }
    }

    public MacFileResult NewFile(string directory)
    {
        var dest = PathUtil.UniquePath(directory, Lang.Text("File.UntitledName"));
        try
        {
            using (File.Create(dest)) { }
            return new MacFileResult(true, null, dest);
        }
        catch (Exception ex)
        {
            return new MacFileResult(false, ex.Message);
        }
    }

    public bool Open(string path) => MacWorkspace.Open(path);

    public bool Reveal(string path) => MacWorkspace.Reveal(path);

    public void Share(IReadOnlyList<string> paths, string service) => MacWorkspace.Share(paths, service);

    public IReadOnlyList<string> Volumes() => MacWorkspace.MountedVolumePaths();

    public string VolumeName(string path) => MacWorkspace.VolumeName(path);
}
