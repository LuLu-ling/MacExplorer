using MacExplorer.Configuration;
using MacExplorer.Infrastructure;
using MacExplorer.Localization;
using MacExplorer.Models;

namespace MacExplorer.Files;

public static class NewFileKinds
{
    public const int OpAdd = 0;
    public const int OpRemove = 1;
    public const int OpMove = 2;
    public const int OpSetName = 3;
    public const int OpSetExtension = 4;

    private static IReadOnlyList<NewFileKind> _all = [];
    private static int _init;
    private static bool _ready;

    public static event Action? Changed;

    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref _init, 1) != 0)
            return;
        Config.Files.NewKindsConfig.Observe(new ConfigObserver(ConfigEvent.Changed, _ => Rebuild()));
        LocalizationService.LanguageChanged += Rebuild;
    }

    public static IReadOnlyList<NewFileKind> All
    {
        get
        {
            Ensure();
            return _all;
        }
    }

    public static string Untitled(string? extension)
    {
        var stem = Lang.Text("File.Untitled");
        if (string.IsNullOrEmpty(stem) || stem[0] == '!')
            stem = "untitled";
        var ext = NormalizeExtension(extension);
        return ext.Length == 0 ? stem : stem + "." + ext;
    }

    public static void HandleNative(int op, int index, int dest, string? text)
    {
        switch (op)
        {
            case OpAdd:
                Add();
                return;
            case OpRemove:
                Remove(index);
                return;
            case OpMove:
                Move(index, dest);
                return;
            case OpSetName:
                SetName(index, text ?? "");
                return;
            case OpSetExtension:
                SetExtension(index, text ?? "");
                return;
        }
    }

    public static void Add()
    {
        var list = Copy();
        list.Add(new NewFileKind
        {
            Name = Lang.Text("Settings.NewFiles.NewKind"),
            Extension = "txt"
        });
        Save(list);
    }

    public static void Remove(int index)
    {
        var list = Copy();
        if ((uint)index >= (uint)list.Count)
            return;
        list.RemoveAt(index);
        Save(list);
    }

    public static void Move(int from, int to)
    {
        var list = Copy();
        if ((uint)from >= (uint)list.Count)
            return;
        var item = list[from];
        list.RemoveAt(from);
        if (to > from)
            to--;
        to = Math.Clamp(to, 0, list.Count);
        list.Insert(to, item);
        Save(list);
    }

    public static void SetName(int index, string name)
    {
        var list = Copy();
        if ((uint)index >= (uint)list.Count)
            return;
        list[index].Name = NormalizeName(name);
        Save(list);
    }

    public static void SetExtension(int index, string extension)
    {
        var list = Copy();
        if ((uint)index >= (uint)list.Count)
            return;
        list[index].Extension = NormalizeExtension(extension);
        Save(list);
    }

    public static string NormalizeExtension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var span = value.AsSpan().Trim();
        while (span.Length > 0 && span[0] == '.')
            span = span[1..];
        var buffer = new char[Math.Min(span.Length, 32)];
        var n = 0;
        foreach (var c in span)
        {
            if (c is '/' or '\\' or ':' or '\0' or '\t' or '\n' or '\r' or ' ')
                continue;
            buffer[n++] = c;
            if (n == buffer.Length)
                break;
        }
        return n == 0 ? "" : new string(buffer, 0, n);
    }

    private static string NormalizeName(string value)
    {
        var name = value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim();
        return name.Length <= 64 ? name : name[..64];
    }

    private static void Ensure()
    {
        if (_ready)
            return;
        if (_init == 0)
        {
            _all = [Fallback()];
            _ready = true;
            return;
        }

        Rebuild();
    }

    private static void Rebuild()
    {
        _all = Unset ? [LocalizedDefault()] : Clone(Config.Files.NewKinds);
        _ready = true;
        Changed?.Invoke();
    }

    private static bool Unset
    {
        get
        {
            try
            {
                return ConfigService.IsInitialized && Config.Files.NewKindsConfig.IsDefault();
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    private static List<NewFileKind> Copy() => Unset ? [LocalizedDefault()] : Clone(Config.Files.NewKinds);

    private static List<NewFileKind> Clone(List<NewFileKind> source)
    {
        var list = new List<NewFileKind>(source.Count);
        foreach (var kind in source)
            list.Add(kind.Clone());
        return list;
    }

    private static void Save(List<NewFileKind> list) => Config.Files.NewKinds = list;

    private static NewFileKind LocalizedDefault()
    {
        var name = Lang.Text("Settings.NewFiles.PlainText");
        if (string.IsNullOrEmpty(name) || name[0] == '!' || name == "Settings.NewFiles.PlainText")
            name = "Plain Text";
        return new NewFileKind { Id = "txt", Name = name, Extension = "txt" };
    }

    private static NewFileKind Fallback() => new() { Id = "txt", Name = "Plain Text", Extension = "txt" };
}
