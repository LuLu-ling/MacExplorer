using System.Text;
using Avalonia.Input;
using MacExplorer.Configuration;
using MacExplorer.Infrastructure;
using MacExplorer.Localization;

namespace MacExplorer.Input;

public static class Shortcuts
{
    public const int NativeClear = -1;
    public const int NativeReset = -2;
    public const int NativeResetAll = -3;

    private static KeyGesture?[] _gestures = [];
    private static string[] _displays = [];
    private static bool[] _custom = [];
    private static int _init;

    public static event Action? Changed;

    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref _init, 1) != 0)
            return;
        Config.Shortcuts.OverridesConfig.Observe(new ConfigObserver(ConfigEvent.Changed, _ => Rebuild()));
        LocalizationService.LanguageChanged += Rebuild;
    }

    public static KeyGesture? Gesture(ShortcutId id)
    {
        Ensure();
        return _gestures[(int)id];
    }

    public static string Display(ShortcutId id)
    {
        Ensure();
        return _displays[(int)id];
    }

    public static bool IsCustom(ShortcutId id)
    {
        Ensure();
        return _custom[(int)id];
    }

    public static bool Matches(ShortcutId id, KeyEventArgs e)
    {
        Ensure();
        return _gestures[(int)id]?.Matches(e) == true;
    }

    public static string Tip(string titleKey, ShortcutId id)
    {
        var title = Lang.Text(titleKey);
        var display = Display(id);
        return display.Length == 0 ? title : $"{title} ({display})";
    }

    public static void Assign(ShortcutId id, KeyGesture? gesture) =>
        Commit(id, gesture is null ? "" : Canonical(gesture));

    public static void Clear(ShortcutId id) => Commit(id, "");

    public static void Reset(ShortcutId id)
    {
        var map = CopyOverrides();
        map.Remove(Name(id));
        Steal(map, id, Parse(ShortcutCatalog.Spec(id).DefaultChord));
        Config.Shortcuts.Overrides = map;
    }

    public static void ResetAll() => Config.Shortcuts.OverridesConfig.Reset();

    public static void HandleNative(int id, int keyCode, int modifiers)
    {
        if (keyCode == NativeResetAll)
        {
            ResetAll();
            return;
        }

        if ((uint)id >= (uint)ShortcutCatalog.Count)
            return;

        var shortcut = (ShortcutId)id;
        switch (keyCode)
        {
            case NativeClear:
                Clear(shortcut);
                return;
            case NativeReset:
                Reset(shortcut);
                return;
        }

        if (!MacKeyCode.TryMap(keyCode, modifiers, out var key, out var mods))
            return;
        if (mods == KeyModifiers.None && key is Key.Escape or Key.Back or Key.Delete)
        {
            Clear(shortcut);
            return;
        }

        Assign(shortcut, new KeyGesture(key, mods));
    }

    private static void Ensure()
    {
        if (_gestures.Length != ShortcutCatalog.Count)
            Rebuild();
    }

    private static void Rebuild()
    {
        var count = ShortcutCatalog.Count;
        var gestures = new KeyGesture?[count];
        var displays = new string[count];
        var custom = new bool[count];
        var overrides = OverridesOrNull();

        for (var i = 0; i < count; i++)
        {
            var spec = ShortcutCatalog.All[i];
            var chord = spec.DefaultChord;
            if (overrides is not null && overrides.TryGetValue(Name(spec.Id), out var stored))
            {
                chord = stored;
                custom[i] = true;
            }

            var gesture = Parse(chord);
            gestures[i] = gesture;
            displays[i] = gesture is null ? "" : Format(gesture);
        }

        _gestures = gestures;
        _displays = displays;
        _custom = custom;
        Changed?.Invoke();
    }

    private static void Commit(ShortcutId id, string chord)
    {
        var map = CopyOverrides();
        var incoming = Parse(chord);
        if (incoming is null)
            map[Name(id)] = "";
        else if (incoming == Parse(ShortcutCatalog.Spec(id).DefaultChord))
            map.Remove(Name(id));
        else
            map[Name(id)] = Canonical(incoming);

        Steal(map, id, incoming);
        Config.Shortcuts.Overrides = map;
    }

    private static void Steal(Dictionary<string, string> map, ShortcutId keep, KeyGesture? incoming)
    {
        if (incoming is null)
            return;
        foreach (var spec in ShortcutCatalog.All)
        {
            if (spec.Id == keep)
                continue;
            if (Parse(Effective(spec, map)) == incoming)
                map[Name(spec.Id)] = "";
        }
    }

    private static string Effective(in ShortcutSpec spec, Dictionary<string, string> map) =>
        map.TryGetValue(Name(spec.Id), out var stored) ? stored : spec.DefaultChord;

    private static Dictionary<string, string>? OverridesOrNull()
    {
        if (!ConfigService.IsInitialized && _init == 0)
            return null;
        try
        {
            return Config.Shortcuts.Overrides;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> CopyOverrides()
    {
        var stored = Config.Shortcuts.Overrides;
        return stored.Count == 0 ? new Dictionary<string, string>() : new Dictionary<string, string>(stored);
    }

    private static string Name(ShortcutId id) => id.ToString();

    private static KeyGesture? Parse(string? chord)
    {
        if (string.IsNullOrWhiteSpace(chord))
            return null;
        try
        {
            var gesture = KeyGesture.Parse(chord);
            return gesture.Key == Key.None ? null : gesture;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Canonical(KeyGesture gesture)
    {
        var s = new StringBuilder(24);
        AppendMod(s, gesture.KeyModifiers.HasFlag(KeyModifiers.Control), "Ctrl");
        AppendMod(s, gesture.KeyModifiers.HasFlag(KeyModifiers.Shift), "Shift");
        AppendMod(s, gesture.KeyModifiers.HasFlag(KeyModifiers.Alt), "Alt");
        AppendMod(s, gesture.KeyModifiers.HasFlag(KeyModifiers.Meta), "Meta");
        if (s.Length > 0)
            s.Append('+');
        s.Append(Token(gesture.Key));
        return s.ToString();
    }

    private static string Format(KeyGesture gesture)
    {
        var s = new StringBuilder(8);
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control)) s.Append('\u2303');
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)) s.Append('\u2325');
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift)) s.Append('\u21e7');
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta)) s.Append('\u2318');
        s.Append(Label(gesture.Key));
        return s.ToString();
    }

    private static void AppendMod(StringBuilder s, bool on, string token)
    {
        if (!on)
            return;
        if (s.Length > 0)
            s.Append('+');
        s.Append(token);
    }

    private static string Token(Key key) => key switch
    {
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.OemPeriod => ".",
        Key.OemComma => ",",
        _ => key.ToString()
    };

    private static string Label(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => (key - Key.D0).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => (key - Key.NumPad0).ToString(),
        >= Key.F1 and <= Key.F24 => $"F{key - Key.F1 + 1}",
        Key.Return or Key.Enter => "\u21a9",
        Key.Back => "\u232b",
        Key.Delete => "\u2326",
        Key.Escape => "\u238b",
        Key.Tab => "\u21e5",
        Key.Space => "Space",
        Key.Left => "\u2190",
        Key.Right => "\u2192",
        Key.Up => "\u2191",
        Key.Down => "\u2193",
        Key.Home => "\u2196",
        Key.End => "\u2198",
        Key.PageUp => "\u21de",
        Key.PageDown => "\u21df",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemQuotes => "'",
        Key.OemSemicolon => ";",
        Key.OemBackslash or Key.Oem102 => "\\",
        Key.OemTilde => "`",
        Key.OemPipe => "\\",
        _ => key.ToString()
    };
}
