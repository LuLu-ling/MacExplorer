using CommunityToolkit.Mvvm.ComponentModel;

namespace MacExplorer.Localization;

public sealed class LanguageOption : ObservableObject
{
    private readonly string? _nativeName;

    public LanguageOption(string code, string? nativeName = null)
    {
        Code = code;
        _nativeName = nativeName;
        if (nativeName is null)
            WeakLanguageChanged.Add(this, static option => option.OnPropertyChanged(nameof(Title)));
    }

    public string Code { get; }

    public string Title => _nativeName ?? Lang.Text("Settings.Language.Auto");
}
