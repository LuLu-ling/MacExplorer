namespace MacExplorer.Configuration;

[Flags]
public enum ConfigEvent
{
    None = 0,
    Init = 0b00001,
    Get = 0b00010,
    Set = 0b00100,
    Reset = 0b01000,
    CheckDefault = 0b10000,
    Read = Get | CheckDefault,
    Update = Set | Reset,
    Changed = Init | Update,
    All = Read | Changed
}
