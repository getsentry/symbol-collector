namespace SymbolCollector.Core;

/// <summary>
/// A decorator for <see cref="ClientMetrics"/> that also emits metrics to Sentry's trace-connected metrics API.
/// </summary>
/// <remarks>
/// This integrates with Sentry SDK 6.1.0's new experimental trace-connected metrics feature.
/// Metrics are attached to the current trace/span for correlation in Sentry's UI.
/// Access via <c>SentrySdk.Experimental.Metrics</c> after enabling with <c>options.Experimental.EnableMetrics = true</c>.
/// </remarks>
public class SentryClientMetrics : ClientMetrics
{
    /// <summary>
    /// Records a file processed event, incrementing both local and Sentry counters.
    /// </summary>
    public override void FileProcessed()
    {
        base.FileProcessed();
        SentrySdk.Experimental.Metrics.AddCounter("symbol_collector.files_processed", 1);
    }

    /// <summary>
    /// Records a Mach-O file discovery.
    /// </summary>
    public override void MachOFileFound()
    {
        base.MachOFileFound();
        SentrySdk.Experimental.Metrics.AddCounter("symbol_collector.debug_images_found", 1,
            [new KeyValuePair<string, object>("format", "macho")]);
    }

    /// <summary>
    /// Records an ELF file discovery.
    /// </summary>
    public override void ElfFileFound()
    {
        base.ElfFileFound();
        SentrySdk.Experimental.Metrics.AddCounter("symbol_collector.debug_images_found", 1,
            [new KeyValuePair<string, object>("format", "elf")]);
    }

    /// <summary>
    /// Records a Fat Mach-O file discovery.
    /// </summary>
    public override void FatMachOFileFound()
    {
        base.FatMachOFileFound();
        SentrySdk.Experimental.Metrics.AddCounter("symbol_collector.debug_images_found", 1,
            [new KeyValuePair<string, object>("format", "fat_macho")]);
    }

    /// <summary>
    /// Records a failed upload.
    /// </summary>
    public override void FailedToUpload()
    {
        base.FailedToUpload();
        SentrySdk.Experimental.Metrics.AddCounter("symbol_collector.uploads", 1,
            [new KeyValuePair<string, object>("status", "failed")]);
    }

    /// <summary>
    /// Records a parse failure.
    /// </summary>
    public override void FailedToParse()
    {
        base.FailedToParse();
        SentrySdk.Experimental.Metrics.AddCounter("symbol_collector.parse_failures", 1);
    }

    /// <summary>
    /// Records a successful upload.
    /// </summary>
    public override void SuccessfulUpload()
    {
        base.SuccessfulUpload();
        SentrySdk.Experimental.Metrics.AddCounter("symbol_collector.uploads", 1,
            [new KeyValuePair<string, object>("status", "success")]);
    }

    /// <summary>
    /// Records when a file already existed on the server.
    /// </summary>
    public override void AlreadyExisted()
    {
        base.AlreadyExisted();
        SentrySdk.Experimental.Metrics.AddCounter("symbol_collector.uploads", 1,
            [new KeyValuePair<string, object>("status", "already_exists")]);
    }

    /// <summary>
    /// Removes jobs from the in-flight count.
    /// </summary>
    public override void JobsInFlightRemove(int tasksCount)
    {
        base.JobsInFlightRemove(tasksCount);
        SentrySdk.Experimental.Metrics.RecordGauge("symbol_collector.jobs_in_flight", JobsInFlightCount);
    }

    /// <summary>
    /// Adds jobs to the in-flight count.
    /// </summary>
    public override void JobsInFlightAdd(int tasksCount)
    {
        base.JobsInFlightAdd(tasksCount);
        SentrySdk.Experimental.Metrics.RecordGauge("symbol_collector.jobs_in_flight", JobsInFlightCount);
    }

    /// <summary>
    /// Records bytes uploaded, emitting as a distribution for percentile analysis.
    /// </summary>
    public override void UploadedBytesAdd(long bytes)
    {
        base.UploadedBytesAdd(bytes);
        // Use distribution for uploaded bytes to capture percentiles and histograms
        SentrySdk.Experimental.Metrics.RecordDistribution("symbol_collector.uploaded_bytes", bytes, "byte");
    }

    /// <summary>
    /// Records an unauthorized access error.
    /// </summary>
    public override void FileOrDirectoryUnauthorizedAccess()
    {
        base.FileOrDirectoryUnauthorizedAccess();
        SentrySdk.Experimental.Metrics.AddCounter("symbol_collector.access_errors", 1,
            [new KeyValuePair<string, object>("type", "unauthorized")]);
    }

    /// <summary>
    /// Records a directory not found error.
    /// </summary>
    public override void DirectoryDoesNotExist()
    {
        base.DirectoryDoesNotExist();
        SentrySdk.Experimental.Metrics.AddCounter("symbol_collector.access_errors", 1,
            [new KeyValuePair<string, object>("type", "directory_not_found")]);
    }

    /// <summary>
    /// Records a file not found error.
    /// </summary>
    public override void FileDoesNotExist()
    {
        base.FileDoesNotExist();
        SentrySdk.Experimental.Metrics.AddCounter("symbol_collector.access_errors", 1,
            [new KeyValuePair<string, object>("type", "file_not_found")]);
    }
}
