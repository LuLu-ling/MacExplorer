using MacExplorer.Configuration;
using MacExplorer.Localization;
using MacExplorer.Models;
namespace MacExplorer.Infrastructure;

public static class Config
{
    internal static void Touch()
    {
        _ = Appearance.ThemeConfig;
        _ = Localization.LanguageConfig;
        _ = Window.WidthConfig;
        _ = Sidebar.PinsConfig;
        _ = Sidebar.LocationOrderConfig;
        _ = Layout.KindConfig;

        _ = Files.ShowHiddenConfig;
        _ = InfoPane.ShowConfig;
        _ = Home.RecentsConfig;
        _ = Logging.MinLevelConfig;
        _ = FileVersionConfig;
    }

    public static ConfigItem<int> FileVersionConfig { get; } = ConfigService.Register("FileVersion", 1);
    public static int FileVersion { get => FileVersionConfig.GetValue(); set => FileVersionConfig.SetValue(value); }

    public static class Appearance
    {
        public static ConfigItem<int> ThemeConfig { get; } = ConfigService.Register("UiTheme", (int)ThemeMode.Default);
        public static ThemeMode Theme
        {
            get => (ThemeMode)ThemeConfig.GetValue();
            set => ThemeConfig.SetValue((int)value);
        }
    }

    public static class Localization
    {
        public static ConfigItem<string> LanguageConfig { get; } =
            ConfigService.Register("UiLanguage", LocalizationService.Auto);
        public static string Language
        {
            get => LanguageConfig.GetValue();
            set => LanguageConfig.SetValue(value);
        }
    }

    public static class Window
    {
        public static ConfigItem<double> WidthConfig { get; } = ConfigService.Register("WindowWidth", 1280d);
        public static ConfigItem<double> HeightConfig { get; } = ConfigService.Register("WindowHeight", 800d);
        public static double Width { get => WidthConfig.GetValue(); set => WidthConfig.SetValue(value); }
        public static double Height { get => HeightConfig.GetValue(); set => HeightConfig.SetValue(value); }
    }

    public static class Sidebar
    {
        public static ConfigItem<double> WidthConfig { get; } = ConfigService.Register("SidebarWidth", 240d);
        public static ConfigItem<bool> IsOpenConfig { get; } = ConfigService.Register("SidebarOpen", true);
        public static ConfigItem<List<string>> PinsConfig { get; } = ConfigService.Register("SidebarPins", DefaultPins);
        public static ConfigItem<List<string>> LocationOrderConfig { get; } = ConfigService.Register("SidebarLocationOrder", static () => new List<string>());
        public static double Width { get => WidthConfig.GetValue(); set => WidthConfig.SetValue(value); }
        public static bool IsOpen { get => IsOpenConfig.GetValue(); set => IsOpenConfig.SetValue(value); }
        public static List<string> Pins { get => PinsConfig.GetValue(); set => PinsConfig.SetValue(value); }
        public static List<string> LocationOrder { get => LocationOrderConfig.GetValue(); set => LocationOrderConfig.SetValue(value); }



        private static List<string> DefaultPins() =>
        [
            SpecialFolders.Desktop,
            SpecialFolders.Documents,
            SpecialFolders.Downloads,
            SpecialFolders.Pictures,
            SpecialFolders.Music,
            SpecialFolders.Movies
        ];
    }

