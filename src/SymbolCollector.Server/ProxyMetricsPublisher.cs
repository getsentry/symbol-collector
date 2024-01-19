namespace SymbolCollector.Server;

public class ProxyMetricsPublisher(
    // going through property to observe a change in the instance on the client.
    // ReSharper disable once SuggestBaseTypeForParameterInConstructor
    StatsDMetricsPublisher metricsPublisherImplementation,
    // ReSharper disable once SuggestBaseTypeForParameterInConstructor
    SentryMetricsPublisher metricsPublisherImplementation2)
    : IMetricsPublisher
{
    public IDisposable BeginOpenBatch()
    {
        return new DisposableProxy(metricsPublisherImplementation.BeginOpenBatch(),
            metricsPublisherImplementation2.BeginOpenBatch());
    }

    public IDisposable BeginCloseBatch()
    {
        return new DisposableProxy(metricsPublisherImplementation.BeginCloseBatch(),
            metricsPublisherImplementation2.BeginCloseBatch());
    }

    public IDisposable BeginSymbolMissingCheck()
    {
        return new DisposableProxy(metricsPublisherImplementation.BeginSymbolMissingCheck(),
            metricsPublisherImplementation2.BeginSymbolMissingCheck());
    }

    public IDisposable BeginUploadSymbol()
    {
        return new DisposableProxy(metricsPublisherImplementation.BeginUploadSymbol(),
            metricsPublisherImplementation2.BeginUploadSymbol());
    }

    public void SymbolCheckExists()
    {
        metricsPublisherImplementation.SymbolCheckExists();
        metricsPublisherImplementation2.SymbolCheckExists();
    }

    public void SymbolCheckMissing()
    {
        metricsPublisherImplementation.SymbolCheckMissing();
        metricsPublisherImplementation2.SymbolCheckMissing();
    }

    public void FileStored(long size)
    {
        metricsPublisherImplementation.FileStored(size);
        metricsPublisherImplementation2.FileStored(size);
    }

    public void FileInvalid()
    {
        metricsPublisherImplementation.FileInvalid();
        metricsPublisherImplementation2.FileInvalid();
    }

    public void FileKnown()
    {
        metricsPublisherImplementation.FileKnown();
        metricsPublisherImplementation2.FileKnown();
    }

    public void DebugIdHashConflict()
    {
        metricsPublisherImplementation.DebugIdHashConflict();
        metricsPublisherImplementation2.DebugIdHashConflict();
    }

    public void SentryEventProcessed()
    {
        metricsPublisherImplementation.SentryEventProcessed();
        metricsPublisherImplementation2.SentryEventProcessed();
    }

    public IDisposable BeginGcsBatchUpload()
    {
        return new DisposableProxy(metricsPublisherImplementation.BeginGcsBatchUpload(),
            metricsPublisherImplementation2.BeginGcsBatchUpload());
    }

    private class DisposableProxy(IDisposable disposableImplementation, IDisposable disposableImplementation2)
        : IDisposable
    {
        public void Dispose()
        {
            disposableImplementation.Dispose();
            disposableImplementation2.Dispose();
        }
    }
}
