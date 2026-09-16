using CommunityToolkit.Mvvm.ComponentModel;
using MacExplorer.Localization;

namespace MacExplorer.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected ViewModelBase()
    {
        WeakLanguageChanged.Add(this, static vm => vm.OnLanguageChanged());
    }

    protected virtual void OnLanguageChanged() { }
}