using System;

namespace SymbolCollector.Core
{
    public interface IClientMetrics
    {
        DateTimeOffset StartedTime { get; }
        long FilesProcessedCount { get; }
        long JobsInFlightCount { get; }
        long FailedToUploadCount { get; }
        long FailedToParseCount { get; }
        long SuccessfullyUploadCount { get; }
        long AlreadyExistedCount { get; }
        long MachOFileFoundCount { get; }
        long ElfFileFoundCount { get; }
        int FatMachOFileFoundCount { get; }
        long UploadedBytesCount { get; }
        int DirectoryUnauthorizedAccessCount { get; }
        int DirectoryDoesNotExistCount { get; }
    }
}
