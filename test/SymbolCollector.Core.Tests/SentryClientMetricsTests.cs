#pragma warning disable SENTRYTRACECONNECTEDMETRICS // IHub.Metrics is experimental
using System.Collections.Concurrent;
using Sentry;
using Sentry.Extensibility;
using Sentry.Protocol.Envelopes;
using Xunit;

namespace SymbolCollector.Core.Tests;

/// <summary>
/// Tests that verify SentryClientMetrics emits metrics to Sentry.
/// Each test initializes an isolated Sentry SDK instance with a recording transport.
/// </summary>
public class SentryClientMetricsTests : IDisposable
{
    private readonly RecordingTransport _transport;
    private readonly IDisposable _sentry;
    private readonly IHub _hub;
    private readonly SentryClientMetrics _metrics;

    public SentryClientMetricsTests()
    {
        _transport = new RecordingTransport();

        // Initialize an isolated Sentry SDK instance
        _sentry = SentrySdk.Init(o =>
        {
            o.Dsn = "https://key@sentry.io/123";
            o.Transport = _transport;
            o.Experimental.EnableMetrics = true;
            o.AutoSessionTracking = false;
        });

        // Get the hub from the initialized SDK via HubAdapter
        _hub = HubAdapter.Instance;
        _metrics = new SentryClientMetrics(_hub);
    }

    public void Dispose()
    {
        // Dispose SDK to ensure isolation (flush happens automatically)
        _sentry.Dispose();
    }

    [Fact]
    public async Task FileProcessed_EmitsTraceMetric()
    {
        _metrics.FileProcessed();
        await SentrySdk.FlushAsync(TimeSpan.FromSeconds(1));

        Assert.Contains("trace_metric", _transport.GetAllItemTypes());
    }

    [Fact]
    public async Task ElfFileFound_EmitsTraceMetric()
    {
        _metrics.ElfFileFound();
        await SentrySdk.FlushAsync(TimeSpan.FromSeconds(1));

        Assert.Contains("trace_metric", _transport.GetAllItemTypes());
    }

    [Fact]
    public async Task SuccessfulUpload_EmitsTraceMetric()
    {
        _metrics.SuccessfulUpload();
        await SentrySdk.FlushAsync(TimeSpan.FromSeconds(1));

        Assert.Contains("trace_metric", _transport.GetAllItemTypes());
    }

    [Fact]
    public async Task UploadedBytesAdd_EmitsTraceMetric()
    {
        _metrics.UploadedBytesAdd(1024);
        await SentrySdk.FlushAsync(TimeSpan.FromSeconds(1));

        Assert.Contains("trace_metric", _transport.GetAllItemTypes());
    }

    [Fact]
    public async Task JobsInFlightAdd_EmitsTraceMetric()
    {
        _metrics.JobsInFlightAdd(5);
        await SentrySdk.FlushAsync(TimeSpan.FromSeconds(1));

        Assert.Contains("trace_metric", _transport.GetAllItemTypes());
    }

    [Fact]
    public void FileProcessed_AlsoIncrementsBaseCounter()
    {
        _metrics.FileProcessed();
        _metrics.FileProcessed();

        Assert.Equal(2, _metrics.FilesProcessedCount);
    }

    [Fact]
    public void VirtualMethodsAreOverridden_PolymorphismWorks()
    {
        // Use SentryClientMetrics via ClientMetrics reference
        ClientMetrics baseRef = _metrics;

        baseRef.FileProcessed();
        baseRef.ElfFileFound();
        baseRef.SuccessfulUpload();

        // Base class counters should be incremented
        Assert.Equal(1, baseRef.FilesProcessedCount);
        Assert.Equal(1, baseRef.ElfFileFoundCount);
        Assert.Equal(1, baseRef.SuccessfullyUploadCount);
    }

    private class RecordingTransport : ITransport
    {
        private readonly ConcurrentBag<Envelope> _envelopes = new();

        public Task SendEnvelopeAsync(Envelope envelope, CancellationToken cancellationToken = default)
        {
            _envelopes.Add(envelope);
            return Task.CompletedTask;
        }

        public IReadOnlyList<string> GetAllItemTypes()
        {
            var types = new List<string>();
            foreach (var envelope in _envelopes)
            {
                foreach (var item in envelope.Items)
                {
                    if (item.TryGetType() is { } type)
                    {
                        types.Add(type);
                    }
                }
            }
            return types;
        }
    }
}
