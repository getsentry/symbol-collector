using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SymbolCollector.Core
{
    public class SymsorterOptions
    {
        public bool WriteIndented { get; set; }
        public bool PrintToStdOut { get; set; } = true;
    }

    public struct SymsorterParameters
    {
        public string Output { get; }
        public string Prefix { get; }
        public string BundleId { get; }

        public bool DryRun { get; }

        public SymsorterParameters(
            string output,
            string prefix,
            string bundleId,
            bool dryRun)
        {
            Output = output;
            Prefix = prefix;
            BundleId = bundleId;
            DryRun = dryRun;
        }
    }

    public class Symsorter
    {
        private readonly SymsorterOptions _options;
        private readonly ObjectFileParser _objectFileParser;
        private readonly ILogger<Symsorter> _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        private static readonly byte[] _refsFileContent = new byte[0];

        public Symsorter(
            IOptions<SymsorterOptions> options,
            ObjectFileParser objectFileParser,
            ILogger<Symsorter> logger)
        {
            _options = options.Value;
            _objectFileParser = objectFileParser;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions { WriteIndented = _options.WriteIndented };
        }

        public async Task ProcessBundle(SymsorterParameters parameters, string target, CancellationToken token)
        {
            var sortedFilesCount = 0;
            foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
            {
                if (_objectFileParser.TryParse(file, out var result) && result is {})
                {
                    if (result is FatMachOFileResult fatMachOFileResult)
                    {
                        foreach (var innerFile in fatMachOFileResult.InnerFiles)
                        {
                            await SortFile(parameters, innerFile, token);
                            sortedFilesCount++;
                        }
                    }
                    else
                    {
                        await SortFile(parameters, result, token);
                        sortedFilesCount++;
                    }
                }
            }

            if (_options.PrintToStdOut)
            {
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("\nDone: sorted ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(sortedFilesCount);
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(" debug files");
                Console.ForegroundColor = originalColor;
            }
        }
        public async Task SortFile(
            SymsorterParameters parameters,
            ObjectFileResult result,
            CancellationToken token)
        {
            Validate(result);

            var directoryRoot = Path.Combine(parameters.Output, result.UnifiedId[..2], result.UnifiedId[2..]);
            var destinationObjectFile = Path.Combine(directoryRoot, result.ObjectKind.ToSymsorterFileName());
            var directoryRefs = Path.Combine(directoryRoot, "refs");
            _ = Directory.CreateDirectory(directoryRefs);

            _logger.LogDebug("Sorting {file} to {destinationFilePath}",
                result, destinationObjectFile);

            var metaFile = Path.Combine(directoryRoot, "meta");
            await using var meta = File.OpenWrite(metaFile);
            var metaContent = new
            {
                name = Path.GetFileName(result.Path),
                arch = result.Architecture.ToSymsorterArchitecture(),
                file_format = result.FileFormat.ToSymsorterFileFormat()
            };

            var metaFileJsonTask = JsonSerializer.SerializeAsync(
                meta,
                metaContent, _jsonOptions, token);

            var refsFile = Path.Combine(directoryRefs, parameters.BundleId);
            var refsFileTask = File.WriteAllBytesAsync(refsFile, _refsFileContent, token);

            if (!parameters.DryRun)
            {
                await using var objectFileOutput = File.OpenWrite(destinationObjectFile);
                await using var objectFileInput = File.OpenRead(result.Path!);

                // TODO: zlib content
                var objectFileTask = objectFileInput.CopyToAsync(objectFileOutput, token);

                await Task.WhenAll(objectFileTask, metaFileJsonTask, refsFileTask);
            }

            if (_options.PrintToStdOut)
            {
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"{metaContent.name} ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("(");
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write(metaContent.arch);
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(") -> ");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine(destinationObjectFile);
                Console.ForegroundColor = originalColor;
            }
        }

        private static void Validate(ObjectFileResult objectFileResult)
        {
            if (string.IsNullOrWhiteSpace(objectFileResult.UnifiedId))
            {
                throw new ArgumentException("A unified id is required for symbol sorting.", nameof(objectFileResult));
            }

            if (objectFileResult.UnifiedId.Contains("-"))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(objectFileResult),
                    objectFileResult.UnifiedId,
                    "A unified id can't contain dashes required for symbol sorting.");
            }

            if (objectFileResult.UnifiedId.Length < 16) // TODO is 16 the absolute min?
            {
                throw new ArgumentOutOfRangeException(
                    nameof(objectFileResult),
                    objectFileResult.UnifiedId.Length,
                    "A valid unified id is require to be at least 16 characters long.");
            }

            if (string.IsNullOrWhiteSpace(objectFileResult.Path))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(objectFileResult),
                    objectFileResult.Path,
                    "No file path for the object file was provided.");
            }
        }
    }
}
