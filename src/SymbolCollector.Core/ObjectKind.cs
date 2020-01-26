namespace SymbolCollector.Core
{
    // As defined by symbolic: https://github.com/getsentry/symbolic/blob/d217a9340df3bbd373323b732880a95e6de353bf/debuginfo/src/base.rs#L18
    public enum ObjectKind
    {
        None,

        /// The Relocatable file type is the format used for intermediate object
        /// files. It is a very compact format containing all its sections in one
        /// segment. The compiler and assembler usually create one Relocatable file
        /// for each source code file. By convention, the file name extension for
        /// this format is .o.
        Relocatable,

        /// The Executable file type is the format used by standard executable
        /// programs.
        Executable,

        /// The Library file type is for dynamic shared libraries. It contains
        /// some additional tables to support multiple modules. By convention, the
        /// file name extension for this format is .dylib, except for the main
        /// shared library of a framework, which does not usually have a file name
        /// extension.
        Library,

        /// The Dump file type is used to store core files, which are
        /// traditionally created when a program crashes. Core files store the
        /// entire address space of a process at the time it crashed. You can
        /// later run gdb on the core file to figure out why the crash occurred.
        Dump,

        /// The Debug file type designates files that store symbol information
        /// for a corresponding binary file.
        Debug,

        /// A container that just stores source code files, but no other debug
        /// information corresponding to the original object file.
        Sources,

        /// The Other type represents any valid object class that does not fit any
        /// of the other classes. These are mostly CPU or OS dependent, or unique
        /// to a single kind of object.
        Other,
    }

    public static class ObjectKindExtensions
    {
        // https://github.com/getsentry/symbolicator/blob/271b4bd2a6bd8aba2116f617ada8227594db6467/symsorter/src/app.rs#L79-L95
        public static string? ToSymsorterFileName(this ObjectKind objectKind) =>
            objectKind switch
            {
                ObjectKind.Debug => "debuginfo",
                ObjectKind.Sources => "sourcebundle",
                ObjectKind.Executable => "executable",
                ObjectKind.Library => "executable",
                ObjectKind.Relocatable => "executable",
                _ => null
            };
    }

    public enum FileFormat
    {
        Unknown,
        Elf,
        MachO,
        FatMachO
    }

    public static class FileFormatExtensions
    {
        public static string? ToSymsorterFileFormat(this FileFormat fileFormat) =>
            fileFormat switch
            {
                FileFormat.Elf => "elf",
                FileFormat.MachO => "macho",
                FileFormat.FatMachO => null, // not symsorted. Inner files are.
                _ => null
            };
    }

    public enum Architecture
    {
        Unknown,
        X86,
        X86Unknown,
        Amd64, // TODO: get the order/values right
        Amd64h,
        Amd64Unknown,
        Arm,
        Arm64,
        Arm64e,
        Arm64V8,
        Arm64Unknown,
        Ppc,
        Ppc64,
        Mips,
        Mips64,
        Arm6432,
        Arm6432V8,
        Arm6432Unknown,
        ArmV5,
        ArmV6,
        ArmV6m,
        ArmV7,
        ArmV7f,
        ArmV7s,
        ArmV7k,
        ArmV7m,
        ArmV7em,
        ArmUnknown
    }

    public static class ArchitectureExtensions
    {
        public static string ToSymsorterArchitecture(this Architecture architecture) =>
            architecture switch
            {
                Architecture.Amd64 => "x86_64",
                Architecture.Amd64Unknown => "x86_64",
                // TODO: add other mappings from symbolic
                var a => a.ToString().ToLower()
            };
    }
}
