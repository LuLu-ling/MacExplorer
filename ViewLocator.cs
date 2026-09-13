using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MacExplorer.ViewModels;

namespace MacExplorer;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);
        if (type is null)
            return null;

        return (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data)
    {
        if (data is not ViewModelBase)
            return false;
        var name = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        return Type.GetType(name) is not null;
    }
}