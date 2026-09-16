using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using MacExplorer.ViewModels;

using MacExplorer.Localization;
namespace MacExplorer.Views;

public partial class PropertiesWindow : FAAppWindow
{
    public PropertiesWindow()
    {
        InitializeComponent();
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.Height = 36;
        Closed += (_, _) => (DataContext as PropertiesViewModel)?.Dispose();
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is Visual visual)
        {
            for (var v = visual; v is not null; v = v.GetVisualParent())
            {
                if (v is Button)
                    return;
            }
        }

        BeginMoveDrag(e);
    }

    private async void Save_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PropertiesViewModel vm)
            return;
        var result = vm.Save();
        if (result is { Ok: false })
        {
            var dialog = new FAContentDialog
            {
                Title = Lang.Text("Properties.SaveFailed"),
                Content = result.Value.Error ?? Lang.Text("Common.Error.Unknown"),
                PrimaryButtonText = Lang.Text("Common.Action.OK")
            };
            await dialog.ShowAsync(this);
            return;
        }

        Close();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async void CopyHash_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hash } || string.IsNullOrEmpty(hash))
            return;
        if (Clipboard is not null)
            await Clipboard.SetTextAsync(hash);
    }
}
