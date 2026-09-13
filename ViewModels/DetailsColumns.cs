using CommunityToolkit.Mvvm.ComponentModel;
using MacExplorer.Infrastructure;

namespace MacExplorer.ViewModels;

public sealed partial class DetailsColumns : ObservableObject
{
    public const double Splitter = 12;
    public const double Icon = 36;

    public static DetailsColumns Shared { get; } = new();

    [ObservableProperty] public partial double Name { get; set; }
    [ObservableProperty] public partial double DateModified { get; set; }
    [ObservableProperty] public partial double DateCreated { get; set; }
    [ObservableProperty] public partial double Type { get; set; }
    [ObservableProperty] public partial double Size { get; set; }
    [ObservableProperty] public partial double Tags { get; set; }


    [ObservableProperty] public partial bool ShowDateModified { get; set; }
    [ObservableProperty] public partial bool ShowDateCreated { get; set; }
    [ObservableProperty] public partial bool ShowType { get; set; }
    [ObservableProperty] public partial bool ShowSize { get; set; }
    [ObservableProperty] public partial bool ShowTags { get; set; }


    public double TotalWidth =>
        Icon + Name + Splitter
        + Visible(ShowTags, Tags)
        + Visible(ShowDateModified, DateModified)
        + Visible(ShowDateCreated, DateCreated)
        + Visible(ShowType, Type)
        + Visible(ShowSize, Size);

    private DetailsColumns()
    {
        Name = Config.Layout.NameColumnWidth;
        DateModified = Config.Layout.DateModifiedColumnWidth;
        DateCreated = Config.Layout.DateCreatedColumnWidth;
        Type = Config.Layout.TypeColumnWidth;
        Size = Config.Layout.SizeColumnWidth;
        Tags = Config.Layout.TagsColumnWidth;
        ShowDateModified = Config.Layout.ShowDateModifiedColumn;
        ShowDateCreated = Config.Layout.ShowDateCreatedColumn;
        ShowType = Config.Layout.ShowTypeColumn;
        ShowSize = Config.Layout.ShowSizeColumn;
        ShowTags = Config.Layout.ShowTagsColumn;
    }

    partial void OnNameChanged(double value) => SetWidth(v => Config.Layout.NameColumnWidth = v, value);
    partial void OnDateModifiedChanged(double value) => SetWidth(v => Config.Layout.DateModifiedColumnWidth = v, value);
    partial void OnDateCreatedChanged(double value) => SetWidth(v => Config.Layout.DateCreatedColumnWidth = v, value);
    partial void OnTypeChanged(double value) => SetWidth(v => Config.Layout.TypeColumnWidth = v, value);
    partial void OnSizeChanged(double value) => SetWidth(v => Config.Layout.SizeColumnWidth = v, value);
    partial void OnTagsChanged(double value) => SetWidth(v => Config.Layout.TagsColumnWidth = v, value);

    partial void OnShowDateModifiedChanged(bool value) => SetVisible(v => Config.Layout.ShowDateModifiedColumn = v, value);
    partial void OnShowDateCreatedChanged(bool value) => SetVisible(v => Config.Layout.ShowDateCreatedColumn = v, value);
    partial void OnShowTypeChanged(bool value) => SetVisible(v => Config.Layout.ShowTypeColumn = v, value);
    partial void OnShowSizeChanged(bool value) => SetVisible(v => Config.Layout.ShowSizeColumn = v, value);
    partial void OnShowTagsChanged(bool value) => SetVisible(v => Config.Layout.ShowTagsColumn = v, value);

    private void SetWidth(Action<double> persist, double value)
    {
        persist(value);
        OnPropertyChanged(nameof(TotalWidth));
    }

    private void SetVisible(Action<bool> persist, bool value)
    {
        persist(value);
        OnPropertyChanged(nameof(TotalWidth));
    }

    private static double Visible(bool show, double width) => show ? width + Splitter : 0;
}
