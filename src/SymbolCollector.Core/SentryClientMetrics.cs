#pragma warning disable SENTRYTRACECONNECTEDMETRICS // IHub.Metrics is experimental
using Sentry;
using Sentry.Extensibility;

namespace SymbolCollector.Core;

/// <summary>
/// A decorator for <see cref="ClientMetrics"/> that also emits metrics to Sentry's trace-connected metrics API.
/// </summary>
/// <remarks>
/// This integrates with Sentry SDK 6.1.0's new experimental trace-connected metrics feature.
/// Metrics are attached to the current trace/span for correlation in Sentry's UI.
/// Enable with <c>options.Experimental.EnableMetrics = true</c>.
/// </remarks>
public class SentryClientMetrics : ClientMetrics
{
    private readonly IHub _hub;

    /// <summary>
    /// Creates a new instance using the default <see cref="HubAdapter.Instance"/>.
    /// </summary>
    public SentryClientMetrics() : this(HubAdapter.Instance)
    {
    }

    /// <summary>
    /// Creates a new instance with the specified hub for metrics emission.
    /// </summary>
    /// <param name="hub">The Sentry hub to use for emitting metrics.</param>
    public SentryClientMetrics(IHub hub)
    {
        _hub = hub;
    }

    private SentryMetricEmitter Metrics => _hub.Metrics;

    /// <summary>
    /// Records a file processed event, incrementing both local and Sentry counters.
    /// </summary>
    public override void FileProcessed()
    {
        base.FileProcessed();
        Metrics.EmitCounter("symbol_collector.files_processed", 1);
    }

    /// <summary>
    /// Records a Mach-O file discovery.
    /// </summary>
    public override void MachOFileFound()
    {
        base.MachOFileFound();
        Metrics.EmitCounter("symbol_collector.debug_images_found", 1,
            [new KeyValuePair<string, object>("format", "macho")]);
    }

    /// <summary>
    /// Records an ELF file discovery.
    /// </summary>
    public override void ElfFileFound()
    {
        base.ElfFileFound();
        Metrics.EmitCounter("symbol_collector.debug_images_found", 1,
            [new KeyValuePair<string, object>("format", "elf")]);
    }

    /// <summary>
    /// Records a Fat Mach-O file discovery.
    /// </summary>
    public override void FatMachOFileFound()
    {
        base.FatMachOFileFound();
        Metrics.EmitCounter("symbol_collector.debug_images_found", 1,
            [new KeyValuePair<string, object>("format", "fat_macho")]);
    }

    /// <summary>
    /// Records a failed upload.
    /// </summary>
    public override void FailedToUpload()
    {
        base.FailedToUpload();
        Metrics.EmitCounter("symbol_collector.uploads", 1,
            [new KeyValuePair<string, object>("status", "failed")]);
    }

    /// <summary>
    /// Records a parse failure.
    /// </summary>
    public override void FailedToParse()
    {
        base.FailedToParse();
        Metrics.EmitCounter("symbol_collector.parse_failures", 1);
    }

    /// <summary>
    /// Records a successful upload.
    /// </summary>
    public override void SuccessfulUpload()
    {
        base.SuccessfulUpload();
        Metrics.EmitCounter("symbol_collector.uploads", 1,
            [new KeyValuePair<string, object>("status", "success")]);
    }

    /// <summary>
    /// Records when a file already existed on the server.
    /// </summary>
    public override void AlreadyExisted()
    {
        base.AlreadyExisted();
        Metrics.EmitCounter("symbol_collector.uploads", 1,
            [new KeyValuePair<string, object>("status", "already_exists")]);
    }

    /// <summary>
    /// Removes jobs from the in-flight count.
    /// </summary>
    public override void JobsInFlightRemove(int tasksCount)
    {
        base.JobsInFlightRemove(tasksCount);
        Metrics.EmitGauge("symbol_collector.jobs_in_flight", JobsInFlightCount);
    }

    /// <summary>
    /// Adds jobs to the in-flight count.
    /// </summary>
    public override void JobsInFlightAdd(int tasksCount)
    {
        base.JobsInFlightAdd(tasksCount);
        Metrics.EmitGauge("symbol_collector.jobs_in_flight", JobsInFlightCount);
    }

    /// <summary>
    /// Records bytes uploaded, emitting as a distribution for percentile analysis.
    /// </summary>
    public override void UploadedBytesAdd(long bytes)
    {
        base.UploadedBytesAdd(bytes);
        Metrics.EmitDistribution("symbol_collector.uploaded_bytes", bytes, MeasurementUnit.Information.Byte);
    }

    /// <summary>
    /// Records an unauthorized access error.
    /// </summary>
    public override void FileOrDirectoryUnauthorizedAccess()
    {
        base.FileOrDirectoryUnauthorizedAccess();
        Metrics.EmitCounter("symbol_collector.access_errors", 1,
            [new KeyValuePair<string, object>("type", "unauthorized")]);
    }

    /// <summary>
    /// Records a directory not found error.
    /// </summary>
    public override void DirectoryDoesNotExist()
    {
        base.DirectoryDoesNotExist();
        Metrics.EmitCounter("symbol_collector.access_errors", 1,
            [new KeyValuePair<string, object>("type", "directory_not_found")]);
    }

    /// <summary>
    /// Records a file not found error.
    /// </summary>
    public override void FileDoesNotExist()
    {
        base.FileDoesNotExist();
        Metrics.EmitCounter("symbol_collector.access_errors", 1,
            [new KeyValuePair<string, object>("type", "file_not_found")]);
    }
}
