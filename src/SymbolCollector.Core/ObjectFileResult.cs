using System.Collections.Generic;

namespace SymbolCollector.Core
{
    public class ObjectFileResult
    {
        public string? BuildId { get; }
        public string Path { get; }
        public BuildIdType BuildIdType { get; }
        // TODO: add hash
        public byte[]? Hash { get; set; }

        public ObjectFileResult(
            string? buildId,
            string path,
            BuildIdType buildIdType)
        {
            BuildId = buildId;
            Path = path;
            BuildIdType = buildIdType;
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
            string? buildId,
            string path,
            BuildIdType buildIdType,
            IEnumerable<ObjectFileResult> innerFiles)
            : base(buildId, path, buildIdType)
            => InnerFiles = innerFiles;
    }
}
