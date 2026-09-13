using FlowNet.Core;
using MacExplorer.Logging;
using MacExplorer.Services;
using MacExplorer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MacExplorer.Lifecycle;

[Flow.Scope("services")]
public static partial class ServiceFlow
{
    [Flow.Task]
    [Flow.Run(After = "config:start")]
    private static Task Start()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DialogCallbacks>();
        services.AddSingleton<FileService>();
        services.AddSingleton<ListingService>();
        services.AddSingleton<IconService>();
        services.AddSingleton<VolumeService>();
        services.AddSingleton<MainViewModel>();
        AppServices.Provider = services.BuildServiceProvider();
        LogWrapper.Info("Services", "Service provider initialized");
        return Task.CompletedTask;
    }
}

public static class AppServices
{
    public static IServiceProvider Provider { get; set; } = null!;

    public static T Get<T>() where T : notnull => Provider.GetRequiredService<T>();
}
