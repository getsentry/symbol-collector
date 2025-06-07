using Microsoft.Extensions.Logging;
using SymbolCollector.Core;
using System.Runtime.InteropServices;
using static System.Console;

namespace SymbolCollector.Console;

internal class ConsoleUploader
{
    private readonly Client _client;
    private readonly ClientMetrics _metrics;
    private readonly ILogger<ConsoleUploader> _logger;

    public ConsoleUploader(
        Client client,
        ClientMetrics metrics,
        ILogger<ConsoleUploader> logger)
    {
        _client = client;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task StartUploadSymbols(IEnumerable<string> paths, string bundleId, BatchType? batchType, CancellationToken token)
    {
        var transaction = SentrySdk.StartTransaction("StartUploadSymbols", "symbols.upload");

        SentrySdk.ConfigureScope(s =>
        {
            s.Transaction = transaction;
        });

        if (!IsInputRedirected && KeyAvailable)
        {
            _ = Task.Run(() =>
            {
                WriteLine("Press Ctrl+C to exit or 'p' to print the status.");
                while (!token.IsCancellationRequested)
                {
                    if (ReadKey(true).Key == ConsoleKey.P)
                    {
                        _metrics.Write(Out);
                    }
                }
            }, token).ContinueWith(t =>
            {
                // Avoid TaskUnobservedException
                if (t.IsFaulted && t.Exception is { } e)
                {
                    SentrySdk.AddBreadcrumb("Failed when attempting to listen to the console to print status",
                        level: BreadcrumbLevel.Warning,
                        data: new Dictionary<string, string> { { "message", e.ToString() }, { "stacktrace", e.StackTrace ?? "null" } });
                }
            }, token);
        }

        try
        {
            var type = batchType ?? DeviceBatchType();
            _logger.LogInformation("Uploading bundle {bundleId} of type {type} and paths: {paths}",
                bundleId, type, paths);
            await _client.UploadAllPathsAsync(bundleId, type, paths, token);
            transaction.Finish(SpanStatus.Ok);
        }
        catch (Exception e)
        {
            transaction.Finish(e);
            throw;
        }
        finally
        {
            _metrics.Write(Out);
        }
    }

    static BatchType DeviceBatchType()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return BatchType.Linux;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return BatchType.MacOS;
        }

        throw new InvalidOperationException("No BatchType available for the current device.");
    }
}