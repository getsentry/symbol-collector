namespace SymbolCollector.Core
{
    public enum ObjectFileType
    {
        Unknown,

        // executable
        Executable,
        // library
        Library,

        //debuginfo
        DebugInfo
    }

    public enum FileFormat
    {
        Unknown,
        Elf,
        MachO,
        FatMachO
    }

    public enum Architecture
    {
        Unknown,
        X86,
        X8664,
        Arm,
        Arm64
    }
}
