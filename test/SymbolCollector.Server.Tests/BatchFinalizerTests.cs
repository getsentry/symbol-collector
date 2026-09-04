using Microsoft.Extensions.Options;
using NSubstitute;
using Sentry;
using SymbolCollector.Core;
using SymbolCollector.Server.Models;
using Xunit;

namespace SymbolCollector.Server.Tests;

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
        var options = Options.Create(new SymbolServiceOptions
        {
            BaseWorkingPath = _workingDirectory,
            SymsorterPath = symsorterPath
        });
        var gcsWriter = Substitute.For<ISymbolGcsWriter>();
        var hub = Substitute.For<IHub>();
        var transaction = Substitute.For<ITransactionTracer>();
        transaction.StartChild(Arg.Any<string>()).Returns(Substitute.For<ISpan>());
        hub.StartTransaction(
                Arg.Any<ITransactionContext>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>())
            .Returns(transaction);

        SentryEvent? capturedEvent = null;
        Action<Scope>? configureEventScope = null;
        hub.CaptureEvent(
            Arg.Do<SentryEvent>(value => capturedEvent = value),
            Arg.Do<Action<Scope>>(value => configureEventScope = value));

        var target = new SymsorterBatchFinalizer(
            options,
            gcsWriter,
            new BundleIdGenerator(new SuffixGenerator()),
            hub,
            Substitute.For<ILogger<SymsorterBatchFinalizer>>());
        var batch = new SymbolUploadBatch(Guid.NewGuid(), "test", BatchType.IOS);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await target.CloseBatch(
            batchLocation,
            batch,
            () => completion.TrySetResult(true),
            CancellationToken.None);
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(capturedEvent);
        Assert.StartsWith($"Batch {batch.BatchId}", capturedEvent.Message?.Message);
        Assert.NotNull(configureEventScope);

        var eventScope = new Scope(new SentryOptions());
        configureEventScope(eventScope);
        Assert.Same(transaction, eventScope.Transaction);
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
