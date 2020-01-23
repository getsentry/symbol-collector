using System;
using JustEat.StatsD;

namespace SymbolCollector.Server
{
    public class MetricsPublisher : IMetricsPublisher
    {
        private readonly IStatsDPublisher _publisher;

        public MetricsPublisher(IStatsDPublisher publisher) => _publisher = publisher;

        public void DebugIdHashConflict() => _publisher.Increment("debug-id-hash-conflict");

        public void SentryEventProcessed() => _publisher.Increment("sentry-event-processed");
        public IDisposable BeginGcsBatchUpload() => _publisher.StartTimer("gcs-upload");

        public IDisposable BeginOpenBatch() => _publisher.StartTimer("batch-open");

        public IDisposable BeginCloseBatch() => _publisher.StartTimer("batch-close");

        public IDisposable BeginSymbolMissingCheck() => _publisher.StartTimer("symbol-check");

        public void SymbolCheckExists() => _publisher.Increment("symbol-check-exists"); // TODO: Could be a tag to 'symbol-check'

        public void SymbolCheckMissing() => _publisher.Increment("symbol-check-missing"); // TODO: Could be a tag to 'symbol-check'

        public IDisposable BeginUploadSymbol() => _publisher.StartTimer("symbol-upload");

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
        IDisposable BeginGcsBatchUpload();
    }
}
