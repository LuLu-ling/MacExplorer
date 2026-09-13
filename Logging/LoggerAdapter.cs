using System.Text;
using Microsoft.Extensions.Logging;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace MacExplorer.Logging;

public sealed class LoggerAdapter(Logger logger, string categoryName) : ILogger
{
    private readonly Logger _innerLogger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly string _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
    private static readonly AsyncLocal<Stack<object>> ScopeStack = new();

    IDisposable ILogger.BeginScope<TState>(TState state)
    {
        ScopeStack.Value ??= new Stack<object>();
        ScopeStack.Value.Push(state!);
        return new ScopeDisposable();
    }

    public bool IsEnabled(MelLogLevel level) => level != MelLogLevel.None;

    public void Log<TState>(
        MelLogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;
        ArgumentNullException.ThrowIfNull(formatter);

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(_categoryName))
            sb.Append('[').Append(_categoryName).Append("] ");
        if (eventId.Id != 0 || !string.IsNullOrEmpty(eventId.Name))
        {
            sb.Append("[EventId:");
            sb.Append(string.IsNullOrEmpty(eventId.Name) ? eventId.Id.ToString() : $"{eventId.Id}:{eventId.Name}");
            sb.Append("] ");
        }

        var scopeContext = BuildScopeContext();
        if (!string.IsNullOrEmpty(scopeContext))
            sb.Append('[').Append(scopeContext).Append("] ");

        sb.Append(formatter(state, exception));
        var finalMessage = sb.ToString();
        switch (logLevel)
        {
            case MelLogLevel.Trace: _innerLogger.Trace(finalMessage); break;
            case MelLogLevel.Debug: _innerLogger.Debug(finalMessage); break;
            case MelLogLevel.Information: _innerLogger.Info(finalMessage); break;
            case MelLogLevel.Warning: _innerLogger.Warn(finalMessage); break;
            case MelLogLevel.Error: _innerLogger.Error(finalMessage); break;
            case MelLogLevel.Critical: _innerLogger.Fatal(finalMessage); break;
        }

        if (exception is not null)
            _innerLogger.Log($"[{_categoryName}] Exception: {exception}");
    }

    private static string BuildScopeContext()
    {
        var stack = ScopeStack.Value;
        if (stack is null || stack.Count == 0)
            return string.Empty;
        return string.Join(" => ", stack.AsEnumerable().Reverse().Select(s => s.ToString()));
    }

    private sealed class ScopeDisposable : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ScopeStack.Value?.TryPop(out _);
        }
    }
}
