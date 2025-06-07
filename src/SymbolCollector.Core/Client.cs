using Microsoft.Extensions.Logging;

namespace SymbolCollector.Core;

public class Client : IDisposable
{
    private readonly ISymbolClient _symbolClient;
    private readonly ObjectFileParser _objectFileParser;
    internal int ParallelTasks { get; }
    private readonly ILogger<Client> _logger;
    private readonly HashSet<string>? _blockListedPaths;

    public ClientMetrics Metrics { get; }

    public Client(
        ISymbolClient symbolClient,
        ObjectFileParser objectFileParser,
        SymbolClientOptions options,
        ClientMetrics metrics,
        ILogger<Client> logger)
    {
        Metrics = metrics;
        _symbolClient = symbolClient;
        _objectFileParser = objectFileParser;
        _logger = logger;

        ParallelTasks = options.ParallelTasks;
        _blockListedPaths = options.BlockListedPaths;

        SentrySdk.ConfigureScope(s => s.SetExtra(nameof(Metrics), Metrics));
    }

    public async Task UploadAllPathsAsync(string friendlyName,
        BatchType type,
        IEnumerable<string> topLevelPaths,
        ISpan span,
        CancellationToken cancellationToken)
    {
        List<List<string>> groups;
        var counter = 0;
        {
            var groupsSpan = span.StartChild("group.get", "Get the group of directories to search in parallel");
            groups =
                (from topPath in topLevelPaths
                    from lookupDirectory in SafeGetDirectories(topPath)
                    where _blockListedPaths?.Contains(lookupDirectory) != true
                    let c = counter++
                    group lookupDirectory by c / ParallelTasks
                    into grp
                    select grp.ToList()).ToList();
            groupsSpan.Finish();
        }

        Guid batchId;
        {
            var startSpan = span.StartChild("batch.start");
            try
            {
                batchId = await _symbolClient.Start(friendlyName, type, cancellationToken);
                startSpan.Finish(SpanStatus.Ok);
            }
            catch (Exception e)
            {
                startSpan.Finish(e);
                throw;
            }
        }

        {
            var uploadSpan = span.StartChild("batch.upload", "concurrent batch upload");
            uploadSpan.SetData("groups", groups.Count.ToString());
            uploadSpan.SetData("total_items", counter.ToString());

            // use this as parent to all outgoing HTTP requests now:
            SentrySdk.ConfigureScope(s => s.Span = uploadSpan);
            int i = 0;
            try
            {
                foreach (var group in groups)
                {
                    if (i++ == 2) break;
                    await UploadParallel(batchId, group, cancellationToken);
                }
                uploadSpan.Finish(SpanStatus.Ok);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed processing files for {batchId}. Rethrowing and leaving the batch open.",
                    batchId);
                uploadSpan.Finish(e);
                throw;
            }
        }

        {
            var stopSpan = span.StartChild("batch.close");
            await _symbolClient.Close(batchId, cancellationToken);
            stopSpan.Finish(SpanStatus.Ok);
        }

        return;

        IEnumerable<string> SafeGetDirectories(string path)
        {
            _logger.LogDebug("Probing {path} for child directories.", path);
            yield return path;
            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.GetDirectories(path, "*");
                // can't yield return here, didn't blow up so go go
            }
            catch (UnauthorizedAccessException)
            {
                Metrics.FileOrDirectoryUnauthorizedAccess();
                yield break;
            }
            catch (DirectoryNotFoundException)
            {
                Metrics.DirectoryDoesNotExist();
                yield break;
            }

            foreach (var dir in dirs)
            foreach (var safeDir in SafeGetDirectories(dir))
            {
                yield return safeDir;
            }
        }
    }

    private async Task UploadParallel(Guid batchId, IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                tasks.Add(UploadFilesAsync(batchId, path, cancellationToken));
                Metrics.JobsInFlightAdd(1);
                // TODO: Remove me. Keeping it short to test e2e
                // return;
            }
            else
            {
                Metrics.DirectoryDoesNotExist();
                _logger.LogWarning("The path {path} doesn't exist.", path);
            }
        }

        try
        {
            if (tasks.Any())
            {
                try
                {
                    _logger.LogInformation("Awaiting {count} upload tasks to finish.", tasks.Count);
                    await Task.WhenAll(tasks);
                }
                finally
                {
                    Metrics.JobsInFlightRemove(tasks.Count);
                }
            }
            else
            {
                _logger.LogWarning("No upload process will be performed.");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Operation cancelled successfully.");
        }
    }

    private async Task UploadFilesAsync(Guid batchId, string path, CancellationToken cancellationToken)
    {
        using var _ = _logger.BeginScope(("path", path));
        IReadOnlyCollection<string> files;
        try
        {
            files = Directory.GetFiles(path);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Can't list files in {path}.", path);
            return;
        }

        _logger.LogInformation("Path {path} has {length} files to process", path, files.Count);

        var failures = 0;
        foreach (var file in files)
        {
            try
            {
                if (_objectFileParser.TryParse(file, out var objectFileResult) && objectFileResult is {})
                {
                    if (objectFileResult is FatMachOFileResult fatMachOFileResult)
                    {
                        foreach (var fatMachOInnerFile in fatMachOFileResult.InnerFiles)
                        {
                            await UploadAsync(batchId, fatMachOInnerFile, cancellationToken);
                        }
                    }
                    else
                    {
                        await UploadAsync(batchId, objectFileResult, cancellationToken);
                    }
                }
                else
                {
                    _logger.LogDebug("File {file} could not be parsed.", file);
                }
            }
            catch (Exception e)
            {
                if (++failures > 10)
                {
                    throw;
                }

                _logger.LogWarning(e, "Failed to upload. Failure count: {count}.", failures);
            }
        }
    }

    private async Task UploadAsync(Guid batchId, ObjectFileResult objectFileResult,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objectFileResult.UnifiedId))
        {
            _logger.LogError("Cannot upload file without debug id: {file}", objectFileResult.Path);
            return;
        }

        using var _ = _logger.BeginScope(new Dictionary<string, string>
        {
            {"unified-id", objectFileResult.UnifiedId}, {"file", objectFileResult.Path},
        });

        // Better would be if `ELF` class would expose its buffer so we don't need to read the file twice.
        // Ideally ELF would read headers as a stream which we could reset to 0 after reading heads
        // and ensuring it's what we need.

        try
        {
            var uploaded = await _symbolClient.Upload(
                batchId,
                objectFileResult.UnifiedId,
                objectFileResult.Hash,
                Path.GetFileName(objectFileResult.Path),
                () => File.OpenRead(objectFileResult.Path),
                cancellationToken);

            if (uploaded)
            {
                Metrics.SuccessfulUpload();
            }
            else
            {
                Metrics.AlreadyExisted();
            }
        }
        catch
        {
            Metrics.FailedToUpload();
            throw;
        }
    }

    public void Dispose() => _symbolClient.Dispose();
}
