using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ELFSharp.ELF;
using ELFSharp.ELF.Sections;
using ELFSharp.MachO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SymbolCollector.Core
{
    public class Client : IDisposable
    {
        private readonly FatBinaryReader? _fatBinaryReader;
        internal int ParallelTasks { get; }
        private readonly Uri _serviceUri;
        private readonly ILogger<Client> _logger;
        private readonly HttpClient _client;
        private readonly string _userAgent;
        private readonly HashSet<string>? _blackListedPaths;

        public Metrics CurrentMetrics { get; } = new Metrics();

        public Client(
            Uri serviceUri,
            FatBinaryReader? fatBinaryReader = null,
            HttpMessageHandler? handler = null,
            AssemblyName? assemblyName = null,
            int? parallelTasks = null,
            HashSet<string>? blackListedPaths = null,
            ILogger<Client>? logger = null)
        {
            _fatBinaryReader = fatBinaryReader;
            ParallelTasks = parallelTasks ?? 10;
            _blackListedPaths = blackListedPaths;
            // We only hit /image here
            _serviceUri = new Uri(serviceUri, "image");
            _logger = logger ?? NullLogger<Client>.Instance;
            _client = new HttpClient(handler ?? new HttpClientHandler());
            assemblyName ??= Assembly.GetEntryAssembly()?.GetName();
            _userAgent = $"{assemblyName?.Name ?? "SymbolCollector"}/{assemblyName?.Version.ToString() ?? "?.?.?"}";
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
                CurrentMetrics.BatchProcessed();
            }

            IEnumerable<string> SafeGetDirectories(string path)
            {
                _logger.LogDebug("Probing {path} for child directories.", path);
                try
                {
                    return Directory.GetDirectories(path, "*", SearchOption.AllDirectories)
                        // Include the directory itself
                        .Prepend(path);
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (DirectoryNotFoundException)
                {
                }

                return Enumerable.Empty<string>();
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
                    CurrentMetrics.JobsInFlightAdd(1);
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
                    _logger.LogInformation("Awaiting {count} upload tasks to finish.", tasks.Count);
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
            finally
            {
                CurrentMetrics.JobsInFlightRemove(tasks.Count);
            }
        }

        private async Task UploadFilesAsync(string path, CancellationToken cancellationToken)
        {
            using var _ = _logger.BeginScope(("path", path));
            var files = Directory.GetFiles(path);
            _logger.LogInformation("Path {path} has {length} files to process", path, files.Length);

            foreach (var (file, debugId) in GetFiles(files))
            {
                if (debugId == null)
                {
                    _logger.LogWarning("Skipping file {file} because no debug id was found.");
                    continue;
                }

                await UploadAsync(debugId, file, cancellationToken);
            }
        }

        private async Task UploadAsync(string debugId, string file, CancellationToken cancellationToken)
        {
            using var _ = _logger.BeginScope(new Dictionary<string, string>
            {
                {"debugId", debugId},
                {"file", file},
                {"User-Agent", _userAgent}
            });

            // Better would be if `ELF` class would expose its buffer so we don't need to read the file twice.
            // Ideally ELF would read headers as a stream which we could reset to 0 after reading heads
            // and ensuring it's what we need.
            using var fileStream = File.OpenRead(file);
            var postResult = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Put, _serviceUri)
            {
                Headers = {{"debug-id", debugId}, {"User-Agent", _userAgent}},
                Content = new MultipartFormDataContent(
                    // TODO: add a proper boundary
                    $"Upload----WebKitFormBoundary7MA4YWxkTrZu0gW--")
                {
                    {new StreamContent(fileStream), file}
                }
            }, cancellationToken);
            CurrentMetrics.UploadedBytesAdd(fileStream.Length);

            if (!postResult.IsSuccessStatusCode)
            {
                CurrentMetrics.FailedToUpload();
                _logger.LogError("{statusCode} for file {file}", postResult.StatusCode, file);
                if (postResult.Headers.TryGetValues("X-Error-Code", out var code))
                {
                    _logger.LogError("Code: {code}", code);
                }
            }
            else
            {
                CurrentMetrics.SuccessfullyUploaded();
                _logger.LogInformation("Sent file: {file}", file);
            }
        }

        // TODO: IAsyncEnumerable when ELF library supports it
        private IEnumerable<(string file, string? debugId)> GetFiles(IEnumerable<string> files)
        {
            var strategies = new List<Func<string, IEnumerable<(string, string?)>>>
            {
                s => new[]
                {
                    (s, GetElfBuildId(s))
                },
                s => new[]
                {
                    (s, GetMachOBuildId(s))
                },
                GetMachOFromFatFile
            };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Move ELF as the last strategy if running on macOS
                var elf = strategies.ElementAt(0);
                strategies.RemoveAt(0);
                strategies.Add(elf);
            }

            foreach (var file in files)
            {
                foreach (var tuple in strategies
                    .Select(strategy => strategy(file))
                    .Where(result => result != null)
                    .SelectMany(result => result))
                {
                    CurrentMetrics.FileProcessed();
                    if (tuple.Item2 == null)
                    {
                        continue;
                    }

                    yield return tuple;
                }
            }
        }

        internal IEnumerable<(string file, string? debugId)> GetMachOFromFatFile(string file)
        {
            // Check if it's a Fat Mach-O
            FatMachO? load = null;
            if (_fatBinaryReader?.TryLoad(file, out load) == true && load is { } fatMachO)
            {
                CurrentMetrics.FatMachOFileFound();
                _logger.LogInformation("Fat binary file with {count} Mach-O files: {file}.",
                    fatMachO.Header.FatArchCount, file);

                using (_logger.BeginScope(new {FatMachO = file}))
                using (fatMachO)
                {
                    foreach (var buildIdFile in GetFiles(fatMachO.MachOFiles))
                    {
                        yield return buildIdFile;
                    }
                }
            }
        }

        internal string? GetElfBuildId(string file)
        {
            IELF? elf = null;
            try
            {
                // TODO: find an async API if this is used by the server
                if (ELFReader.TryLoad(file, out elf))
                {
                    CurrentMetrics.ElfFileFound();
                    var hasUnwindingInfo = elf.TryGetSection(".eh_frame", out _);
                    var hasDwarfDebugInfo = elf.TryGetSection(".debug_frame", out _);

                    _logger.LogDebug("Contains unwinding info: {hasUnwindingInfo}", hasUnwindingInfo);
                    _logger.LogDebug("Contains DWARF debug info: {hasDwarfDebugInfo}", hasDwarfDebugInfo);

                    var hasBuildId = elf.TryGetSection(".note.gnu.build-id", out var buildId);
                    if (hasBuildId)
                    {
                        var desc = buildId switch
                        {
                            NoteSection<uint> noteUint => noteUint.Description,
                            NoteSection<ulong> noteUlong => noteUlong.Description,
                            _ => null
                        };
                        if (desc == null)
                        {
                            _logger.LogError("build-id exists but bytes (desc) are null.");
                        }
                        else
                        {
                            // TODO ns2.1: get a slice
                            desc = desc.Take(16).ToArray();
                            if (desc.Length != 16)
                            {
                                // TODO: Throw?
                                _logger.LogError("build-id exists but bytes (desc) length is unexpected {bytes}.",
                                    desc.Length);
                            }
                            else
                            {
                                return new Guid(desc).ToString();
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No Debug Id in {file}", file);
                    }
                }
                else
                {
                    _logger.LogDebug("Couldn't load': {file} with ELF reader.", file);
                }
            }
            catch (Exception e)
            {
                // You would expect TryLoad doesn't throw but that's not the case
                _logger.LogError(e, "Failed processing file {file}.", file);
            }
            finally
            {
                elf?.Dispose();
            }

            return null;
        }

        internal string? GetMachOBuildId(string file)
        {
            try
            {
                // TODO: find an async API if this is used by the server
                if (MachOReader.TryLoad(file, out var mach0) == MachOResult.OK)
                {
                    CurrentMetrics.MachOFileFound();
                    _logger.LogDebug("Mach-O found {file}", file);
                    LogTrace(mach0);

                    string? buildId = null;
                    var uuid = mach0.GetCommandsOfType<Uuid?>().FirstOrDefault();
                    if (!(uuid is null))
                    {
                        // TODO: Verify this is coming out correctly. Endianess not verified!!!
                        buildId = uuid.Id.ToString();
                    }

                    return buildId;
                }

                _logger.LogDebug("Couldn't load': {file} with mach-O reader.", file);
            }
            catch (Exception e)
            {
                // You would expect TryLoad doesn't throw but that's not the case
                // _logger.LogError(e, "Failed processing file {file}. " + e, file);
                // TODO: Print exception to console in debug
                 _logger.LogError(e, "Failed processing file {file}. ", file);
            }

            return null;
        }

        private object GetHash(string file)
        {
            using var algorithm = SHA256.Create();
            var hashingAlgo = algorithm.ComputeHash(File.ReadAllBytes(file));
            var builder = new StringBuilder();
            foreach (var b in hashingAlgo)
            {
                builder.Append(b.ToString("x2"));
            }

            var hash = builder.ToString();
            return hash;
        }

        private void LogTrace(MachO mach0)
        {
            if (!_logger.IsEnabled(LogLevel.Trace))
            {
                return;
            }

            foreach (var o in mach0.GetCommandsOfType<Command>())
            {
                switch (o)
                {
                    case Uuid uuid:
                        _logger.LogTrace("Uuid: {Uuid}", uuid.Id);
                        break;
                    case MacOsMinVersion macOsMinVersion:
                        _logger.LogTrace("MacOsMinVersion Sdk: {sdk}", macOsMinVersion.Sdk);
                        _logger.LogTrace("MacOsMinVersion Version: {version}", macOsMinVersion.Version);
                        break;
                    case IPhoneOsMinVersion iPhoneOsMinVersion:
                        _logger.LogTrace("IPhoneOsMinVersion Sdk: {sdk}", iPhoneOsMinVersion.Sdk);
                        _logger.LogTrace("IPhoneOsMinVersion Version: {version}", iPhoneOsMinVersion.Version);
                        break;
                    case Segment segment:
                        _logger.LogTrace("Segment Name: {name}", segment.Name);
                        _logger.LogTrace("Segment Address: {address}", segment.Address);
                        _logger.LogTrace("Segment Size: {size}", segment.Size);
                        _logger.LogTrace("Segment InitialProtection: {initialProtection}",
                            segment.InitialProtection);
                        _logger.LogTrace("Segment MaximalProtection: {maximalProtection}",
                            segment.MaximalProtection);
                        foreach (var section in segment.Sections)
                        {
                            _logger.LogTrace("Section Name: {name}", section.Name);
                            _logger.LogTrace("Section Address: {address}", section.Address);
                            _logger.LogTrace("Section Size: {size}", section.Size);
                            _logger.LogTrace("Section AlignExponent: {alignExponent}",
                                section.AlignExponent);
                        }

                        break;
                    case EntryPoint entryPoint:
                        _logger.LogTrace("EntryPoint Value: {entryPoint}", entryPoint.Value);
                        _logger.LogTrace("StackSize Value: {stackSize}", entryPoint.StackSize);
                        break;
                    case SymbolTable symbolTable:
                        _logger.LogTrace("Symbol table:");
                        foreach (var symbol in symbolTable.Symbols)
                        {
                            _logger.LogTrace("Symbol Name: {name}", symbol.Name);
                            _logger.LogTrace("Symbol Value: {value}", symbol.Value);
                        }

                        break;
                }
            }
        }

        public class Metrics
        {
            private DateTimeOffset StartedTime { get; } = DateTimeOffset.Now;
            private long _filesProcessed;
            private long _batchesProcessed;
            private long _jobsInFlight;
            private long _failedToUploadCount;
            private long _successfullyUploadCount;
            private long _machOFileFound;
            private long _elfFileFound;
            private long _fatMockOFileFound;
            private long _uploadedBytes;
            public void FileProcessed() => Interlocked.Increment(ref _filesProcessed);
            public void BatchProcessed() => Interlocked.Increment(ref _batchesProcessed);
            public void MachOFileFound() => Interlocked.Increment(ref _machOFileFound);
            public void ElfFileFound() => Interlocked.Increment(ref _elfFileFound);
            public void FatMachOFileFound() => Interlocked.Increment(ref _fatMockOFileFound);
            public void FailedToUpload() => Interlocked.Increment(ref _failedToUploadCount);
            public void SuccessfullyUploaded() => Interlocked.Increment(ref _successfullyUploadCount);
            public void JobsInFlightRemove(int tasksCount) => Interlocked.Add(ref _jobsInFlight, -tasksCount);
            public void JobsInFlightAdd(int tasksCount) => Interlocked.Add(ref _jobsInFlight, tasksCount);
            public void UploadedBytesAdd(long bytes) => Interlocked.Add(ref _uploadedBytes, bytes);

            public void Print(TextWriter writer)
            {
                writer.WriteLine();
                writer.Write("Started at:\t\t\t\t");
                writer.WriteLine(StartedTime);
                writer.Write("Ran for:\t\t\t\t");
                writer.WriteLine(DateTimeOffset.Now - StartedTime);
                writer.Write("FileProcessed:\t\t\t\t");
                writer.WriteLine(_filesProcessed);
                writer.Write("Batches completed:\t\t\t");
                writer.WriteLine(_batchesProcessed);
                writer.Write("Job in flight:\t\t\t\t");
                writer.WriteLine(_jobsInFlight);
                writer.Write("Failed to upload:\t\t\t");
                writer.WriteLine(_failedToUploadCount);
                writer.Write("Successfully uploaded:\t\t\t");
                writer.WriteLine(_successfullyUploadCount);
                writer.Write("Uploaded bytes:\t\t\t\t");
                writer.WriteLine(_uploadedBytes);
                writer.Write("ELF files loaded:\t\t\t");
                writer.WriteLine(_elfFileFound);
                writer.Write("Mach-O files loaded:\t\t\t");
                writer.WriteLine(_machOFileFound);
                writer.Write("Fat Mach-O files loaded:\t\t");
                writer.WriteLine(_fatMockOFileFound);
            }
        }

        public void Dispose() => _client.Dispose();
    }
}
