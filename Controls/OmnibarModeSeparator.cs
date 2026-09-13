using Avalonia.Controls.Primitives;

namespace MacExplorer.Controls;

public sealed class OmnibarModeSeparator : TemplatedControl
{
    public OmnibarModeSeparator()
    {
        Focusable = false;
        IsTabStop = false;
    }
}
