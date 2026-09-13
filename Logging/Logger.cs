using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace MacExplorer.Logging;

public sealed class Logger : IAsyncDisposable
{
    public Logger(LoggerConfiguration configuration)
    {
        Configuration = configuration;
        CreateNewFile();
        _processingTask = ProcessLogQueueAsync();
    }

    private StreamWriter? _currentStream;
    private FileStream? _currentFile;
    private readonly List<string> _files = [];
    private long _droppedCount;
    private readonly Task _processingTask;
    private readonly Channel<string> _logChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true
    });
    private bool _disposed;

    public long DroppedLogCount => Interlocked.Read(ref _droppedCount);
    public ReadOnlyCollection<string> CurrentLogFiles => _files.AsReadOnly();
    public LoggerConfiguration Configuration { get; }

    public void Trace(string message) => Log($"[{GetTimeFormatted()}] [TRA] {message}");
    public void Debug(string message) => Log($"[{GetTimeFormatted()}] [DBG] {message}");
    public void Info(string message) => Log($"[{GetTimeFormatted()}] [INFO] {message}");
    public void Warn(string message) => Log($"[{GetTimeFormatted()}] [WARN] {message}");
    public void Error(string message) => Log($"[{GetTimeFormatted()}] [ERR!] {message}");
    public void Fatal(string message) => Log($"[{GetTimeFormatted()}] [FTL!] {message}");

    public void Log(string message)
    {
        if (_disposed) return;
        if (!_logChannel.Writer.TryWrite(message))
        {
            Interlocked.Increment(ref _droppedCount);
            Console.WriteLine($"Log dropped error: {message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _logChannel.Writer.Complete();
        await _processingTask.ConfigureAwait(false);
        if (_currentStream is not null)
            await _currentStream.DisposeAsync().ConfigureAwait(false);
        if (_currentFile is not null)
            await _currentFile.DisposeAsync().ConfigureAwait(false);
    }

    private void CreateNewFile()
    {
        var now = DateTime.Now;
        var nameFormat = (Configuration.FileNameFormat ?? $"Launch-{now.ToString("yyyy-M-d", CultureInfo.InvariantCulture)}-{{0}}") + ".log";
        var filename = nameFormat.Replace("{0}", now.ToString("HHmmssfff", CultureInfo.InvariantCulture));
        var filePath = Path.Combine(Configuration.StoreFolder, filename);
        _files.Add(filePath);
        var lastWriter = _currentStream;
        var lastFile = _currentFile;
        Directory.CreateDirectory(Configuration.StoreFolder);

        _currentFile = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _currentStream = new StreamWriter(_currentFile);

        _ = Task.Run(async () =>
        {
            if (lastWriter is not null)
            {
                try { await lastWriter.DisposeAsync().ConfigureAwait(false); }
                catch (Exception) { /* Don't care */ }
            }

            if (lastFile is not null)
            {
                try { await lastFile.DisposeAsync().ConfigureAwait(false); }
                catch (Exception) { /* Don't care */ }
            }

            if (!Configuration.AutoDeleteOldFile)
                return;

            var logFiles = Directory.GetFiles(Configuration.StoreFolder, "*.log", SearchOption.TopDirectoryOnly);
            var needToDelete = logFiles.Select(x => new FileInfo(x))
                .OrderBy(x => x.CreationTime)
                .Take(Math.Max(0, logFiles.Length - Configuration.MaxKeepOldFile));
            foreach (var logFile in needToDelete)
            {
                try { logFile.Delete(); }
                catch (Exception) { /* Don't care */ }
            }
        });
    }

    private async Task ProcessLogQueueAsync()
    {
        const int maxBatchLines = 198;
        var writeTimeout = TimeSpan.FromMilliseconds(325);
        var batch = new StringBuilder(4096);
        var lineCount = 0u;
        var lastFlush = Stopwatch.GetTimestamp();

        try
        {
            await foreach (var message in _logChannel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
#if DEBUG
                Console.WriteLine(message);
                System.Diagnostics.Debug.WriteLine(message);
#endif
                batch.AppendLine(message);
                lineCount++;

                var elapsed = Stopwatch.GetElapsedTime(lastFlush);
                if (lineCount >= maxBatchLines || elapsed > writeTimeout)
                    await DoRefreshAsync().ConfigureAwait(false);
            }

            if (lineCount != 0)
                await DoRefreshAsync().ConfigureAwait(false);

            async Task DoRefreshAsync()
            {
                await DoWriteAsync(batch).ConfigureAwait(false);
                batch.Clear();
                lineCount = 0;
                lastFlush = Stopwatch.GetTimestamp();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[{GetTimeFormatted()}] [ERROR] An error occurred while processing log queue: {e.Message}");
            throw;
        }
    }

    private async Task DoWriteAsync(StringBuilder ctx)
    {
        try
        {
            if (_currentFile?.Length >= Configuration.MaxFileSize)
                CreateNewFile();
            await _currentStream!.WriteAsync(ctx, CancellationToken.None).ConfigureAwait(false);
            await _currentStream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[{GetTimeFormatted()}] [ERROR] An error occurred while writing log file: {e.Message}");
            await File.AppendAllTextAsync(
                Path.Combine(Configuration.StoreFolder, "Error.log"),
                $"[{GetTimeFormatted()}] LogCycle Error: {e}\n");
            throw;
        }
    }

    private static string GetTimeFormatted() => $"{DateTime.Now:HH:mm:ss.fff}";
}
