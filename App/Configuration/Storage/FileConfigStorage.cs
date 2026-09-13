using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using MacExplorer.Logging;

namespace MacExplorer.Configuration.Storage;

public class FileConfigStorage : ConfigStorage
{
    public IKeyValueFileProvider File { get; }

    private readonly Channel<(string, Action)> _writeActionChannel;
    private readonly CancellationTokenSource _writeActionCts;
    private readonly ManualResetEventSlim _writeStopEvent = new(true);

    public FileConfigStorage(IKeyValueFileProvider file)
    {
        File = file;
        _writeActionChannel = Channel.CreateUnbounded<(string, Action)>();
        _writeActionCts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            _writeStopEvent.Reset();
            const long syncInterval = 10000; // ms
            var lastSyncTick = Environment.TickCount64;
            var cancelToken = _writeActionCts.Token;
            var writeActionMap = new Dictionary<string, Action>();
            var reader = _writeActionChannel.Reader;
            try
            {
                while (!cancelToken.IsCancellationRequested)
                {
                    // 读入并合并暂存操作
                    var (key, action) = await reader.ReadAsync(cancelToken);
                    writeActionMap[key] = action;
                    Drain();

                    var wait = syncInterval - (Environment.TickCount64 - lastSyncTick);
                    if (wait > 0)
                    {
                        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
                        delayCts.CancelAfter(TimeSpan.FromMilliseconds(wait));
                        try
                        {
                            while (await reader.WaitToReadAsync(delayCts.Token))
                                Drain();
                        }
                        catch (OperationCanceledException) when (!cancelToken.IsCancellationRequested)
                        {
                            /* interval elapsed */
                        }
                    }

                    if (cancelToken.IsCancellationRequested)
                        break;

                    // 同步文件
                    Sync();
                    lastSyncTick = Environment.TickCount64;
                    writeActionMap.Clear();
                }
            }
            catch (OperationCanceledException)
            {
                /* ignoring */
            }
            finally
            {
                // 结束时执行一次同步
                Sync();
            }

            _writeStopEvent.Set();
            return;

            void Drain()
            {
                while (reader.TryRead(out var extra))
                    writeActionMap[extra.Item1] = extra.Item2;
            }

            void Sync()
            {
                try
                {
                    if (writeActionMap.Count == 0)
                        return;
                    LogWrapper.Debug("Config", $"Saving {writeActionMap.Count} key(s) to {File.FilePath}: {string.Join(", ", writeActionMap.Keys)}");
                    foreach (var action in writeActionMap.Values) action();
                    File.Sync();
                }
                catch (Exception ex)
                {
                    LogWrapper.Error(ex, "Config", "配置文件保存失败");
                }
            }
        });
    }

    protected override void OnStop()
    {
        _writeActionCts.Cancel();
        if (!_writeStopEvent.Wait(TimeSpan.FromSeconds(5)))
            LogWrapper.Warn("Config", "Config writer stop timed out");
        else
            _writeStopEvent.Dispose();
    }

    protected override bool OnAccess<TKey, TValue>(
        StorageAction action,
        ref TKey key,
        [NotNullWhen(true)] ref TValue value,
        object? argument)
    {
        if (key is not string strKey) throw new NotSupportedException($"Key '{key}' is not supported");
#pragma warning disable CS8762
        switch (action)
        {
            case StorageAction.Get:
                if (!File.Exists(strKey)) return false;
                try
                {
                    value = File.Get<TValue>(strKey);
                }
                catch (Exception ex) when (ex is JsonException
                                               or InvalidCastException
                                               or FormatException
                                               or OverflowException
                                               or ArgumentException
                                               or KeyNotFoundException
                                               or InvalidDataException)
                {
                    LogWrapper.Warn(ex, "Config", $"配置项 {strKey} 读取失败（可能已损坏），重置为默认值");
                    if (!_writeActionChannel.Writer.TryWrite((strKey, () => File.Remove(strKey))))
                    {
                        try
                        {
                            File.Remove(strKey);
                            File.Sync();
                        }
                        catch (Exception cleanupEx)
                        {
                            LogWrapper.Error(cleanupEx, "Config", $"配置项 {strKey} 同步删除失败");
                        }
                    }

                    return false;
                }

                return true;
            case StorageAction.Exists:
                if (typeof(TValue) == typeof(bool)) Unsafe.As<TValue, bool>(ref value) = File.Exists(strKey);
                else throw new InvalidOperationException("Exists must have a boolean value");
                return true;
            case StorageAction.Set:
                var localValue = value;
                _writeActionChannel.Writer.TryWrite((strKey, () => File.Set(strKey, localValue)));
                return false;
            case StorageAction.Delete:
                _writeActionChannel.Writer.TryWrite((strKey, () => File.Remove(strKey)));
                return false;
            default: throw new InvalidOperationException($"Invalid storage action: {action}");
        }
#pragma warning restore CS8762
    }

    public override string ToString() => $"{base.ToString()} ({File.FilePath})";
}
