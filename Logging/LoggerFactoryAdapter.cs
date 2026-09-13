using Microsoft.Extensions.Logging;

namespace MacExplorer.Logging;

public sealed class LoggerFactoryAdapter(Logger logger) : ILoggerFactory
{
    private readonly Logger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly List<IDisposable> _disposables = [];

    public void AddProvider(ILoggerProvider provider) => _disposables.Add(provider);

    public ILogger CreateLogger(string categoryName) => new LoggerAdapter(_logger, categoryName);

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
        _disposables.Clear();
    }
}
