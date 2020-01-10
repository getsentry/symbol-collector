using System.Collections.Generic;

namespace SymbolCollector.Core
{
    public class ObjectFileResult
    {
        // https://github.com/getsentry/symbolicator/blob/c68091f0b52f88cee72eb70d2275737227667aaa/symsorter/src/app.rs#L71-L77
        public string UnifiedId => string.IsNullOrWhiteSpace(CodeId) ? DebugId : CodeId; // TODO: DebugId in 'breakpad format', lowercase

        /// <summary>
        /// The original platform-specific identifier
        /// </summary>
        /// <remarks>
        /// ELF: The first 16 bytes of the GNU build id interpreted as little-endian GUID.
        /// This flips the byte order of the first three components in the build ID.
        /// An age of 0 is appended at the end. The remaining bytes of the build ID are discarded.
        /// This identifier is only specified by Breakpad, but by SSQP.
        /// Mach-O: The same UUID from CodeId, amended by a 0 for age.
        /// </remarks>
        /// <see href="https://github.com/getsentry/symbolicator/blob/c5813650a3866de05a40af87366e6e0448f11e18/docs/advanced/symbol-server-compatibility.md#identifiers"/>
        public string DebugId { get; }

        /// <summary>
        /// A potentially lossy transformation of the code identifier into a unified format similar to the PDB debug identifiers.
        /// </summary>
        /// <remarks>
        /// ELF: The contents of the .note.gnu.build-id section, or if not present the value of the NT_GNU_BUILD_ID program header.
        /// This value is traditionally 20 bytes formatted as hex string (40 characters). If neither are present, there is no code id.
        /// Mach-O: The UUID as specified in the LC_UUID load command header.
        /// Breakpad does not save this value explicitly since it can be converted bidirectionally from the UUID.
        /// </remarks>
        /// <see href="https://github.com/getsentry/symbolicator/blob/c5813650a3866de05a40af87366e6e0448f11e18/docs/advanced/symbol-server-compatibility.md#identifiers"/>
        public string CodeId { get; }

        public string Path { get; internal set; } // settable for testing
        public BuildIdType BuildIdType { get; }
        public FileFormat FileFormat { get; }
        public Architecture Architecture { get; }
        public ObjectKind ObjectKind { get; }
        public string Hash { get; }

        public ObjectFileResult(
            string debugId,
            string codeId,
            string path,
            string hash,
            BuildIdType buildIdType,
            ObjectKind objectKind,
            FileFormat fileFormat,
            Architecture architecture)
        {
            DebugId = debugId;
            CodeId = codeId;
            Path = path;
            Hash = hash;
            BuildIdType = buildIdType;
            ObjectKind = objectKind;
            FileFormat = fileFormat;
            Architecture = architecture;
        }

        public override string ToString() =>
             $"{nameof(UnifiedId)}: {UnifiedId}, " +
             $"{nameof(DebugId)}: {DebugId}, " +
             $"{nameof(CodeId)}: {CodeId}, " +
             $"{nameof(Path)}: {Path}, " +
             $"{nameof(BuildIdType)}: {BuildIdType}, " +
             $"{nameof(FileFormat)}: {FileFormat}, " +
             $"{nameof(Architecture)}: {Architecture}, " +
             $"{nameof(ObjectKind)}: {ObjectKind}, " +
             $"{nameof(Hash)}: {Hash}";
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
            string debugId,
            string codeId,
            string path,
            string hash,
            IEnumerable<ObjectFileResult> innerFiles)
            : base(
                debugId,
                codeId,
                path,
                hash,
                BuildIdType.None,
                ObjectKind.None,
                FileFormat.FatMachO,
                Architecture.Unknown)
            => InnerFiles = innerFiles;
    }
}
