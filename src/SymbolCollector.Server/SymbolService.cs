using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SymbolCollector.Core;

namespace SymbolCollector.Server
{
    // prefix to final structure: ios, watchos, macos, android
    public enum BatchType
    {
        Unknown,

        // watchos
        WatchOS,

        // macos
        MacOS,

        // ios
        IOS,

        // android (doesn't exist yet)
        Android
    }

    public class SymbolUploadBatch
    {
        public Guid BatchId { get; }
        public DateTimeOffset StartTime { get; }
        public DateTimeOffset? EndTime { get; private set; }

        // Will be used as BundleId (caller doesn't need to worry about it being unique).
        public string FriendlyName { get; }

        public BatchType BatchType { get; }

        public Dictionary<string, SymbolMetadata> Symbols { get; } = new Dictionary<string, SymbolMetadata>();

        public IClientMetrics? ClientMetrics { get; set; }

        public bool IsClosed => EndTime.HasValue;

        public SymbolUploadBatch(Guid batchId, string friendlyName, BatchType batchType)
        {
            if (batchId == default)
            {
                throw new ArgumentException("Empty Batch Id.");
            }

            if (string.IsNullOrWhiteSpace(friendlyName))
            {
                throw new ArgumentException("Friendly name is required.");
            }

            if (batchType == BatchType.Unknown)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(batchType),
                    batchType,
                    "A batch type is required.");
            }

