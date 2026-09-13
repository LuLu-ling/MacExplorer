using MacExplorer.Models;
using MacExplorer.Native;

namespace MacExplorer.Services;

public sealed class VolumeInfo
{
    public required string Path { get; init; }
    public required string Name { get; init; }
}

public sealed class VolumeService
{
    public IReadOnlyList<VolumeInfo> List()
    {
        var volumes = new List<VolumeInfo>();
        foreach (var path in MacWorkspace.MountedVolumePaths())
        {
            if (path is "/System/Volumes/Data" or "/private/var/vm")
                continue;
            var name = path == "/" ? "Macintosh HD" : MacWorkspace.VolumeName(path);
            volumes.Add(new VolumeInfo { Path = path, Name = name });
        }

        if (volumes.All(v => v.Path != "/"))
            volumes.Insert(0, new VolumeInfo { Path = "/", Name = "Macintosh HD" });

        return volumes;
    }
}
