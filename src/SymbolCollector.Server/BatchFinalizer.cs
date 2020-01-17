using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SymbolCollector.Core;
using SymbolCollector.Server.Models;

namespace SymbolCollector.Server
{
    public interface IBatchFinalizer
    {
        public Task CloseBatch(
            string batchLocation,
            SymbolUploadBatch batch,
            CancellationToken token);
    }

    // TODO: replace symsorter shell out BatchFinalizer with:
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
    public class SymsorterBatchFinalizer : IBatchFinalizer, IDisposable
    {
        private readonly SuffixGenerator _generator;
        private readonly SymbolServiceOptions _options;
        private readonly IMetricsPublisher _metrics;
        private readonly ILogger<SymsorterBatchFinalizer> _logger;
        private readonly ISymbolGcsWriter _gcsWriter;
        private readonly SuffixGenerator _suffixGenerator;
        private readonly string _symsorterOutputPath;

        public SymsorterBatchFinalizer(
            IMetricsPublisher metrics,
            IOptions<SymbolServiceOptions> options,
            ISymbolGcsWriter gcsWriter,
            SuffixGenerator suffixGenerator,
            SuffixGenerator generator,
            ILogger<SymsorterBatchFinalizer> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _metrics = metrics;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _generator = generator;
            _gcsWriter = gcsWriter ?? throw new ArgumentNullException(nameof(gcsWriter));
            _suffixGenerator = suffixGenerator ?? throw new ArgumentNullException(nameof(suffixGenerator));

            if (!File.Exists(_options.SymsorterPath))
            {
                throw new ArgumentException($"Symsorter not found at: {_options.SymsorterPath}");
            }

            _symsorterOutputPath = Path.Combine(_options.BaseWorkingPath, "symsorter_output");
            Directory.CreateDirectory(_symsorterOutputPath);
        }

         public async Task CloseBatch(
            string batchLocation,
            SymbolUploadBatch batch,
            CancellationToken token)
        {
            // get logger factory and create a logger for symsorter
            var symsorterOutput = Path.Combine(_symsorterOutputPath, batch.BatchId.ToString());

            Directory.CreateDirectory(symsorterOutput);

            if (SorterSymbols(batchLocation, batch, symsorterOutput))
            {
                return;
            }

            // TODO: Turn into a job.
            var trimDown = symsorterOutput + "/";
            async Task UploadToGoogle(string filePath, CancellationToken token)
            {
                var destinationName = filePath.Replace(trimDown, string.Empty);
                await using var file = File.OpenRead(filePath);
                await _gcsWriter.WriteAsync(destinationName, file, token);
            }

            var counter = 0;
            var groups =
                from directory in Directory.GetDirectories(symsorterOutput, "*", SearchOption.AllDirectories)
                from file in Directory.GetFiles(directory)
                let c = counter++
                group file by c / 20 // TODO: config
                into fileGroup
                select fileGroup.ToList();

            try
            {
                foreach (var group in groups)
                {
                    await Task.WhenAll(group.Select(file => UploadToGoogle(file, token)));
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed uploading files to GCS.");
                throw;
            }
        }

         private bool SorterSymbols(string batchLocation, SymbolUploadBatch batch, string symsorterOutput)
         {
             var bundleId = ToBundleId(batch.FriendlyName);
             var symsorterPrefix = batch.BatchType.ToSymsorterPrefix();

             var args = $"-zz -o {symsorterOutput} --prefix {symsorterPrefix} --bundle-id {bundleId} {batchLocation}";

             var process = new Process
             {
                 StartInfo = new ProcessStartInfo(_options.SymsorterPath, args)
                 {
                     UseShellExecute = false,
                     RedirectStandardOutput = true,
                     CreateNoWindow = true
                 }
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

             var match = Regex.Match(lastLine, "Done: sorted (?<count>\\d+) debug files");
             if (!match.Success)
             {
                 _logger.LogError("Last line didn't match success: {lastLine}", lastLine);
                 return true;
             }

             _logger.LogInformation("Symsorter processed: {count}", match.Groups["count"].Value);
             return false;

             string ToBundleId(string friendlyName)
             {
                 var invalids = Path.GetInvalidFileNameChars().Concat(" ").ToArray();
                 return string.Join("_",
                         friendlyName.Split(invalids, StringSplitOptions.RemoveEmptyEntries)
                             .Append(_generator.Generate()))
                     .TrimEnd('.');
             }
         }

         public void Dispose() => _suffixGenerator.Dispose();
    }
}
