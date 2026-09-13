using Microsoft.Extensions.Logging;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace MacExplorer.Logging;

public static class LoggerExtensions
{
    public static ILogger CreateLogger(this Logger logger, string categoryName) =>
        new LoggerAdapter(logger, categoryName);

    public static ILoggerFactory CreateLoggerFactory(this Logger logger) =>
        new LoggerFactoryAdapter(logger);

    public static void LogIf(this ILogger logger, bool condition, MelLogLevel level, string message)
    {
        if (condition)
            logger.Log(level, message);
    }

    public static void LogIf(this ILogger logger, bool condition, MelLogLevel level, Exception? exception, string message)
    {
        if (condition)
            logger.Log(level, exception, message);
    }

    public static IDisposable LogPerformance(this ILogger logger, string operationName) =>
        new PerformanceScope(logger, operationName);

    private sealed class PerformanceScope(ILogger logger, string operationName) : IDisposable
    {
        private readonly long _start = Environment.TickCount64;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            logger.LogInformation("{Operation} finished in {Elapsed} ms", operationName, Environment.TickCount64 - _start);
        }
    }
}
