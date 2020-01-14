using System;
using System.Collections;
using System.IO;
using System.Threading;

namespace SymbolCollector.Core
{
    public class ClientMetrics : IClientMetrics
    {
        private int _filesProcessedCount;
        private int _jobsInFlightCount;
        private int _failedToUploadCount;
        private int _failedToParse;
        private int _successfullyUploadCount;
        private int _machOFileFoundCount;
        private int _elfFileFoundCount;
        private int _fatMachOFileFoundCount;
        private long _uploadedBytesCount;
        private int _fileOrDirectoryUnauthorizedAccessCount;
        private int _directoryDoesNotExistCount;
        private int _fileDoesNotExistCount;
        private int _alreadyExistedCount;

        public DateTimeOffset StartedTime { get; } = DateTimeOffset.Now;

        public long FilesProcessedCount => _filesProcessedCount;

        public long JobsInFlightCount => _jobsInFlightCount;

        public long FailedToUploadCount => _failedToUploadCount;
        public long FailedToParseCount => _failedToParse;

        public long SuccessfullyUploadCount => _successfullyUploadCount;
        public long AlreadyExistedCount => _alreadyExistedCount;

        public long MachOFileFoundCount => _machOFileFoundCount;

        public long ElfFileFoundCount => _elfFileFoundCount;

        public int FatMachOFileFoundCount => _fatMachOFileFoundCount;

        public long UploadedBytesCount => _uploadedBytesCount;

        public int FileOrDirectoryUnauthorizedAccessCount => _fileOrDirectoryUnauthorizedAccessCount;
        public int DirectoryDoesNotExistCount => _directoryDoesNotExistCount;
        public int FileDoesNotExistCount => _fileDoesNotExistCount;

        public void FileProcessed() => Interlocked.Increment(ref _filesProcessedCount);
        public void MachOFileFound() => Interlocked.Increment(ref _machOFileFoundCount);
        public void ElfFileFound() => Interlocked.Increment(ref _elfFileFoundCount);
        public void FatMachOFileFound() => Interlocked.Increment(ref _fatMachOFileFoundCount);
        public void FailedToUpload() => Interlocked.Increment(ref _failedToUploadCount);
        public void FailedToParse() => Interlocked.Increment(ref _failedToParse);
        public void SuccessfulUpload() => Interlocked.Increment(ref _successfullyUploadCount);
        public void AlreadyExisted() => Interlocked.Increment(ref _alreadyExistedCount);
        public void JobsInFlightRemove(int tasksCount) => Interlocked.Add(ref _jobsInFlightCount, -tasksCount);
        public void JobsInFlightAdd(int tasksCount) => Interlocked.Add(ref _jobsInFlightCount, tasksCount);
        public void UploadedBytesAdd(long bytes) => Interlocked.Add(ref _uploadedBytesCount, bytes);
        public void FileOrDirectoryUnauthorizedAccess() => Interlocked.Increment(ref _fileOrDirectoryUnauthorizedAccessCount);
        public void DirectoryDoesNotExist() => Interlocked.Increment(ref _directoryDoesNotExistCount);
        public void FileDoesNotExist() => Interlocked.Increment(ref _fileDoesNotExistCount);
        public TimeSpan RanFor => DateTimeOffset.Now - StartedTime;

        public void Write(IDictionary data)
        {
            data["Started at"] = StartedTime;
            data["Ran for"] = RanFor;
            data["File Processed"] = FilesProcessedCount;
            // TODO: Fix PII stripping, Sentry is deleting the field for containing "Unauthorized"
            data["File or Directory Unauthorized"] = FileOrDirectoryUnauthorizedAccessCount;
            data["Directory Does Not Exist"] = DirectoryDoesNotExistCount;
            data["File Does Not Exist"] = FileDoesNotExistCount;
            data["Jobs in flight"] = JobsInFlightCount;
            data["Failed to upload"] = FailedToUploadCount;
            data["Successfully uploaded"] = SuccessfullyUploadCount;
            data["Already existed"] = AlreadyExistedCount;
            data["Uploaded bytes"] = UploadedBytesCountHumanReadable();
            data["ELF files loaded"] = ElfFileFoundCount;
            data["Mach-O files loaded"] = MachOFileFoundCount;
            data["Fat Mach-O files loaded"] = FatMachOFileFoundCount;
        }

        public void Write(TextWriter writer)
        {
            writer.WriteLine();
            writer.Write("Started at:\t\t\t\t");
            writer.WriteLine(StartedTime);
            writer.Write("Ran for:\t\t\t\t");
            writer.WriteLine(RanFor);
            writer.Write("File Processed:\t\t\t\t");
            writer.WriteLine(FilesProcessedCount);
            writer.Write("File or Directory Unauthorized\t\t");
            writer.WriteLine(FileOrDirectoryUnauthorizedAccessCount);
            writer.Write("Directory DoesNotExist:\t\t\t");
            writer.WriteLine(DirectoryDoesNotExistCount);
            writer.Write("File DoesNotExist:\t\t\t");
            writer.WriteLine(FileDoesNotExistCount);
            writer.Write("Job in flight:\t\t\t\t");
            writer.WriteLine(JobsInFlightCount);
            writer.Write("Failed to upload:\t\t\t");
            writer.WriteLine(FailedToUploadCount);
            writer.Write("Successfully uploaded:\t\t\t");
            writer.WriteLine(SuccessfullyUploadCount);
            writer.Write("Already existed:\t\t\t");
            writer.WriteLine(AlreadyExistedCount);
            writer.Write("Uploaded bytes:\t\t\t\t");
            writer.WriteLine(UploadedBytesCountHumanReadable());
            writer.Write("ELF files loaded:\t\t\t");
            writer.WriteLine(ElfFileFoundCount);
            writer.Write("Mach-O files loaded:\t\t\t");
            writer.WriteLine(MachOFileFoundCount);
            writer.Write("Fat Mach-O files loaded:\t\t");
            writer.WriteLine(FatMachOFileFoundCount);
        }

        public string UploadedBytesCountHumanReadable()
        {
            const int scale = 1024;
            var orders = new[] { "GB", "MB", "KB", "Bytes" };
            var max = (long)Math.Pow(scale, orders.Length - 1);

            var count = UploadedBytesCount;
            foreach (var order in orders)
            {
                if (count > max)
                {
                    return $"{decimal.Divide(count, max):##.##} {order}";
                }

                max /= scale;
            }

            return "0 Bytes";
        }
    }
}
