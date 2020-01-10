using System.Collections.Generic;

namespace SymbolCollector.Core
{
    public class ObjectFileResult
    {
        public string BuildId { get; }

        /// <summary>
        /// ELF: The first 16 bytes of the GNU build id interpreted as little-endian GUID.
        /// Mach-O: The same UUID from CodeId, amended by a 0 for age.
        /// </summary>
        /// <remarks>
        /// ELF: This flips the byte order of the first three components in the build ID.
        /// An age of 0 is appended at the end. The remaining bytes of the build ID are discarded.
        /// This identifier is only specified by Breakpad, but by SSQP.
        /// </remarks>
        /// <see href="https://github.com/getsentry/symbolicator/blob/c5813650a3866de05a40af87366e6e0448f11e18/docs/advanced/symbol-server-compatibility.md#identifiers"/>
        public string DebugId { get; }

        /// <summary>
        /// ELF: The contents of the .note.gnu.build-id section, or if not present the value of the NT_GNU_BUILD_ID program header.
        /// Mach-O:  The UUID as specified in the LC_UUID load command header.
        /// </summary>
        /// <remarks>
        /// ELF: This value is traditionally 20 bytes formatted as hex string (40 characters). If neither are present, there is no code id.
        /// Mach-O: Breakpad does not save this value explicitly since it can be converted bidirectionally from the UUID.
        /// </remarks>
        /// <see href="https://github.com/getsentry/symbolicator/blob/c5813650a3866de05a40af87366e6e0448f11e18/docs/advanced/symbol-server-compatibility.md#identifiers"/>
        public string CodeId { get; }

        public string Path { get; }
        public BuildIdType BuildIdType { get; }
        public FileFormat FileFormat { get; }
        public Architecture Architecture { get; }
        public ObjectKind ObjectKind { get; }
        public string Hash { get; set; }

        public ObjectFileResult(
            string buildId,
            string debugId,
            string codeId,
            string path,
            string hash,
            BuildIdType buildIdType,
            ObjectKind objectKind,
            FileFormat fileFormat,
            Architecture architecture)
        {
            BuildId = buildId;
            DebugId = debugId;
            CodeId = codeId;
            Path = path;
            Hash = hash;
            BuildIdType = buildIdType;
            ObjectKind = objectKind;
            FileFormat = fileFormat;
            Architecture = architecture;
        }
    }

    public enum BuildIdType
    {
        None,
        GnuBuildId,
        Uuid,
        TextSectionHash,
    }

    public class FatMachOFileResult : ObjectFileResult
    {
        public IEnumerable<ObjectFileResult> InnerFiles { get; }

        public FatMachOFileResult(
            string buildId,
            string debugId,
            string codeId,
            string path,
            string hash,
            BuildIdType buildIdType,
            IEnumerable<ObjectFileResult> innerFiles)
            : base(
                buildId,
                debugId,
                codeId,
                path,
                hash,
                buildIdType,
                ObjectKind.None,
                FileFormat.FatMachO,
                Architecture.Unknown)
            => InnerFiles = innerFiles;
    }
}
