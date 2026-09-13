using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;
using FlowNet.Core;
using MacExplorer.Logging;
namespace MacExplorer.Lifecycle;

/// <summary>
/// Process/UI exception hooks matching PCL-CE <c>Lifecycle.OnInitialize</c> / <c>OnException</c>,
/// adapted to Avalonia + FlowNet (no WPF Lifecycle IoC).
/// </summary>
public static class AppLifecycle
{
    private static int _processHooks;
    private static int _dispatcherHook;

    public static void OnException(object? ex)
    {
        var exception = ex as Exception;
        Console.WriteLine($"[Lifecycle] 未捕获的异常: {exception}");
        LogWrapper.Fatal(exception, "Lifecycle", "未捕获的异常");
    }

    public static void RegisterProcessHooks()
    {
        if (Interlocked.Exchange(ref _processHooks, 1) == 1)
            return;

        AppDomain.CurrentDomain.UnhandledException += (_, e) => OnException(e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogWrapper.Error(e.Exception, "Lifecycle", "未观测到的异步任务异常");
            e.SetObserved();
        };
        LogWrapper.Debug("Lifecycle", "已注册进程级异常钩子");
    }

    public static void RegisterDispatcherHook()
    {
        if (Interlocked.Exchange(ref _dispatcherHook, 1) == 1)
            return;

        Dispatcher.UIThread.UnhandledException += (_, e) => OnException(e.Exception);
        LogWrapper.Debug("Lifecycle", "已注册 UI 调度器异常钩子");
    }

    public static void Shutdown()
    {
        Console.WriteLine("[Lifecycle] Shutdown starting");
        LogWrapper.Info("Lifecycle", "Shutdown starting");
        var captured = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            RunStop("config:stop");
            RunStop("log:stop");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(captured);
            Console.WriteLine("[Lifecycle] Shutdown finished");
        }
    }

    private static void RunStop(string taskId)
    {
        Console.WriteLine($"[Lifecycle] Invoking {taskId}");
        var task = Flow.InvokeTask(taskId);
        if (task.Wait(TimeSpan.FromSeconds(8)))
            return;
        Console.WriteLine($"[Lifecycle] {taskId} timed out");
        LogWrapper.Warn("Lifecycle", $"{taskId} timed out");
    }

    public static void LogEnvironment()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "ARM64",
            var other => other.ToString()
        };
        var osArch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "ARM64",
            var other => other.ToString()
        };
        var version = typeof(AppLifecycle).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(AppLifecycle).Assembly.GetName().Version?.ToString()
            ?? "?";
        var args = Environment.GetCommandLineArgs().AsSpan(1).ToArray();
        var info = new StringBuilder();
        info.Append("\n版本: ").Append(version).Append(" (").Append(arch).Append(')');
        info.Append("\n运行时: ").Append(RuntimeInformation.FrameworkDescription);
        info.Append("\n路径: ").Append(Environment.ProcessPath ?? AppContext.BaseDirectory);
        info.Append("\n命令行参数:");
        if (args.Length == 0) info.Append(" []");
        else foreach (var x in args) info.Append("\n - ").Append(x);
        info.Append("\n系统: ").Append(Environment.OSVersion.Version).Append(" (").Append(osArch).Append(')');
        info.Append("\n工作集: ").Append(Environment.WorkingSet / (1024 * 1024)).Append(" MiB");
        info.Append("\nFlow 任务: ").Append(string.Join(", ", Flow.TaskIdentifiers.OrderBy(x => x)));
        LogWrapper.Info("Startup", info.ToString());
    }
}
