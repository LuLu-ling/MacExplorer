using Avalonia.Threading;
using MacExplorer.Models;

namespace MacExplorer.Services;

internal sealed class DragHoverOpen
{
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(1300);

    private readonly DispatcherTimer _timer;
    private string? _path;
    private Action<string>? _open;

    public DragHoverOpen()
    {
        _timer = new DispatcherTimer { Interval = Delay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            var path = _path;
            var open = _open;
            _path = null;
            _open = null;
            if (path is not null)
                open?.Invoke(path);
        };
    }

    public void Update(string? path, string? currentPath, Action<string> open)
    {
        if (path is null ||
            SpecialFolders.IsVirtual(path) ||
            !Directory.Exists(path) ||
            Same(path, currentPath))
        {
            if (_path is not null)
                Cancel();
            return;
        }

        if (string.Equals(_path, path, StringComparison.OrdinalIgnoreCase))
            return;

        _path = path;
        _open = open;
        _timer.Stop();
        _timer.Start();
    }

    public void Cancel()
    {
        _timer.Stop();
        _path = null;
        _open = null;
    }

    private static bool Same(string path, string? other)
    {
        if (other is null || SpecialFolders.IsVirtual(other))
            return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(path).TrimEnd('/'),
                Path.GetFullPath(other).TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(path, other, StringComparison.OrdinalIgnoreCase);
        }
    }
}
