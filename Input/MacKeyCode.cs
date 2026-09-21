using Avalonia.Input;

namespace MacExplorer.Input;

internal static class MacKeyCode
{
    private const int ShiftBit = 1 << 17;
    private const int ControlBit = 1 << 18;
    private const int OptionBit = 1 << 19;
    private const int CommandBit = 1 << 20;
    private const int ModifierMask = ShiftBit | ControlBit | OptionBit | CommandBit;

    private static readonly Key[] Map = Create();

    public static bool TryMap(int keyCode, int modifierFlags, out Key key, out KeyModifiers modifiers)
    {
        modifiers = ToModifiers(modifierFlags);
        key = (uint)keyCode < (uint)Map.Length ? Map[keyCode] : Key.None;
        return key != Key.None;
    }

    public static KeyModifiers ToModifiers(int modifierFlags)
    {
        var flags = modifierFlags & ModifierMask;
        var modifiers = KeyModifiers.None;
        if ((flags & ShiftBit) != 0) modifiers |= KeyModifiers.Shift;
        if ((flags & ControlBit) != 0) modifiers |= KeyModifiers.Control;
        if ((flags & OptionBit) != 0) modifiers |= KeyModifiers.Alt;
        if ((flags & CommandBit) != 0) modifiers |= KeyModifiers.Meta;
        return modifiers;
    }

    private static Key[] Create()
    {
        var map = new Key[128];
        map[0x00] = Key.A;
        map[0x01] = Key.S;
        map[0x02] = Key.D;
        map[0x03] = Key.F;
        map[0x04] = Key.H;
        map[0x05] = Key.G;
        map[0x06] = Key.Z;
        map[0x07] = Key.X;
        map[0x08] = Key.C;
        map[0x09] = Key.V;
        map[0x0A] = Key.Oem102;
        map[0x0B] = Key.B;
        map[0x0C] = Key.Q;
        map[0x0D] = Key.W;
        map[0x0E] = Key.E;
        map[0x0F] = Key.R;
        map[0x10] = Key.Y;
        map[0x11] = Key.T;
        map[0x12] = Key.D1;
        map[0x13] = Key.D2;
        map[0x14] = Key.D3;
        map[0x15] = Key.D4;
        map[0x16] = Key.D6;
        map[0x17] = Key.D5;
        map[0x18] = Key.OemPlus;
        map[0x19] = Key.D9;
        map[0x1A] = Key.D7;
        map[0x1B] = Key.OemMinus;
        map[0x1C] = Key.D8;
        map[0x1D] = Key.D0;
        map[0x1E] = Key.OemCloseBrackets;
        map[0x1F] = Key.O;
        map[0x20] = Key.U;
        map[0x21] = Key.OemOpenBrackets;
        map[0x22] = Key.I;
        map[0x23] = Key.P;
        map[0x24] = Key.Return;
        map[0x25] = Key.L;
        map[0x26] = Key.J;
        map[0x27] = Key.OemQuotes;
        map[0x28] = Key.K;
        map[0x29] = Key.OemSemicolon;
        map[0x2A] = Key.OemBackslash;
        map[0x2B] = Key.OemComma;
        map[0x2C] = Key.OemQuestion;
        map[0x2D] = Key.N;
        map[0x2E] = Key.M;
        map[0x2F] = Key.OemPeriod;
        map[0x30] = Key.Tab;
        map[0x31] = Key.Space;
        map[0x32] = Key.OemTilde;
        map[0x33] = Key.Back;
        map[0x35] = Key.Escape;
        map[0x41] = Key.Decimal;
        map[0x43] = Key.Multiply;
        map[0x45] = Key.Add;
        map[0x47] = Key.NumLock;
        map[0x4B] = Key.Divide;
        map[0x4C] = Key.Enter;
        map[0x4E] = Key.Subtract;
        map[0x51] = Key.OemPlus;
        map[0x52] = Key.NumPad0;
        map[0x53] = Key.NumPad1;
        map[0x54] = Key.NumPad2;
        map[0x55] = Key.NumPad3;
        map[0x56] = Key.NumPad4;
        map[0x57] = Key.NumPad5;
        map[0x58] = Key.NumPad6;
        map[0x59] = Key.NumPad7;
        map[0x5B] = Key.NumPad8;
        map[0x5C] = Key.NumPad9;
        map[0x60] = Key.F5;
        map[0x61] = Key.F6;
        map[0x62] = Key.F7;
        map[0x63] = Key.F3;
        map[0x64] = Key.F8;
        map[0x65] = Key.F9;
        map[0x67] = Key.F11;
        map[0x69] = Key.F13;
        map[0x6A] = Key.F16;
        map[0x6B] = Key.F14;
        map[0x6D] = Key.F10;
        map[0x6F] = Key.F12;
        map[0x71] = Key.F15;
        map[0x72] = Key.Insert;
        map[0x73] = Key.Home;
        map[0x74] = Key.PageUp;
        map[0x75] = Key.Delete;
        map[0x76] = Key.F4;
        map[0x77] = Key.End;
        map[0x78] = Key.F2;
        map[0x79] = Key.PageDown;
        map[0x7A] = Key.F1;
        map[0x7B] = Key.Left;
        map[0x7C] = Key.Right;
        map[0x7D] = Key.Down;
        map[0x7E] = Key.Up;
        return map;
    }
}
