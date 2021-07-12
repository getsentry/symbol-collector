using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sentry;
using SymbolCollector.Core;
using static System.Console;

namespace SymbolCollector.Console
{
    internal class Program
    {
#if DEBUG
        private const string Dsn = "https://02619ad38bcb40d0be5167e1fb335954@sentry.io/1847454";
#else
        private const string Dsn = "https://2262a4fa0a6d409c848908ec90c3c5b4@sentry.io/1886021";
#endif

        private static readonly ClientMetrics _metrics = new ClientMetrics();

        static async Task Main(
            string? upload = null,
            string? check = null,
            string? path = null,
            string? symsorter = null,
            string? bundleId = null,
            string? batchType = null,
            bool dryrun = false,
            Uri? serverEndpoint = null)
        {
            var cancellation = new CancellationTokenSource();
            var userAgent = "Console/" + typeof(Program).Assembly.GetName().Version;
            var args = new Args(upload, check, path, symsorter, bundleId, batchType, serverEndpoint,
                userAgent, dryrun,
                cancellation);

            Bootstrap(args);

            try
            {
                await Run(args);
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
        }

        private static async Task Run(Args args)
        {
            using var host = Startup.Init(s =>
            {
                if (args.ServerEndpoint != null)
                {
                    s.AddOptions()
                        .PostConfigure<SymbolClientOptions>(o =>
                        {
                            o.UserAgent = args.UserAgent;
                            o.BaseAddress = args.ServerEndpoint;
                        });
                }

                s.AddSingleton(_metrics);
                s.AddSingleton<ConsoleUploader>();
            });

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            var uploader = host.Services.GetRequiredService<ConsoleUploader>();

            switch (args.Upload)
            {
                case "device":
                    if (args.BundleId is null)
                    {
                        WriteLine("A 'bundleId' is required to upload symbols from this device.");
                        return;
                    }

                    SentrySdk.ConfigureScope(s =>
                    {
                        s.SetTag("friendly-name", args.BundleId);
                    });

                    logger.LogInformation("Uploading images from this device.");
                    await uploader.StartUploadSymbols(
                        DefaultSymbolPathProvider.GetDefaultPaths(),
                        args.BundleId,
                        args.BatchType,
                        args.Cancellation.Token);
                    return;
                case "directory":
                    if (args.Path is null || args.BatchType is null || args.BundleId is null)
                    {
                        WriteLine(@"Missing required parameters:
            --bundle-id MacOS_15.11
            --batch-type macos
            --path path/to/dir");
                        return;
                    }

                    if (!Directory.Exists(args.Path))
                    {
                        WriteLine($@"Directory {args.Path} doesn't exist.");
                        return;
                    }

                    logger.LogInformation("Uploading stuff from directory: '{path}'.", args.Path);
                    await uploader.StartUploadSymbols(
                        new[] {args.Path},
                        args.BundleId,
                        args.BatchType,
                        args.Cancellation.Token);
                    break;
            }

            if (args.Check is { } checkLib)
            {
                if (!File.Exists(args.Check))
                {
                    WriteLine($"File to check '{checkLib}' doesn't exist.");
                    return;
                }

                logger.LogInformation("Checking '{checkLib}'.", checkLib);
                var parser = host.Services.GetRequiredService<ObjectFileParser>();
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

            if (args.Symsorter is {} && args.BatchType is {} && args.BundleId is {} && args.Path is {})
            {
                if (string.IsNullOrWhiteSpace(args.BundleId))
                {
                    WriteLine("Missing bundle Id");
                    return;
                }

                if (!Directory.Exists(args.Symsorter))
                {
                    WriteLine($"Directory '{args.Symsorter}' doesn't exist.");
                    return;
                }

                var sorter = host.Services.GetRequiredService<Symsorter>();

                await sorter.ProcessBundle(
                    new SymsorterParameters(
                        args.Path,
                        args.BatchType?.ToString() ?? string.Empty,
                        args.BundleId,
                        args.DryRun),
                    args.Symsorter,
                    args.Cancellation.Token);

                return;
            }

            WriteLine(@"Parameters:
            --upload device --bundle-id id
            --upload directory --bundle-id id --batch-type type --path ~/location
                Valid Batch Types are: android, macos, ios, watchos, android
            --symsorter path/to/symbols --bundle-id macos_10.11 --batch-type macos --path output/path [--dryrun true]
            --check file-to-check");
        }

        private static void Bootstrap(Args args)
        {
            using var _ = SentrySdk.Init(o =>
            {
                o.Debug = true;
                o.DiagnosticLevel = SentryLevel.Warning;
                o.AttachStacktrace = true;
                o.SendDefaultPii = true;
                o.AddInAppExclude("Polly");

                o.AddExceptionFilterForType<OperationCanceledException>();

                o.Dsn = Dsn;
                // TODO: This needs to be built-in
                o.BeforeSend += @event =>
                {
                    const string traceIdKey = "TraceIdentifier";
                    switch (@event.Exception)
                    {
                        case var e when e is OperationCanceledException:
                            return null!;
                        case var e when e?.Data.Contains(traceIdKey) == true:
                            @event.SetTag(traceIdKey, e.Data[traceIdKey]?.ToString() ?? "unknown");
                            break;
                    }

                    return @event;
                };
            });
            {
                SentrySdk.ConfigureScope(s =>
                {
                    s.SetTag("app", typeof(Program).Assembly.GetName().Name ?? "SymbolCollector");
                    s.SetTag("user-agent", args.UserAgent);
                    if (args.ServerEndpoint is {})
                    {
                        s.SetTag("server-endpoint", args.ServerEndpoint.AbsoluteUri);
                    }

                    s.Contexts["parameters"] = args;
                });
            }

            CancelKeyPress += (s, ev) =>
            {
                _metrics.Write(Out);
                WriteLine("Shutting down.");
                ev.Cancel = false;
                args.Cancellation.Cancel();
            };
        }
    }

    internal class Args
    {
        public Args(
            string? upload,
            string? check,
            string? path,
            string? symsorter,
            string? bundleId,
            string? batchType,
            Uri? serverEndpoint,
            string userAgent,
            bool dryRun,
            CancellationTokenSource cancellation)
        {
            Upload = upload;
            Check = check;
            Path = path;
            Symsorter = symsorter;
            BundleId = bundleId;
            if (Enum.TryParse<BatchType>(batchType, true, out var result) &&
                result != Core.BatchType.Unknown)
            {
                BatchType = result;
            }
            ServerEndpoint = serverEndpoint;
            UserAgent = userAgent;
            DryRun = dryRun;
            Cancellation = cancellation;
        }

        public string? Upload { get; }
        public string? Check { get; }
        public string? Path { get; }
        public string? Symsorter { get; }
        public string? BundleId { get; }
        public BatchType? BatchType { get; }
        public Uri? ServerEndpoint { get; }
        public string UserAgent { get; }
        public bool DryRun { get; }
        public CancellationTokenSource Cancellation { get; }
    }
}
