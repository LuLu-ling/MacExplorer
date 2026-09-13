using System.Threading;
using FlowNet.Core;
using MacExplorer.Infrastructure;
using MacExplorer.Lifecycle;

namespace MacExplorer.Logging;

[Flow.Scope("log")]
public static partial class LogService
{
    private static bool _wrapperRegistered;

    public static Logger Logger { get; private set; } = null!;
    public static LogLevel MinLevel { get; set; } = LogLevel.Info;

    [Flow.Task]
    [Flow.Run(After = "app:loading")]
    private static Task Start()
    {
        var config = new LoggerConfiguration(Paths.Logs);
        Logger = new Logger(config);
        LogWrapper.OnLog += OnWrapperLog;
        _wrapperRegistered = true;
        LogWrapper.Info("Log", $"Logger initialized at {Paths.Logs}");
        AppLifecycle.RegisterProcessHooks();
        AppLifecycle.LogEnvironment();
        return Task.CompletedTask;
    }

    [Flow.Task("stop")]
    private static async Task Stop()
    {
        LogWrapper.Info("Log", "Logger stopping");
        if (_wrapperRegistered)
        {
            LogWrapper.OnLog -= OnWrapperLog;
            _wrapperRegistered = false;
        }

        if (Logger is not null)
            await Logger.DisposeAsync().ConfigureAwait(false);
        Console.WriteLine("[Log] Logger stopped");
    }

    private static void OnWrapperLog(LogLevel level, string msg, string? module, Exception? ex)
    {
        if ((int)level.RealLevel() < (int)MinLevel.RealLevel())
            return;

        var thread = Thread.CurrentThread.Name ?? $"#{Environment.CurrentManagedThreadId}";
        var moduleText = module is null ? string.Empty : $"[{module}] ";
        var result = $"[{DateTime.Now:HH:mm:ss.fff}] [{level.PrintName()}] [{thread}] {moduleText}{msg}";
        Logger.Log(ex is null ? result : $"{result}\n{ex}");
    }
}