            BatchId = batchId;
            FriendlyName = friendlyName;
            BatchType = batchType;
            StartTime = DateTimeOffset.UtcNow;
        }

        public void Close()
        {
            if (EndTime.HasValue)
            {
                throw new InvalidOperationException(
                    $"Can't close batch '{BatchId}'. It was already closed at {EndTime}.");
            }

            EndTime = DateTimeOffset.UtcNow;
        }
    }

    // https://github.com/getsentry/symbolicator/blob/cd545b3bdbb7c3a0869de20c387740baced2be5c/symsorter/src/app.rs
    public class SymbolMetadata
    {
        public string DebugId { get; set; }
        public string? Hash { get; set; }
        public string Path { get; set; }

        // Symsorter uses this to name the file
        public ObjectFileType ObjectFileType { get; set; }

        // /refs
        // name=
        public string Name { get; set; }

        // arch= arm, arm64, x86, x86_64
        public Architecture Arch { get; set; }

        // file_format= elf, macho
        public FileFormat FileFormat { get; set; }

        public HashSet<Guid> BatchIds { get; }

        public SymbolMetadata(
            string debugId,
            string? hash,
            string path,
            ObjectFileType objectFileType,
            string name,
            Architecture arch,
            FileFormat fileFormat,
            HashSet<Guid> batchIds)
        {
            DebugId = debugId;
            Hash = hash;
            Path = path;
            ObjectFileType = objectFileType;
            Name = name;
            Arch = arch;
            FileFormat = fileFormat;
            BatchIds = batchIds;
        }
    }

    public interface ISymbolService
    {
        Task Start(Guid batchId, string friendlyName, BatchType batchType, CancellationToken token);
        Task<SymbolUploadBatch?> GetBatch(Guid batchId, CancellationToken token);
        Task<SymbolMetadata?> GetSymbol(string debugId, CancellationToken token);
        Task Relate(Guid batchId, SymbolMetadata symbolMetadata, CancellationToken token);
        Task Finish(Guid batchId, IClientMetrics? clientMetrics, CancellationToken token);
        Task<StoreResult> Store(Guid batchId, string fileName, Stream stream, CancellationToken token);
    }

    public enum StoreResult
    {
        Invalid,
        Created,
        AlreadyExisted
    }

    internal class InMemorySymbolService : ISymbolService
    {
        private readonly ObjectFileParser _parser;
        private readonly ILogger<InMemorySymbolService> _logger;
        private readonly Dictionary<Guid, SymbolUploadBatch> _batches = new Dictionary<Guid, SymbolUploadBatch>();

        public InMemorySymbolService(ObjectFileParser parser, ILogger<InMemorySymbolService> logger)
        {
            _parser = parser;
            _logger = logger;
            Directory.CreateDirectory("done");
        }

        public Task Start(Guid batchId, string friendlyName, BatchType batchType, CancellationToken token)
        {
            if (_batches.TryGetValue(batchId, out _))
            {
                throw new ArgumentException($"Batch Id {batchId} was already used.");
            }

            _batches[batchId] = new SymbolUploadBatch(batchId, friendlyName, batchType);
            var batchIdString = batchId.ToString();
            var processingDir = Path.Combine(Directory.GetCurrentDirectory(), "processing", batchIdString);
            Directory.CreateDirectory(processingDir);

            _logger.LogInformation("Started batch {batchId} with friendly name {friendlyName} and type {batchType}",
                batchIdString, friendlyName, batchType);

            return Task.CompletedTask;
        }

        public Task<SymbolMetadata?> GetSymbol(string debugId, CancellationToken token)
        {
            var symbol =
                _batches.Values.SelectMany(b => b.Symbols)
                    .Select(s => s.Value)
                    .FirstOrDefault(s => s.DebugId == debugId);

            return Task.FromResult((SymbolMetadata?)symbol);
        }

        public async Task<StoreResult> Store(Guid batchId, string fileName, Stream stream, CancellationToken token)
        {
            var batch = await GetOpenBatch(batchId, token);

            // TODO: Until parser supports Stream instead of file path, we write the file to TMP before we can validate it.
            var destination = Path.Combine("processing", batchId.ToString(), fileName);
            var tempDestination = Path.Combine(Path.GetTempPath(), destination);
            Directory.CreateDirectory(Path.GetDirectoryName(tempDestination));

            await using (var file = File.OpenWrite(tempDestination))
            {
                await stream.CopyToAsync(file, token);
            }

            if (!_parser.TryParse(tempDestination, out var fileResult) || fileResult is null)
            {
                File.Delete(tempDestination);
                return StoreResult.Invalid;
            }

            var symbol = await GetSymbol(fileResult.BuildId, token);
            if (symbol is {})
            {
                if (fileResult.Hash is {} && fileResult.Hash == symbol.Hash)
                {
                    if (symbol.BatchIds.Any(b => b == batchId))
                    {
                        _logger.LogWarning(
                            "Client uploading the same file {fileName} as part of the same batch {batchId}",
                            fileName, batchId);
                    }
                    else
                    {
                        await Relate(batchId, symbol, token);
                    }

                } // else
                // TODO: Unlikely case a debugId on un-matching file hash (modified file?)
                // TODO: Store the file for debugging, raise a Sentry event (attachments?)

                _logger.LogDebug("Symbol {debugId} already exists.", symbol.DebugId);

                return StoreResult.AlreadyExisted;
            }

            var metadata = new SymbolMetadata(
                fileResult.BuildId,
                fileResult.Hash,
                destination,
                fileResult.ObjectFileType,
                fileName,
                fileResult.Architecture,
                fileResult.FileFormat,
                new HashSet<Guid> { batchId });

            batch.Symbols[metadata.DebugId] = metadata;

            File.Move(tempDestination, destination);

            _logger.LogDebug("File {fileName} created.", metadata.Name);

            return StoreResult.Created;
        }

        public async Task Relate(Guid batchId, SymbolMetadata symbolMetadata, CancellationToken token)
        {
            var batch = await GetOpenBatch(batchId, token);
            batch.Symbols[symbolMetadata.DebugId] = symbolMetadata;
            _logger.LogDebug("Symbol {debugId} is now related to batch {batchId}.",
                symbolMetadata.DebugId, batchId);
        }

        public Task<SymbolUploadBatch?> GetBatch(Guid batchId, CancellationToken token) =>
            _batches.TryGetValue(batchId, out var batch)
                ? Task.FromResult<SymbolUploadBatch?>(batch)
                : Task.FromResult<SymbolUploadBatch?>(null);

        public async Task Finish(Guid batchId, IClientMetrics? clientMetrics, CancellationToken token)
        {
            var batch = await GetOpenBatch(batchId, token);

            // TODO: Validate client metrics against data collected (recon)
            batch.ClientMetrics = clientMetrics;
            batch.Close();

            var processingLocation = Path.Combine("processing", batchId.ToString());

            await using (var file = File.OpenWrite(Path.Combine(processingLocation, "metadata.json")))
            {
                await JsonSerializer.SerializeAsync(
                    file,
                    batch,
                    cancellationToken: token,
                    options: new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
            }

            var destination = Path.Combine("done", batchId.ToString());

            Directory.Move(processingLocation,destination);

            _logger.LogInformation("Batch {batchId} is now closed at {location}.",
                batchId, destination);

            // TODO: could write file:
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
        }

        private async Task<SymbolUploadBatch> GetOpenBatch(Guid batchId, CancellationToken token)
        {
            var batch = await GetBatch(batchId, token);
            if (batch == null)
            {
                throw new InvalidOperationException($"Batch '{batchId}' was not found.");
            }

            if (batch.IsClosed)
            {
                throw new InvalidOperationException($"Batch '{batchId}' was already closed at {batch.EndTime}.");
            }

            return batch;
        }
    }
}
