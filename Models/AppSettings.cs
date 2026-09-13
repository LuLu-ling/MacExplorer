namespace MacExplorer.Models;

public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.Default;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public double SidebarWidth { get; set; } = 240;
    public bool IsSidebarOpen { get; set; } = true;
    public bool ShowInfoPane { get; set; }
    public double InfoPaneWidth { get; set; } = 284;
    public bool ShowHidden { get; set; }
    public bool ShowExtensions { get; set; } = true;
    public LayoutKind Layout { get; set; } = LayoutKind.Details;
    public SortField SortField { get; set; } = SortField.Name;
    public SortDirection SortDirection { get; set; } = SortDirection.Ascending;
    public FolderPriority FolderPriority { get; set; } = FolderPriority.FoldersFirst;
    public int LayoutSize { get; set; } = 3;
    public bool ShowQuickAccess { get; set; } = true;
    public bool ShowVolumes { get; set; } = true;
    public bool ShowRecents { get; set; } = true;
    public List<string> Pins { get; set; } = [];
    public List<string> Recents { get; set; } = [];
}
