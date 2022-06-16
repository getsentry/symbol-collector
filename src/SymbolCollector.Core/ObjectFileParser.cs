using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ELFSharp.ELF;
using ELFSharp.ELF.Sections;
using ELFSharp.ELF.Segments;
using ELFSharp.MachO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentry;
using FileType = ELFSharp.ELF.FileType;
using ELFMachine = ELFSharp.ELF.Machine;
using MachOMachine = ELFSharp.MachO.Machine;

namespace SymbolCollector.Core
{
    public class ObjectFileParserOptions
    {
        public bool UseFallbackObjectFileParser { get; set; } = true;
        public bool IncludeHash { get; set; } = true;
    }

    public class ObjectFileParser
    {
        private readonly ObjectFileParserOptions _options;
        private readonly FatBinaryReader? _fatBinaryReader;
        private readonly ILogger<ObjectFileParser> _logger;

        public ClientMetrics Metrics { get; }

        public ObjectFileParser(
            ClientMetrics metrics,
            IOptions<ObjectFileParserOptions> options,
            ILogger<ObjectFileParser> logger,
            FatBinaryReader? fatBinaryReader = null)
        {
            if (fatBinaryReader is null
                && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                logger.LogWarning("No FatBinaryReader was provided while running on macOS.");
            }
            _options = options.Value;
            _fatBinaryReader = fatBinaryReader;
            _logger = logger;
            Metrics = metrics;
        }

        public bool TryParse(string file, out ObjectFileResult? result)
        {
            var parsed = false;
            try
            {
                parsed = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? TryMachO(file, out result)
                    : TryElf(file, out result);
            }
            catch (UnauthorizedAccessException ua)
            {
                // Too often to bother. Can't blacklist them all as it differs per device.
                Metrics.FileOrDirectoryUnauthorizedAccess();
                _logger.LogDebug(ua, "Unauthorized for {file}.", file);
                result = null;
            }
            catch (FileNotFoundException dnf)
            {
                _logger.LogDebug(dnf, "File not found: {file}.", file);
                Metrics.FileDoesNotExist();
                result = null;
            }
            catch (Exception known)
                when ("Unexpected name of the section's segment.".Equals(known.Message)
                || "The size defined on the header is smaller than the subsequent file size.".Equals(known.Message))
            {
                result = null;
                Metrics.FailedToParse();
                _logger.LogWarning(known, "Malformed Mach-O file: {file}", file);
            }
            catch (Exception e)
            {
                result = null;
                Metrics.FailedToParse();
                // You would expect TryLoad doesn't throw but that's not the case
                e.Data["filename"] = file;
                SentrySdk.CaptureException(e, s => s.AddAttachment(file));
            }

            Metrics.FileProcessed();
            return parsed;
        }
        private bool TryElf(string file, out ObjectFileResult? result)
        {
            var parsed = false;
            result = null;
            if (TryParseElfFile(file, out var elf) && elf is {})
            {
                result = elf;
                parsed = true;
            }
            else if (_options.UseFallbackObjectFileParser)
            {
                if (TryParseMachOFile(file, out var machO) && machO is {})
                {
                    result = machO;
                    parsed = true;
                }
                else if (TryParseFatMachO(file, out var fatMachO) && fatMachO is {})
                {
                    result = fatMachO;
                    parsed = true;
                }
            }

            return parsed;
        }

        private bool TryMachO(string file, out ObjectFileResult? result)
        {
            result = null;
            var parsed = false;
            // On macOS look for Mach-O first
            if (TryParseMachOFile(file, out var machO) && machO is {})
            {
                result = machO;
                parsed = true;
            }
            else if (TryParseFatMachO(file, out var fatMachO) && fatMachO is {})
            {
                result = fatMachO;
                parsed = true;
            }
            else if (_options.UseFallbackObjectFileParser
                     && TryParseElfFile(file, out var elf) && elf is {})
            {
                result = elf;
                parsed = true;
            }

            return parsed;
        }

        private bool TryParseFatMachO(string file, out FatMachOFileResult? result)
        {
            if (TryGetMachOFilesFromFatFile(file, out var files))
            {
                result = new FatMachOFileResult(
                    string.Empty,
                    string.Empty,
                    file,
                    GetSha256Hash(file),
                    files);
                return true;
            }

            result = null;
            return false;
        }

