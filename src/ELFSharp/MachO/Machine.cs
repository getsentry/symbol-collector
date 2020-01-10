namespace ELFSharp.MachO
{
    public enum Machine : int
    {
        Any = -1,
        Vax = 1,
        Romp = 2,
        NS32032 = 4,
        NS32332 = 5,
        M68k = 6,
        I386 = 7,
        X86_64 = I386 | MachO.Architecture64,
        Mips = 8,
        PaRisc = 11,
        ARM = 12,
        ARM64 = ARM | MachO.Architecture64,
        ARM64_32 = ARM | MachO.Architecture6432,
        M88k = 13,
        Sparc = 14,
        I860BE = 15,
        I860LE = 16,
        RS6000 = 17,
        M98k = 18,
        PowerPC = 19,
        PowerPC64 = PowerPC | MachO.Architecture64
    }

    public enum CpuSubType : uint
    {
        // https://github.com/m4b/goblin/blob/c2e87de5c0afb1ec9d7c29b19def623c3b581e51/src/mach/constants.rs#L254-L327
        Arm6432All = 0,
        Arm64All = 0,
        ArmAll = 0,
        PowerPCAll = 0,
        Arm64V8 = 1,
        Arm6432V8 = 1,
        Arm64E = 2,
        I386All = 3,
        X8664All = 3,
        ArmV6 = 6,
        Armv5Tej = 7,
        X8664H = 8,
        ArmV7 = 9,
        ArmV7f = 10,
        ArmV7s = 11,
        ArmV7k = 12,
        ArmV6m = 14,
        ArmV7m = 15,
        ArmV7Em = 16,

    }
}

