using SymbolCollector.Core;

namespace SymbolCollector.Server.Models
{
    public class ClientMetricsModel : IClientMetrics
    {
        public DateTimeOffset StartedTime { get; set; }
        public long FilesProcessedCount { get; set; }
        public long BatchesProcessedCount { get; set; }
        public long JobsInFlightCount { get; set; }
        public long FailedToUploadCount { get; set; }
        public long FailedToParseCount { get; set; }
        public long SuccessfullyUploadCount { get; set; }
        public long AlreadyExistedCount { get; set; }
        public long MachOFileFoundCount { get; set; }
        public long ElfFileFoundCount { get; set; }
        public int FatMachOFileFoundCount { get; set; }
        public long UploadedBytesCount { get; set; }
        public int FileOrDirectoryUnauthorizedAccessCount { get; set; }
        public int DirectoryDoesNotExistCount { get; set; }
    }
}