        private bool TryGetMachOFilesFromFatFile(string file, out IEnumerable<ObjectFileResult> result)
        {
            // Check if it's a Fat Mach-O
            FatMachO? load = null;
            if (_fatBinaryReader?.TryLoad(file, out load) == true
                && load is { } fatMachO && fatMachO.Header.FatArchCount > 0)
            {
                Metrics.FatMachOFileFound();
                _logger.LogInformation("Fat binary file with {count} Mach-O files: {file}.",
                    fatMachO.Header.FatArchCount, file);

                result = Files();
                return true;

                IEnumerable<ObjectFileResult> Files()
                {
                    using (_logger.BeginScope(new {FatMachO = file}))
                    using (fatMachO)
                    {
                        foreach (var buildIdFile in fatMachO.MachOFiles)
                        {
                            ObjectFileResult? machO;
                            try
                            {
                                if (!TryParseMachOFile(buildIdFile, out machO) || machO is null)
                                {
                                    continue;
                                }
                            }
                            catch (Exception e)
                            {
                                _logger.LogWarning(e, "Fat binary file contains an invalid item with codeId: {codeId}. {file}.",
                                    buildIdFile, file);
                                continue;
                            }

                            yield return machO;
                        }
                    }
                }
            }

            result = Enumerable.Empty<ObjectFileResult>();
            return false;
        }

