using Microsoft.Extensions.Options;
using NSubstitute;
using Sentry;
using Sentry.Extensibility;
using SymbolCollector.Core;
using SymbolCollector.Server.Models;
using Xunit;

namespace SymbolCollector.Server.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public class SentrySdkCollectionDefinition
{
    public const string Name = "Sentry SDK";
}

[Collection(SentrySdkCollectionDefinition.Name)]
public class BatchFinalizerTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(),
        $"{nameof(BatchFinalizerTests)}-{Guid.NewGuid():N}");

    public BatchFinalizerTests()
    {
        Directory.CreateDirectory(_workingDirectory);
    }

    [Fact]
    public async Task CloseBatch_CompletionEventUsesCloseBatchTransaction()
    {
        var symsorterPath = CreateSymsorterStub();
        var batchLocation = Directory.CreateDirectory(Path.Combine(_workingDirectory, "batch")).FullName;
        var batch = new SymbolUploadBatch(Guid.NewGuid(), "test", BatchType.IOS);
        var options = Options.Create(new SymbolServiceOptions
        {
            BaseWorkingPath = _workingDirectory,
            SymsorterPath = symsorterPath
        });
        var gcsWriter = Substitute.For<ISymbolGcsWriter>();
        SentryEvent? capturedEvent = null;
        SentryTransaction? closeBatchTransaction = null;
        var sentryOptions = new SentryOptions
        {
            DisableFileWrite = true,
            Dsn = "https://public@example.com/1",
            ShutdownTimeout = TimeSpan.Zero,
            TracesSampleRate = 1
        };
        sentryOptions.DisableAppDomainProcessExitFlush();
        sentryOptions.DisableAppDomainUnhandledExceptionCapture();
        sentryOptions.SetBeforeSend(@event =>
        {
            if (@event.Message?.Message?.StartsWith($"Batch {batch.BatchId}", StringComparison.Ordinal) == true)
            {
                capturedEvent = @event;
            }

            return null;
        });
        sentryOptions.SetBeforeSendTransaction(transaction =>
        {
            if (transaction.Name == "CloseBatch")
            {
                closeBatchTransaction = transaction;
            }

            return null;
        });

        using var sentry = SentrySdk.Init(sentryOptions);
        var requestTransaction = SentrySdk.StartTransaction("request", "http.request");
        SentrySdk.ConfigureScope(scope => scope.Transaction = requestTransaction);

        var target = new SymsorterBatchFinalizer(
            options,
            gcsWriter,
            new BundleIdGenerator(new SuffixGenerator()),
            HubAdapter.Instance,
            Substitute.For<ILogger<SymsorterBatchFinalizer>>());
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await target.CloseBatch(
            batchLocation,
            batch,
            () => completion.TrySetResult(true),
            CancellationToken.None);
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        requestTransaction.Finish();

        Assert.NotNull(capturedEvent);
        Assert.NotNull(closeBatchTransaction);
        Assert.Equal(closeBatchTransaction.SpanId, capturedEvent.Contexts.Trace.SpanId);
    }

    public void Dispose()
    {
        Directory.Delete(_workingDirectory, true);
    }

    private string CreateSymsorterStub()
    {
        var path = Path.Combine(
            _workingDirectory,
            OperatingSystem.IsWindows() ? "symsorter.cmd" : "symsorter");
        var contents = OperatingSystem.IsWindows()
            ? "@echo off\r\necho Sorted 0 debug files\r\n"
            : "#!/bin/sh\necho 'Sorted 0 debug files'\n";
        File.WriteAllText(path, contents);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}
