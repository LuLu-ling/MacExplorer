namespace MacExplorer.Models;

public enum LayoutKind
{
    Details,
    List,
    Cards,
    Grid,
    Columns
}

public enum SortField
{
    Name,
    DateModified,
    DateCreated,
    Type,
    Size
}

public enum SortDirection
{
    Ascending,
    Descending
}

public enum GroupOption
{
    None = 0,
    Name = 1,
    DateModified = 2,
    DateCreated = 3,
    Size = 4,
    FileType = 5,
    SyncStatus = 6,
    FileTag = 7,
    OriginalFolder = 8,
    DateDeleted = 9,
    FolderPath = 10
}

public enum GroupByDateUnit
{
    Year = 0,
    Month = 1,
    Day = 2
}

public enum FolderPriority
{
    FoldersFirst,
    FilesFirst,
    Mixed
}


public enum ConflictDecision
{
    Replace,
    Skip,
    KeepBoth,
    Cancel
}

public enum ThemeMode
{
    Default,
    Light,
    Dark
}
