using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SymbolCollector.Core;

namespace SymbolCollector.Server
{
    public class SymbolUploadBatch
    {
        public Guid BatchId { get; }
        public DateTimeOffset StartTime { get; }
        public DateTimeOffset? EndTime { get; private set; }

        // Will be used as BundleId (caller doesn't need to worry about it being unique).
        public string FriendlyName { get; }

        public BatchType BatchType { get; }

        public ConcurrentDictionary<string, SymbolMetadata> Symbols { get; } =
            new ConcurrentDictionary<string, SymbolMetadata>();

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

    internal class InMemorySymbolService : ISymbolService, IDisposable
    {
        private readonly ObjectFileParser _parser;
        private readonly ISymbolGcsWriter _gcsWriter;
        private readonly SymbolServiceOptions _options;
        private readonly ILogger<InMemorySymbolService> _logger;
        private readonly Random _random = new Random();
        private readonly SuffixGenerator _generator = new SuffixGenerator();

        private readonly ConcurrentDictionary<Guid, SymbolUploadBatch> _batches =
            new ConcurrentDictionary<Guid, SymbolUploadBatch>();

        private readonly string _donePath;
        private readonly string _processingPath;
        private readonly string _symsorterPath;
        private readonly string _conflictPath;

        public InMemorySymbolService(ObjectFileParser parser, IOptions<SymbolServiceOptions> options, ISymbolGcsWriter gcsWriter, ILogger<InMemorySymbolService> logger)
        {
            _parser = parser;
            _gcsWriter = gcsWriter;
            _options = options.Value;
            _logger = logger;

            var basePath = ".";
            if (!string.IsNullOrWhiteSpace(_options.BaseWorkingPath))
            {
                if (!Directory.Exists(_options.BaseWorkingPath))
                {
                    var info = Directory.CreateDirectory(_options.BaseWorkingPath);
                    if (!info.Exists)
                    {
                        throw new InvalidOperationException("Base path configured does not exist and could not be created.");
                    }
                }

                basePath = _options.BaseWorkingPath;
            }

            _donePath = Path.Combine(basePath, "done");
            _processingPath = Path.Combine(basePath, "processing");
            _symsorterPath = Path.Combine(basePath, "symsorter_output");
            _conflictPath = Path.Combine(basePath, "conflict");
            Directory.CreateDirectory(_donePath);
            Directory.CreateDirectory(_processingPath);
            Directory.CreateDirectory(_symsorterPath);
            Directory.CreateDirectory(_conflictPath);
        }

        public Task Start(Guid batchId, string friendlyName, BatchType batchType, CancellationToken token)
        {
            if (_batches.ContainsKey(batchId))
            {
                throw new ArgumentException($"Batch Id {batchId} was already used.");
            }

            _batches[batchId] = new SymbolUploadBatch(batchId, friendlyName, batchType);
            var batchIdString = batchId.ToString();
            var processingDir = Path.Combine(_processingPath, batchIdString);
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
            var destination = Path.Combine(
                _processingPath,
                batchId.ToString(),
                // To avoid files with conflicting name from the same batch
                _random.Next().ToString(CultureInfo.InvariantCulture),
                fileName);
            var tempDestination = Path.Combine(Path.GetTempPath(), destination);
            Directory.CreateDirectory(Path.GetDirectoryName(tempDestination));

            await using (var file = File.OpenWrite(tempDestination))
            {
                await stream.CopyToAsync(file, token);
            }

            if (!_parser.TryParse(tempDestination, out var fileResult) || fileResult is null)
            {
                _logger.LogDebug("Failed parsing {file}.", Path.GetFileName(tempDestination));
                File.Delete(tempDestination);
                return StoreResult.Invalid;
            }

            _logger.LogInformation("Parsed file with {buildId}", fileResult.BuildId);
            var symbol = await GetSymbol(fileResult.BuildId, token);
            if (symbol is {})
            {
                if (fileResult.Hash is {}
                    && symbol.Hash is {}
                    && string.CompareOrdinal(fileResult.Hash, symbol.Hash) != 0)
                {
                    // TODO: Unlikely case a debugId on un-matching file hash (modified file?)
                    // TODO: Store the file for debugging, raise a Sentry event attachments
                    var conflictDestination = Path.Combine(
                        _conflictPath,
                        batchId.ToString(),
                        // To avoid files with conflicting name from the same batch
                        _random.Next().ToString(CultureInfo.InvariantCulture),
                        fileName);

                    using (_logger.BeginScope(new Dictionary<string, string>()
                    {
                        {"existing-file-hash", symbol.Hash},
                        {"existing-file-name", symbol.Name},
                        {"staging-location", conflictDestination},
                        {"new-file-hash", fileResult.Hash},
                        {"new-file-name", Path.GetFileName(fileResult.Path)}
                    }))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(conflictDestination));
                        _logger.LogError(
                            "File with the same debug id and un-matching hashes. File stored at: {path}",
                            conflictDestination);
                        File.Move(tempDestination, conflictDestination);
                    }
                }
                else
                {
                    if (symbol.BatchIds.Any(b => b == batchId))
                    {
                        _logger.LogDebug(
                            "Client uploading the same file {fileName} as part of the same batch {batchId}",
                            fileName, batchId);
                    }
                    else
                    {
                        await Relate(batchId, symbol, token);
                    }
                }

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
                new HashSet<Guid> {batchId});

