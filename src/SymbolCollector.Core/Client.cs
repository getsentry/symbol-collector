using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ELFSharp.ELF;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SymbolCollector.Core
{
    public class Client : IDisposable
    {
        private readonly Uri _serviceUri;
        private readonly ILogger<Client> _logger;
        private readonly HttpClient _client;

        public Client(
            Uri serviceUri,
            HttpMessageHandler? handler = null,
            ILogger<Client>? logger = null)
        {
            _serviceUri = serviceUri;
            _logger = logger ?? NullLogger<Client>.Instance;
            _client = new HttpClient(handler ?? new HttpClientHandler());
        }

        public async Task UploadAllPathsAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    tasks.Add(UploadFilesAsync(path, cancellationToken));
                    _logger.LogInformation("Uploading files from: {path}", path);
                }
                else
                {
                    _logger.LogWarning("The path {path} doesn't exist.", path);
                }
            }

            try
            {
                if (tasks.Any())
                {
                    _logger.LogWarning("Awaiting {count} upload tasks to finish.", tasks.Count);
                    await Task.WhenAll(tasks);
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
            var files = Directory.GetFiles(path);
            _logger.LogInformation("Path {path} has {length} files to process", path, files.Length);

            foreach (var (debugId, file) in GetFiles(files))
            {
                await UploadAsync(debugId, file, cancellationToken);
            }
        }

        private async Task UploadAsync(string debugId, string file, CancellationToken cancellationToken)
        {
            // Better would be if `ELF` class would expose its buffer so we don't need to read the file twice.
            // Ideally ELF would read headers as a stream which we could reset to 0 after reading heads
            // and ensuring it's what we need.
            using var fileStream = File.OpenRead(file);
            var postResult = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, _serviceUri)
            {
                Headers = {{"debug-id", debugId}},
                Content = new MultipartFormDataContent(
                    // TODO: add a proper boundary
                    $"Upload----WebKitFormBoundary7MA4YWxkTrZu0gW--")
                {
                    {new StreamContent(fileStream), file}
                }
            }, cancellationToken);

            if (!postResult.IsSuccessStatusCode)
            {
                _logger.LogError("{statusCode} for file {file}", postResult.StatusCode, file);
                if (postResult.Headers.TryGetValues("X-Error-Code", out var code))
                {
                    _logger.LogError("Code: {code}", code);
                }
            }
            else
            {
                _logger.LogInformation("Sent file: {file}", file);
            }
        }

        // TODO: IAsyncEnumerable when ELF library supports it
        private IEnumerable<(string debugId, string file)> GetFiles(IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                _logger.LogInformation("Processing file: {file}.", file);
                IELF? elf = null;
                string? buildIdHex;
                try
                {
                    // TODO: find an async API
                    if (!ELFReader.TryLoad(file, out elf))
                    {
                        _logger.LogWarning("Couldn't load': {file} with ELF reader.", file);
                        continue;
                    }

                    var hasBuildId = elf.TryGetSection(".note.gnu.build-id", out var buildId);
                    if (!hasBuildId)
                    {
                        _logger.LogWarning("No Debug Id in {file}", file);
                        continue;
                    }

                    var hasUnwindingInfo = elf.TryGetSection(".eh_frame", out _);
                    var hasDwarfDebugInfo = elf.TryGetSection(".debug_frame", out _);

                    if (!hasUnwindingInfo && !hasDwarfDebugInfo)
                    {
                        _logger.LogWarning("No unwind nor DWARF debug info in {file}", file);
                        continue;
                    }

                    _logger.LogInformation("Contains unwinding info: {hasUnwindingInfo}", hasUnwindingInfo);
                    _logger.LogInformation("Contains DWARF debug info: {hasDwarfDebugInfo}", hasDwarfDebugInfo);

                    var builder = new StringBuilder();
                    var bytes = buildId.GetContents().Skip(16);

                    foreach (var @byte in bytes)
                    {
                        builder.Append(@byte.ToString("x2"));
                    }

                    buildIdHex = builder.ToString();
                }
                catch (Exception e)
                {
                    // You would expect TryLoad doesn't throw but that's not the case
                    _logger.LogError(e, "Failed processing file {file}.", file);
                    continue;
                }
                finally
                {
                    elf?.Dispose();
                }

                yield return (buildIdHex, file);
            }
        }

        public void Dispose() => _client.Dispose();
    }
}
