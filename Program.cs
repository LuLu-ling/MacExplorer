using System.Runtime.Versioning;
using Avalonia;
using FlowNet.Core;
using MacExplorer.Lifecycle;

namespace MacExplorer;

[Flow.Scope("app")]
[SupportedOSPlatform("macos")]
internal static partial class Program
{
    internal static string[] Args { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        Args = args;
        Thread.CurrentThread.Name = "STA";
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("MacExplorer is macOS-only.");

        // Loading DAG (log → config → services) runs on the thread pool.
        // Avalonia must stay on this STA thread, so block until loading finishes.
        FlowInterops.Run().GetAwaiter().GetResult();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(Args);
        }
        catch (Exception ex)
        {
            AppLifecycle.OnException(ex);
            throw;
        }
        finally
        {
            AppLifecycle.Shutdown();
        }
    }

    [Flow.Task("loading")]
    [Flow.Task("exit")]
    private static Task Wildcard() => Task.CompletedTask;

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