        private bool TryParseElfFile(string file, out ObjectFileResult? result)
        {
            IELF? elf = null;
            try
            {
                // TODO: find an async API if this is used by the server
                if (ELFReader.TryLoad(file, out elf))
                {
                    Metrics.ElfFileFound();
                    var hasUnwindingInfo = elf.TryGetSection(".eh_frame", out _);
                    var hasDwarfDebugInfo = elf.TryGetSection(".debug_frame", out _);

                    var objectKind = GetObjectKind(elf);

                    _logger.LogDebug("Contains unwinding info: {hasUnwindingInfo}", hasUnwindingInfo);
                    _logger.LogDebug("Contains DWARF debug info: {hasDwarfDebugInfo}", hasDwarfDebugInfo);
                    var arch = GetArchitecture(elf);

                    var hasBuildId = elf.TryGetSection(".note.gnu.build-id", out var buildId);
                    if (hasBuildId)
                    {
                        var desc = buildId switch
                        {
                            NoteSection<uint> noteUint => noteUint.Description,
                            NoteSection<ulong> noteUlong => noteUlong.Description,
                            _ => null
                        };
                        if (desc == null)
                        {
                            _logger.LogError("build-id exists but bytes (desc) are null.");
                        }
                        else
                        {
                            // TODO ns2.1: get a slice
                            var desc16bytes = desc.Take(16).ToArray();
                            if (desc16bytes.Length != 16)
                            {
                                _logger.LogWarning("build-id exists but bytes (desc) length is unexpected {bytes}.",
                                    desc16bytes.Length);
                            }
                            else
                            {
                                var debugId = new Guid(desc16bytes).ToString();
                                result = new ObjectFileResult(
                                    debugId,
                                    BitConverter.ToString(desc).Replace("-", "").ToLower(),
                                    file,
                                    GetSha256Hash(file),
                                    BuildIdType.GnuBuildId,
                                    objectKind,
                                    FileFormat.Elf,
                                    arch);
                                return true;
                            }
                        }
                    }
                    else
                    {
                        if (elf.TryGetSection(".text", out var textSection))
                        {
                            try
                            {
                                var fallbackDebugId = GetFallbackDebugId(textSection.GetContents());
                                if (fallbackDebugId is {})
                                {
                                    result = new ObjectFileResult(
                                        fallbackDebugId,
                                        fallbackDebugId.Replace("-", string.Empty)
                                            .ToLower(), // TODO: prob needs NT_GNU_BUILD_ID here
                                        file,
                                        GetSha256Hash(file),
                                        BuildIdType.TextSectionHash,
                                        objectKind,
                                        FileFormat.Elf,
                                        arch);
                                    return true;
                                }

                                _logger.LogDebug(
                                    "Could not compute fallback id with textSection {textSection} from file {file}",
                                    textSection, file);
                            }
                            catch (Exception e)
                            {
                                _logger.LogError(e,
                                    "Failed to compute fallback id with textSection {textSection} from file {file}",
                                    textSection, file);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("No Debug Id and no .text section for fallback in {file}", file);
                        }
                    }
                }
                else
                {
                    _logger.LogDebug("Couldn't load': {file} with ELF reader.", file);
                }
            }
            finally
            {
                elf?.Dispose();
            }

            result = null;
            return false;
        }

        private bool TryParseMachOFile(string file, out ObjectFileResult? result)
        {
            // TODO: find an async API if this is used by the server
            if (MachOReader.TryLoad(file, out var machO) == MachOResult.OK)
            {
                Metrics.MachOFileFound();
                _logger.LogDebug("Mach-O found {file}", file);

                // https://github.com/getsentry/symbolic/blob/d951dd683a62d32595cc232e93843bffe5bd6a17/debuginfo/src/macho.rs#L112-L127
                var objectKind = GetObjectKind(machO);

                var arch = GetArchitecture(machO);

                var codeId = string.Empty;
                var uuid = machO.GetCommandsOfType<Uuid?>().FirstOrDefault();
                if (!(uuid is null))
                {
                    // TODO: Verify this is coming out correctly. Endianess not verified!!!
                    codeId = uuid.Id.ToString();
                }

                if (!string.IsNullOrWhiteSpace(codeId))
                {

                    result = new ObjectFileResult(
                        codeId,
                        // TODO: Figure out when to append + "0" (age),
                        codeId.Replace("-", string.Empty).ToLower(),
                        file,
                        GetSha256Hash(file),
                        BuildIdType.Uuid,
                        objectKind,
                        FileFormat.MachO,
                        arch);
                    return true;
                }

                _logger.LogInformation("File: {file} is a Mach-O but misses a UUID.", file);
            }

            _logger.LogDebug("Couldn't load': {file} with mach-O reader.", file);
            result = null;
            return false;
        }

        // https://github.com/getsentry/symbolic/blob/d951dd683a62d32595cc232e93843bffe5bd6a17/debuginfo/src/elf.rs#L116
        private static Architecture GetArchitecture(IELF elf)
        {
            // O32 ABI extended for 64-bit architecture.
            const uint EF_MIPS_ABI_O64 = 0x0000_2000;
            // EABI in 64 bit mode.
            const uint EF_MIPS_ABI_EABI64 = 0x0000_4000;
            const uint MIPS_64_FLAGS = EF_MIPS_ABI_O64 | EF_MIPS_ABI_EABI64;

            var arch = elf.Machine switch
            {
                ELFMachine.Intel386 => Architecture.X86, // Intel386
                ELFMachine.AMD64 => Architecture.Amd64, // EM_X86_64
                ELFMachine.AArch64 => Architecture.Arm64, // EM_AARCH64
                // NOTE: This could actually be any of the other 32bit ARMs. Since we don't need this
                // information, we use the generic Architecture.Arm. By reading CPU_arch and FP_arch attributes
                // from the SHT_ARM_ATTRIBUTES section it would be possible to distinguish the ARM arch
                // version and infer hard/soft FP.
                //
                // For more information, see:
                // http://code.metager.de/source/xref/gnu/src/binutils/readelf.c#11282
                // https://stackoverflow.com/a/20556156/4228225
                ELFMachine.ARM => Architecture.Arm, // ARM
                ELFMachine.PPC => Architecture.Ppc, // EM_PPC
                ELFMachine.PPC64 => Architecture.Ppc64, // EM_PPC64
                var m when m == ELFMachine.MIPS || m == ELFMachine.MIPSRS3LE =>
                (elf switch
                {
                    ELF<uint> @uint => @uint.MachineFlags,
                    ELF<ulong> @ulong => @ulong.MachineFlags,
                    _ => 0u
                } & MIPS_64_FLAGS) != 0
                    ? Architecture.Mips64
                    : Architecture.Mips,
                _ => Architecture.Unknown
            };
            return arch;
        }

        // https://github.com/getsentry/symbolic/blob/d951dd683a62d32595cc232e93843bffe5bd6a17/debuginfo/src/macho.rs#L79
        private static Architecture GetArchitecture(MachO mach0) =>
            (mach0.Machine, mach0.CpuSubType) switch
            {
                (MachOMachine.I386, CpuSubType.I386All) => Architecture.X86,
                (MachOMachine.I386, _) => Architecture.X86Unknown,
                (MachOMachine.X86_64, CpuSubType.X8664All) => Architecture.Amd64,
                (MachOMachine.X86_64, CpuSubType.X8664H) => Architecture.Amd64h,
                (MachOMachine.X86_64, _) => Architecture.Amd64Unknown,
                (MachOMachine.ARM64, CpuSubType.Arm64All) => Architecture.Arm64,
                (MachOMachine.ARM64, CpuSubType.Arm64V8) => Architecture.Arm64V8,
                (MachOMachine.ARM64, CpuSubType.Arm64E) => Architecture.Arm64e,
                (MachOMachine.ARM64, _) => Architecture.Arm64Unknown,
                (MachOMachine.ARM64_32, CpuSubType.Arm6432All) => Architecture.Arm6432,
                (MachOMachine.ARM64_32, CpuSubType.Arm6432V8) => Architecture.Arm6432V8,
                (MachOMachine.ARM64_32, _) => Architecture.Arm6432Unknown,
                (MachOMachine.ARM, CpuSubType.ArmAll) => Architecture.Arm,
                (MachOMachine.ARM, CpuSubType.Armv5Tej) => Architecture.ArmV5,
                (MachOMachine.ARM, CpuSubType.ArmV6) => Architecture.ArmV6,
                (MachOMachine.ARM, CpuSubType.ArmV6m) => Architecture.ArmV6m,
                (MachOMachine.ARM, CpuSubType.ArmV7) => Architecture.ArmV7,
                (MachOMachine.ARM, CpuSubType.ArmV7f) => Architecture.ArmV7f,
                (MachOMachine.ARM, CpuSubType.ArmV7s) => Architecture.ArmV7s,
                (MachOMachine.ARM, CpuSubType.ArmV7k) => Architecture.ArmV7k,
                (MachOMachine.ARM, CpuSubType.ArmV7m) => Architecture.ArmV7m,
                (MachOMachine.ARM, CpuSubType.ArmV7Em) => Architecture.ArmV7em,
                (MachOMachine.ARM, _) => Architecture.ArmUnknown,
                (MachOMachine.PowerPC, CpuSubType.PowerPCAll) => Architecture.Ppc,
                (MachOMachine.PowerPC64, CpuSubType.PowerPCAll) => Architecture.Ppc64,
                (_, _) => Architecture.Unknown
            };

        // Ported from: https://github.com/getsentry/symbolic/blob/d951dd683a62d32595cc232e93843bffe5bd6a17/debuginfo/src/elf.rs#L144-L171
        private static ObjectKind GetObjectKind(IELF elf)
        {
            var objectKind = elf.Type switch
            {
                FileType.None => ObjectKind.None,
                FileType.Relocatable => ObjectKind.Relocatable,
                FileType.Executable => ObjectKind.Executable,
                FileType.SharedObject => ObjectKind.Library,
                FileType.Core => ObjectKind.Other, // TODO: Clarify
                _ => ObjectKind.Other
            };

            if (objectKind == ObjectKind.Executable && elf.Segments.All(s => s.Type != SegmentType.Interpreter))
            {
                // When stripping debug information into a separate file with objcopy,
                // the eh_type field still reads ET_EXEC. However, the interpreter is
                // removed. Since an executable without interpreter does not make any
                // sense, we assume ``Debug`` in this case.
                objectKind = ObjectKind.Debug;
            }
            else if (objectKind == ObjectKind.Library && !elf.TryGetSection(".text", out _))
            {
                // The same happens for libraries. However, here we can only check for
                // a missing text section. If this still yields too many false positives,
                // we will have to check either the size or offset of that section in
                // the future.
                objectKind = ObjectKind.Debug;
            }

            return objectKind;
        }

        private static ObjectKind GetObjectKind(MachO machO)
        {
            return machO.FileType switch
            {
                ELFSharp.MachO.FileType.Object => ObjectKind.Relocatable, // MH_OBJECT
                ELFSharp.MachO.FileType.Executable => ObjectKind.Executable, // MH_EXECUTE
                ELFSharp.MachO.FileType.FixedVM => ObjectKind.Library, // MH_FVMLIB
                ELFSharp.MachO.FileType.Core => ObjectKind.Dump, // MH_CORE
                ELFSharp.MachO.FileType.Preload => ObjectKind.Executable, // MH_PRELOAD
                ELFSharp.MachO.FileType.DynamicLibrary => ObjectKind.Library, // MH_DYLIB
                ELFSharp.MachO.FileType.DynamicLinker => ObjectKind.Executable, // MH_DYLINKER
                ELFSharp.MachO.FileType.Bundle => ObjectKind.Library, // MH_BUNDLE
                ELFSharp.MachO.FileType.DynamicLibraryStub => ObjectKind.Other, // MH_DYLIB_STUB
                ELFSharp.MachO.FileType.Debug => ObjectKind.Debug, // MH_DSYM
                ELFSharp.MachO.FileType.Kext => ObjectKind.Library, // MH_KEXT_BUNDLE
                _ => ObjectKind.Other
            };
        }

        private string GetSha256Hash(string file)
        {
            var hash = string.Empty;
            if (_options.IncludeHash)
            {
                using var algorithm = SHA256.Create();
                var hashingAlgo = algorithm.ComputeHash(File.ReadAllBytes(file));
                var builder = new StringBuilder();
                foreach (var b in hashingAlgo)
                {
                    builder.Append(b.ToString("x2"));
                }

                hash = builder.ToString();
            }

            return hash;
        }

        internal string? GetFallbackDebugId(IReadOnlyList<byte> textSection)
        {
            if (textSection is null)
            {
                _logger.LogWarning("Can't create fallback debug id from a null buffer.");
                return null;
            }

            if (textSection.Count == 0)
            {
                _logger.LogWarning(".text section is 0 bytes long.");
                return null;
            }

            var length = Math.Min(4096, textSection.Count);
            var uuidSize = 16;
            var hash = new byte[uuidSize];
            for (var i = 0; i < length; i++)
            {
                hash[i % uuidSize] ^= textSection[i];
            }

            var hashId = new Guid(hash).ToString();
            return hashId;
        }
    }
}
