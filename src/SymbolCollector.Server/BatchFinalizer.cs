using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Sentry;
using SymbolCollector.Core;
using SymbolCollector.Server.Models;

namespace SymbolCollector.Server;

public interface IBatchFinalizer
{
    public Task CloseBatch(
        string batchLocation,
        SymbolUploadBatch batch,
        Action doneCallback,
        CancellationToken token);
}

// TODO: replace symsorter shell out BatchFinalizer with:
// $"output/{batch.BatchType}/bundles/{batch.FriendlyName}";
// Format correct output i.e: output/ios/bundles/10.3_ABCD

// With contents in the format:
// $"{\"name\":"{batch.FriendlyName},\"timestamp\":\"{batchId.StartTime}\",\"debug_ids\":[ ... ]}";
// Matching format i.e: {"name":"10.3_ABCD","timestamp":"2019-12-27T12:43:27.955330Z","debug_ids":[
// BatchId has no dashes

// And for each file, write:
// output/{batch.BatchType}/10/8f1100326466498e655588e72a3e1e/
// zstd compressed.
// Name the file {symbol.SymbolType.ToLower()}
// file named: meta
// {"name":"System.Net.Http.Native.dylib","arch":"x86_64","file_format":"macho"}
// folder called /refs/ with an empty file named batch.FriendlyName
public class SymsorterBatchFinalizer : IBatchFinalizer
{
    private readonly SymbolServiceOptions _options;
    private readonly IMetricsPublisher _metrics;
    private readonly ILogger<SymsorterBatchFinalizer> _logger;
    private readonly ISymbolGcsWriter _gcsWriter;
    private readonly BundleIdGenerator _bundleIdGenerator;
    private readonly IHub _hub;
    private readonly string _symsorterOutputPath;

    public SymsorterBatchFinalizer(
        IMetricsPublisher metrics,
        IOptions<SymbolServiceOptions> options,
        ISymbolGcsWriter gcsWriter,
        BundleIdGenerator bundleIdGenerator,
        IHub hub,
        ILogger<SymsorterBatchFinalizer> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _metrics = metrics;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gcsWriter = gcsWriter ?? throw new ArgumentNullException(nameof(gcsWriter));
        _bundleIdGenerator = bundleIdGenerator;
        _hub = hub;
        if (!File.Exists(_options.SymsorterPath))
        {
            throw new ArgumentException($"Symsorter not found at: {_options.SymsorterPath}");
        }

        _symsorterOutputPath = Path.Combine(_options.BaseWorkingPath, "symsorter_output");
        Directory.CreateDirectory(_symsorterOutputPath);
    }

