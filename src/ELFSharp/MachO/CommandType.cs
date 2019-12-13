namespace ELFSharp.MachO
{
    public enum CommandType : uint
    {
        Segment = 0x1,
        SymbolTable = 0x2,
        Segment64 = 0x19,
        Main = 0x80000028u,
        Uuid = 0x1b, // LC_UUID
        VersionMinMacOS = 0x24u, // LC_VERSION_MIN_MACOSX
        VersionMinIPhoneOS = 0x25u // LC_VERSION_MIN_IPHONEOS
    }
}