    public static class Layout
    {
        public static ConfigItem<int> KindConfig { get; } = ConfigService.Register("LayoutKind", (int)LayoutKind.Details);
        public static ConfigItem<int> SizeConfig { get; } = ConfigService.Register("LayoutSize", 3);
        public static ConfigItem<int> SortFieldConfig { get; } = ConfigService.Register("SortField", (int)Models.SortField.Name);
        public static ConfigItem<int> SortDirectionConfig { get; } = ConfigService.Register("SortDirection", (int)Models.SortDirection.Ascending);
        public static ConfigItem<int> FolderPriorityConfig { get; } = ConfigService.Register("FolderPriority", (int)Models.FolderPriority.FoldersFirst);
        public static ConfigItem<int> GroupOptionConfig { get; } = ConfigService.Register("GroupOption", (int)Models.GroupOption.None);
        public static ConfigItem<int> GroupDirectionConfig { get; } = ConfigService.Register("GroupDirection", (int)Models.SortDirection.Ascending);
        public static ConfigItem<int> GroupByDateUnitConfig { get; } = ConfigService.Register("GroupByDateUnit", (int)Models.GroupByDateUnit.Year);
        public static ConfigItem<double> NameColumnWidthConfig { get; } = ConfigService.Register("NameColumnWidth", 240d);
        public static ConfigItem<double> DateModifiedColumnWidthConfig { get; } = ConfigService.Register("DateModifiedColumnWidth", 200d);
        public static ConfigItem<double> DateCreatedColumnWidthConfig { get; } = ConfigService.Register("DateCreatedColumnWidth", 200d);
        public static ConfigItem<double> TypeColumnWidthConfig { get; } = ConfigService.Register("TypeColumnWidth", 140d);
        public static ConfigItem<double> SizeColumnWidthConfig { get; } = ConfigService.Register("SizeColumnWidth", 100d);
        public static ConfigItem<double> TagsColumnWidthConfig { get; } = ConfigService.Register("TagsColumnWidth", 100d);
        public static ConfigItem<bool> ShowDateModifiedColumnConfig { get; } = ConfigService.Register("ShowDateModifiedColumn", true);
        public static ConfigItem<bool> ShowDateCreatedColumnConfig { get; } = ConfigService.Register("ShowDateCreatedColumn", true);
        public static ConfigItem<bool> ShowTypeColumnConfig { get; } = ConfigService.Register("ShowTypeColumn", true);
        public static ConfigItem<bool> ShowSizeColumnConfig { get; } = ConfigService.Register("ShowSizeColumn", true);
        public static ConfigItem<bool> ShowTagsColumnConfig { get; } = ConfigService.Register("ShowTagsColumn", true);
        public static ConfigItem<List<string>> ColumnOrderConfig { get; } = ConfigService.Register("ColumnOrder", DefaultColumnOrder);
        public static int Kind { get => KindConfig.GetValue(); set => KindConfig.SetValue(value); }
        public static int Size { get => SizeConfig.GetValue(); set => SizeConfig.SetValue(value); }
        public static int SortField { get => SortFieldConfig.GetValue(); set => SortFieldConfig.SetValue(value); }
        public static int SortDirection { get => SortDirectionConfig.GetValue(); set => SortDirectionConfig.SetValue(value); }
        public static int FolderPriority { get => FolderPriorityConfig.GetValue(); set => FolderPriorityConfig.SetValue(value); }
        public static int GroupOption { get => GroupOptionConfig.GetValue(); set => GroupOptionConfig.SetValue(value); }
        public static int GroupDirection { get => GroupDirectionConfig.GetValue(); set => GroupDirectionConfig.SetValue(value); }
        public static int GroupByDateUnit { get => GroupByDateUnitConfig.GetValue(); set => GroupByDateUnitConfig.SetValue(value); }
        public static double NameColumnWidth { get => NameColumnWidthConfig.GetValue(); set => NameColumnWidthConfig.SetValue(value); }
        public static double DateModifiedColumnWidth { get => DateModifiedColumnWidthConfig.GetValue(); set => DateModifiedColumnWidthConfig.SetValue(value); }
        public static double DateCreatedColumnWidth { get => DateCreatedColumnWidthConfig.GetValue(); set => DateCreatedColumnWidthConfig.SetValue(value); }
        public static double TypeColumnWidth { get => TypeColumnWidthConfig.GetValue(); set => TypeColumnWidthConfig.SetValue(value); }
        public static double SizeColumnWidth { get => SizeColumnWidthConfig.GetValue(); set => SizeColumnWidthConfig.SetValue(value); }
        public static double TagsColumnWidth { get => TagsColumnWidthConfig.GetValue(); set => TagsColumnWidthConfig.SetValue(value); }
        public static bool ShowDateModifiedColumn { get => ShowDateModifiedColumnConfig.GetValue(); set => ShowDateModifiedColumnConfig.SetValue(value); }
        public static bool ShowDateCreatedColumn { get => ShowDateCreatedColumnConfig.GetValue(); set => ShowDateCreatedColumnConfig.SetValue(value); }
        public static bool ShowTypeColumn { get => ShowTypeColumnConfig.GetValue(); set => ShowTypeColumnConfig.SetValue(value); }
        public static bool ShowSizeColumn { get => ShowSizeColumnConfig.GetValue(); set => ShowSizeColumnConfig.SetValue(value); }
        public static bool ShowTagsColumn { get => ShowTagsColumnConfig.GetValue(); set => ShowTagsColumnConfig.SetValue(value); }
        public static List<string> ColumnOrder { get => ColumnOrderConfig.GetValue(); set => ColumnOrderConfig.SetValue(value); }
        public static LayoutKind KindValue => (LayoutKind)Kind;
        public static Models.SortField SortFieldValue => (Models.SortField)SortField;
        public static Models.SortDirection SortDirectionValue => (Models.SortDirection)SortDirection;
        public static Models.FolderPriority FolderPriorityValue => (Models.FolderPriority)FolderPriority;
        public static Models.GroupOption GroupOptionValue => (Models.GroupOption)GroupOption;
        public static Models.SortDirection GroupDirectionValue => (Models.SortDirection)GroupDirection;
        public static Models.GroupByDateUnit GroupByDateUnitValue => (Models.GroupByDateUnit)GroupByDateUnit;

