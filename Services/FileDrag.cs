using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using MacExplorer.Localization;
using MacExplorer.Models;
using MacExplorer.Native;

namespace MacExplorer.Services;

internal static class FileDrag
{
    public const double Threshold = 6;

    public static bool SuppressToolTips { get; private set; }

    public static void Begin(Visual? origin = null)
    {
        SuppressToolTips = true;
        CloseOpen(origin);
    }

    public static void End() => SuppressToolTips = false;

    public static void CloseOpen(Visual? origin)
    {
        for (var visual = origin; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Control control && ToolTip.GetIsOpen(control))
                ToolTip.SetIsOpen(control, false);
        }
    }

    public static bool PreferMove(IReadOnlyList<string> sources, string destination, KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Alt) || modifiers.HasFlag(KeyModifiers.Control))
            return false;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            return true;
        return sources.Count > 0 && sources.All(source => MacWorkspace.SameVolume(source, destination));
    }

    public static bool CanAccept(IReadOnlyList<string> sources, string destination)
    {
        if (sources.Count == 0 || SpecialFolders.IsVirtual(destination) || !Directory.Exists(destination))
            return false;

        string dest;
        try
        {
            dest = Path.GetFullPath(destination).TrimEnd('/');
        }
        catch
        {
            return false;
        }
        if (dest.Length == 0)
            dest = "/";

        var alreadyThere = true;
        foreach (var source in sources)
        {
            string src;
            try
            {
                src = Path.GetFullPath(source).TrimEnd('/');
            }
            catch
            {
                return false;
            }
            if (src.Length == 0)
                src = "/";

            if (src.Equals(dest, StringComparison.OrdinalIgnoreCase) ||
                dest.StartsWith(src + "/", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!Parent(src).Equals(dest, StringComparison.OrdinalIgnoreCase))
                alreadyThere = false;
        }

        return !alreadyThere;
    }

    private static string Parent(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent))
            return "/";
        var trimmed = parent.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    public static DragDropEffects Effect(
        IReadOnlyList<string> sources,
        string destination,
        DragDropEffects allowed,
        KeyModifiers modifiers)
    {
        if (!CanAccept(sources, destination))
            return DragDropEffects.None;

        var wanted = PreferMove(sources, destination, modifiers)
            ? DragDropEffects.Move
            : DragDropEffects.Copy;
        if ((allowed & wanted) != 0)
            return wanted;
        if ((allowed & DragDropEffects.Copy) != 0)
            return DragDropEffects.Copy;
        if ((allowed & DragDropEffects.Move) != 0)
            return DragDropEffects.Move;
        return DragDropEffects.None;
    }

    public static string Caption(DragDropEffects effect, string? destination)
    {
        if (effect == DragDropEffects.None || string.IsNullOrEmpty(destination))
            return Lang.Text("Drag.CantDrop");

        var name = destination is "/" ? Lang.Text("Places.MacintoshHD") : Path.GetFileName(destination.TrimEnd('/'));
        if (string.IsNullOrEmpty(name))
            name = destination;
        return effect switch
        {
            DragDropEffects.Move => Lang.Text("Drag.MoveTo", name),
            DragDropEffects.Link => Lang.Text("Drag.AddTo", name),
            _ => Lang.Text("Drag.CopyTo", name)
        };
    }


    public static IReadOnlyList<string>? Paths(IDataTransfer? data)
    {
        var files = data?.TryGetFiles();
        if (files is null || files.Length == 0)
            return null;

        List<string>? paths = null;
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
                continue;
            paths ??= new List<string>(files.Length);
            paths.Add(path);
        }

        return paths is { Count: > 0 } ? paths : null;
    }

    public static async Task<DataTransfer?> Create(IStorageProvider storage, IReadOnlyList<string> paths)
    {
        DataTransfer? transfer = null;
        foreach (var path in paths)
        {
            IStorageItem? item = Directory.Exists(path)
                ? await storage.TryGetFolderFromPathAsync(path)
                : File.Exists(path)
                    ? await storage.TryGetFileFromPathAsync(path)
                    : null;
            if (item is null)
                continue;
            transfer ??= new DataTransfer();
            transfer.Add(DataTransferItem.CreateFile(item));
        }

        return transfer;
    }
}
