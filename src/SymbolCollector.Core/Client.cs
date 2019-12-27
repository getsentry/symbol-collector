using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SymbolCollector.Core
{
    public class Client : IDisposable
    {
        private readonly ObjectFileParser _objectFileParser;
        internal int ParallelTasks { get; }
        private readonly Uri _serviceUri;
        private readonly ILogger<Client> _logger;
        private readonly HttpClient _client;
        private readonly string _userAgent;
        private readonly HashSet<string>? _blackListedPaths;

        public ClientMetrics Metrics { get; }

        public Client(
            Uri serviceUri,
            ObjectFileParser objectFileParser,
            HttpMessageHandler? handler = null,
            AssemblyName? assemblyName = null,
            int? parallelTasks = null,
            HashSet<string>? blackListedPaths = null,
            ClientMetrics? metrics = null,
            ILogger<Client>? logger = null)
        {
            _objectFileParser = objectFileParser;
            ParallelTasks = parallelTasks ?? 10;

            _blackListedPaths = blackListedPaths;
            // We only hit /image here
            _serviceUri = new Uri(serviceUri, "symbol");
            _logger = logger ?? NullLogger<Client>.Instance;
            _client = new HttpClient(handler ?? new HttpClientHandler());
            assemblyName ??= Assembly.GetEntryAssembly()?.GetName();
            _userAgent = $"{assemblyName?.Name ?? "SymbolCollector"}/{assemblyName?.Version.ToString() ?? "?.?.?"}";
            Metrics = metrics ?? new ClientMetrics();
        }

        public async Task UploadAllPathsAsync(IEnumerable<string> topLevelPaths, CancellationToken cancellationToken)
        {
            var counter = 0;
            var batches =
                from topPath in topLevelPaths
                from lookupDirectory in SafeGetDirectories(topPath)
                where _blackListedPaths?.Contains(lookupDirectory) != true
                let c = counter++
                group lookupDirectory by c / ParallelTasks
                into grp
                select grp.ToList();

            foreach (var batch in batches)
            {
                await UploadParallel(batch, cancellationToken);
                Metrics.BatchProcessed();
            }

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
                    Metrics.DirectoryUnauthorizedAccess();
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

        private async Task UploadParallel(IEnumerable<string> paths, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    tasks.Add(UploadFilesAsync(path, cancellationToken));
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

        private async Task UploadFilesAsync(string path, CancellationToken cancellationToken)
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
                            await UploadAsync(fatMachOInnerFile, cancellationToken);
                        }
                    }
                    else
                    {
                        await UploadAsync(objectFileResult, cancellationToken);
                    }
                }
                else
                {
                    _logger.LogDebug("File {file} could not be parsed.", file);
                }
            }
        }

        private async Task UploadAsync(ObjectFileResult objectFileResult, CancellationToken cancellationToken)
        {
            if (objectFileResult.BuildId is null)
            {
                _logger.LogError("Cannot upload file without debug id: {file}", objectFileResult.Path);
                return;
            }

            using var _ = _logger.BeginScope(new Dictionary<string, string>
            {
                {"debugId", objectFileResult.BuildId},
                {"file", objectFileResult.Path},
                {"User-Agent", _userAgent}
            });

            // Better would be if `ELF` class would expose its buffer so we don't need to read the file twice.
            // Ideally ELF would read headers as a stream which we could reset to 0 after reading heads
            // and ensuring it's what we need.
            using var fileStream = File.OpenRead(objectFileResult.Path);
            var postResult = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, _serviceUri)
            {
                Headers = {{"debug-id", objectFileResult.BuildId}, {"User-Agent", _userAgent}},
                Content = new MultipartFormDataContent(
                    // TODO: add a proper boundary
                    $"Upload----WebKitFormBoundary7MA4YWxkTrZu0gW--")
                {
                    {new StreamContent(fileStream), objectFileResult.Path, Path.GetFileName(objectFileResult.Path)}
                }
            }, cancellationToken);
            Metrics.UploadedBytesAdd(fileStream.Length);

            if (!postResult.IsSuccessStatusCode)
            {
                Metrics.FailedToUpload();
                var error = await postResult.Content.ReadAsStringAsync();
                _logger.LogError("{statusCode} for file {file} with body: {body}",
                    postResult.StatusCode,
                    objectFileResult.Path,
                    error);
            }
            else
            {
                Metrics.SuccessfulUpload();
                _logger.LogInformation("Sent file: {file}", objectFileResult.Path);
            }
        }

        public void Dispose() => _client.Dispose();
    }
}