        private static List<string> DefaultColumnOrder() =>
            ["Name", "Tags", "DateModified", "DateCreated", "Type", "Size"];
    }

    public static class Files
    {
        public static ConfigItem<bool> ShowHiddenConfig { get; } = ConfigService.Register("ShowHidden", false);
        public static ConfigItem<bool> ShowExtensionsConfig { get; } = ConfigService.Register("ShowExtensions", true);
        public static bool ShowHidden { get => ShowHiddenConfig.GetValue(); set => ShowHiddenConfig.SetValue(value); }
        public static bool ShowExtensions { get => ShowExtensionsConfig.GetValue(); set => ShowExtensionsConfig.SetValue(value); }
    }

    public static class InfoPane
    {
        public static ConfigItem<bool> ShowConfig { get; } = ConfigService.Register("InfoPaneShow", false);
        public static ConfigItem<double> WidthConfig { get; } = ConfigService.Register("InfoPaneWidth", 284d);
        public static bool Show { get => ShowConfig.GetValue(); set => ShowConfig.SetValue(value); }
        public static double Width { get => WidthConfig.GetValue(); set => WidthConfig.SetValue(value); }
    }

    public static class Home
    {
        public static ConfigItem<bool> ShowQuickAccessConfig { get; } = ConfigService.Register("HomeQuickAccess", true);
        public static ConfigItem<bool> ShowVolumesConfig { get; } = ConfigService.Register("HomeVolumes", true);
        public static ConfigItem<bool> ShowRecentsConfig { get; } = ConfigService.Register("HomeRecents", true);
        public static ConfigItem<List<string>> RecentsConfig { get; } = ConfigService.Register("HomeRecentPaths", static () => new List<string>());
        public static bool ShowQuickAccess { get => ShowQuickAccessConfig.GetValue(); set => ShowQuickAccessConfig.SetValue(value); }
        public static bool ShowVolumes { get => ShowVolumesConfig.GetValue(); set => ShowVolumesConfig.SetValue(value); }
        public static bool ShowRecents { get => ShowRecentsConfig.GetValue(); set => ShowRecentsConfig.SetValue(value); }
        public static List<string> Recents { get => RecentsConfig.GetValue(); set => RecentsConfig.SetValue(value); }
    }

    public static class Logging
    {
        public static ConfigItem<int> MinLevelConfig { get; } = ConfigService.Register("LogMinLevel", (int)MacExplorer.Logging.LogLevel.Info);
        public static MacExplorer.Logging.LogLevel MinLevel
        {
            get => (MacExplorer.Logging.LogLevel)MinLevelConfig.GetValue();
            set => MinLevelConfig.SetValue((int)value);
        }
    }
}
