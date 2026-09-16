using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MacExplorer.Localization;
using MacExplorer.Models;
using MacExplorer.Services;
namespace MacExplorer.ViewModels;

public sealed partial class HashesViewModel : ViewModelBase, IDisposable
{
    private readonly string _path;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, bool> _showHashes = new(StringComparer.Ordinal)
    {
        ["CRC32"] = true,
        ["MD5"] = true,
        ["SHA1"] = true,
        ["SHA256"] = true,
        ["SHA384"] = false,
        ["SHA512"] = false
    };

    public HashesViewModel(string path)
    {
        _path = path;
        Hashes =
        [
            new() { Algorithm = "CRC32" },
            new() { Algorithm = "MD5" },
            new() { Algorithm = "SHA1" },
            new() { Algorithm = "SHA256" },
            new() { Algorithm = "SHA384" },
            new() { Algorithm = "SHA512" }
        ];

        foreach (var algorithm in _showHashes.Where(static x => x.Value).Select(static x => x.Key).ToArray())
            ToggleIsEnabled(algorithm);
    }

    public ObservableCollection<HashInfoItem> Hashes { get; }

    [RelayCommand]
    private void ToggleIsEnabled(string? algorithm)
    {
        if (algorithm is null)
            return;
        var item = Hashes.First(x => x.Algorithm == algorithm);
        item.IsEnabled = !item.IsEnabled;
        _showHashes[item.Algorithm] = item.IsEnabled;

        if (item.HashValue is null && item.IsEnabled)
            _ = CalculateAsync(item);
    }

    public void Dispose() => _cts.Cancel();

    private async Task CalculateAsync(HashInfoItem item)
    {
        await Dispatcher.UIThread.InvokeAsync(() => item.IsCalculating = true);
        string? value = null;
        string? error = null;
        var calculated = false;
        try
        {
            await using var stream = File.OpenRead(_path);
            value = item.Algorithm switch
            {
                "CRC32" => await ChecksumHelpers.CreateCRC32(stream, _cts.Token),
                "MD5" => await ChecksumHelpers.CreateMD5(stream, _cts.Token),
                "SHA1" => await ChecksumHelpers.CreateSHA1(stream, _cts.Token),
                "SHA256" => await ChecksumHelpers.CreateSHA256(stream, _cts.Token),
                "SHA384" => await ChecksumHelpers.CreateSHA384(stream, _cts.Token),
                "SHA512" => await ChecksumHelpers.CreateSHA512(stream, _cts.Token),
                _ => throw new InvalidOperationException($"The hash algorithm '{item.Algorithm}' is not supported.")
            };
            calculated = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            error = Lang.Text("Properties.Hash.FileOpen");
        }
        catch
        {
            error = Lang.Text("Properties.Hash.Failed");
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (value is not null)
                item.HashValue = value;
            else if (error is not null)
                item.HashValue = error;
            item.IsCalculated = calculated;
            item.IsCalculating = false;
        });
    }
}
