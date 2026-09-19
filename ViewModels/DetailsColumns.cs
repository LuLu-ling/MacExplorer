using CommunityToolkit.Mvvm.ComponentModel;
using MacExplorer.Controls;
using MacExplorer.Infrastructure;
using MacExplorer.Models;
namespace MacExplorer.ViewModels;

public sealed partial class DetailsColumns : ObservableObject
{
    public const double Splitter = 3;
    public const double Icon = 36;

    public static readonly DetailsColumnKind[] DefaultOrder =
    [
        DetailsColumnKind.Name,
        DetailsColumnKind.Tags,
        DetailsColumnKind.DateModified,
        DetailsColumnKind.DateCreated,
        DetailsColumnKind.Type,
        DetailsColumnKind.Size,
    ];

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

    private DetailsColumnKind[] _order = DefaultOrder;
    private int _widthEpoch;

    public IReadOnlyList<DetailsColumnKind> Order => _order;

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
        _order = LoadOrder();
    }

    public bool IsShown(DetailsColumnKind kind) => kind switch
    {
        DetailsColumnKind.Name => true,
        DetailsColumnKind.Tags => ShowTags,
        DetailsColumnKind.DateModified => ShowDateModified,
        DetailsColumnKind.DateCreated => ShowDateCreated,
        DetailsColumnKind.Type => ShowType,
        DetailsColumnKind.Size => ShowSize,
        _ => false
    };

    public bool TryMoveVisible(int from, int to)
    {
        var visible = Visible();
        if ((uint)from >= (uint)visible.Count || (uint)to >= (uint)visible.Count || from == to)
            return false;

        var moved = visible[from];
        visible.RemoveAt(from);
        visible.Insert(to, moved);

        var index = 0;
        for (var i = 0; i < _order.Length; i++)
        {
            if (!IsShown(_order[i]))
                continue;
            _order[i] = visible[index++];
        }

        Config.Layout.ColumnOrder = [.. _order.Select(static kind => kind.ToString())];
        OnPropertyChanged(nameof(Order));
        return true;
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
        OnPropertyChanged(nameof(Order));
        if (value)
        {
            _widthEpoch++;
            OnPropertyChanged(nameof(TotalWidth));
            return;
        }

        var epoch = ++_widthEpoch;
        _ = CommitWidth(epoch);
    }

    private async Task CommitWidth(int epoch)
    {
        await Task.Delay(ReorderShift.Duration);
        if (epoch == _widthEpoch)
            OnPropertyChanged(nameof(TotalWidth));
    }

    private List<DetailsColumnKind> Visible()
    {
        var list = new List<DetailsColumnKind>(_order.Length);
        foreach (var kind in _order)
        {
            if (IsShown(kind))
                list.Add(kind);
        }

        return list;
    }

    private static DetailsColumnKind[] LoadOrder()
    {
        var parsed = new List<DetailsColumnKind>(DefaultOrder.Length);
        var seen = new HashSet<DetailsColumnKind>();
        foreach (var name in Config.Layout.ColumnOrder)
        {
            if (Enum.TryParse(name, true, out DetailsColumnKind kind) && seen.Add(kind))
                parsed.Add(kind);
        }

        foreach (var kind in DefaultOrder)
        {
            if (seen.Add(kind))
                parsed.Add(kind);
        }

        return [.. parsed];
    }

    private static double Visible(bool show, double width) => show ? width + Splitter : 0;
}
