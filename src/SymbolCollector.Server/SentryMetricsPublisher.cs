using System.Diagnostics;
using Sentry;

namespace SymbolCollector.Server;

public class SentryMetricsPublisher(IHub hub) : IMetricsPublisher
{
    private const string BatchOpenCurrentCount = "batch-current";

    private class Timer(IMetricAggregator metricAggregator, string key, Action? onDispose = null) : IDisposable
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public void Dispose()
        {
            _stopwatch.Stop();
            metricAggregator.Timing(key, _stopwatch.ElapsedMilliseconds, MeasurementUnit.Duration.Millisecond);
            onDispose?.Invoke();
        }
    }
    public IDisposable BeginOpenBatch()
    {
        hub.Metrics.Gauge(BatchOpenCurrentCount);
        return hub.Metrics.StartTimer(BatchOpenCurrentCount);
    }

    public IDisposable BeginCloseBatch()
    {
        return new Timer(hub.Metrics,"batch-close",
            () => hub.Metrics.Gauge(BatchOpenCurrentCount, -1));
    }

    public IDisposable BeginSymbolMissingCheck()
    {
        return new Timer(hub.Metrics,"symbol-check");
    }

    public IDisposable BeginUploadSymbol()
    {
        return new Timer(hub.Metrics,"symbol-upload");
    }

    public void SymbolCheckExists()
    {
        hub.Metrics.Increment("symbol-check-exists");
    }

    public void SymbolCheckMissing()
    {
        hub.Metrics.Increment("symbol-check-missing");
    }

    public void FileStored(long size)
    {
        hub.Metrics.Increment("file-stored-bytes", size, MeasurementUnit.Custom("bytes"));
    }

    public void FileInvalid()
    {
        hub.Metrics.Increment("file-invalid");
    }

    public void FileKnown()
    {
        hub.Metrics.Increment("file-known");
    }

    public void DebugIdHashConflict()
    {
        hub.Metrics.Increment("debug-id-hash-conflict");
    }

    public void SentryEventProcessed()
    {
        hub.Metrics.Increment("sentry-event-processed");
    }

    public IDisposable BeginGcsBatchUpload()
    {
        return new Timer(hub.Metrics, "gcs-upload");
    }
}
