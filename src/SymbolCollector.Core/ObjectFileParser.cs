using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ELFSharp.ELF;
using ELFSharp.ELF.Sections;
using ELFSharp.MachO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SymbolCollector.Core
{
    public class ObjectFileParser
    {
        private readonly FatBinaryReader? _fatBinaryReader;
        private readonly ILogger<ObjectFileParser> _logger;

        public ClientMetrics Metrics { get; }

        public ObjectFileParser(
            FatBinaryReader? fatBinaryReader = null,
            ClientMetrics? metrics = null,
            ILogger<ObjectFileParser>? logger = null)
        {
            if (fatBinaryReader is null
                && logger is {}
                && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                logger.LogWarning("No FatBinaryReader was provided while running on macOS.");
            }

            _fatBinaryReader = fatBinaryReader;
            _logger = logger ?? NullLogger<ObjectFileParser>.Instance;
            Metrics = metrics ?? new ClientMetrics();
        }

        public bool TryParse(string file, out ObjectFileResult? result)
        {
            var parsed = false;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
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
                else if (TryParseElfFile(file, out var elf) && elf is {})
                {
                    result = elf;
                    parsed = true;
                }
                else
                {
                    result = null;
                }
            }
            else if (TryParseElfFile(file, out var elf) && elf is {})
            {
                result = elf;
                parsed = true;
            }
            else if (TryParseMachOFile(file, out var machO) && machO is {})
            {
                result = machO;
                parsed = true;
            }
            else if (TryParseFatMachO(file, out var fatMachO) && fatMachO is {})
            {
                result = fatMachO;
                parsed = true;
            }
            else
            {
                result = null;
            }

            Metrics.FileProcessed();
            return parsed;
        }

        internal bool TryParseFatMachO(string file, out FatMachOFileResult? result)
        {
            if (TryGetMachOFilesFromFatFile(file, out var files))
            {
                result = new FatMachOFileResult(null, file, BuildIdType.None, files);
                return true;
            }

            result = null;
            return false;
        }

        internal bool TryGetMachOFilesFromFatFile(string file, out IEnumerable<ObjectFileResult> result)
        {
            // Check if it's a Fat Mach-O
            FatMachO? load = null;
            if (_fatBinaryReader?.TryLoad(file, out load) == true && load is {
                    }
                    fatMachO &&
                fatMachO.Header.FatArchCount > 0)
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
                            if (!TryParseMachOFile(buildIdFile, out var machO) || machO is null)
                            {
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

        internal bool TryParseElfFile(string file, out ObjectFileResult? result)
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

                    _logger.LogDebug("Contains unwinding info: {hasUnwindingInfo}", hasUnwindingInfo);
                    _logger.LogDebug("Contains DWARF debug info: {hasDwarfDebugInfo}", hasDwarfDebugInfo);

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
                            desc = desc.Take(16).ToArray();
                            if (desc.Length != 16)
                            {
                                // TODO: Throw?
                                _logger.LogError("build-id exists but bytes (desc) length is unexpected {bytes}.",
                                    desc.Length);
                            }
                            else
                            {
                                result = new ObjectFileResult(
                                    new Guid(desc).ToString(),
                                    file,
                                    BuildIdType.GnuBuildId);
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
                                        file,
                                        BuildIdType.TextSectionHash);
                                    return true;
                                }
                                _logger.LogDebug("Could not compute fallback id with textSection {textSection} from file {file}", textSection, file);
                            }
                            catch (Exception e)
                            {
                                _logger.LogError(e, "Failed to compute fallback id with textSection {textSection} from file {file}", textSection, file);
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
            catch (Exception e)
            {
                // You would expect TryLoad doesn't throw but that's not the case
                _logger.LogError(e, "Failed processing file {file}.", file);
            }

            finally
            {
                elf?.Dispose();
            }

            result = null;
            return false;
        }

        internal bool TryParseMachOFile(string file, out ObjectFileResult? result)
        {
            try
            {
                // TODO: find an async API if this is used by the server
                if (MachOReader.TryLoad(file, out var mach0) == MachOResult.OK)
                {
                    Metrics.MachOFileFound();
                    _logger.LogDebug("Mach-O found {file}", file);

                    string? buildId = null;
                    var uuid = mach0.GetCommandsOfType<Uuid?>().FirstOrDefault();
                    if (!(uuid is null))
                    {
                        // TODO: Verify this is coming out correctly. Endianess not verified!!!
                        buildId = uuid.Id.ToString();
                    }

                    result = new ObjectFileResult(buildId, file, BuildIdType.Uuid);
                    return true;
                }

                _logger.LogDebug("Couldn't load': {file} with mach-O reader.", file);
            }
            catch (Exception e)
            {
                // You would expect TryLoad doesn't throw but that's not the case
                _logger.LogError(e, "Failed processing file {file}.", file);
            }

            result = null;
            return false;
        }

        // TODO: Hash the file
        private object GetHash(string file)
        {
            using var algorithm = SHA256.Create();
            var hashingAlgo = algorithm.ComputeHash(File.ReadAllBytes(file));
            var builder = new StringBuilder();
            foreach (var b in hashingAlgo)
            {
                builder.Append(b.ToString("x2"));
            }

            var hash = builder.ToString();
            return hash;
        }

        internal string? GetFallbackDebugId(byte[] textSection)
        {
            if (textSection is null)
            {
                _logger.LogWarning("Can't create fallback debug id from a null buffer.");
                return null;
            }

            if (textSection.Length == 0)
            {
                _logger.LogWarning(".text section is 0 bytes long.");
                return null;
            }

            var length = Math.Min(4096, textSection.Length);
            var UUID_SIZE = 16;
            var hash = new byte[UUID_SIZE];
            for (var i = 0; i < length; i++)
            {
                hash[i % UUID_SIZE] ^= textSection[i];
            }

            var hashId = new Guid(hash).ToString();
            return hashId;
        }
    }
}
