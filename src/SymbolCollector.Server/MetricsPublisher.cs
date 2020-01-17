using System;
using JustEat.StatsD;

namespace SymbolCollector.Server
{
    public class MetricsPublisher : IMetricsPublisher
    {
        private class GaugeTimer : IDisposable
        {
            private readonly IStatsDPublisher _publisher;
            private readonly string _bucket;
            private readonly IDisposable _disposable;

            public GaugeTimer(IStatsDPublisher publisher, string bucket)
            {
                _publisher = publisher;
                _bucket = bucket;
                _publisher.Increment(bucket);
                _disposable = _publisher.StartTimer(bucket + "-timing");
            }

            public void Dispose()
            {
                _publisher.Decrement(_bucket);
                _disposable.Dispose();
            }
        }

        private const string BatchOpenBucket = "batch-open";
        private readonly IStatsDPublisher _publisher;

        public MetricsPublisher(IStatsDPublisher publisher) => _publisher = publisher;

        public void DebugIdHashConflict() => _publisher.Increment("debug-id-hash-conflict");

        public void SentryEventProcessed() => _publisher.Increment("sentry-event-processed");

        public IDisposable BeginOpenBatch()
        {
            _publisher.Increment(BatchOpenBucket);
            return _publisher.StartTimer("batch-open-timing");
        }

        public IDisposable BeginCloseBatch()
        {
            _publisher.Decrement(BatchOpenBucket);
            return _publisher.StartTimer("batch-open-timing");
        }

        public IDisposable BeginSymbolMissingCheck() => _publisher.StartTimer("symbol-is-missing");

        public void SymbolCheckExists() => _publisher.Increment("symbol-check-exists");

        public void SymbolCheckMissing() => _publisher.Increment("symbol-check-is-missing");

        public IDisposable BeginUploadSymbol() => new GaugeTimer(_publisher, "symbol-upload");

        public void FileStored(long size) => _publisher.Increment(size, "file-stored-bytes");

        public void FileInvalid() => _publisher.Increment("file-invalid");

        public void FileKnown() => _publisher.Increment("file-known");
    }

    public interface ISymbolControllerMetrics
    {
        IDisposable BeginOpenBatch();
        IDisposable BeginCloseBatch();
        IDisposable BeginSymbolMissingCheck();
        IDisposable BeginUploadSymbol();
        void SymbolCheckExists();
        void SymbolCheckMissing();
        void FileStored(long size);
        void FileInvalid();
        void FileKnown();
    }

    public interface ISymbolServiceMetrics
    {
        void DebugIdHashConflict();
    }

    public interface IMetricsPublisher : ISymbolControllerMetrics, ISymbolServiceMetrics
    {
        public void SentryEventProcessed();
    }
}