    public Task CloseBatch(
        string batchLocation,
        SymbolUploadBatch batch,
        Action doneCallback,
        CancellationToken token)
    {
        // TODO: Turn into a job.
        var stopwatch = Stopwatch.StartNew();
        var gcsUploadCancellation = CancellationToken.None;

        // Since we'll run the closing of the batch on a background thread, trigger an event
        // (that will be dropped since Debug events are not captured in prod)
        // in order to get the Sentry SDK to read the request data and add to the Scope. In case there's an error
        // when closing the batch, the request data will already be available to add to outgoing events.
        SentrySdk.CaptureMessage("To read Request data on the request thread", SentryLevel.Debug);

        SentryTraceHeader? traceHeader = null;
        _hub.ConfigureScope(s => traceHeader = s.Transaction?.GetTraceHeader());

        var closeBatchTransaction =
            traceHeader is not null
                ? _hub.StartTransaction("CloseBatch", "batch.close", traceHeader)
                : _hub.StartTransaction("CloseBatch", "batch.close");

        var handle = _metrics.BeginGcsBatchUpload();
        _ = Task.Run(async () =>
            {
                // get logger factory and create a logger for symsorter
                var symsorterOutput = Path.Combine(_symsorterOutputPath, batch.BatchId.ToString());

                try
                {
                    var symsorterSpan = closeBatchTransaction.StartChild("symsorter");

                    Directory.CreateDirectory(symsorterOutput);

                    if (SortSymbols(batchLocation, batch, symsorterOutput, symsorterSpan))
                    {
                        symsorterSpan.Finish(SpanStatus.UnknownError);
                        return;
                    }
                    symsorterSpan.Finish();

                    if (_options.DeleteDoneDirectory)
                    {
                        Directory.Delete(batchLocation, true);
                    }

                    var trimDown = symsorterOutput + "/";

                    async Task UploadToGoogle(string filePath)
                    {
                        var destinationName = filePath.Replace(trimDown!, string.Empty);
                        await using var file = File.OpenRead(filePath);
                        await _gcsWriter.WriteAsync(destinationName, file,
                            // The client disconnecting at this point shouldn't affect closing this batch.
                            // This should anyway be a background job queued by the batch finalizer
                            gcsUploadCancellation);
                    }

                    var counter = 0;
                    var groups =
                        from directory in Directory.GetDirectories(symsorterOutput, "*", SearchOption.AllDirectories)
                        from file in Directory.GetFiles(directory)
                        let c = counter++
                        group file by c / 20 // TODO: config
                        into fileGroup
                        select fileGroup.ToList();

                    symsorterSpan.SetExtra("batch_size", counter);
                    var gcpUploadSpan = closeBatchTransaction.StartChild("GcpUpload");
                    try
                    {
                        foreach (var group in groups)
                        {
                            var gcpUploadSpanGroup = gcpUploadSpan.StartChild("GcpUploadBatch");
                            gcpUploadSpanGroup.SetExtra("Count", group.Count);

                            try
                            {
                                await Task.WhenAll(group.Select(g => UploadToGoogle(g)));
                                gcpUploadSpanGroup.Finish(SpanStatus.Ok);
                            }
                            catch (Exception e)
                            {
                                gcpUploadSpanGroup.Finish(e);
                                throw;
                            }
                        }
                        gcpUploadSpan.Finish();
                    }
                    catch (Exception e)
                    {
                        gcpUploadSpan.Finish(e);
                        _logger.LogError(e, "Failed uploading files to GCS.");
                        throw;
                    }

                    SentrySdk.CaptureMessage($"Batch {batch.BatchId} with name {batch.FriendlyName} completed in {stopwatch.Elapsed}");
                }
                catch (Exception e)
                {
                    closeBatchTransaction.Finish(e);
                    _logger.LogError(e, "Batch {batchId} with name {friendlyName} completed in {stopwatch}.",
                        batch.BatchId, batch.FriendlyName, stopwatch.Elapsed);
                    throw;
                }
                finally
                {
                    try
                    {
                        if (_options.DeleteSymsortedDirectory)
                        {
                            Directory.Delete(symsorterOutput, true);

                            _logger.LogInformation(
                                "Batch {batchId} with name {friendlyName} deleted sorted directory {symsorterOutput}.",
                                batch.BatchId, batch.FriendlyName, symsorterOutput);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed attempting to delete symsorter directory.");
                    }
                    handle.Dispose();
                }

            }, gcsUploadCancellation)
            .ContinueWith(t =>
            {
                _logger.LogInformation("Batch {batchId} with name {friendlyName} completed in {stopwatch}.",
                    batch.BatchId, batch.FriendlyName, stopwatch.Elapsed);

                if (t.IsFaulted)
                {
                    closeBatchTransaction.Finish(SpanStatus.InternalError);
                    _logger.LogError(t.Exception, "GCS upload Task failed.");
                }
                else
                {
                    closeBatchTransaction.Finish();
                }

                doneCallback();
            }, gcsUploadCancellation);

        return Task.CompletedTask;
    }

    private bool SortSymbols(string batchLocation, SymbolUploadBatch batch, string symsorterOutput, ISpan symsorterSpan)
    {
        var bundleId = _bundleIdGenerator.CreateBundleId(batch.FriendlyName);
        var symsorterPrefix = batch.BatchType.ToSymsorterPrefix();

        var args = $"--ignore-errors -zz -o {symsorterOutput} --prefix {symsorterPrefix} --bundle-id {bundleId} {batchLocation}";

        symsorterSpan.SetExtra("args", args);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo(_options.SymsorterPath, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Environment = { {"RUST_BACKTRACE","full"} }
            }
        };

        string? lastLine = null;
        var sw = Stopwatch.StartNew();
        if (!process.Start())
        {
            throw new InvalidOperationException("symsorter failed to start");
        }

        while (!process.StandardOutput.EndOfStream)
        {
            var line = process.StandardOutput.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            _logger.LogInformation(line);
            lastLine = line;
        }

        while (!process.StandardError.EndOfStream)
        {
            var line = process.StandardError.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            _logger.LogWarning(line);
            lastLine = line;
        }

        const int waitUpToMs = 500_000;
        process.WaitForExit(waitUpToMs);
        sw.Stop();
        if (!process.HasExited)
        {
            throw new InvalidOperationException($"Timed out waiting for {batch.BatchId}. Symsorter args: {args}");
        }

        lastLine ??= string.Empty;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Symsorter exit code: {process.ExitCode}. Args: {args}");
        }

        _logger.LogInformation("Symsorter finished in {timespan} and logged last: {lastLine}",
            sw.Elapsed, lastLine);

        var match = Regex.Match(lastLine, "Sorted (?<count>\\d+) debug files");
        if (!match.Success)
        {
            _logger.LogError("Last line didn't match success: {lastLine}", lastLine);
            return true;
        }

        _logger.LogInformation("Symsorter processed: {count}", match.Groups["count"].Value);
        return false;
    }
}