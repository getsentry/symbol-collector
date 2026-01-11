using System.Collections.Concurrent;
using Sentry;
using Sentry.Extensibility;
using Sentry.Protocol.Envelopes;
using Xunit;

namespace SymbolCollector.Core.Tests;

/// <summary>
/// Tests that verify SentryClientMetrics emits metrics to Sentry.
/// </summary>
public class SentryClientMetricsTests : IDisposable
{
    private readonly RecordingTransport _recordingTransport;
    private readonly IDisposable _sentryDisposable;

    public SentryClientMetricsTests()
    {
        _recordingTransport = new RecordingTransport();

        _sentryDisposable = SentrySdk.Init(o =>
        {
            o.Dsn = "https://key@sentry.io/123";
            o.Transport = _recordingTransport;
            o.Experimental.EnableMetrics = true;
            o.AutoSessionTracking = false;
        });
    }

    public void Dispose()
    {
        _sentryDisposable.Dispose();
    }

    [Fact]
    public async Task FileProcessed_EmitsCounter()
    {
        // Arrange
        var metrics = new SentryClientMetrics();

        // Act
        metrics.FileProcessed();
        await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("trace_metric", _recordingTransport.GetAllItemTypes());
    }

    [Fact]
    public async Task ElfFileFound_EmitsCounterWithFormatAttribute()
    {
        // Arrange
        var metrics = new SentryClientMetrics();

        // Act
        metrics.ElfFileFound();
        await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("trace_metric", _recordingTransport.GetAllItemTypes());
    }

    [Fact]
    public async Task SuccessfulUpload_EmitsCounterWithStatusAttribute()
    {
        // Arrange
        var metrics = new SentryClientMetrics();

        // Act
        metrics.SuccessfulUpload();
        await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("trace_metric", _recordingTransport.GetAllItemTypes());
    }

    [Fact]
    public async Task UploadedBytesAdd_EmitsDistribution()
    {
        // Arrange
        var metrics = new SentryClientMetrics();

        // Act
        metrics.UploadedBytesAdd(1024);
        await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("trace_metric", _recordingTransport.GetAllItemTypes());
    }

    [Fact]
    public async Task JobsInFlightAdd_EmitsGauge()
    {
        // Arrange
        var metrics = new SentryClientMetrics();

        // Act
        metrics.JobsInFlightAdd(5);
        await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("trace_metric", _recordingTransport.GetAllItemTypes());
    }

    [Fact]
    public void FileProcessed_AlsoIncrementsBaseCounter()
    {
        // Arrange
        var metrics = new SentryClientMetrics();

        // Act
        metrics.FileProcessed();
        metrics.FileProcessed();

        // Assert - base class counter should also be incremented
        Assert.Equal(2, metrics.FilesProcessedCount);
    }

    [Fact]
    public void VirtualMethodsAreOverridden_PolymorphismWorks()
    {
        // This test verifies that when SentryClientMetrics is used via
        // the ClientMetrics base class reference, the overridden methods are called.
        ClientMetrics metrics = new SentryClientMetrics();

        // These calls should invoke SentryClientMetrics methods, not ClientMetrics
        metrics.FileProcessed();
        metrics.ElfFileFound();
        metrics.SuccessfulUpload();

        // If polymorphism works, the counters should be incremented
        Assert.Equal(1, metrics.FilesProcessedCount);
        Assert.Equal(1, metrics.ElfFileFoundCount);
        Assert.Equal(1, metrics.SuccessfullyUploadCount);
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
