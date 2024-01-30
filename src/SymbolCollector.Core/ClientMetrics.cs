using System.Collections;
using Sentry;

namespace SymbolCollector.Core;

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

    public void FileProcessed()
    {
        Interlocked.Increment(ref _filesProcessedCount);
        SentrySdk.Metrics.Increment("files_processed");
    }

    public void MachOFileFound()
    {
        Interlocked.Increment(ref _machOFileFoundCount);
        SentrySdk.Metrics.Increment("debug_image_found", tags: new Dictionary<string, string> { {"type", "mach-o"} } );
    }

    public void ElfFileFound()
    {
        Interlocked.Increment(ref _elfFileFoundCount);
        SentrySdk.Metrics.Increment("debug_image_found", tags: new Dictionary<string, string> { {"type", "elf" } } );
    }

    public void FatMachOFileFound()
    {
        Interlocked.Increment(ref _fatMachOFileFoundCount);
        SentrySdk.Metrics.Increment("debug_image_found", tags: new Dictionary<string, string> { {"type", "fat-mach-o" } } );
    }

    public void FailedToUpload()
    {
        Interlocked.Increment(ref _failedToUploadCount);
        SentrySdk.Metrics.Increment("upload", tags: new Dictionary<string, string> { {"type", "failed" } } );
    }

    public void FailedToParse()
    {
        Interlocked.Increment(ref _failedToParse);
        SentrySdk.Metrics.Increment("parse_failed");
    }

    public void SuccessfulUpload()
    {
        Interlocked.Increment(ref _successfullyUploadCount);
        SentrySdk.Metrics.Increment("upload", tags: new Dictionary<string, string> { {"type", "successful" } } );
    }

    public void AlreadyExisted()
    {
        Interlocked.Increment(ref _alreadyExistedCount);
        SentrySdk.Metrics.Increment("already_existed");
    }

    public void JobsInFlightRemove(int tasksCount)
    {
        Interlocked.Add(ref _jobsInFlightCount, -tasksCount);
        SentrySdk.Metrics.Increment("jobs_in_flight", -tasksCount);
    }

    public void JobsInFlightAdd(int tasksCount)
    {
        Interlocked.Add(ref _jobsInFlightCount, tasksCount);
        SentrySdk.Metrics.Increment("jobs_in_flight", tasksCount);
    }

    public void UploadedBytesAdd(long bytes)
    {
        Interlocked.Add(ref _uploadedBytesCount, bytes);
        SentrySdk.Metrics.Increment("uploaded_bytes", bytes, MeasurementUnit.Custom("bytes"));
    }

    public void FileOrDirectoryUnauthorizedAccess()
    {
        Interlocked.Increment(ref _fileOrDirectoryUnauthorizedAccessCount);
        SentrySdk.Metrics.Increment("file_or_directory_unauthorized");
    }

    public void DirectoryDoesNotExist()
    {
        Interlocked.Increment(ref _directoryDoesNotExistCount);
        SentrySdk.Metrics.Increment("directory_does_not_exist");
    }

    public void FileDoesNotExist()
    {
        Interlocked.Increment(ref _fileDoesNotExistCount);
        SentrySdk.Metrics.Increment("file_does_not_exist");
    }

    public TimeSpan RanFor => DateTimeOffset.Now - StartedTime;

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
