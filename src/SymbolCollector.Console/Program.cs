using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sentry;
using SymbolCollector.Core;
using static System.Console;

namespace SymbolCollector.Console
{
    internal class Program
    {
        private const string Dsn = "https://02619ad38bcb40d0be5167e1fb335954@sentry.io/1847454";
        private const string SymbolCollectorServiceUrl = "http://sentry.garcia.in/";

        private static readonly ClientMetrics _metrics = new ClientMetrics();

        private static async Task UploadSymbols(Uri endpoint, BatchType type, string bundleId)
        {
            SentrySdk.ConfigureScope(s =>
            {
                s.AddEventProcessor(@event =>
                {
                    var uploadMetrics = new Dictionary<string, object>();
                    @event.Contexts["metrics"] = uploadMetrics;
                    _metrics.Write(uploadMetrics);
                    return @event;
                });
            });

            // TODO: Get the paths via parameter or config file/env var?
            var paths = new List<string> {"/usr/lib/", "/usr/local/lib/"};
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // TODO: Add per OS paths
                paths.Add("/System/Library/Frameworks/");
            }
            else
            {
                paths.Add("/lib/");
            }

            var blackListedPaths = new HashSet<string> {"/usr/lib/cron/tabs"};

            var cancellation = new CancellationTokenSource();
            CancelKeyPress += (s, ev) =>
            {
                _metrics.Write(Out);
                WriteLine("Shutting down.");
                ev.Cancel = false;
                cancellation.Cancel();
            };

            _ = Task.Run(() =>
            {
                WriteLine("Press Ctrl+C to exit or 'p' to print the status.");
                while (!cancellation.IsCancellationRequested)
                {
                    if (ReadKey(true).Key == ConsoleKey.P)
                    {
                        _metrics.Write(Out);
                    }
                }
            }, cancellation.Token);


            // TODO: M.E.DependencyInjection/Configuration
            var logLevel = LogLevel.Warning;
            var loggerFatBinaryReader = new LoggerAdapter<FatBinaryReader>(logLevel);
            var parser = new ObjectFileParser(
                new FatBinaryReader(loggerFatBinaryReader),
                _metrics,
                new LoggerAdapter<ObjectFileParser>(logLevel));

            var loggerClient = new LoggerAdapter<Client>(logLevel);
            var client = new Client(
                new SymbolClient(endpoint, new LoggerAdapter<SymbolClient>(logLevel)),
                parser,
                blackListedPaths: blackListedPaths,
                metrics: _metrics,
                logger: loggerClient);

            try
            {
                await client.UploadAllPathsAsync(bundleId, type, paths, cancellation.Token);
            }
            finally
            {
                _metrics.Write(Out);
            }
        }

        static async Task Main(
            string? upload = null,
            string? check = null,
            string? package = null,
            string? symsorter = null,
            string? bundleId = null,
            string? batchType = null,
            Uri? serverEndpoint = null)
        {
            SentrySdk.Init(o =>
            {
                o.Debug = true;
                o.DiagnosticsLevel = Sentry.Protocol.SentryLevel.Warning;
                o.AttachStacktrace = true;
                o.Dsn = new Dsn(Dsn);
            });
            {
                var capturedEndpoint = serverEndpoint;
                SentrySdk.ConfigureScope(s =>
                {
                    s.SetTag("app", typeof(Program).Assembly.GetName().Name);
                    s.SetExtra("parameters", new {upload, check, package, endpoint = capturedEndpoint});
                });
            }

            serverEndpoint ??= new Uri(SymbolCollectorServiceUrl);

            try
            {
                switch (upload)
                {
                    case "device":
                        if (bundleId is null)
                        {
                            WriteLine("A 'bundleId' is required to upload symbols from this device.");
                            return;
                        }

                        WriteLine("Uploading images from this device.");
                        await UploadSymbols(serverEndpoint, DeviceBatchType(), bundleId);
                        return;
                    case "package":
                        if (package is null || batchType is null || bundleId is null)
                        {
                            WriteLine(@"Missing required parameters:
--bundle-id MacOS_15.11
--batch-type macos
--package path/to/package.dmg");
                            return;
                        }

                        WriteLine($"Uploading stuff from package: '{package}'.");
                        // TODO:
                        break;
                }

                // TODO: M.E.DependencyInjection/Configuration
                var logLevel = LogLevel.Warning;
                var loggerFatBinaryReader = new LoggerAdapter<FatBinaryReader>(logLevel);
                var parser = new ObjectFileParser(
                    new FatBinaryReader(loggerFatBinaryReader),
                    _metrics,
                    new LoggerAdapter<ObjectFileParser>(logLevel));

                if (check is { } checkLib)
                {
                    if (!File.Exists(check))
                    {
                        WriteLine($"File to check '{checkLib}' doesn't exist.");
                        return;
                    }

                    WriteLine($"Checking '{checkLib}'.");
                    if (parser.TryParse(checkLib, out var result) && result is {})
                    {
                        if (result is FatMachOFileResult fatMachOFileResult)
                        {
                            WriteLine($"Fat Mach-O File:");
                            Print(fatMachOFileResult);
                            foreach (var innerFile in fatMachOFileResult.InnerFiles)
                            {
                                WriteLine("Inner file:");
                                Print(innerFile);
                            }
                        }
                        else
                        {
                            Print(result);
                        }

                        static void Print(ObjectFileResult r)
                            => WriteLine($@"
Path: {r.Path}
CodeId: {r.CodeId}
DebugId: {r.DebugId}
BuildId: {r.UnifiedId}
BuildIdType: {r.BuildIdType}
File hash: {r.Hash}
File Format: {r.FileFormat}
Architecture: {r.Architecture}
ObjectKind: {r.ObjectKind}
");
                    }
                    else
                    {
                        WriteLine($"Failed to parse {checkLib}.");
                    }

                    return;
                }

                if (symsorter is { })
                {
                    if (string.IsNullOrWhiteSpace(bundleId))
                    {
                        WriteLine("Missing bundle Id");
                        return;
                    }

                    if (!Directory.Exists(symsorter))
                    {
                        WriteLine($"Directory '{symsorter}' doesn't exist.");
                        return;
                    }

                    foreach (var file in Directory.GetFiles(symsorter, "*", SearchOption.AllDirectories))
                    {
                        if (parser.TryParse(file, out var result) && result is {})
                        {
                            if (result is FatMachOFileResult fatMachOFileResult)
                            {
                                foreach (var innerFile in fatMachOFileResult.InnerFiles)
                                {
                                    WriteLine($"{innerFile.DebugId[..2]}/{innerFile.DebugId[2..].Replace("-", "")}");
                                }
                            }
                            else
                            {
                                if (result.FileFormat == FileFormat.Elf)
                                {
                                    WriteLine($"{result.CodeId[..2]}/{result.CodeId[2..].Replace("-", "")}");
                                }
                                else
                                {
                                    WriteLine($"{result.DebugId[..2]}/{result.DebugId[2..].Replace("-", "")}");
                                }
                            }
                        }
                    }

                    return;
                }

                WriteLine(@"Parameters:
--upload device --bundle-id id
--upload package --bundle-id id --batch-type type --package ~/location
    Valid Batch Types are: android, macos, ios, watchos, android
--check file-to-check");
            }
            catch (Exception e)
            {
                WriteLine(e);
                SentrySdk.CaptureException(e);
            }
            finally
            {
                await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
            }

            static BatchType DeviceBatchType()
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return BatchType.Linux;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return BatchType.MacOS;
                }

                throw new InvalidOperationException("No BatchType available for the current device.");
            }
        }
    }
}
