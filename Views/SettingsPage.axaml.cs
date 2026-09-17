using Avalonia.Controls;
using Avalonia.Input;
using MacExplorer.ViewModels;

namespace MacExplorer.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    private void Nav_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        if (sender is not Border { Tag: string page } || DataContext is not SettingsViewModel vm)
            return;
        e.Handled = true;
        vm.SelectPageCommand.Execute(page);
    }
}
