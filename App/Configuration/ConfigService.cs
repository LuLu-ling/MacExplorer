using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FlowNet.Core;
using MacExplorer.Infrastructure;
using MacExplorer.Configuration.Storage;
using MacExplorer.Logging;
using MacExplorer.Models;

namespace MacExplorer.Configuration;

[Flow.Scope("config")]
public static partial class ConfigService
{
    private static readonly Dictionary<string, ConfigItem> Items = [];
    private static readonly HashSet<string> Keys = [];
    private static FileConfigStorage? _local;
    private static FileConfigStorage? _shared;
    private static bool _providersInitialized;
    private static bool _itemsInitialized;

    public static bool IsInitialized { get; private set; }
    public static IReadOnlySet<string> KeySet => Keys;
    public static string LocalConfigPath => Paths.LocalConfig;

    public static ConfigItem<TValue> Register<TValue>(string key, TValue defaultValue, ConfigSource source = ConfigSource.Local)
    {
        if (Items.TryGetValue(key, out var existing))
            return (ConfigItem<TValue>)existing;
        var item = new ConfigItem<TValue>(key, defaultValue, source);
        Items[key] = item;
        Keys.Add(key);
        return item;
    }

    public static ConfigItem<TValue> Register<TValue>(string key, Func<TValue> defaultValue, ConfigSource source = ConfigSource.Local)
    {
        if (Items.TryGetValue(key, out var existing))
            return (ConfigItem<TValue>)existing;
        var item = new ConfigItem<TValue>(key, defaultValue, source);
        Items[key] = item;
        Keys.Add(key);
        return item;
    }

    public static bool TryGetConfigItemNoType(string key, [NotNullWhen(true)] out ConfigItem? item) =>
        Items.TryGetValue(key, out item);

    public static bool TryGetConfigItem<TValue>(string key, out ConfigItem<TValue>? item)
    {
        if (!_itemsInitialized) throw new InvalidOperationException("Not initialized");
        var result = TryGetConfigItemNoType(key, out var value);
        item = result ? value as ConfigItem<TValue> : null;
        return result;
    }

    public static ConfigItem<TValue> GetConfigItem<TValue>(string key)
    {
        if (!TryGetConfigItem<TValue>(key, out var item))
            throw new KeyNotFoundException($"Config key not found: '{key}'");
        return item ?? throw new InvalidCastException($"Type of '{key}' is incompatible with {typeof(TValue).FullName}");
    }

    public static IConfigProvider GetProvider(ConfigSource source)
    {
        if (!_providersInitialized) throw new InvalidOperationException("Not initialized");
        return source switch
        {
            ConfigSource.Shared => _shared!,
            ConfigSource.Local => _local!,
            _ => throw new ArgumentException($"Invalid source: {source}")
        };
    }

    public static void RegisterObserver(IConfigScope scope, ConfigObserver observer)
    {
        foreach (var key in scope.CheckScope(KeySet))
            Items[key].Observe(observer);
    }

    [Flow.Task]
    [Flow.Run(After = "log:start")]
    private static Task Start()
    {
        if (IsInitialized) return Task.CompletedTask;
        LogWrapper.Info("Config", "Config initialization started");
        Config.Touch();
        _itemsInitialized = true;
        LogWrapper.Debug("Config", $"Finished initialize {Items.Count} item(s)");

        Directory.CreateDirectory(Paths.Data);
        TryMigrateLegacySettings();
        var file = new JsonFileProvider(LocalConfigPath);
        _local = new FileConfigStorage(file);
        _shared = _local;
        _providersInitialized = true;
        if (_legacy is not null)
            ApplyLegacy(_legacy);

        foreach (var item in Items.Values)
            item.TriggerEvent(ConfigEvent.Init, null, true, true);

        IsInitialized = true;
        LogWrapper.Info("Config", $"Config loaded from {LocalConfigPath} ({Items.Count} items)");
        return Task.CompletedTask;
    }

    [Flow.Task("stop")]
    private static Task Stop()
    {
        LogWrapper.Info("Config", "Saving config...");
        _local?.Stop();
        return Task.CompletedTask;
    }

    private static AppSettings? _legacy;

    private static void TryMigrateLegacySettings()
    {
        if (System.IO.File.Exists(LocalConfigPath) || !System.IO.File.Exists(Paths.LegacySettings))
            return;
        try
        {
            var json = System.IO.File.ReadAllText(Paths.LegacySettings);
            _legacy = JsonSerializer.Deserialize<AppSettings>(json);
            LogWrapper.Info("Config", "Found legacy settings.json");
        }
        catch (Exception ex)
        {
            LogWrapper.Warn(ex, "Config", "Legacy settings read failed");
        }
    }

    private static void ApplyLegacy(AppSettings legacy)
    {
        Config.Appearance.ThemeConfig.SetValue((int)legacy.Theme);
        Config.Window.Width = legacy.WindowWidth;
        Config.Window.Height = legacy.WindowHeight;
        Config.Sidebar.Width = legacy.SidebarWidth;
        Config.Sidebar.IsOpen = legacy.IsSidebarOpen;
        Config.InfoPane.Show = legacy.ShowInfoPane;
        Config.InfoPane.Width = legacy.InfoPaneWidth;
        Config.Files.ShowHidden = legacy.ShowHidden;
        Config.Files.ShowExtensions = legacy.ShowExtensions;
        Config.Layout.Kind = (int)legacy.Layout;
        Config.Layout.SortField = (int)legacy.SortField;
        Config.Layout.SortDirection = (int)legacy.SortDirection;
        Config.Layout.FolderPriority = (int)legacy.FolderPriority;
        Config.Layout.Size = legacy.LayoutSize;
        Config.Home.ShowQuickAccess = legacy.ShowQuickAccess;
        Config.Home.ShowVolumes = legacy.ShowVolumes;
        Config.Home.ShowRecents = legacy.ShowRecents;
        Config.Sidebar.Pins = [.. legacy.Pins];
        Config.Home.Recents = [.. legacy.Recents];
    }
}
