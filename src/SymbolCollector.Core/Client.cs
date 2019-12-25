using System;
using System.Buffers;
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
using LibObjectFile.Elf;
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

        public ClientMetrics CurrentMetrics { get; } = new ClientMetrics();

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
                yield return path;
                IEnumerable<string> dirs;
                try
                {
                    dirs = Directory.GetDirectories(path, "*");
                    // can't yield return here, didn't blow up so go go
                }
                catch (UnauthorizedAccessException)
                {
                    CurrentMetrics.DirectoryUnauthorizedAccess();
                    yield break;
                }
                catch (DirectoryNotFoundException)
                {
                    CurrentMetrics.DirectoryDoesNotExist();
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
                    CurrentMetrics.JobsInFlightAdd(1);
                    _logger.LogInformation("Uploading files from: {path}", path);
                }
                else
                {
                    CurrentMetrics.DirectoryDoesNotExist();
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
                        CurrentMetrics.JobsInFlightRemove(tasks.Count);
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

            foreach (var (file, debugId) in GetFiles(files))
            {
                await UploadAsync(debugId, file, cancellationToken);
            }
        }

        private async Task UploadAsync(string debugId, string file, CancellationToken cancellationToken)
        {
            using var _ = _logger.BeginScope(new Dictionary<string, string>
            {
                {"debugId", debugId}, {"file", file}, {"User-Agent", _userAgent}
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
                    {new StreamContent(fileStream), file, Path.GetFileName(file)}
                }
            }, cancellationToken);
            CurrentMetrics.UploadedBytesAdd(fileStream.Length);

            if (!postResult.IsSuccessStatusCode)
            {
                CurrentMetrics.FailedToUpload();
                var error = await postResult.Content.ReadAsStringAsync();
                _logger.LogError("{statusCode} for file {file} with body: {body}", postResult.StatusCode, file, error);
            }
            else
            {
                CurrentMetrics.SuccessfulUpload();
                _logger.LogInformation("Sent file: {file}", file);
            }
        }

        // TODO: IAsyncEnumerable when ELF library supports it
        private IEnumerable<(string file, string debugId)> GetFiles(IEnumerable<string> files)
        {
            var strategies = new List<Func<string, IEnumerable<(string, string?)>>>
            {
                s => new[] {(s, GetElfBuildId2(s))}, s => new[] {(s, GetMachOBuildId(s))}, GetMachOFromFatFile
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

        internal string? GetElfBuildId2(string file)
        {
            Stream inStream;
            try
            {
                inStream = File.OpenRead(file);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed opening file {file}.", file);
                return null;
            }

            try
            {
                if (ElfObjectFile.TryRead(inStream, out var elf, out var diagnosticBag))
                {
                    CurrentMetrics.ElfFileFound();
                    var hasUnwindingInfo = elf.Sections.Any(s => s.Name == ".eh_frame");
                    var hasDwarfDebugInfo = elf.Sections.Any(s => s.Name == ".debug_frame");

                    _logger.LogDebug("Contains unwinding info: {hasUnwindingInfo}", hasUnwindingInfo);
                    _logger.LogDebug("Contains DWARF debug info: {hasDwarfDebugInfo}", hasDwarfDebugInfo);

                    if (elf.Sections.FirstOrDefault(s => s.Name == ".note.gnu.build-id") is ElfNoteTable note
                        && note.Entries.FirstOrDefault() is ElfGnuNoteBuildId theThing)
                    {
                        var buildId = ToGuid(theThing.BuildId).ToString();
                        var oldBuildId = GetElfBuildId(file);
                        if (buildId != oldBuildId)
                        {
                            throw new Exception($"Doesn't match {buildId} - {oldBuildId}");
                        }

                        return buildId;
                    }
                    else
                    {
                        // No debug id so fallback to symbolic/breakpad strategy of:
                        // 'hashing the first page of the ".text" (program code) section'.
                        // https://github.com/getsentry/symbolic/blob/f928869b64f43112ec70ecd87aa24441ebc899e6/debuginfo/src/elf.rs#L100
                        if (elf.Sections.FirstOrDefault(s => s.Name == ".text") is ElfBinarySection textSection)
                        {
                            Console.WriteLine(textSection);
                            if (textSection.Size == 0)
                            {
                                _logger.LogWarning(".text section is 0 bytes long on file {file}", file);
                                return null;
                            }
                            var length = (int)Math.Min(4096, textSection.Size);
                            var buffer = ArrayPool<byte>.Shared.Rent(length);
                            try
                            {
                                var read = textSection.Stream.Read(buffer, 0, length);
                                if (read == length)
                                {
                                    var UUID_SIZE = 16;
                                    var hash = new byte[UUID_SIZE];
                                    for (var i = 0; i < length; i++)
                                    {
                                        hash[i % UUID_SIZE] ^= buffer[i];
                                    }
                                    var hashId = new Guid(hash).ToString();
                                    return hashId;
                                }
                                else
                                {
                                    _logger.LogError("LibObjectFile claimed section is {libSize} but stream read only {streamRead} bytes from {file}",
                                        textSection.Size, read, file);
                                }
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(buffer);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("File has no .text section: {file}", file);
                        }
                    }
                }
                else
                {
                    _logger.LogDebug("Couldn't load': {file} with ELF reader.", file);
                    _logger.LogTrace("File {file} diagnostics: {diagnostic}.", file, diagnosticBag);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "LibObjectFile shouldn't throw. File: {file}", file);
            }

            var oldBuildId2 = GetElfBuildId(file);
            if (!(oldBuildId2 is null))
            {
                _logger.LogError("LibObjectFile returned null but ELFSharp didn't. File: {file}", file);
            }

            return null;
        }

        // TODO: To internal Extension meehod
        private Guid ToGuid(Stream stream)
        {
            Span<byte> bytes = stackalloc byte[16];
            stream.Read(bytes);
            return new Guid(bytes);
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
                _logger.LogError(e, "Failed processing file {file}.", file);
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

        public void Dispose() => _client.Dispose();
    }
}