            batch.Symbols[metadata.DebugId] = metadata;

            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Move(tempDestination, destination);

            _logger.LogDebug("File {fileName} created.", metadata.Name);

            return StoreResult.Created;
        }

        public async Task Relate(Guid batchId, SymbolMetadata symbolMetadata, CancellationToken token)
        {
            var batch = await GetOpenBatch(batchId, token);
            batch.Symbols[symbolMetadata.DebugId] = symbolMetadata;
            symbolMetadata.BatchIds.Add(batchId);

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

            var processingLocation = Path.Combine(_processingPath, batchId.ToString());

            var destination = Path.Combine(_donePath, batchId.ToString());
            foreach (var symbol in batch.Symbols.Values)
            {
                symbol.Path = symbol.Path.Replace(processingLocation, destination);
            }

            await using (var file = File.OpenWrite(Path.Combine(processingLocation, "metadata.json")))
            {
                await JsonSerializer.SerializeAsync(
                    file,
                    batch,
                    cancellationToken: token,
                    options: new JsonSerializerOptions {WriteIndented = true});
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            Directory.Move(processingLocation, destination);

            _logger.LogInformation("Batch {batchId} is now closed at {location}.",
                batchId, destination);

            static string ToSymsorterPrefix(BatchType type) =>
                type switch
                {
                    BatchType.WatchOS => "watchos",
                    BatchType.MacOS => "macos",
                    BatchType.IOS => "ios",
                    BatchType.Android => "android",
                    _ => throw new InvalidOperationException($"Invalid BatchType {type}."),
                };

            string ToBundleId(string friendlyName)
            {
                var invalids = Path.GetInvalidFileNameChars().Concat(" ").ToArray();
                return string.Join("_",
                        friendlyName.Split(invalids, StringSplitOptions.RemoveEmptyEntries)
                            .Append(_generator.Generate()))
                    .TrimEnd('.');
            }

            // get logger factory and create a logger for symsorter
            var process = new Process();
            var symsorterOutput = Path.Combine(_symsorterPath, batch.BatchId.ToString());

            Directory.CreateDirectory(symsorterOutput);

            var bundleId = ToBundleId(batch.FriendlyName);
            var symsorterPrefix = ToSymsorterPrefix(batch.BatchType);
            var args = $"-zz -o {symsorterOutput} --prefix {symsorterPrefix} --bundle-id {bundleId} {destination}";

            process.StartInfo = new ProcessStartInfo(_options.SymsorterPath, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
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
                _logger.LogInformation(line);
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

            var match = Regex.Match(lastLine , "Done: sorted (?<count>\\d+) debug files");
            if (!match.Success)
            {
                _logger.LogError("Last line didn't match success: {lastLine}", lastLine);
                return;
            }

            _logger.LogInformation("Symsorter processed: {count}", match.Groups["count"].Value);

            var trimDown = symsorterOutput + "/";
            foreach (var directories in Directory.GetDirectories(symsorterOutput, "*", SearchOption.AllDirectories))
            {
                foreach (var filePath in Directory.GetFiles(directories))
                {
                    var destinationName = filePath.Replace(trimDown, string.Empty);
                    await using ( var file = File.OpenRead(filePath))
                    {
                        await _gcsWriter.WriteAsync(destinationName, file, token);
                    }

                    File.Delete(filePath);
                }
            }

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
            if (batch is null)
            {
                throw new InvalidOperationException($"Batch '{batchId}' was not found.");
            }

            if (batch.IsClosed)
            {
                throw new InvalidOperationException($"Batch '{batchId}' was already closed at {batch.EndTime}.");
            }

            return batch;
        }

        private class SuffixGenerator : IDisposable
        {
            private readonly RandomNumberGenerator _randomNumberGenerator;
            private const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

            public SuffixGenerator(RandomNumberGenerator? randomNumberGenerator = null)
                => _randomNumberGenerator = randomNumberGenerator ?? new RNGCryptoServiceProvider();

            public string Generate()
            {
                var higherBound = Characters.Length;

                const int keyLength = 6;
                Span<byte> randomBuffer = stackalloc byte[4];
                var stringBaseBuffer = ArrayPool<char>.Shared.Rent(keyLength);
                try
                {
                    for (var i = 0; i < keyLength; i++)
                    {
                        _randomNumberGenerator.GetBytes(randomBuffer);
                        var generatedValue = Math.Abs(BitConverter.ToInt32(randomBuffer));
                        var index = generatedValue % higherBound;
                        stringBaseBuffer[i] = Characters[index];
                    }

                    return new string(stringBaseBuffer[..keyLength]);
                }
                finally
                {
                    ArrayPool<char>.Shared.Return(stringBaseBuffer);
                }
            }

            public void Dispose() => _randomNumberGenerator.Dispose();
        }

        public void Dispose() => _generator.Dispose();
    }
}
