using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ELFSharp.ELF;
using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SymbolCollector.Core
{
    public class Client : IDisposable
    {
        private readonly ILogger<Client> _logger;
        private readonly SymbolCollection.SymbolCollectionClient _client;
        private readonly IDisposable _disposable;

        public Client(Uri serviceUri, ILogger<Client>? logger = null)
        {
            _logger = logger ?? NullLogger<Client>.Instance;
            var channel = GrpcChannel.ForAddress(serviceUri);
            _disposable = channel;
            _client = new SymbolCollection.SymbolCollectionClient(channel);
        }

        public async Task UploadAllPathsAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    tasks.Add(UploadFilesAsync(path, cancellationToken));
                    _logger.LogInformation("Uploading files from: {0}", path);
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

        public async Task UploadFilesAsync(string path, CancellationToken cancellationToken)
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
            using var call = _client.Uploads(cancellationToken: cancellationToken);
            for (var i = 0; i < 3; i++)
            {
                await using Stream stream = File.OpenRead(file);
                await call.RequestStream.WriteAsync(new SymbolUploadRequest
                {
                    DebugId = debugId,
                    File = await ByteString.FromStreamAsync(stream, cancellationToken)
                });
            }

            await call.RequestStream.CompleteAsync();

            _ = await call;
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

        public void Dispose() => _disposable.Dispose();
    }
}
