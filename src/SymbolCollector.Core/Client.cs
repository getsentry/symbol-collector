using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sentry;

namespace SymbolCollector.Core
{
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
        }

        public async Task UploadAllPathsAsync(
            string friendlyName,
            BatchType type,
            IEnumerable<string> topLevelPaths,
            CancellationToken cancellationToken)
        {
            var groupsSpan = SentrySdk.GetSpan()?.StartChild("group.get", "Get the group of directories to search in parallel");
            var counter = 0;
            var groups =
                (from topPath in topLevelPaths
                from lookupDirectory in SafeGetDirectories(topPath)
                where _blockListedPaths?.Contains(lookupDirectory) != true
                let c = counter++
                group lookupDirectory by c / ParallelTasks
                into grp
                select grp.ToList()).ToList();
            groupsSpan?.Finish();

            var startSpan = SentrySdk.GetSpan()?.StartChild("batch.start");
            Guid batchId;
            try
            {
                batchId = await _symbolClient.Start(friendlyName, type, cancellationToken);
                startSpan?.Finish(SpanStatus.Ok);
            }
            catch (Exception e)
            {
                startSpan?.Finish(e);
                throw;
            }

            var uploadSpan = SentrySdk.GetSpan()?.StartChild("batch.upload");
            uploadSpan?.SetTag("groups", groups.Count.ToString());
            uploadSpan?.SetTag("total_items", counter.ToString());
            try
            {
                foreach (var group in groups)
                {
                    await UploadParallel(batchId, group, cancellationToken);
                }
                uploadSpan?.Finish(SpanStatus.Ok);
            }
            catch (Exception e)
            {
                uploadSpan?.Finish(e);
                _logger.LogError(e, "Failed processing files for {batchId}. Rethrowing and leaving the batch open.",
                    batchId);
                throw;
            }

            var stopSpan = SentrySdk.GetSpan()?.StartChild("batch.close");
            await _symbolClient.Close(batchId, Metrics, cancellationToken);
            stopSpan?.Finish(SpanStatus.Ok);

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
                    _logger.LogInformation("Uploading files from: {path}", path);
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

            foreach (var file in files)
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
            catch (Exception e)
            {
                Metrics.FailedToUpload();
                _logger.LogError(e, "Failed to upload.");
                throw;
            }
        }

        public void Dispose() => _symbolClient.Dispose();
    }

}
