using System.Diagnostics;
using Sentry;

namespace SymbolCollector.Server;

public class SentryMetricsPublisher(ISentryClient sentryClient) : IMetricsPublisher
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
        sentryClient.Metrics.Gauge(BatchOpenCurrentCount);
        return new Timer(sentryClient.Metrics,BatchOpenCurrentCount);
    }

    public IDisposable BeginCloseBatch()
    {
        return new Timer(sentryClient.Metrics,"batch-close",
            () => sentryClient.Metrics.Gauge(BatchOpenCurrentCount, -1));
    }

    public IDisposable BeginSymbolMissingCheck()
    {
        return new Timer(sentryClient.Metrics,"symbol-check");
    }

    public IDisposable BeginUploadSymbol()
    {
        return new Timer(sentryClient.Metrics,"symbol-upload");
    }

    public void SymbolCheckExists()
    {
        sentryClient.Metrics.Increment("symbol-check-exists");
    }

    public void SymbolCheckMissing()
    {
        sentryClient.Metrics.Increment("symbol-check-missing");
    }

    public void FileStored(long size)
    {
        sentryClient.Metrics.Increment("file-stored-bytes", size, MeasurementUnit.Custom("bytes"));
    }

    public void FileInvalid()
    {
        sentryClient.Metrics.Increment("file-invalid");
    }

    public void FileKnown()
    {
        sentryClient.Metrics.Increment("file-known");
    }

    public void DebugIdHashConflict()
    {
        sentryClient.Metrics.Increment("debug-id-hash-conflict");
    }

    public void SentryEventProcessed()
    {
        sentryClient.Metrics.Increment("sentry-event-processed");
    }

    public IDisposable BeginGcsBatchUpload()
    {
        return new Timer(sentryClient.Metrics, "gcs-upload");
    }
}
